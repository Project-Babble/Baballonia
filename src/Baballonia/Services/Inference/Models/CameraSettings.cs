using Avalonia;

namespace Baballonia.Services.Inference.Models;

public class CameraSettings
{
    public class RegionOfInterest
    {
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int Width { get; set; } = 192;
        public int Height { get; set; } = 192;

        public RegionOfInterest(){}

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

        public Rect GetRect()
        {
            return new Rect(X ,Y ,Width ,Height);
        }
    }

    public Enums.Camera Camera { get; set; }
    public RegionOfInterest Roi { get; set; } = new RegionOfInterest();
    public float RotationRadians { get; set; } = 0;
    public float Gamma { get; set; } = 1f;
    public bool UseRedChannel { get; set; } = false;
    public bool UseHorizontalFlip { get; set; } = false;
    public bool UseVerticalFlip { get; set; } = false;
}
