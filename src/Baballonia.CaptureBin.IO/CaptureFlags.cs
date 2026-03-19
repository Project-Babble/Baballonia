namespace Baballonia.CaptureBin.IO;

/// <summary>
/// Bit flags used in <see cref="CaptureFrameHeader.RoutineState"/> to mark routine state and metadata.
/// Keep in sync with the native definitions in capture_data.h.
/// </summary>
public static class CaptureFlags
{
    public const uint FLAG_ROUTINE_BIT1 = 1U << 0;
    public const uint FLAG_ROUTINE_2    = 1U << 1;
    public const uint FLAG_ROUTINE_3    = 1U << 2;
    public const uint FLAG_ROUTINE_4    = 1U << 3;
    public const uint FLAG_ROUTINE_5    = 1U << 4;
    public const uint FLAG_ROUTINE_6    = 1U << 5;
    public const uint FLAG_ROUTINE_7    = 1U << 6;
    public const uint FLAG_ROUTINE_8    = 1U << 7;
    public const uint FLAG_ROUTINE_9    = 1U << 8;
    public const uint FLAG_ROUTINE_10   = 1U << 9;
    public const uint FLAG_ROUTINE_11   = 1U << 10;
    public const uint FLAG_ROUTINE_12   = 1U << 11;
    public const uint FLAG_ROUTINE_13   = 1U << 12;
    public const uint FLAG_ROUTINE_14   = 1U << 13;
    public const uint FLAG_ROUTINE_15   = 1U << 14;
    public const uint FLAG_ROUTINE_16   = 1U << 15;
    public const uint FLAG_ROUTINE_17   = 1U << 16;
    public const uint FLAG_ROUTINE_18   = 1U << 17;
    public const uint FLAG_ROUTINE_19   = 1U << 18;
    public const uint FLAG_ROUTINE_20   = 1U << 19;
    public const uint FLAG_VERSION_BIT1 = 1U << 20;
    public const uint FLAG_VERSION_BIT2 = 1U << 21;
    public const uint FLAG_VERSION_BIT3 = 1U << 22;
    public const uint FLAG_VERSION_BIT4 = 1U << 23;

    public const uint FLAG_CONVERGENCE = 1u << 24;
    public const uint FLAG_IN_MOVEMENT = 1u << 25;
    public const uint FLAG_RESTING = 1u << 26; // Unused right now, was used to denote tutorial sections
    public const uint FLAG_DILATION_BLACK = 1u << 27;   // Black screen for full dilation
    public const uint FLAG_DILATION_WHITE = 1u << 28;   // White screen for full constriction
    public const uint FLAG_DILATION_GRADIENT = 1u << 29; // Gradient fade from white to black

    public const uint FLAG_GOOD_DATA = 1u << 30;

    public const uint FLAG_ROUTINE_COMPLETE = 1u << 31;
}
