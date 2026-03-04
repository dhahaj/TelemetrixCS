// TelemetrixCS - C# port of the Telemetrix Python library
// Copyright (c) 2025 - Based on original work by Alan Yoriks
// Licensed under GNU AGPL Version 3
//
// Target framework: .NET 6+ (or .NET Standard 2.0+)
// NuGet dependency: System.IO.Ports (if targeting .NET Core / .NET 5+)
//
// Supported features (basic Arduino):
//   - Auto COM-port detection or manual COM-port specification
//   - Digital output  : SetPinModeDigitalOutput / DigitalWrite
//   - Digital input   : SetPinModeDigitalInput / SetPinModeDigitalInputPullup (callback)
//   - Analog output   : SetPinModeAnalogOutput / AnalogWrite  (PWM)
//   - Analog input    : SetPinModeAnalogInput (callback)
//   - Reporting control: Enable/Disable per-pin or all
//   - Analog scan interval tuning
//   - Clean shutdown

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace TelemetrixCS
{
    // ─── Callback data types ──────────────────────────────────────────────────

    /// <summary>Data delivered to a digital-input callback.</summary>
    public readonly struct DigitalInputData
    {
        /// <summary>Report type identifier (always 2 = DIGITAL_REPORT).</summary>
        public int ReportType { get; }
        /// <summary>Arduino pin number.</summary>
        public int Pin        { get; }
        /// <summary>Pin value: 0 or 1.</summary>
        public int Value      { get; }
        /// <summary>UTC timestamp of the reading.</summary>
        public DateTime Timestamp { get; }

        internal DigitalInputData(int pin, int value)
        {
            ReportType = PrivateConstants.DIGITAL_REPORT;
            Pin        = pin;
            Value      = value;
            Timestamp  = DateTime.UtcNow;
        }
    }

    /// <summary>Data delivered to an analog-input callback.</summary>
    public readonly struct AnalogInputData
    {
        /// <summary>Report type identifier (always 3 = ANALOG_REPORT).</summary>
        public int ReportType { get; }
        /// <summary>Analog pin number (0 = A0, 1 = A1, …).</summary>
        public int Pin        { get; }
        /// <summary>10-bit ADC value (0–1023).</summary>
        public int Value      { get; }
        /// <summary>UTC timestamp of the reading.</summary>
        public DateTime Timestamp { get; }

        internal AnalogInputData(int pin, int value)
        {
            ReportType = PrivateConstants.ANALOG_REPORT;
            Pin        = pin;
            Value      = value;
            Timestamp  = DateTime.UtcNow;
        }
    }

    // ─── Main Telemetrix class ────────────────────────────────────────────────

    /// <summary>
    /// C# client for the Telemetrix4Arduino firmware.
    /// Communicates over a serial link, mirroring the Python Telemetrix API.
    /// </summary>
    public sealed class Telemetrix : IDisposable
    {
        // ── Public delegates ─────────────────────────────────────────────────

        /// <summary>Callback invoked when a digital input pin changes value.</summary>
        public delegate void DigitalInputCallback(DigitalInputData data);

        /// <summary>Callback invoked when an analog input reading is ready.</summary>
        public delegate void AnalogInputCallback(AnalogInputData data);

        // ── Private state ────────────────────────────────────────────────────

        private SerialPort?  _serialPort;
        private Thread?      _receiverThread;
        private Thread?      _reporterThread;

        private readonly ConcurrentQueue<byte> _dataQueue = new();
        private volatile bool  _running;
        private volatile bool  _disposed;

        private int[]?   _firmwareVersion;
        private int?     _reportedArduinoId;

        private readonly Dictionary<int, DigitalInputCallback> _digitalCallbacks = new();
        private readonly Dictionary<int, AnalogInputCallback>  _analogCallbacks  = new();

        // Report dispatch: maps report-type byte → handler
        private readonly Dictionary<byte, Action<List<byte>>> _reportDispatch = new();

        private readonly int _arduinoInstanceId;
        private readonly int _arduinoWaitSeconds;
        private readonly double _sleepTuneMs;
        private readonly bool _shutdownOnException;

        // ── Constructor ──────────────────────────────────────────────────────

        /// <summary>
        /// Connect to a Telemetrix4Arduino-flashed Arduino.
        /// </summary>
        /// <param name="comPort">
        ///   Explicit port name (e.g. "COM3" or "/dev/ttyACM0").
        ///   Pass <c>null</c> to auto-detect.
        /// </param>
        /// <param name="arduinoInstanceId">
        ///   Must match the ARDUINO_ID defined in the sketch (default 1).
        /// </param>
        /// <param name="arduinoWaitSeconds">
        ///   Seconds to wait for the Arduino to reset after opening the port (default 4).
        /// </param>
        /// <param name="sleepTuneMs">
        ///   Polling sleep in milliseconds for background threads (default 0.001).
        /// </param>
        /// <param name="shutdownOnException">
        ///   If true, <see cref="Shutdown"/> is called before any exception is thrown.
        /// </param>
        public Telemetrix(
            string? comPort             = null,
            int     arduinoInstanceId   = 1,
            int     arduinoWaitSeconds  = 4,
            double  sleepTuneMs         = 0.001,
            bool    shutdownOnException = true)
        {
            _arduinoInstanceId  = arduinoInstanceId;
            _arduinoWaitSeconds = arduinoWaitSeconds;
            _sleepTuneMs        = sleepTuneMs;
            _shutdownOnException = shutdownOnException;

            Console.WriteLine($"TelemetrixCS  Version {PrivateConstants.TELEMETRIX_VERSION}");
            Console.WriteLine("C# port – basic digital/analog features\n");

            BuildDispatchTable();
            StartBackgroundThreads();

            if (comPort is null)
                FindArduino();
            else
                ManualOpen(comPort);

            // Enable all reports and reset server state
            SendCommand(new[] { PrivateConstants.ENABLE_ALL_REPORTS });
            SendCommand(new[] { PrivateConstants.GET_FEATURES });
            Thread.Sleep(200);
            SendCommand(new[] { PrivateConstants.RESET });
        }

        // ── Pin-mode setup ───────────────────────────────────────────────────

        /// <summary>Configure a pin as a digital output.</summary>
        public void SetPinModeDigitalOutput(int pin) =>
            SetPinMode(pin, PrivateConstants.AT_OUTPUT);

        /// <summary>
        /// Configure a pin as a digital input and register a callback for value changes.
        /// </summary>
        public void SetPinModeDigitalInput(int pin, DigitalInputCallback callback)
        {
            _digitalCallbacks[pin] = callback;
            SetPinMode(pin, PrivateConstants.AT_INPUT);
        }

        /// <summary>
        /// Configure a pin as a digital input with internal pull-up and register a callback.
        /// </summary>
        public void SetPinModeDigitalInputPullup(int pin, DigitalInputCallback callback)
        {
            _digitalCallbacks[pin] = callback;
            SetPinMode(pin, PrivateConstants.AT_INPUT_PULLUP);
        }

        /// <summary>Configure a pin as a PWM (analog) output.</summary>
        public void SetPinModeAnalogOutput(int pin) =>
            SetPinMode(pin, PrivateConstants.AT_OUTPUT);

        /// <summary>
        /// Configure a pin as an analog input and register a callback for readings.
        /// </summary>
        /// <param name="pin">Analog pin index (0 = A0, 1 = A1, …).</param>
        /// <param name="differential">
        ///   Minimum change from the last reported value before a new report is sent.
        ///   Use 0 to report every sample.
        /// </param>
        /// <param name="callback">Callback invoked with each new reading.</param>
        public void SetPinModeAnalogInput(int pin, int differential, AnalogInputCallback callback)
        {
            _analogCallbacks[pin] = callback;
            byte[] cmd =
            {
                PrivateConstants.SET_PIN_MODE,
                (byte)pin,
                PrivateConstants.AT_ANALOG,
                (byte)(differential >> 8),
                (byte)(differential & 0xFF),
                1   // enable reporting
            };
            SendCommand(cmd);
        }

        // ── Write methods ────────────────────────────────────────────────────

        /// <summary>Write a digital HIGH (1) or LOW (0) to a pin.</summary>
        public void DigitalWrite(int pin, int value) =>
            SendCommand(new byte[] { PrivateConstants.DIGITAL_WRITE, (byte)pin, (byte)value });

        /// <summary>
        /// Write a PWM value to a pin (analog output).
        /// The value is a 16-bit integer; most Arduino boards only use the low 8 bits.
        /// </summary>
        public void AnalogWrite(int pin, int value)
        {
            byte msb = (byte)(value >> 8);
            byte lsb = (byte)(value & 0xFF);
            SendCommand(new byte[] { PrivateConstants.ANALOG_WRITE, (byte)pin, msb, lsb });
        }

        // ── Reporting control ────────────────────────────────────────────────

        /// <summary>Disable reporting on all digital and analog input pins.</summary>
        public void DisableAllReporting() =>
            SendCommand(new byte[]
            {
                PrivateConstants.MODIFY_REPORTING,
                PrivateConstants.REPORTING_DISABLE_ALL,
                0
            });

        /// <summary>Enable analog reporting on a single analog pin.</summary>
        public void EnableAnalogReporting(int pin) =>
            SendCommand(new byte[]
            {
                PrivateConstants.MODIFY_REPORTING,
                PrivateConstants.REPORTING_ANALOG_ENABLE,
                (byte)pin
            });

        /// <summary>Disable analog reporting on a single analog pin.</summary>
        public void DisableAnalogReporting(int pin) =>
            SendCommand(new byte[]
            {
                PrivateConstants.MODIFY_REPORTING,
                PrivateConstants.REPORTING_ANALOG_DISABLE,
                (byte)pin
            });

        /// <summary>Enable digital reporting on a single digital pin.</summary>
        public void EnableDigitalReporting(int pin) =>
            SendCommand(new byte[]
            {
                PrivateConstants.MODIFY_REPORTING,
                PrivateConstants.REPORTING_DIGITAL_ENABLE,
                (byte)pin
            });

        /// <summary>Disable digital reporting on a single digital pin.</summary>
        public void DisableDigitalReporting(int pin) =>
            SendCommand(new byte[]
            {
                PrivateConstants.MODIFY_REPORTING,
                PrivateConstants.REPORTING_DIGITAL_DISABLE,
                (byte)pin
            });

        /// <summary>
        /// Set the analog scan interval (0–255 ms).
        /// Lower values = faster sampling; higher values = lower CPU load on the Arduino.
        /// </summary>
        public void SetAnalogScanInterval(int intervalMs)
        {
            if (intervalMs < 0 || intervalMs > 255)
                Throw(new ArgumentOutOfRangeException(nameof(intervalMs), "Interval must be 0–255."));

            SendCommand(new byte[]
            {
                PrivateConstants.SET_ANALOG_SCANNING_INTERVAL,
                (byte)intervalMs
            });
        }

        // ── Firmware info ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the firmware version reported by the Arduino as [major, minor, patch],
        /// or null if not yet received.
        /// </summary>
        public IReadOnlyList<int>? FirmwareVersion => _firmwareVersion;

        // ── Shutdown / Dispose ───────────────────────────────────────────────

        /// <summary>
        /// Perform an orderly shutdown: stop reporting, join threads, close the serial port.
        /// Safe to call multiple times.
        /// </summary>
        public void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;
            _running  = false;

            try { SendCommand(new[] { PrivateConstants.STOP_ALL_REPORTS }); }
            catch { /* best-effort */ }

            Thread.Sleep(200);

            _receiverThread?.Join(500);
            _reporterThread?.Join(500);

            try
            {
                if (_serialPort?.IsOpen == true)
                {
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    _serialPort.Close();
                }
                _serialPort?.Dispose();
            }
            catch { /* ignore errors during cleanup */ }
        }

        /// <inheritdoc/>
        public void Dispose() => Shutdown();

        // ── Private: connection helpers ──────────────────────────────────────

        private void FindArduino()
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
                Throw(new InvalidOperationException("No serial ports found."));

            Console.WriteLine("Scanning serial ports for a Telemetrix Arduino...");

            var candidates = new List<SerialPort>();
            foreach (string portName in ports)
            {
                try
                {
                    var sp = new SerialPort(portName, 115200) { ReadTimeout = 1000, WriteTimeout = 1000 };
                    sp.Open();
                    candidates.Add(sp);
                    Console.WriteLine($"  Opened: {portName}");
                }
                catch { /* port busy or unavailable – skip */ }
            }

            if (candidates.Count == 0)
                Throw(new InvalidOperationException("Could not open any serial port."));

            Console.WriteLine($"Waiting {_arduinoWaitSeconds}s for Arduino(s) to reset…");
            Thread.Sleep(_arduinoWaitSeconds * 1000);

            foreach (var sp in candidates)
            {
                _serialPort = sp;
                sp.DiscardInBuffer();

                _reportedArduinoId = null;
                GetArduinoId();

                // Wait up to 10 seconds for the ID reply
                int retries = 50;
                while (_reportedArduinoId is null && retries-- > 0)
                    Thread.Sleep(200);

                if (_reportedArduinoId == _arduinoInstanceId)
                {
                    Console.WriteLine($"Valid Arduino found on {sp.PortName}");
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();
                    // Close the other candidates
                    foreach (var other in candidates.Where(c => c != sp))
                    { try { other.Close(); other.Dispose(); } catch { } }
                    return;
                }
            }

            foreach (var sp in candidates) { try { sp.Close(); sp.Dispose(); } catch { } }
            Throw(new InvalidOperationException($"No Arduino with instance ID {_arduinoInstanceId} found."));
        }

        private void ManualOpen(string comPort)
        {
            Console.WriteLine($"Opening {comPort}…");
            _serialPort = new SerialPort(comPort, 115200) { ReadTimeout = 1000, WriteTimeout = 1000 };
            _serialPort.Open();

            Console.WriteLine($"Waiting {_arduinoWaitSeconds}s for Arduino to reset…");
            Thread.Sleep(_arduinoWaitSeconds * 1000);

            _reportedArduinoId = null;
            GetArduinoId();

            int retries = 50;
            while (_reportedArduinoId is null && retries-- > 0)
                Thread.Sleep(200);

            if (_reportedArduinoId != _arduinoInstanceId)
                Throw(new InvalidOperationException($"Incorrect Arduino ID: {_reportedArduinoId} (expected {_arduinoInstanceId})."));

            Console.WriteLine($"Arduino confirmed on {comPort}");

            GetFirmwareVersion();
            Thread.Sleep(500);

            if (_firmwareVersion is null)
                Throw(new InvalidOperationException("Could not retrieve firmware version."));

            Console.WriteLine($"Firmware: {string.Join(".", _firmwareVersion)}");
        }

        private void GetArduinoId()      => SendCommand(new[] { PrivateConstants.ARE_U_THERE });
        private void GetFirmwareVersion() => SendCommand(new[] { PrivateConstants.GET_FIRMWARE_VERSION });

        // ── Private: threading ───────────────────────────────────────────────

        private void StartBackgroundThreads()
        {
            _running = true;

            _receiverThread = new Thread(SerialReceiverLoop)
            {
                IsBackground = true,
                Name = "Telemetrix-Receiver"
            };

            _reporterThread = new Thread(ReporterLoop)
            {
                IsBackground = true,
                Name = "Telemetrix-Reporter"
            };

            _receiverThread.Start();
            _reporterThread.Start();
        }

        /// <summary>Continuously read bytes from the serial port into the queue.</summary>
        private void SerialReceiverLoop()
        {
            while (_running)
            {
                try
                {
                    if (_serialPort is { IsOpen: true } sp && sp.BytesToRead > 0)
                    {
                        int b = sp.ReadByte();
                        if (b >= 0) _dataQueue.Enqueue((byte)b);
                    }
                    else
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(_sleepTuneMs));
                    }
                }
                catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(_sleepTuneMs));
                }
                catch (Exception)
                {
                    if (!_running) break;
                }
            }
        }

        /// <summary>
        /// Continuously dequeue bytes, assemble packets, and dispatch report handlers.
        /// Packet format: [length] [report-type] [data…]
        /// </summary>
        private void ReporterLoop()
        {
            while (_running)
            {
                if (!_dataQueue.TryDequeue(out byte packetLength))
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(_sleepTuneMs));
                    continue;
                }

                if (packetLength == 0)
                {
                    if (_shutdownOnException)
                        Shutdown();
                    throw new InvalidOperationException("Received a zero-length packet from Arduino.");
                }

                // Collect all bytes for this packet
                var payload = new List<byte>(packetLength);
                while (payload.Count < packetLength)
                {
                    if (_dataQueue.TryDequeue(out byte b))
                        payload.Add(b);
                    else
                        Thread.Sleep(TimeSpan.FromMilliseconds(_sleepTuneMs));
                }

                byte reportType = payload[0];
                payload.RemoveAt(0);

                if (_reportDispatch.TryGetValue(reportType, out var handler))
                {
                    try { handler(payload); }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Telemetrix] Report handler {reportType} threw: {ex.Message}");
                    }
                }
                // Unknown report types are silently ignored (forward-compatible)
            }
        }

        // ── Private: dispatch table ──────────────────────────────────────────

        private void BuildDispatchTable()
        {
            _reportDispatch[PrivateConstants.DIGITAL_REPORT]   = HandleDigitalMessage;
            _reportDispatch[PrivateConstants.ANALOG_REPORT]    = HandleAnalogMessage;
            _reportDispatch[PrivateConstants.FIRMWARE_REPORT]  = HandleFirmwareMessage;
            _reportDispatch[PrivateConstants.I_AM_HERE_REPORT] = HandleIAmHere;
            _reportDispatch[PrivateConstants.FEATURES]         = HandleFeatures;
            _reportDispatch[PrivateConstants.DEBUG_PRINT]      = HandleDebugPrint;
        }

        // ── Private: report handlers ─────────────────────────────────────────

        private void HandleDigitalMessage(List<byte> data)
        {
            // data: [pin, value]
            int pin   = data[0];
            int value = data[1];

            if (_digitalCallbacks.TryGetValue(pin, out var cb))
                cb(new DigitalInputData(pin, value));
        }

        private void HandleAnalogMessage(List<byte> data)
        {
            // data: [pin, value_msb, value_lsb]
            int pin   = data[0];
            int value = (data[1] << 8) | data[2];

            if (_analogCallbacks.TryGetValue(pin, out var cb))
                cb(new AnalogInputData(pin, value));
        }

        private void HandleFirmwareMessage(List<byte> data)
        {
            // data: [major, minor, patch]
            _firmwareVersion = new[] { data[0], data[1], data.Count > 2 ? data[2] : 0 };
            Console.WriteLine($"Firmware version: {string.Join(".", _firmwareVersion)}");
        }

        private void HandleIAmHere(List<byte> data)
        {
            _reportedArduinoId = data[0];
        }

        private void HandleFeatures(List<byte> data)
        {
            // Features bitmask – stored but not used in basic build
            // (kept for protocol completeness)
        }

        private static void HandleDebugPrint(List<byte> data)
        {
            if (data.Count >= 3)
            {
                int id    = data[0];
                int value = (data[1] << 8) | data[2];
                Console.WriteLine($"[Arduino DEBUG] ID={id}  Value={value}");
            }
        }

        // ── Private: helpers ─────────────────────────────────────────────────

        private void SetPinMode(int pin, byte mode)
        {
            byte[] cmd = mode switch
            {
                PrivateConstants.AT_INPUT =>
                    new byte[] { PrivateConstants.SET_PIN_MODE, (byte)pin, PrivateConstants.AT_INPUT, 1 },

                PrivateConstants.AT_INPUT_PULLUP =>
                    new byte[] { PrivateConstants.SET_PIN_MODE, (byte)pin, PrivateConstants.AT_INPUT_PULLUP, 1 },

                PrivateConstants.AT_OUTPUT =>
                    new byte[] { PrivateConstants.SET_PIN_MODE, (byte)pin, PrivateConstants.AT_OUTPUT },

                _ => throw new ArgumentException($"Unknown pin mode: {mode}")
            };

            SendCommand(cmd);
        }

        /// <summary>
        /// Prepend the length byte and write the command to the serial port.
        /// The length byte counts only the payload (does NOT include itself),
        /// matching the Telemetrix protocol.
        /// </summary>
        private void SendCommand(byte[] command)
        {
            if (_serialPort is null || !_serialPort.IsOpen)
                throw new InvalidOperationException("Serial port is not open.");

            byte[] packet = new byte[command.Length + 1];
            packet[0] = (byte)command.Length;
            Array.Copy(command, 0, packet, 1, command.Length);

            lock (_serialPort)
            {
                _serialPort.Write(packet, 0, packet.Length);
            }
        }

        // Overload accepting IEnumerable for single-element arrays (convenience)
        private void SendCommand(byte[] command, int _) => SendCommand(command);

        private void Throw(Exception ex)
        {
            if (_shutdownOnException) Shutdown();
            throw ex;
        }
    }
}
