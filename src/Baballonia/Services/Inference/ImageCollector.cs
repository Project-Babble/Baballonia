using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public class ImageCollector : IImageTransformer
{
    private Queue<Mat> ImageQueue = new();
    public Mat? Apply(Mat image)
    {
        Mat[] split = image.Split();
        foreach (var mat in split)
        {
            Cv2.EqualizeHist(mat, mat);
        }

        Mat merged = new Mat();
        // swap left and right because inference requires them in that way
        Cv2.Merge(split.Reverse().ToArray(), merged);

        ImageQueue.Enqueue(merged);

        if (ImageQueue.Count < 5)
            return null;

        var removed = ImageQueue.Dequeue();
        removed.Dispose();

        var last4 = ImageQueue.Skip(ImageQueue.Count - 4).Take(4).Reverse().ToArray();

        var channels = new List<Mat>();
        foreach (var m in last4)
        {
            Mat[] splitChannels = Cv2.Split(m);
            channels.AddRange(splitChannels);
        }
        Mat octoMatrix = new Mat();
        Cv2.Merge(channels.ToArray(), octoMatrix);

        foreach (var channel in channels)
            channel.Dispose();

        return octoMatrix;
    }
}
