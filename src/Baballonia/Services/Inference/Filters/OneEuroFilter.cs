using System;
using System.Collections.Generic;

namespace Baballonia.Services.Inference.Filters;

public class OneEuroFilter : IFilter
{
    private const float TwoPi = 2.0f * MathF.PI;

    private readonly float _initialMinCutoff;
    private readonly float _initialBeta;
    private bool _isInitialized;

    private string[] _keys;
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
    private DateTime _tPrev;

    public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.0f)
    {
        _initialMinCutoff = minCutoff;
        _initialBeta = beta;
        _isInitialized = false;
    }

    public OrderedFloatMap Filter(OrderedFloatMap x)
    {
        // Lazily initialize on the very first call
        if (!_isInitialized)
        {
            Initialize(x);
            return _output; // Return the initial state on the first frame
        }

        DateTime now = DateTime.UtcNow;
        float elapsedTime = (float)(now - _tPrev).TotalSeconds;

        ReadOnlySpan<float> xSpan = x.ValuesSpan;
        Span<float> outSpan = _output.ValuesSpan;
        int length = _xPrev.Length;

        if (elapsedTime <= 0.0f)
        {
            for (int i = 0; i < length; i++)
            {
                float val = xSpan[i];
                outSpan[i] = val;
                _xPrev[i] = val;
            }
            return _output;
        }

        for (int i = 0; i < length; i++)
        {
            float val = xSpan[i];

            _dx[i] = (val - _xPrev[i]) / elapsedTime;

            float r_d = TwoPi * _dCutoff[i] * elapsedTime;
            float a_d = r_d / (r_d + 1.0f);

            _dxHat[i] = a_d * _dx[i] + (1.0f - a_d) * _dxPrev[i];

            _cutoff[i] = _minCutoff[i] + _beta[i] * MathF.Abs(_dxHat[i]);

            float r = TwoPi * _cutoff[i] * elapsedTime;
            float a = r / (r + 1.0f);

            _xHat[i] = a * val + (1.0f - a) * _xPrev[i];

            _xPrev[i] = _xHat[i];
            _dxPrev[i] = _dxHat[i];

            outSpan[i] = _xHat[i];
        }

        _tPrev = now;
        return _output;
    }

    private void Initialize(OrderedFloatMap x0)
    {
        int length = x0.Count;

        _keys = new string[length];
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
            _keys[i] = kvp.Key;
            _xPrev[i] = kvp.Value;
            _minCutoff[i] = _initialMinCutoff;
            _beta[i] = _initialBeta;
            _dCutoff[i] = 1.0f;
            _dxPrev[i] = 0.0f;
            i++;
        }

        _output = new OrderedFloatMap(_keys);
        
        _xPrev.CopyTo(_output.ValuesSpan);

        _tPrev = DateTime.UtcNow;
        _isInitialized = true;
    }
}