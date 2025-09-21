using System;
using Avalonia;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using SkiaSharp;

namespace Baballonia.Helpers;

public class CameraSize()
{
    public int Width = 0;
    public int Height = 0;
}
public class CropManager()
{
    private bool _isCropping;
    private double _startX;
    private double _startY;
    private RegionOfInterest _cropZone;

    public RegionOfInterest CropZone => _cropZone;
    public bool IsCropping => _isCropping;
    public CameraSize MaxSize { get; set; } = new();

    public void StartCrop(Point startPosition)
    {
        _startX = startPosition.X;
        _startY = startPosition.Y;
        _isCropping = true;
    }

    public void UpdateCrop(Point currentPosition)
    {
        if (!_isCropping) return;

        // Clamp currentPosition to the image boundaries
        double clampedX = Math.Max(0, Math.Min(currentPosition.X, MaxSize.Width));
        double clampedY = Math.Max(0, Math.Min(currentPosition.Y, MaxSize.Height));

        double x, y, w, h;

        if (clampedX < _startX)
        {
            x = clampedX;
            w = _startX - x;
        }
        else
        {
            x = _startX;
            w = clampedX - _startX;
        }

        if (clampedY < _startY)
        {
            y = clampedY;
            h = _startY - y;
        }
        else
        {
            y = _startY;
            h = clampedY - _startY;
        }

        _cropZone = new RegionOfInterest(
            (int)x,
            (int)y,
            (int)w,
            (int)h
        );
    }

    public void EndCrop()
    {
        _isCropping = false;
    }

    public void SetCropZone(RegionOfInterest rectangle)
    {
        _cropZone = rectangle;
    }

    public void SelectEntireFrame(Camera camera)
    {
        // Special BSB2E-like stereo camera logic
        var halfWidth = MaxSize.Width / 2;

        int x;
        int y;
        int width;
        int height;

        if (halfWidth == MaxSize.Height)
        {
            switch (camera)
            {
                case Camera.Left:
                    x = 0;
                    y = 0;
                    width = halfWidth - 1;
                    height = MaxSize.Height - 1;
                    break;
                case Camera.Right:
                    x = halfWidth;
                    y = 0;
                    width = halfWidth - 1;
                    height = MaxSize.Height - 1;
                    break;
                default: // Face
                    x = 0;
                    y = 0;
                    width = MaxSize.Width;
                    height = MaxSize.Width;
                    break;
            }
        }
        else
        {
            x = 0;
            y = 0;
            width = MaxSize.Width;
            height = MaxSize.Height;
        }

        _cropZone = new RegionOfInterest(x, y, width, height);
    }
}
