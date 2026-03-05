// TelemetrixCS - C# port of the Telemetrix Python library
// Copyright (c) 2025 - Based on original work by Alan Yorinks
// Licensed under GNU AGPL Version 3
//
// Target framework: .NET 6+ (or .NET Standard 2.0+)
// NuGet dependency: System.IO.Ports (if targeting .NET Core / .NET 5+)
//
// Supported features:
//   - Auto COM-port detection or manual COM-port specification
//   - Digital output  : SetPinModeDigitalOutput / DigitalWrite
//   - Digital input   : SetPinModeDigitalInput / SetPinModeDigitalInputPullup (callback)
//   - Analog output   : SetPinModeAnalogOutput / AnalogWrite  (PWM)
//   - Analog input    : SetPinModeAnalogInput (callback)
//   - I2C             : SetPinModeI2C / I2CRead / I2CReadRestartTransmission / I2CWrite
//   - SPI             : SetPinModeSpi / SpiReadBlocking / SpiWriteBlocking / SpiSetFormat / SpiCsControl
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
        public int ReportType { get; }
        public int Pin { get; }
        public int Value { get; }
        public DateTime Timestamp { get; }

        internal DigitalInputData(int pin, int value)
        {
            ReportType = PrivateConstants.DIGITAL_REPORT;
            Pin = pin;
            Value = value;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>Data delivered to an analog-input callback.</summary>
    public readonly struct AnalogInputData
    {
        public int ReportType { get; }
        public int Pin { get; }
        public int Value { get; }
        public DateTime Timestamp { get; }

        internal AnalogInputData(int pin, int value)
        {
            ReportType = PrivateConstants.ANALOG_REPORT;
            Pin = pin;
            Value = value;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>Data delivered to an I2C read callback.</summary>
    public readonly struct I2CReadData
    {
        /// <summary>Report type identifier (always 10 = I2C_READ_REPORT).</summary>
        public int ReportType { get; }

        /// <summary>I2C port index (0 = primary, 1 = secondary).</summary>
        public int I2CPort { get; }

        /// <summary>Number of bytes returned by the device.</summary>
        public int ByteCount { get; }

        /// <summary>I2C device address.</summary>
        public int Address { get; }

        /// <summary>Register that was read from.</summary>
        public int Register { get; }

        /// <summary>The data bytes read from the device.</summary>
        public byte[] Data { get; }

        /// <summary>UTC timestamp of the reading.</summary>
        public DateTime Timestamp { get; }

        internal I2CReadData(int i2cPort, int byteCount, int address, int register, byte[] data)
        {
            ReportType = PrivateConstants.I2C_READ_REPORT;
            I2CPort = i2cPort;
            ByteCount = byteCount;
            Address = address;
            Register = register;
            Data = data;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>Data delivered to an SPI read callback.</summary>
    public readonly struct SpiReadData
    {
        /// <summary>Report type identifier (always 13 = SPI_REPORT).</summary>
        public int ReportType { get; }

        /// <summary>Number of data bytes returned.</summary>
        public int ByteCount { get; }

        /// <summary>The data bytes read from the SPI device.</summary>
        public byte[] Data { get; }

        /// <summary>UTC timestamp of the reading.</summary>
        public DateTime Timestamp { get; }

        internal SpiReadData(int byteCount, byte[] data)
        {
            ReportType = PrivateConstants.SPI_REPORT;
            ByteCount = byteCount;
            Data = data;
            Timestamp = DateTime.UtcNow;
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

        public delegate void DigitalInputCallback(DigitalInputData data);
        public delegate void AnalogInputCallback(AnalogInputData data);
        public delegate void I2CReadCallback(I2CReadData data);
        public delegate void SpiReadCallback(SpiReadData data);

        // ── Private state ────────────────────────────────────────────────────

        private SerialPort? _serialPort;
        private Thread? _receiverThread;
        private Thread? _reporterThread;

        private readonly ConcurrentQueue<byte> _dataQueue = new();
        private volatile bool _running;
        private volatile bool _disposed;

        private int[]? _firmwareVersion;
        private int? _reportedArduinoId;
        private int _reportedFeatures;

        private readonly Dictionary<int, DigitalInputCallback> _digitalCallbacks = new();
        private readonly Dictionary<int, AnalogInputCallback> _analogCallbacks = new();

        // I2C state
        private I2CReadCallback? _i2cCallback;
        private I2CReadCallback? _i2cCallback2;
        private bool _i2c1Active;
        private bool _i2c2Active;

        // SPI state
        private SpiReadCallback? _spiCallback;
        private bool _spiEnabled;
        private readonly List<int> _csPinsEnabled = new();

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
            string? comPort = null,
            int arduinoInstanceId = 1,
            int arduinoWaitSeconds = 4,
            double sleepTuneMs = 0.001,
            bool shutdownOnException = true)
        {
            _arduinoInstanceId = arduinoInstanceId;
            _arduinoWaitSeconds = arduinoWaitSeconds;
            _sleepTuneMs = sleepTuneMs;
            _shutdownOnException = shutdownOnException;

            Console.WriteLine($"TelemetrixCS  Version {PrivateConstants.TELEMETRIX_VERSION}");
            Console.WriteLine("C# port – digital/analog/I2C/SPI features\n");

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

        // ══════════════════════════════════════════════════════════════════════
        //  Pin-mode setup – Digital / Analog (unchanged from v1.0.0-cs)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Configure a pin as a digital output.</summary>
        public void SetPinModeDigitalOutput(int pin) =>
            SetPinMode(pin, PrivateConstants.AT_OUTPUT);

        /// <summary>Configure a pin as a digital input and register a callback.</summary>
        public void SetPinModeDigitalInput(int pin, DigitalInputCallback callback)
        {
            _digitalCallbacks[pin] = callback;
            SetPinMode(pin, PrivateConstants.AT_INPUT);
        }

        /// <summary>Configure a pin as a digital input with pull-up and register a callback.</summary>
        public void SetPinModeDigitalInputPullup(int pin, DigitalInputCallback callback)
        {
            _digitalCallbacks[pin] = callback;
            SetPinMode(pin, PrivateConstants.AT_INPUT_PULLUP);
        }

        /// <summary>Configure a pin as a PWM (analog) output.</summary>
        public void SetPinModeAnalogOutput(int pin) =>
            SetPinMode(pin, PrivateConstants.AT_OUTPUT);

        /// <summary>Configure a pin as an analog input with callback.</summary>
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
                1
            };
            SendCommand(cmd);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  I2C
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Establish the standard Arduino I2C pins for I2C utilization.
        /// This method MUST be called before any I2C request is made.
        /// Callbacks are set within the individual I2C read methods.
        /// </summary>
        /// <param name="i2cPort">0 = primary I2C port, 1 = secondary I2C port.</param>
        public void SetPinModeI2C(int i2cPort = 0)
        {
            if (i2cPort != 0)
            {
                if (_i2c2Active) return;
                _i2c2Active = true;
            }
            else
            {
                if (_i2c1Active) return;
                _i2c1Active = true;
            }

            SendCommand(new byte[] { PrivateConstants.I2C_BEGIN, (byte)i2cPort });
        }

        /// <summary>
        /// Read the specified number of bytes from the specified register
        /// for the I2C device.
        /// </summary>
        /// <param name="address">I2C device address.</param>
        /// <param name="register">
        ///   I2C register to read from, or 0 if no register selection is needed.
        /// </param>
        /// <param name="numberOfBytes">Number of bytes to read.</param>
        /// <param name="callback">Required callback to report I2C data.</param>
        /// <param name="i2cPort">0 = primary, 1 = secondary.</param>
        /// <param name="writeRegister">
        ///   If true (default), the register byte is written before the read.
        ///   Set to false to suppress the write phase.
        /// </param>
        public void I2CRead(int address, int register, int numberOfBytes,
                            I2CReadCallback callback,
                            int i2cPort = 0, bool writeRegister = true)
        {
            I2CReadRequest(address, register, numberOfBytes,
                           stopTransmission: true, callback: callback,
                           i2cPort: i2cPort, writeRegister: writeRegister);
        }

        /// <summary>
        /// Read the specified number of bytes from the specified register for the
        /// I2C device. This restarts the transmission after the read. It is required
        /// for some I2C devices such as the MMA8452Q accelerometer.
        /// </summary>
        /// <param name="address">I2C device address.</param>
        /// <param name="register">
        ///   I2C register to read from, or 0 if no register selection is needed.
        /// </param>
        /// <param name="numberOfBytes">Number of bytes to read.</param>
        /// <param name="callback">Required callback to report I2C data.</param>
        /// <param name="i2cPort">0 = primary, 1 = secondary.</param>
        /// <param name="writeRegister">
        ///   If true (default), the register byte is written before the read.
        ///   Set to false to suppress the write phase.
        /// </param>
        public void I2CReadRestartTransmission(int address, int register,
                                                int numberOfBytes,
                                                I2CReadCallback callback,
                                                int i2cPort = 0,
                                                bool writeRegister = true)
        {
            I2CReadRequest(address, register, numberOfBytes,
                           stopTransmission: false, callback: callback,
                           i2cPort: i2cPort, writeRegister: writeRegister);
        }

        /// <summary>
        /// Write data to an I2C device.
        /// </summary>
        /// <param name="address">I2C device address.</param>
        /// <param name="data">
        ///   A variable number of bytes to send to the device, passed as an array or list.
        /// </param>
        /// <param name="i2cPort">0 = primary, 1 = secondary.</param>
        public void I2CWrite(int address, byte[] data, int i2cPort = 0)
        {
            if (i2cPort == 0 && !_i2c1Active)
                Throw(new InvalidOperationException(
                    "I2CWrite: SetPinModeI2C was never called for I2C port 1."));

            if (i2cPort != 0 && !_i2c2Active)
                Throw(new InvalidOperationException(
                    "I2CWrite: SetPinModeI2C was never called for I2C port 2."));

            // command: [I2C_WRITE, length_of_data, address, i2c_port, data...]
            var command = new byte[4 + data.Length];
            command[0] = PrivateConstants.I2C_WRITE;
            command[1] = (byte)data.Length;
            command[2] = (byte)address;
            command[3] = (byte)i2cPort;
            Array.Copy(data, 0, command, 4, data.Length);

            SendCommand(command);
        }

        // ── I2C private helper ───────────────────────────────────────────────

        private void I2CReadRequest(int address, int register, int numberOfBytes,
                                    bool stopTransmission, I2CReadCallback callback,
                                    int i2cPort, bool writeRegister)
        {
            if (i2cPort == 0 && !_i2c1Active)
                Throw(new InvalidOperationException(
                    "I2CRead: SetPinModeI2C was never called for I2C port 1."));

            if (i2cPort != 0 && !_i2c2Active)
                Throw(new InvalidOperationException(
                    "I2CRead: SetPinModeI2C was never called for I2C port 2."));

            if (callback is null)
                Throw(new ArgumentNullException(nameof(callback),
                    "I2CRead: A callback function must be specified."));

            if (i2cPort == 0)
                _i2cCallback = callback;
            else
                _i2cCallback2 = callback;

            // command: [I2C_READ, address, register, number_of_bytes,
            //           stop_transmission, i2c_port, write_register]
            byte[] cmd =
            {
                PrivateConstants.I2C_READ,
                (byte)address,
                (byte)register,
                (byte)numberOfBytes,
                (byte)(stopTransmission ? 1 : 0),
                (byte)i2cPort,
                (byte)(writeRegister ? 1 : 0)
            };
            SendCommand(cmd);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SPI
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initialize the SPI interface by specifying chip-select pins.
        /// Standard Arduino MISO, MOSI, and CLK pins are used for the board in use.
        /// Chip Select is any digital-output-capable pin.
        /// </summary>
        /// <param name="chipSelectPins">
        ///   A list of pins to be used for chip select. The pins will be configured as
        ///   outputs and set HIGH, ready for use as chip selects.
        /// </param>
        /// <exception cref="InvalidOperationException">
        ///   Thrown if the SPI feature is not enabled on the server.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///   Thrown if <paramref name="chipSelectPins"/> is null or empty.
        /// </exception>
        public void SetPinModeSpi(int[] chipSelectPins)
        {
            if ((_reportedFeatures & PrivateConstants.SPI_FEATURE) == 0)
                Throw(new InvalidOperationException("The SPI feature is disabled in the server."));

            if (chipSelectPins is null || chipSelectPins.Length == 0)
                Throw(new ArgumentException("Chip select pins were not specified.", nameof(chipSelectPins)));

            _spiEnabled = true;

            // command: [SPI_INIT, number_of_cs_pins, cs_pin_0, cs_pin_1, ...]
            var command = new byte[2 + chipSelectPins!.Length];
            command[0] = PrivateConstants.SPI_INIT;
            command[1] = (byte)chipSelectPins.Length;

            for (int i = 0; i < chipSelectPins.Length; i++)
            {
                command[2 + i] = (byte)chipSelectPins[i];
                _csPinsEnabled.Add(chipSelectPins[i]);
            }

            SendCommand(command);
        }

        /// <summary>
        /// Read the specified number of bytes from the SPI bus and
        /// report them via a callback.
        /// </summary>
        /// <param name="registerSelection">Register to be selected for the read.</param>
        /// <param name="numberOfBytesToRead">Number of bytes to read.</param>
        /// <param name="callback">Required callback to report SPI data.</param>
        /// <param name="enableReadBit">
        ///   Many SPI devices require the register selection to be OR'ed with 0x80.
        ///   If true (default), the bit will be set by the server.
        /// </param>
        public void SpiReadBlocking(int registerSelection, int numberOfBytesToRead,
                                    SpiReadCallback callback, bool enableReadBit = true)
        {
            if (!_spiEnabled)
                Throw(new InvalidOperationException("SpiReadBlocking: SPI interface is not enabled."));

            if (callback is null)
                Throw(new ArgumentNullException(nameof(callback),
                    "SpiReadBlocking: A callback must be specified."));

            _spiCallback = callback;

            // command: [SPI_READ_BLOCKING, number_of_bytes, register, enable_read_bit]
            byte[] cmd =
            {
                PrivateConstants.SPI_READ_BLOCKING,
                (byte)numberOfBytesToRead,
                (byte)registerSelection,
                (byte)(enableReadBit ? 1 : 0)
            };
            SendCommand(cmd);
        }

        /// <summary>
        /// Write a list of bytes to the SPI device.
        /// </summary>
        /// <param name="bytesToWrite">The bytes to write to the SPI bus.</param>
        public void SpiWriteBlocking(byte[] bytesToWrite)
        {
            if (!_spiEnabled)
                Throw(new InvalidOperationException("SpiWriteBlocking: SPI interface is not enabled."));

            if (bytesToWrite is null)
                Throw(new ArgumentNullException(nameof(bytesToWrite)));

            // command: [SPI_WRITE_BLOCKING, length, data...]
            var command = new byte[2 + bytesToWrite!.Length];
            command[0] = PrivateConstants.SPI_WRITE_BLOCKING;
            command[1] = (byte)bytesToWrite.Length;
            Array.Copy(bytesToWrite, 0, command, 2, bytesToWrite.Length);

            SendCommand(command);
        }

        /// <summary>
        /// Configure how the SPI serializes and deserializes data on the wire.
        /// See Arduino SPI reference materials for details.
        /// </summary>
        /// <param name="clockDivisor">SPI clock divisor.</param>
        /// <param name="bitOrder">
        ///   0 = LSBFIRST, 1 = MSBFIRST (default).
        ///   Use <see cref="PrivateConstants.SPI_LSBFIRST"/> and
        ///   <see cref="PrivateConstants.SPI_MSBFIRST"/>.
        /// </param>
        /// <param name="dataMode">
        ///   SPI_MODE0 (0x00, default), SPI_MODE1 (0x04),
        ///   SPI_MODE2 (0x08), SPI_MODE3 (0x0C).
        /// </param>
        public void SpiSetFormat(int clockDivisor, int bitOrder, int dataMode)
        {
            if (!_spiEnabled)
                Throw(new InvalidOperationException("SpiSetFormat: SPI interface is not enabled."));

            byte[] cmd =
            {
                PrivateConstants.SPI_SET_FORMAT,
                (byte)clockDivisor,
                (byte)bitOrder,
                (byte)dataMode
            };
            SendCommand(cmd);
        }

        /// <summary>
        /// Control an SPI chip-select line.
        /// </summary>
        /// <param name="chipSelectPin">Pin connected to the chip-select line.</param>
        /// <param name="select">0 = select (LOW), 1 = deselect (HIGH).</param>
        public void SpiCsControl(int chipSelectPin, int select)
        {
            if (!_spiEnabled)
                Throw(new InvalidOperationException("SpiCsControl: SPI interface is not enabled."));

            if (!_csPinsEnabled.Contains(chipSelectPin))
                Throw(new InvalidOperationException(
                    $"SpiCsControl: Chip select pin {chipSelectPin} was never enabled."));

            byte[] cmd =
            {
                PrivateConstants.SPI_CS_CONTROL,
                (byte)chipSelectPin,
                (byte)select
            };
            SendCommand(cmd);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Digital / Analog write (unchanged)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Write a digital HIGH (1) or LOW (0) to a pin.</summary>
        public void DigitalWrite(int pin, int value) =>
            SendCommand(new byte[] { PrivateConstants.DIGITAL_WRITE, (byte)pin, (byte)value });

        /// <summary>Write a PWM value to a pin (analog output).</summary>
        public void AnalogWrite(int pin, int value)
        {
            byte msb = (byte)(value >> 8);
            byte lsb = (byte)(value & 0xFF);
            SendCommand(new byte[] { PrivateConstants.ANALOG_WRITE, (byte)pin, msb, lsb });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Reporting control (unchanged)
        // ══════════════════════════════════════════════════════════════════════

        public void DisableAllReporting() =>
            SendCommand(new byte[]
            { PrivateConstants.MODIFY_REPORTING, PrivateConstants.REPORTING_DISABLE_ALL, 0 });

        public void EnableAnalogReporting(int pin) =>
            SendCommand(new byte[]
            { PrivateConstants.MODIFY_REPORTING, PrivateConstants.REPORTING_ANALOG_ENABLE, (byte)pin });

        public void DisableAnalogReporting(int pin) =>
            SendCommand(new byte[]
            { PrivateConstants.MODIFY_REPORTING, PrivateConstants.REPORTING_ANALOG_DISABLE, (byte)pin });

        public void EnableDigitalReporting(int pin) =>
            SendCommand(new byte[]
            { PrivateConstants.MODIFY_REPORTING, PrivateConstants.REPORTING_DIGITAL_ENABLE, (byte)pin });

        public void DisableDigitalReporting(int pin) =>
            SendCommand(new byte[]
            { PrivateConstants.MODIFY_REPORTING, PrivateConstants.REPORTING_DIGITAL_DISABLE, (byte)pin });

        public void SetAnalogScanInterval(int intervalMs)
        {
            if (intervalMs < 0 || intervalMs > 255)
                Throw(new ArgumentOutOfRangeException(nameof(intervalMs), "Interval must be 0–255."));

            SendCommand(new byte[] { PrivateConstants.SET_ANALOG_SCANNING_INTERVAL, (byte)intervalMs });
        }

        // ── Firmware info ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the firmware version reported by the Arduino as [major, minor, patch],
        /// or null if not yet received.
        /// </summary>
        public IReadOnlyList<int>? FirmwareVersion => _firmwareVersion;

        /// <summary>
        /// Returns the feature bitmask reported by the Arduino.
        /// </summary>
        public int ReportedFeatures => _reportedFeatures;

        // ══════════════════════════════════════════════════════════════════════
        //  Shutdown / Dispose
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Perform an orderly shutdown: stop reporting, join threads, close the serial port.
        /// Safe to call multiple times.
        /// </summary>
        public void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

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

        // ══════════════════════════════════════════════════════════════════════
        //  Private: connection helpers
        // ══════════════════════════════════════════════════════════════════════

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
                    var sp = new SerialPort(portName, 115200)
                    { ReadTimeout = 1000, WriteTimeout = 1000 };
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

                int retries = 50;
                while (_reportedArduinoId is null && retries-- > 0)
                    Thread.Sleep(200);

                if (_reportedArduinoId == _arduinoInstanceId)
                {
                    Console.WriteLine($"Valid Arduino found on {sp.PortName}");
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();
                    foreach (var other in candidates.Where(c => c != sp))
                    { try { other.Close(); other.Dispose(); } catch { } }
                    return;
                }
            }

            foreach (var sp in candidates)
            { try { sp.Close(); sp.Dispose(); } catch { } }
            Throw(new InvalidOperationException(
                $"No Arduino with instance ID {_arduinoInstanceId} found."));
        }

        private void ManualOpen(string comPort)
        {
            Console.WriteLine($"Opening {comPort}…");
            _serialPort = new SerialPort(comPort, 115200)
            { ReadTimeout = 1000, WriteTimeout = 1000 };
            _serialPort.Open();

            Console.WriteLine($"Waiting {_arduinoWaitSeconds}s for Arduino to reset…");
            Thread.Sleep(_arduinoWaitSeconds * 1000);

            _reportedArduinoId = null;
            GetArduinoId();

            int retries = 50;
            while (_reportedArduinoId is null && retries-- > 0)
                Thread.Sleep(200);

            if (_reportedArduinoId != _arduinoInstanceId)
                Throw(new InvalidOperationException(
                    $"Incorrect Arduino ID: {_reportedArduinoId} (expected {_arduinoInstanceId})."));

            Console.WriteLine($"Arduino confirmed on {comPort}");

            GetFirmwareVersion();
            Thread.Sleep(500);

            if (_firmwareVersion is null)
                Throw(new InvalidOperationException("Could not retrieve firmware version."));

            Console.WriteLine($"Firmware: {string.Join(".", _firmwareVersion)}");
        }

        private void GetArduinoId() => SendCommand(new[] { PrivateConstants.ARE_U_THERE });
        private void GetFirmwareVersion() => SendCommand(new[] { PrivateConstants.GET_FIRMWARE_VERSION });

        // ── Private: threading ───────────────────────────────────────────────

        private void StartBackgroundThreads()
        {
            _running = true;

            _receiverThread = new Thread(SerialReceiverLoop)
            { IsBackground = true, Name = "Telemetrix-Receiver" };

            _reporterThread = new Thread(ReporterLoop)
            { IsBackground = true, Name = "Telemetrix-Reporter" };

            _receiverThread.Start();
            _reporterThread.Start();
        }

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
                    if (_shutdownOnException) Shutdown();
                    throw new InvalidOperationException(
                        "Received a zero-length packet from Arduino.");
                }

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
                        Console.Error.WriteLine(
                            $"[Telemetrix] Report handler {reportType} threw: {ex.Message}");
                    }
                }
            }
        }

        // ── Private: dispatch table ──────────────────────────────────────────

        private void BuildDispatchTable()
        {
            _reportDispatch[PrivateConstants.DIGITAL_REPORT] = HandleDigitalMessage;
            _reportDispatch[PrivateConstants.ANALOG_REPORT] = HandleAnalogMessage;
            _reportDispatch[PrivateConstants.FIRMWARE_REPORT] = HandleFirmwareMessage;
            _reportDispatch[PrivateConstants.I_AM_HERE_REPORT] = HandleIAmHere;
            _reportDispatch[PrivateConstants.FEATURES] = HandleFeatures;
            _reportDispatch[PrivateConstants.DEBUG_PRINT] = HandleDebugPrint;
            _reportDispatch[PrivateConstants.I2C_READ_REPORT] = HandleI2CReadReport;
            _reportDispatch[PrivateConstants.I2C_TOO_FEW_BYTES_RCVD] = HandleI2CTooFew;
            _reportDispatch[PrivateConstants.I2C_TOO_MANY_BYTES_RCVD] = HandleI2CTooMany;
            _reportDispatch[PrivateConstants.SPI_REPORT] = HandleSpiReport;
        }

        // ── Private: report handlers ─────────────────────────────────────────

        private void HandleDigitalMessage(List<byte> data)
        {
            int pin = data[0];
            int value = data[1];
            if (_digitalCallbacks.TryGetValue(pin, out var cb))
                cb(new DigitalInputData(pin, value));
        }

        private void HandleAnalogMessage(List<byte> data)
        {
            int pin = data[0];
            int value = (data[1] << 8) | data[2];
            if (_analogCallbacks.TryGetValue(pin, out var cb))
                cb(new AnalogInputData(pin, value));
        }

        private void HandleFirmwareMessage(List<byte> data)
        {
            _firmwareVersion = new[] { data[0], data[1], data.Count > 2 ? data[2] : 0 };
            Console.WriteLine($"Firmware version: {string.Join(".", _firmwareVersion)}");
        }

        private void HandleIAmHere(List<byte> data)
        {
            _reportedArduinoId = data[0];
        }

        private void HandleFeatures(List<byte> data)
        {
            _reportedFeatures = data[0];
        }

        private static void HandleDebugPrint(List<byte> data)
        {
            if (data.Count >= 3)
            {
                int id = data[0];
                int value = (data[1] << 8) | data[2];
                Console.WriteLine($"[Arduino DEBUG] ID={id}  Value={value}");
            }
        }

        /// <summary>
        /// Handle incoming I2C read report.
        /// Protocol data: [i2c_port, number_of_bytes_returned, address, register, data_bytes...]
        /// Matches the Python: cb_list = [I2C_READ_REPORT, data[0], data[1]] + data[2:]
        /// </summary>
        private void HandleI2CReadReport(List<byte> data)
        {
            // data[0] = i2c_port
            // data[1] = number of bytes returned
            // data[2] = address
            // data[3] = register
            // data[4..] = actual data bytes
            if (data.Count < 4) return;

            int i2cPort = data[0];
            int byteCount = data[1];
            int address = data[2];
            int register = data[3];

            byte[] readData;
            if (data.Count > 4)
            {
                readData = new byte[data.Count - 4];
                for (int i = 4; i < data.Count; i++)
                    readData[i - 4] = data[i];
            }
            else
            {
                readData = Array.Empty<byte>();
            }

            var report = new I2CReadData(i2cPort, byteCount, address, register, readData);

            if (i2cPort != 0)
                _i2cCallback2?.Invoke(report);
            else
                _i2cCallback?.Invoke(report);
        }

        private void HandleI2CTooFew(List<byte> data)
        {
            int port = data.Count > 0 ? data[0] : -1;
            int address = data.Count > 1 ? data[1] : -1;

            if (_shutdownOnException) Shutdown();
            throw new InvalidOperationException(
                $"I2C too few bytes received from I2C port {port}, address {address}.");
        }

        private void HandleI2CTooMany(List<byte> data)
        {
            int port = data.Count > 0 ? data[0] : -1;
            int address = data.Count > 1 ? data[1] : -1;

            if (_shutdownOnException) Shutdown();
            throw new InvalidOperationException(
                $"I2C too many bytes received from I2C port {port}, address {address}.");
        }

        /// <summary>
        /// Handle incoming SPI read report.
        /// Protocol data: [number_of_bytes, data_bytes...]
        /// </summary>
        private void HandleSpiReport(List<byte> data)
        {
            if (data.Count < 1) return;

            int byteCount = data[0];

            byte[] readData;
            if (data.Count > 1)
            {
                readData = new byte[data.Count - 1];
                for (int i = 1; i < data.Count; i++)
                    readData[i - 1] = data[i];
            }
            else
            {
                readData = Array.Empty<byte>();
            }

            _spiCallback?.Invoke(new SpiReadData(byteCount, readData));
        }

        // ── Private: helpers ─────────────────────────────────────────────────

        private void SetPinMode(int pin, byte mode)
        {
            byte[] cmd = mode switch
            {
                PrivateConstants.AT_INPUT =>
                    new byte[] { PrivateConstants.SET_PIN_MODE, (byte)pin,
                                 PrivateConstants.AT_INPUT, 1 },
                PrivateConstants.AT_INPUT_PULLUP =>
                    new byte[] { PrivateConstants.SET_PIN_MODE, (byte)pin,
                                 PrivateConstants.AT_INPUT_PULLUP, 1 },
                PrivateConstants.AT_OUTPUT =>
                    new byte[] { PrivateConstants.SET_PIN_MODE, (byte)pin,
                                 PrivateConstants.AT_OUTPUT },
                _ => throw new ArgumentException($"Unknown pin mode: {mode}")
            };
            SendCommand(cmd);
        }

        /// <summary>
        /// Prepend the length byte and write the command to the serial port.
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

        private void Throw(Exception ex)
        {
            if (_shutdownOnException) Shutdown();
            throw ex;
        }
    }
}