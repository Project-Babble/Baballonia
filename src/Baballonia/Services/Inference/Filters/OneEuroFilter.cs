using System;
using System.Collections.Generic;

namespace Baballonia.Services.Inference.Filters;

public readonly record struct OneEuroFilterParameters(bool Enabled, float MinCutoff, float Beta);

public class OneEuroFilter : IFilter
{
    private readonly Func<string, OneEuroFilterParameters> _getParameters;
    private bool _isInitialized;

    private string[] _keys;
    private bool[] _enabled;
    private float[] _minCutoff;
    private float[] _beta;
    private float[] _dCutoff;
    private float[] _xPrev;
    private float[] _dxPrev;

    private float[] _dx;
    private float[] _dxHat;
    private float[] _cutoff;
    private float[] _xHat;

    // Reusable output map
    private OrderedFloatMap _output;
    // The input map we sized our state to; detects a model hot-swap.
    private OrderedFloatMap _source;

    public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.0f)
        : this(_ => new OneEuroFilterParameters(true, minCutoff, beta))
    {
    }

    public OneEuroFilter(Func<string, OneEuroFilterParameters> getParameters)
    {
        _getParameters = getParameters;
    }

    public OrderedFloatMap Filter(OrderedFloatMap x, double elapsedSeconds)
    {
        // (Re)init on first frame or after a model swap. State is sized to the first frame;
        // a shrunk output overruns it, a grown one gets truncated/mis-keyed.
        if (!_isInitialized || ShapeChanged(x))
        {
            Initialize(x);
            return _output; // Return the initial state on the first frame
        }

        ReadOnlySpan<float> xSpan = x.ValuesSpan;
        Span<float> outSpan = _output.ValuesSpan;
        int length = _xPrev.Length;

        if (elapsedSeconds <= 0.0 || !double.IsFinite(elapsedSeconds))
        {
            ReadOnlySpan<float> src = xSpan.Slice(0, length);
            src.CopyTo(outSpan);
            src.CopyTo(_xPrev);
            return _output;
        }

        float elapsedTime = (float)elapsedSeconds;

        for (int i = 0; i < length; i++)
        {
            float val = xSpan[i];

            if (!_enabled[i])
            {
                _xPrev[i] = val;
                _dxPrev[i] = 0.0f;
                outSpan[i] = val;
                continue;
            }

            _dx[i] = (val - _xPrev[i]) / elapsedTime;

            float r_d = MathF.Tau * _dCutoff[i] * elapsedTime;
            float a_d = r_d / (r_d + 1.0f);

            _dxHat[i] = a_d * _dx[i] + (1.0f - a_d) * _dxPrev[i];

            _cutoff[i] = _minCutoff[i] + _beta[i] * MathF.Abs(_dxHat[i]);

            float r = MathF.Tau * _cutoff[i] * elapsedTime;
            float a = r / (r + 1.0f);

            _xHat[i] = a * val + (1.0f - a) * _xPrev[i];

            _xPrev[i] = _xHat[i];
            _dxPrev[i] = _dxHat[i];

            outSpan[i] = _xHat[i];
        }

        return _output;
    }

    // True when x's output set differs from ours. The runner reuses one map per model, so a
    // reference change flags a swap; length/keys confirm it so an equivalent map never resets us.
    private bool ShapeChanged(OrderedFloatMap x)
    {
        if (ReferenceEquals(x, _source))
            return false;

        if (x.Count != _keys.Length)
            return true;

        int i = 0;
        foreach (string key in x.Keys)
        {
            if (!string.Equals(key, _keys[i], StringComparison.Ordinal))
                return true;
            i++;
        }

        // Same shape, new instance — adopt it for the fast path.
        _source = x;
        return false;
    }

    private void Initialize(OrderedFloatMap x0)
    {
        int length = x0.Count;

        _keys = new string[length];
        _enabled = new bool[length];
        _minCutoff = new float[length];
        _beta = new float[length];
        _dCutoff = new float[length];
        _xPrev = new float[length];
        _dxPrev = new float[length];

        _dx = new float[length];
        _dxHat = new float[length];
        _cutoff = new float[length];
        _xHat = new float[length];

        int i = 0;
        foreach (KeyValuePair<string, float> kvp in x0)
        {
            var parameters = _getParameters(kvp.Key);
            _keys[i] = kvp.Key;
            _enabled[i] = parameters.Enabled;
            _xPrev[i] = kvp.Value;
            _minCutoff[i] = parameters.MinCutoff;
            _beta[i] = parameters.Beta;
            _dCutoff[i] = 1.0f;
            _dxPrev[i] = 0.0f;
            i++;
        }

        _output = new OrderedFloatMap(_keys);
        _source = x0;

        _xPrev.CopyTo(_output.ValuesSpan);

        _isInitialized = true;
    }
}
