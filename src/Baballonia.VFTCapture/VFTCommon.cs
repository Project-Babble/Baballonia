using OpenCvSharp;

namespace Baballonia.VFTCapture;

public static class VFTCommon
{
    public static readonly Mat Lut = new();
    public static readonly OpenCvSharp.Range ColumnRange = new OpenCvSharp.Range(0, 200);
    public static readonly Size ImageSize = new Size(400, 400);
    public static readonly Size GaussianBlurSize = new Size(15, 15);

    static VFTCommon()
    {
        Lut = new Mat(new Size(1, 256), MatType.CV_8U);
        for (var i = 0; i <= 255; i++)
        {
            Lut.Set(i, (byte)(Math.Pow(i / 2048.0, (1 / 2.5)) * 255.0));
        }
    }
}
