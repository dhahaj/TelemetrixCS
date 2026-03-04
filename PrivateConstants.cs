// TelemetrixCS - C# port of the Telemetrix Python library
// Copyright (c) 2025 - Based on original work by Alan Yoriks
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

        public const byte LOOP_COMMAND              = 0;
        public const byte SET_PIN_MODE              = 1;
        public const byte DIGITAL_WRITE             = 2;
        public const byte ANALOG_WRITE              = 3;
        public const byte MODIFY_REPORTING          = 4;
        public const byte GET_FIRMWARE_VERSION      = 5;
        public const byte ARE_U_THERE               = 6;
        public const byte STOP_ALL_REPORTS          = 15;
        public const byte SET_ANALOG_SCANNING_INTERVAL = 16;
        public const byte ENABLE_ALL_REPORTS        = 17;
        public const byte RESET                     = 18;
        public const byte GET_FEATURES              = 54;

        // ── Reports (received FROM Arduino) ─────────────────────────────────
        // Note: report IDs intentionally alias command IDs (matching the protocol)

        public const byte DIGITAL_REPORT    = DIGITAL_WRITE;        // 2
        public const byte ANALOG_REPORT     = ANALOG_WRITE;         // 3
        public const byte FIRMWARE_REPORT   = GET_FIRMWARE_VERSION; // 5
        public const byte I_AM_HERE_REPORT  = ARE_U_THERE;          // 6
        public const byte FEATURES          = 20;
        public const byte DEBUG_PRINT       = 99;

        // ── Reporting control values ─────────────────────────────────────────

        public const byte REPORTING_DISABLE_ALL      = 0;
        public const byte REPORTING_ANALOG_ENABLE    = 1;
        public const byte REPORTING_DIGITAL_ENABLE   = 2;
        public const byte REPORTING_ANALOG_DISABLE   = 3;
        public const byte REPORTING_DIGITAL_DISABLE  = 4;

        // ── Pin mode definitions ─────────────────────────────────────────────

        public const byte AT_INPUT          = 0;
        public const byte AT_OUTPUT         = 1;
        public const byte AT_INPUT_PULLUP   = 2;
        public const byte AT_ANALOG         = 3;
        public const byte AT_MODE_NOT_SET   = 255;

        // ── Limits ───────────────────────────────────────────────────────────

        public const int NUMBER_OF_DIGITAL_PINS = 100;
        public const int NUMBER_OF_ANALOG_PINS  = 20;

        // ── Library version ──────────────────────────────────────────────────

        public const string TELEMETRIX_VERSION = "1.0.0-cs";
    }
}
