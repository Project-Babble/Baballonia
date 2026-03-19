using OpenCvSharp;

namespace Baballonia.VFTCapture;

public static class VFTCommon
{
    public static readonly Mat Lut = new();
    public static readonly OpenCvSharp.Range ColumnRange = new(0, 200);
    public static readonly Size ImageSize = new(400, 400);
    public static readonly Size GaussianBlurSize = new(5, 5);
    public static readonly double InvGamma = 1.0 / 2.5;

    static VFTCommon()
    {
        Lut = new Mat(new Size(1, 256), MatType.CV_8U);
        for (var i = 0; i <= 255; i++)
        {
            Lut.Set(i, (byte)(Math.Pow(i / 2048.0, InvGamma) * 255.0));
        }
    }
}
