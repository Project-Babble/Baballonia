using Baballonia.Services.Inference.Enums;
using OpenCvSharp;
using System;

namespace Baballonia.Services.Inference;

public interface IVideoSource : IDisposable
{
    bool Start();
    bool Stop();
    Mat? GetFrame(ColorType? color = null);

}
