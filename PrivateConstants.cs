// TelemetrixCS - C# port of the Telemetrix Python library
// Copyright (c) 2025 - Based on original work by Alan Yorinks
// Licensed under GNU AGPL Version 3

namespace TelemetrixCS
{
    /// <summary>
    /// Internal constants for the Telemetrix protocol.
    /// Mirrors the Python PrivateConstants class.
    /// </summary>
    internal static class PrivateConstants
    {
        // ── Commands (sent TO Arduino) ───────────────────────────────────────

        public const byte LOOP_COMMAND = 0;
        public const byte SET_PIN_MODE = 1;
        public const byte DIGITAL_WRITE = 2;
        public const byte ANALOG_WRITE = 3;
        public const byte MODIFY_REPORTING = 4;
        public const byte GET_FIRMWARE_VERSION = 5;
        public const byte ARE_U_THERE = 6;
        public const byte SERVO_ATTACH = 7;
        public const byte SERVO_WRITE = 8;
        public const byte SERVO_DETACH = 9;
        public const byte I2C_BEGIN = 10;
        public const byte I2C_READ = 11;
        public const byte I2C_WRITE = 12;
        public const byte SONAR_NEW = 13;
        public const byte DHT_NEW = 14;
        public const byte STOP_ALL_REPORTS = 15;
        public const byte SET_ANALOG_SCANNING_INTERVAL = 16;
        public const byte ENABLE_ALL_REPORTS = 17;
        public const byte RESET = 18;
        public const byte SPI_INIT = 19;
        public const byte SPI_WRITE_BLOCKING = 20;
        public const byte SPI_READ_BLOCKING = 21;
        public const byte SPI_SET_FORMAT = 22;
        public const byte SPI_CS_CONTROL = 23;
        public const byte ONE_WIRE_INIT = 24;
        public const byte ONE_WIRE_RESET = 25;
        public const byte ONE_WIRE_SELECT = 26;
        public const byte ONE_WIRE_SKIP = 27;
        public const byte ONE_WIRE_WRITE = 28;
        public const byte ONE_WIRE_READ = 29;
        public const byte ONE_WIRE_RESET_SEARCH = 30;
        public const byte ONE_WIRE_SEARCH = 31;
        public const byte ONE_WIRE_CRC8 = 32;
        public const byte SET_PIN_MODE_STEPPER = 33;
        public const byte STEPPER_MOVE_TO = 34;
        public const byte STEPPER_MOVE = 35;
        public const byte STEPPER_RUN = 36;
        public const byte STEPPER_RUN_SPEED = 37;
        public const byte STEPPER_SET_MAX_SPEED = 38;
        public const byte STEPPER_SET_ACCELERATION = 39;
        public const byte STEPPER_SET_SPEED = 40;
        public const byte STEPPER_SET_CURRENT_POSITION = 41;
        public const byte STEPPER_RUN_SPEED_TO_POSITION = 42;
        public const byte STEPPER_STOP = 43;
        public const byte STEPPER_DISABLE_OUTPUTS = 44;
        public const byte STEPPER_ENABLE_OUTPUTS = 45;
        public const byte STEPPER_SET_MINIMUM_PULSE_WIDTH = 46;
        public const byte STEPPER_SET_ENABLE_PIN = 47;
        public const byte STEPPER_SET_3_PINS_INVERTED = 48;
        public const byte STEPPER_SET_4_PINS_INVERTED = 49;
        public const byte STEPPER_IS_RUNNING = 50;
        public const byte STEPPER_GET_CURRENT_POSITION = 51;
        public const byte STEPPER_GET_DISTANCE_TO_GO = 52;
        public const byte STEPPER_GET_TARGET_POSITION = 53;
        public const byte GET_FEATURES = 54;
        public const byte SONAR_DISABLE = 55;
        public const byte SONAR_ENABLE = 56;

        // ── Reports (received FROM Arduino) ─────────────────────────────────
        // Note: report IDs intentionally alias command IDs (matching the protocol)

        public const byte DIGITAL_REPORT = DIGITAL_WRITE;        // 2
        public const byte ANALOG_REPORT = ANALOG_WRITE;         // 3
        public const byte FIRMWARE_REPORT = GET_FIRMWARE_VERSION;  // 5
        public const byte I_AM_HERE_REPORT = ARE_U_THERE;          // 6
        public const byte SERVO_UNAVAILABLE = SERVO_ATTACH;         // 7
        public const byte I2C_TOO_FEW_BYTES_RCVD = 8;
        public const byte I2C_TOO_MANY_BYTES_RCVD = 9;
        public const byte I2C_READ_REPORT = 10;
        public const byte SONAR_DISTANCE = 11;
        public const byte DHT_REPORT = 12;
        public const byte SPI_REPORT = 13;
        public const byte ONE_WIRE_REPORT = 14;
        public const byte STEPPER_DISTANCE_TO_GO = 15;
        public const byte STEPPER_TARGET_POSITION = 16;
        public const byte STEPPER_CURRENT_POSITION = 17;
        public const byte STEPPER_RUNNING_REPORT = 18;
        public const byte STEPPER_RUN_COMPLETE_REPORT = 19;
        public const byte FEATURES = 20;
        public const byte DEBUG_PRINT = 99;

        // ── Library version ──────────────────────────────────────────────────

        public const string TELEMETRIX_VERSION = "1.1.0-cs";

        // ── Reporting control values ─────────────────────────────────────────

        public const byte REPORTING_DISABLE_ALL = 0;
        public const byte REPORTING_ANALOG_ENABLE = 1;
        public const byte REPORTING_DIGITAL_ENABLE = 2;
        public const byte REPORTING_ANALOG_DISABLE = 3;
        public const byte REPORTING_DIGITAL_DISABLE = 4;

        // ── Pin mode definitions ─────────────────────────────────────────────

        public const byte AT_INPUT = 0;
        public const byte AT_OUTPUT = 1;
        public const byte AT_INPUT_PULLUP = 2;
        public const byte AT_ANALOG = 3;
        public const byte AT_SERVO = 4;
        public const byte AT_SONAR = 5;
        public const byte AT_DHT = 6;
        public const byte AT_MODE_NOT_SET = 255;

        // ── Limits ───────────────────────────────────────────────────────────

        public const int NUMBER_OF_DIGITAL_PINS = 100;
        public const int NUMBER_OF_ANALOG_PINS = 20;
        public const int MAX_SONARS = 6;
        public const int MAX_DHTS = 6;

        // ── DHT report sub-types ─────────────────────────────────────────────

        public const byte DHT_DATA = 0;
        public const byte DHT_ERROR = 1;

        // ── Feature masks ────────────────────────────────────────────────────

        public const byte ONEWIRE_FEATURE = 0x01;
        public const byte DHT_FEATURE = 0x02;
        public const byte STEPPERS_FEATURE = 0x04;
        public const byte SPI_FEATURE = 0x08;
        public const byte SERVO_FEATURE = 0x10;
        public const byte SONAR_FEATURE = 0x20;

        // ── SPI constants ────────────────────────────────────────────────────

        public const byte SPI_LSBFIRST = 0;
        public const byte SPI_MSBFIRST = 1;

        public const byte SPI_MODE0 = 0x00;
        public const byte SPI_MODE1 = 0x04;
        public const byte SPI_MODE2 = 0x08;
        public const byte SPI_MODE3 = 0x0C;
    }
}