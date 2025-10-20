using Avalonia;

namespace Baballonia.Services.Inference.Models;

public record RegionOfInterest
{
    public int X { get; init; } = 0;
    public int Y { get; init; } = 0;
    public int Width { get; init; } = 192;
    public int Height { get; init; } = 192;

    public RegionOfInterest() {}

    public RegionOfInterest(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public RegionOfInterest(Rect rect)
    {
        X = (int)rect.X;
        Y = (int)rect.Y;
        Width = (int)rect.Width;
        Height = (int)rect.Height;
    }

    public Rect GetRect() => new Rect(X, Y, Width, Height);
}

public record CameraSettings
(
    Enums.Camera Camera,
    RegionOfInterest Roi,
    float RotationRadians = 0,
    float Gamma = 1f,
    bool UseRedChannel = false,
    bool UseHorizontalFlip = false,
    bool UseVerticalFlip = false
);
