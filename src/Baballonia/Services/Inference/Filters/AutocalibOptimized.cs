using System;

namespace Baballonia.Services.Inference.Filters;

public class AutocalibOptimized : IFilter
{
    private int _expressionCount;

    private float _alpha;
    private float _beta;
    private float _multiplier;
    private bool _isInitialized;

    private readonly float[] _neutralMask;
    private readonly float[] _activeMask;
    private readonly float[] _minDiff;
    private readonly float[] _maxDiff;
    private readonly float[] _diff;
    private readonly float[] _std;
    private readonly float[] _min;
    private readonly float[] _max;
    private readonly float[] _mean;
    private readonly float[] _variance;
    private readonly float[] _threshold;
    private float _sampleCount;
    private float[] _sampleCounts;
    private bool[] _calibratedFlags;
    private int _warmupFrames;
    private float _adaptationRate;
    private float _minAdaptationRate;
    private float _adaptationDecay;
    private float _hysteresisThreshold;
    private int[] _decayCounters;
    private int _daceyThreshold;
    private float[] _decayLevels;

    public AutocalibOptimized(int expressionCount, float alpha = 0.1f, float beta = 0.1f,
        float thresholdMultiplier = 2.0f)
    {
        _expressionCount = expressionCount;
        _alpha = alpha;
        _beta = beta;
        _multiplier = thresholdMultiplier;
        _isInitialized = false;

        _neutralMask = InitAndFill<float>(expressionCount);

        _neutralMask = InitAndFill<float>(expressionCount);
        _activeMask = InitAndFill<float>(expressionCount);
        _minDiff = InitAndFill<float>(expressionCount);
        _maxDiff = InitAndFill<float>(expressionCount);
        _diff = InitAndFill<float>(expressionCount);
        _std = InitAndFill<float>(expressionCount);

        _min = InitAndFill<float>(expressionCount, float.PositiveInfinity);
        _max = InitAndFill<float>(expressionCount, float.NegativeInfinity);

        _mean = InitAndFill<float>(expressionCount);
        _variance = InitAndFill<float>(expressionCount);
        _threshold = InitAndFill<float>(expressionCount);
        _sampleCounts = InitAndFill<float>(expressionCount);
        _calibratedFlags = InitAndFill<bool>(expressionCount);
        _decayCounters = InitAndFill<int>(expressionCount);
        _decayLevels = InitAndFill<float>(expressionCount);

        _sampleCount = 0f;
        _warmupFrames = 300;

        _adaptationRate = 1f;
        _minAdaptationRate = 0.01f;
        _adaptationDecay = 0.99998f;
        _hysteresisThreshold = 0.05f;
    }

    public float[] Filter(float[] input)
    {
        var (minVals, maxVals, calibratedFlags) = Update(input);

        // Reuse buffer for denominator & calibrated
        var calibrated = new float[_expressionCount];
        for (var i = 0; i < _expressionCount; i++)
        {
            var denom = maxVals[i] - minVals[i];
            if (denom == 0) denom = float.MinValue; // avoid divide by zero

            if (!calibratedFlags[i]) continue;

            calibrated[i] = (input[i] - minVals[i]) / denom;
            calibrated[i] = Math.Min(Math.Max(calibrated[i], 0f), 1f);
        }
        return calibrated;
    }

    private T[] InitAndFill<T>(int count, T defaultValue = default!)
    {
        var result = new T[count];
        Array.Fill(result, defaultValue);
        return result;
    }

    private (float[] minVals, float[] maxVals, bool[] calibratedFlags) Update(float[] input)
    {
        if (input.Length != _expressionCount)
            throw new ArgumentException(
                $@"Input length mismatch. Expected: {_expressionCount}, got: {input.Length}",
                nameof(input));

        if (_sampleCount > 0)
            _adaptationRate = Math.Max(_minAdaptationRate, _adaptationRate * _adaptationDecay);

        if (!_isInitialized)
        {
            input.CopyTo(_min, 0);
            input.CopyTo(_max, 0);

            for (var i = 0; i < _expressionCount; i++)
                _mean[i] = _alpha * input[i] + (1 - _alpha) * _mean[i];

            _sampleCount++;
            _isInitialized = true;
            return (_min, _max, _calibratedFlags);
        }

        const float neutralThreshold = 0.15f;
        var neutralCount = 0;
        for (var i = 0; i < _expressionCount; i++)
        {
            var neutral = Math.Abs(input[i]) < neutralThreshold;
            if (neutral) neutralCount++;
            _neutralMask[i] = neutral ? 1f : 0f;
            _activeMask[i] = neutral ? 0f : 1f;

            _minDiff[i] = _min[i] - input[i];
            _maxDiff[i] = input[i] - _max[i];
        }

        var isNeutralPose = neutralCount >= (int)(0.8f * _expressionCount);

        if (isNeutralPose)
        {
            for (var i = 0; i < _expressionCount; i++)
            {
                if (_minDiff[i] > _hysteresisThreshold && _neutralMask[i] > 0)
                    _min[i] = _adaptationRate * input[i] + (1 - _adaptationRate) * _min[i];

                if (_neutralMask[i] > 0)
                    _mean[i] = _alpha * input[i] + (1 - _alpha) * _mean[i];
            }
        }

        for (var i = 0; i < _expressionCount; i++)
        {
            if (_maxDiff[i] > _hysteresisThreshold && _activeMask[i] > 0)
                _max[i] = ((_decayLevels[i] > 0.1f) ? 0.5f : _adaptationRate) * input[i]
                          + (1 - ((_decayLevels[i] > 0.1f) ? 0.5f : _adaptationRate)) * _max[i];

            if (input[i] < _max[i] * 0.9f)
                _decayCounters[i]++;

            if (_decayCounters[i] > _daceyThreshold)
            {
                _decayLevels[i] += 0.01f;
                _max[i] *= 0.999f;
            }
            else
            {
                _decayCounters[i] = 0;
                _decayLevels[i] *= 0.95f;
            }

            if (!(_min[i] > _max[i])) continue;

            var avg = (_min[i] + _max[i]) / 2f;
            _min[i] = avg;
            _max[i] = avg;
        }

        for (var i = 0; i < _expressionCount; i++)
        {
            _diff[i] = input[i] - _mean[i];
            _variance[i] = _beta * (_diff[i] * _diff[i]) + (1 - _beta) * _variance[i];

            _std[i] = (float)Math.Sqrt(_variance[i]) + 1e-6f;
            _threshold[i] = _multiplier * _std[i];

            _sampleCounts[i]++;
        }

        for (var i = 0; i < _expressionCount; i++)
        {
            if (!_calibratedFlags[i] &&
                (_max[i] - _min[i] > 1e-3f) &&
                (_sampleCounts[i] >= 300) &&
                (_variance[i] > 1e-4f))
            {
                _calibratedFlags[i] = true;
            }
        }

        _sampleCount++;
        return (_min, _max, _calibratedFlags);
    }
}
