using System;
using System.Collections.Generic;

namespace Baballonia.Services.Inference.Filters;

public class GroupedOneEuroFilter : IFilter
{
    private sealed class GroupState
    {
        public int[] Indices = Array.Empty<int>();
        public float[] XPrev = Array.Empty<float>();
        public float[] DxPrev = Array.Empty<float>();
        public float MinCutoff;
        public float Beta;
        public float DCutoff = 1.0f;
        public DateTime TPrev;
        public bool Initialized;
    }

    private readonly Dictionary<string, GroupState> _groups = new();

    public void ConfigureGroup(string groupName, int[] parameterIndices, float minCutoff, float beta)
    {
        if (parameterIndices.Length == 0)
            return;

        var state = new GroupState
        {
            Indices = (int[])parameterIndices.Clone(),
            XPrev = new float[parameterIndices.Length],
            DxPrev = new float[parameterIndices.Length],
            MinCutoff = Math.Max(0.001f, minCutoff),
            Beta = Math.Max(0f, beta),
            TPrev = DateTime.UtcNow,
            Initialized = false
        };

        _groups[groupName] = state;
    }

    public void DisableGroup(string groupName)
    {
        _groups.Remove(groupName);
    }

    public float[] Filter(float[] input)
    {
        if (_groups.Count == 0)
            return input;

        var now = DateTime.UtcNow;
        float[] result = (float[])input.Clone();

        foreach (var kvp in _groups)
        {
            var state = kvp.Value;
            if (state.Indices.Length == 0)
                continue;

            int n = state.Indices.Length;
            float[] x = new float[n];
            var indices = state.Indices;
            for (int i = 0; i < n; i++)
            {
                x[i] = input[indices[i]];
            }

            float dt = (float)(now - state.TPrev).TotalSeconds;
            if (!state.Initialized || dt <= 0f)
            {
                for (int i = 0; i < n; i++)
                    state.XPrev[i] = x[i];
                state.TPrev = now;
                state.Initialized = true;
                continue;
            }

            // dx = (x - xPrev) / dt
            for (int i = 0; i < n; i++)
            {
                state.DxPrev[i] = OneEuroSmooth(state.DCutoff, dt, (x[i] - state.XPrev[i]) / dt, state.DxPrev[i]);
            }

            // cutoff = minCutoff + beta * |dxHat|
            for (int i = 0; i < n; i++)
            {
                float cutoff = state.MinCutoff + state.Beta * MathF.Abs(state.DxPrev[i]);
                float a = SmoothingFactor(cutoff, dt);
                float xHat = a * x[i] + (1f - a) * state.XPrev[i];
                state.XPrev[i] = xHat;
                result[indices[i]] = xHat;
            }

            state.TPrev = now;
        }

        return result;
    }

    private static float OneEuroSmooth(float cutoff, float dt, float value, float prev)
    {
        float a = SmoothingFactor(cutoff, dt);
        return a * value + (1f - a) * prev;
    }

    private static float SmoothingFactor(float cutoff, float dt)
    {
        float r = 2f * MathF.PI * cutoff * dt;
        return r / (r + 1f);
    }
}
