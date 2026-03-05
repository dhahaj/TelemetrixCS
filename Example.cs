// ── TelemetrixCS Usage Example ────────────────────────────────────────────────
//
// This file demonstrates the most common use-cases for the TelemetrixCS library,
// including digital I/O, analog I/O, I2C, and SPI.
//
// Hardware assumed in this example:
//   • LED on digital pin 13 (built-in on most Arduinos)
//   • Push-button on digital pin 2 (pulled-up, active-LOW)
//   • Potentiometer on analog pin A0
//   • PWM-capable LED on digital pin 9
//   • I2C device (e.g. PCF8574 I/O expander) on the default I2C bus
//   • SPI device with chip-select on pin 10
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Threading;
using TelemetrixCS;

// ── 1. Connect to the Arduino ─────────────────────────────────────────────────
using var board = new Telemetrix();
Console.WriteLine("Connected!\n");

// ── 2. Digital output – blink the built-in LED ───────────────────────────────

board.SetPinModeDigitalOutput(pin: 13);

Console.WriteLine("Blinking LED on pin 13 for 3 seconds…");
for (int i = 0; i < 6; i++)
{
    board.DigitalWrite(pin: 13, value: i % 2);
    Thread.Sleep(500);
}
board.DigitalWrite(13, 0);

// ── 3. Digital input with pull-up – read a button ────────────────────────────

board.SetPinModeDigitalInputPullup(pin: 2, callback: OnButtonChange);

Console.WriteLine("Monitoring button on pin 2 for 5 seconds…");
Thread.Sleep(5000);

void OnButtonChange(DigitalInputData d)
{
    string state = d.Value == 0 ? "PRESSED" : "released";
    Console.WriteLine($"  Button pin {d.Pin}: {state}  (at {d.Timestamp:HH:mm:ss.fff})");
}

// ── 4. Analog input – read a potentiometer ───────────────────────────────────

board.SetPinModeAnalogInput(pin: 0, differential: 10, callback: OnPotChange);

Console.WriteLine("Reading potentiometer on A0 for 5 seconds…");
Thread.Sleep(5000);

void OnPotChange(AnalogInputData d)
{
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

// ══════════════════════════════════════════════════════════════════════════════
//  6. I2C – Communicate with an I2C device
// ══════════════════════════════════════════════════════════════════════════════
//
// Example: reading 2 bytes from register 0x00 of a device at address 0x20
// (e.g. a PCF8574 I/O expander or any other I2C peripheral).

Console.WriteLine("\n── I2C Example ──");

// Step 1: Initialize the I2C bus (must be called before any I2C operation)
board.SetPinModeI2C(i2cPort: 0);

// Step 2: Write data to the I2C device
//   For example, write a single config byte (0x01) to address 0x20:
board.I2CWrite(address: 0x20, data: new byte[] { 0x01 }, i2cPort: 0);

// Step 3: Read from the I2C device with a callback
board.I2CRead(
    address: 0x20,
    register: 0x00,
    numberOfBytes: 2,
    callback: OnI2CRead,
    i2cPort: 0,
    writeRegister: true
);

Console.WriteLine("Waiting for I2C read reply…");
Thread.Sleep(2000);

void OnI2CRead(I2CReadData d)
{
    Console.Write($"  I2C port {d.I2CPort}, addr 0x{d.Address:X2}, " +
                  $"reg 0x{d.Register:X2}, {d.ByteCount} bytes: ");
    foreach (byte b in d.Data)
        Console.Write($"0x{b:X2} ");
    Console.WriteLine($" (at {d.Timestamp:HH:mm:ss.fff})");
}

// For devices that need a restart transmission after read (e.g. MMA8452Q):
// board.I2CReadRestartTransmission(
//     address: 0x1D, register: 0x01, numberOfBytes: 6,
//     callback: OnI2CRead, i2cPort: 0, writeRegister: true);

// If your board has a secondary I2C bus, initialize it with:
// board.SetPinModeI2C(i2cPort: 1);
// Then pass i2cPort: 1 to I2CRead / I2CWrite.

// ══════════════════════════════════════════════════════════════════════════════
//  7. SPI – Communicate with an SPI device
// ══════════════════════════════════════════════════════════════════════════════
//
// Example: reading from an SPI sensor with chip-select on pin 10.

Console.WriteLine("\n── SPI Example ──");

// Step 1: Initialize SPI with chip-select pin(s)
board.SetPinModeSpi(chipSelectPins: new[] { 10 });

// Step 2 (optional): Configure SPI format
//   clockDivisor=4, MSBFIRST, SPI_MODE0
board.SpiSetFormat(clockDivisor: 4, bitOrder: 1, dataMode: 0x00);

// Step 3: Select the device (drive CS LOW)
board.SpiCsControl(chipSelectPin: 10, select: 0);

// Step 4: Read 4 bytes from register 0x00
board.SpiReadBlocking(
    registerSelection: 0x00,
    numberOfBytesToRead: 4,
    callback: OnSpiRead,
    enableReadBit: true
);

Console.WriteLine("Waiting for SPI read reply…");
Thread.Sleep(2000);

// Step 5: Write bytes to SPI
board.SpiWriteBlocking(bytesToWrite: new byte[] { 0x20, 0xFF });

// Step 6: Deselect the device (drive CS HIGH)
board.SpiCsControl(chipSelectPin: 10, select: 1);

void OnSpiRead(SpiReadData d)
{
    Console.Write($"  SPI read {d.ByteCount} bytes: ");
    foreach (byte b in d.Data)
        Console.Write($"0x{b:X2} ");
    Console.WriteLine($" (at {d.Timestamp:HH:mm:ss.fff})");
}

// ── 8. Shutdown ───────────────────────────────────────────────────────────────

Console.WriteLine("\nDemo complete – shutting down.");