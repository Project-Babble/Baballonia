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

        // feed the most recent matrix here at the start
        var last4 = ImageQueue.Skip(ImageQueue.Count - 4).Take(4).Reverse().ToArray();

        var leftChannels = new List<Mat>();
        var rightChannels = new List<Mat>();
        foreach (var m in last4)
        {
            Mat[] splitChannels = Cv2.Split(m);
            leftChannels.Add(splitChannels[0]);
            rightChannels.Add(splitChannels[1]);
        }

        Mat octoMatrix = new Mat();
        Cv2.Merge(leftChannels.Concat(rightChannels).ToArray(), octoMatrix);

        foreach (var channel in leftChannels)
            channel.Dispose();

        foreach (var channel in rightChannels)
            channel.Dispose();

        return octoMatrix;
    }
}
