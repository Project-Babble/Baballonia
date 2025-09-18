using System;

namespace Baballonia.Services.Inference.Filters;

public class TimeBasedFloorNormalizationFilter : IFilter
{
    private readonly float _targetFloor;

    private readonly float _floorStep;
    private float[] _minVal;
    private float[] _maxVal;
    private readonly float[] _floor;
    private readonly int _count;

    private readonly float[] _rawNorm;
    private readonly bool[] _newMin;

    public TimeBasedFloorNormalizationFilter(int count = Utils.FaceRawExpressions, float inputFrequency = 60f, float decayMinutes = 50f, float targetFloor = 0.5f)
    {
        _targetFloor = targetFloor;
        _count = count;

        var steps = Math.Max(1, inputFrequency * decayMinutes * 60);
        _floorStep = targetFloor / steps;

        _minVal = InitAndFill<float>(count, float.NaN);
        _maxVal = InitAndFill<float>(count, float.NaN);
        _maxVal = InitAndFill<float>(count);
        _rawNorm = InitAndFill<float>(count);
        _newMin = InitAndFill<bool>(count);
    }

    private T[] InitAndFill<T>(int count, T defaultValue = default!)
    {
        var result = new T[count];
        Array.Fill(result, defaultValue);
        return result;
    }

    public float[] Filter(float[] input)
    {
        if (float.IsNaN(_minVal[0]))
        {
            _minVal = input;
            _maxVal = input;
        }

        var normalized = new float[_count];
        for (var i = 0; i < _count; i++)
        {
            if (input[i] > _maxVal[i])
                _maxVal[i] = input[i];

            _newMin[i] = input[i] < _minVal[i];
            if (_newMin[i])
            {
                _minVal[i] = input[i];
                _floor[i] = 0f;
            }

            if (Math.Abs(_maxVal[i] - _minVal[i]) < 0.001)
                _rawNorm[i] = 0.5f;
            else
            {
                _rawNorm[i] = (input[i] - _minVal[i]) / (_maxVal[i] - _minVal[i]);
                _rawNorm[i] = Math.Max(0f, Math.Min(1f, _rawNorm[i]));
            }

            if (!_newMin[i] && _floor[i] < _targetFloor)
                _floor[i] = Math.Min(_targetFloor, _floor[i] + _floorStep);

            normalized[i] = _floor[i] + (1f - _floor[i]);
        }

        return normalized;
    }
}
