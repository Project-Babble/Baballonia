using System;

namespace Baballonia.Services.Inference.Filters;

public class OneEuroFilter
{
    public OneEuroFilter(float minCutoff, float beta)
    {
        FirstTime = true;
        this.MinCutoff = minCutoff;
        this.Beta = beta;

        XFilt = new LowpassFilter();
        DxFilt = new LowpassFilter();
        Dcutoff = 1;
    }

    protected bool FirstTime;
    protected float MinCutoff;
    protected float Beta;
    protected LowpassFilter XFilt;
    protected LowpassFilter DxFilt;
    protected float Dcutoff;

    public float Filter(float x, float rate)
    {
        float dx = FirstTime ? 0 : (x - XFilt.Last()) * rate;
        if (FirstTime)
        {
            FirstTime = false;
        }

        var edx = DxFilt.Filter(dx, Alpha(rate, Dcutoff));
        var cutoff = MinCutoff + Beta * Math.Abs(edx);

        return XFilt.Filter(x, Alpha(rate, cutoff));
    }

    protected float Alpha(float rate, float cutoff)
    {
        var tau = 1.0f / ((float)Math.Tau * cutoff);
        var te = 1.0f / rate;
        return 1.0f / (1.0f + tau / te);
    }
}

public class LowpassFilter
{
    public LowpassFilter()
    {
        FirstTime = true;
    }

    protected bool FirstTime;
    protected float HatXPrev;

    public float Last()
    {
        return HatXPrev;
    }

    public float Filter(float x, float alpha)
    {
        float hatX = 0;
        if (FirstTime)
        {
            FirstTime = false;
            hatX = x;
        }
        else
            hatX = alpha * x + (1 - alpha) * HatXPrev;

        HatXPrev = hatX;

        return hatX;
    }
}
