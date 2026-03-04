# TelemetrixCS

A C# client library for communicating with Arduino boards running the [Telemetrix4Arduino](https://github.com/MrYsLab/Telemetrix4Arduino) firmware over a serial connection.

Based on the original Python [Telemetrix](https://github.com/MrYsLab/telemetrix) library by Alan Yorinks.

## Features

- Auto COM-port detection or manual port specification
- Digital output (`SetPinModeDigitalOutput` / `DigitalWrite`)
- Digital input with callbacks (`SetPinModeDigitalInput` / `SetPinModeDigitalInputPullup`)
- Analog output / PWM (`SetPinModeAnalogOutput` / `AnalogWrite`)
- Analog input with callbacks and configurable differential (`SetPinModeAnalogInput`)
- Per-pin and global reporting control
- Analog scan interval tuning
- Clean shutdown via `IDisposable`

## Installation

```
dotnet add package TelemetrixCS
```

## Quick Start

```csharp
using TelemetrixCS;

// Auto-detect Arduino on any COM port
using var board = new Telemetrix();

// Blink the built-in LED
board.SetPinModeDigitalOutput(pin: 13);
board.DigitalWrite(pin: 13, value: 1);
Thread.Sleep(1000);
board.DigitalWrite(pin: 13, value: 0);

// Read a button with a callback
board.SetPinModeDigitalInputPullup(pin: 2, callback: data =>
{
    Console.WriteLine($"Pin {data.Pin} = {data.Value}");
});

// Read a potentiometer on A0
board.SetPinModeAnalogInput(pin: 0, differential: 10, callback: data =>
{
    Console.WriteLine($"A{data.Pin} = {data.Value}");
});

Thread.Sleep(5000);
// Shutdown is called automatically by 'using'
```

## Requirements

- .NET 6.0 or later
- An Arduino flashed with [Telemetrix4Arduino](https://github.com/MrYsLab/Telemetrix4Arduino)

## License

GNU AGPL Version 3 — see [LICENSE](LICENSE) for details.
