using Baballonia.Services.Inference.Enums;
using OpenCvSharp;
using System;
using System.Threading;

namespace Baballonia.Services.Inference;

public interface IVideoSource : IDisposable
{
    bool Start();
    bool Stop();
    Mat? GetFrame(ColorType? color = null);

    /// <summary>
    /// Wait handles that become signalled when a fresh frame is available from this source's
    /// underlying capture(s). A consumer can <see cref="WaitHandle.WaitAny(WaitHandle[], int)"/>
    /// across them to block until a new frame arrives instead of busy-polling <see cref="GetFrame"/>.
    /// May be empty when no capture is currently active.
    /// </summary>
    WaitHandle[] GetFrameWaitHandles();
}
