// ── TelemetrixCS Usage Example ────────────────────────────────────────────────
//
// This file demonstrates the most common use-cases for the TelemetrixCS library.
// Copy the TelemetrixCS project files into your solution, or reference the DLL,
// then adapt the pin numbers for your own circuit.
//
// Hardware assumed in this example:
//   • LED on digital pin 13 (built-in on most Arduinos)
//   • Push-button on digital pin 2 (pulled-up, active-LOW)
//   • Potentiometer on analog pin A0
//   • PWM-capable LED on digital pin 9
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Threading;
using TelemetrixCS;

// ── 1. Connect to the Arduino ─────────────────────────────────────────────────
//
// Option A – auto-detect the Arduino on any COM port:
using var board = new Telemetrix();
//
// Option B – specify a port explicitly (Windows: "COM3", Linux: "/dev/ttyACM0"):
// using var board = new Telemetrix(comPort: "COM3");
//
// Constructor parameters (all optional):
//   comPort             – explicit port name, null = auto-detect
//   arduinoInstanceId   – must match ARDUINO_ID in sketch (default 1)
//   arduinoWaitSeconds  – reset wait time in seconds (default 4)
//   shutdownOnException – call Shutdown() before rethrowing (default true)

Console.WriteLine("Connected!\n");

// ── 2. Digital output – blink the built-in LED ───────────────────────────────

board.SetPinModeDigitalOutput(pin: 13);

Console.WriteLine("Blinking LED on pin 13 for 3 seconds…");
for (int i = 0; i < 6; i++)
{
    board.DigitalWrite(pin: 13, value: i % 2);   // 1 = HIGH, 0 = LOW
    Thread.Sleep(500);
}
board.DigitalWrite(13, 0);   // leave LED off

// ── 3. Digital input with pull-up – read a button ────────────────────────────
//
// The callback fires on every change, on a background thread.
// Use a ManualResetEventSlim (or similar) if you need to await a value.

board.SetPinModeDigitalInputPullup(pin: 2, callback: OnButtonChange);

Console.WriteLine("Monitoring button on pin 2 (press Ctrl+C to exit early)…");
Thread.Sleep(5000);   // listen for 5 seconds

void OnButtonChange(DigitalInputData d)
{
    // Value = 0 → button pressed (active-LOW with pull-up)
    // Value = 1 → button released
    string state = d.Value == 0 ? "PRESSED" : "released";
    Console.WriteLine($"  Button pin {d.Pin}: {state}  (at {d.Timestamp:HH:mm:ss.fff})");
}

// ── 4. Analog input – read a potentiometer ───────────────────────────────────
//
// differential=10 means: only report when the reading changes by ≥ 10 counts.
// Use differential=0 to receive every sample (higher serial traffic).

board.SetPinModeAnalogInput(pin: 0, differential: 10, callback: OnPotChange);

Console.WriteLine("Reading potentiometer on A0 for 5 seconds…");
Thread.Sleep(5000);

void OnPotChange(AnalogInputData d)
{
    // Value range: 0–1023 (10-bit ADC)
    double volts = d.Value * (5.0 / 1023.0);
    Console.WriteLine($"  A{d.Pin} = {d.Value,4}  ({volts:F2} V)");
}

// ── 5. Analog output (PWM) – fade an LED ─────────────────────────────────────

board.SetPinModeAnalogOutput(pin: 9);

Console.WriteLine("Fading PWM LED on pin 9…");
for (int brightness = 0; brightness <= 255; brightness += 5)
{
    board.AnalogWrite(pin: 9, value: brightness);
    Thread.Sleep(20);
}
for (int brightness = 255; brightness >= 0; brightness -= 5)
{
    board.AnalogWrite(pin: 9, value: brightness);
    Thread.Sleep(20);
}
board.AnalogWrite(9, 0);

// ── 6. Reporting control helpers ─────────────────────────────────────────────

// Temporarily stop all pin reports (useful while reconfiguring):
// board.DisableAllReporting();

// Re-enable a single analog pin:
// board.EnableAnalogReporting(pin: 0);

// Slow down analog scanning to reduce Arduino CPU load:
// board.SetAnalogScanInterval(intervalMs: 50);   // 0–255 ms

// ── 7. Shutdown ───────────────────────────────────────────────────────────────
//
// The 'using' declaration above calls Shutdown() + Dispose() automatically
// when the block exits. You can also call it explicitly:
//
//   board.Shutdown();
//
// Shutdown() sends STOP_ALL_REPORTS, drains the threads, and closes the port.

Console.WriteLine("\nDemo complete – shutting down.");
// (Shutdown called implicitly by 'using')
