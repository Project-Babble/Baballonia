using OpenCvSharp;

namespace Baballonia.FastCorruptionDetector;

public class CorruptionResult
{
    public bool LeftCorrupted { get; set; }
    public bool RightCorrupted { get; set; }
    public double LeftValue { get; set; }
    public double RightValue { get; set; }
    public double LeftThreshold { get; set; }
    public double RightThreshold { get; set; }
}

public class DetectionStats
{
    public int TotalFrames { get; set; }
    public int CorruptedLeft { get; set; }
    public int CorruptedRight { get; set; }
    public double CorruptionRateLeft { get; set; }
    public double CorruptionRateRight { get; set; }
    public double BaseThreshold { get; set; }
    public double CurrentThreshold { get; set; }
    public int ThresholdUpdates { get; set; }
    public bool AdaptiveEnabled { get; set; }
}

public class FastCorruptionDetector(
    double threshold = 0.022669,
    bool useAdaptive = true,
    int adaptationWindow = 100)
{
    private readonly double _baseThreshold = threshold;
    private double _currentThreshold = threshold;

    private readonly Queue<double> _recentValues = new();

    private int _totalFrames;
    private int _detectedCorruptedLeft;
    private int _detectedCorruptedRight;
    private int _thresholdUpdates;

    private static double CalculateRowPatternConsistency(Mat image)
    {
        Mat gray;

        if (image.Channels() == 3)
        {
            gray = new Mat();
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            gray = image.Clone();
        }

        Mat grayNorm = new Mat();
        gray.ConvertTo(grayNorm, MatType.CV_32F, 1.0 / 255.0);

        var rowMeans = new double[grayNorm.Rows];
        for (int i = 0; i < grayNorm.Rows; i++)
        {
            using (var row = grayNorm.Row(i))
            {
                Scalar meanScalar = Cv2.Mean(row);
                rowMeans[i] = meanScalar.Val0;
            }
        }

        if (rowMeans.Length <= 1)
        {
            gray.Dispose();
            grayNorm.Dispose();
            return 0.0;
        }

        var diffs = new double[rowMeans.Length - 1];
        for (int i = 0; i < diffs.Length; i++)
        {
            diffs[i] = rowMeans[i + 1] - rowMeans[i];
        }

        double mean = diffs.Average();
        double variance = diffs.Select(d => Math.Pow(d - mean, 2)).Average();
        double standardDeviation = Math.Sqrt(variance);

        gray.Dispose();
        grayNorm.Dispose();

        return standardDeviation;
    }

    private void UpdateAdaptiveThreshold(double value)
    {
        if (!useAdaptive)
            return;

        _recentValues.Enqueue(value);

        if (_recentValues.Count > adaptationWindow)
        {
            _recentValues.Dequeue();
        }

        if (_recentValues.Count < 20)
            return;

        var values = _recentValues.ToArray();
        Array.Sort(values);

        double median = values[values.Length / 2];

        var absDeviations = values.Select(v => Math.Abs(v - median)).ToArray();
        Array.Sort(absDeviations);
        double mad = absDeviations[absDeviations.Length / 2];

        double adaptiveThreshold = median + 3.0 * mad;

        double minThreshold = _baseThreshold * 0.5;
        double maxThreshold = _baseThreshold * 3.0;

        _currentThreshold = Math.Max(minThreshold, Math.Min(maxThreshold, adaptiveThreshold));
        _thresholdUpdates++;
    }

    public (bool isCorrupted, double metricValue, double thresholdUsed) IsCorrupted(Mat frame)
    {
        double metricValue = CalculateRowPatternConsistency(frame);

        UpdateAdaptiveThreshold(metricValue);

        bool isCorrupted = metricValue > _currentThreshold;

        return (isCorrupted, metricValue, _currentThreshold);
    }

    public CorruptionResult ProcessFramePair(Mat leftFrame, Mat rightFrame)
    {
        _totalFrames++;

        var (leftCorrupted, leftValue, leftThreshold) = IsCorrupted(leftFrame);
        var (rightCorrupted, rightValue, rightThreshold) = IsCorrupted(rightFrame);

        if (leftCorrupted)
            _detectedCorruptedLeft++;
        if (rightCorrupted)
            _detectedCorruptedRight++;

        return new CorruptionResult
        {
            LeftCorrupted = leftCorrupted,
            RightCorrupted = rightCorrupted,
            LeftValue = leftValue,
            RightValue = rightValue,
            LeftThreshold = leftThreshold,
            RightThreshold = rightThreshold
        };
    }

    public DetectionStats GetStats()
    {
        return new DetectionStats
        {
            TotalFrames = _totalFrames,
            CorruptedLeft = _detectedCorruptedLeft,
            CorruptedRight = _detectedCorruptedRight,
            CorruptionRateLeft = _detectedCorruptedLeft / (double)Math.Max(1, _totalFrames),
            CorruptionRateRight = _detectedCorruptedRight / (double)Math.Max(1, _totalFrames),
            BaseThreshold = _baseThreshold,
            CurrentThreshold = _currentThreshold,
            ThresholdUpdates = _thresholdUpdates,
            AdaptiveEnabled = useAdaptive
        };
    }
}
