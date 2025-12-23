namespace Baballonia.CaptureBin.IO;

/// <summary>
/// Bit flags used in <see cref="CaptureFrameHeader.RoutineState"/> to mark routine state and metadata.
/// Keep in sync with the native definitions in capture_data.h.
/// </summary>
public static class CaptureFlags
{
    public const uint FLAG_CONVERGENCE = 1u << 24;
    public const uint FLAG_IN_MOVEMENT = 1u << 25;
    public const uint FLAG_RESTING = 1u << 26;
    public const uint FLAG_DILATION_BLACK = 1u << 27;   // Black screen for full dilation
    public const uint FLAG_DILATION_WHITE = 1u << 28;   // White screen for full constriction
    public const uint FLAG_DILATION_GRADIENT = 1u << 29; // Gradient fade from white to black

    public const uint FLAG_GOOD_DATA = 1u << 30;

    public const uint FLAG_ROUTINE_COMPLETE = 1u << 31;
}
