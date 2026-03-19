using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Utilities;
using System;

namespace Baballonia.Controls.Primitives;

/// <summary>
/// Base class for controls that display a value within a range.
/// </summary>
public abstract class RangeBase : TemplatedControl
{
    /// <summary>
    /// Defines the <see cref="Minimum"/> property.
    /// </summary>
    public static readonly DirectProperty<RangeBase, double> MinimumProperty =
        AvaloniaProperty.RegisterDirect<RangeBase, double>(
            nameof(Minimum),
            o => o.Minimum,
            (o, v) => o.Minimum = v);

    /// <summary>
    /// Defines the <see cref="Maximum"/> property.
    /// </summary>
    public static readonly DirectProperty<RangeBase, double> MaximumProperty =
        AvaloniaProperty.RegisterDirect<RangeBase, double>(
            nameof(Maximum),
            o => o.Maximum,
            (o, v) => o.Maximum = v);

    /// <summary>
    /// Defines the <see cref="LowerSelectedValue"/> property.
    /// </summary>
    public static readonly DirectProperty<RangeBase, double> LowerSelectedValueProperty =
        AvaloniaProperty.RegisterDirect<RangeBase, double>(
            nameof(LowerSelectedValue),
            o => o.LowerSelectedValue,
            (o, v) => o.LowerSelectedValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="MiddleSelectedValue"/> property.
    /// </summary>
    public static readonly DirectProperty<RangeBase, double> MiddleSelectedValueProperty =
        AvaloniaProperty.RegisterDirect<RangeBase, double>(
            nameof(MiddleSelectedValue),
            o => o.MiddleSelectedValue,
            (o, v) => o.MiddleSelectedValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="UpperSelectedValue"/> property.
    /// </summary>
    public static readonly DirectProperty<RangeBase, double> UpperSelectedValueProperty =
        AvaloniaProperty.RegisterDirect<RangeBase, double>(
            nameof(UpperSelectedValue),
            o => o.UpperSelectedValue,
            (o, v) => o.UpperSelectedValue = v,
            defaultBindingMode: BindingMode.TwoWay);


    /// <summary>
    /// Defines the <see cref="SmallChange"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SmallChangeProperty =
        AvaloniaProperty.Register<RangeBase, double>(nameof(SmallChange), 1);

    /// <summary>
    /// Defines the <see cref="LargeChange"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LargeChangeProperty =
        AvaloniaProperty.Register<RangeBase, double>(nameof(LargeChange), 10);

    private double _minimum;
    private double _maximum = 100.0;
    private double _lowerSelectedValue;
    private double _middleSelectedValue;
    private double _upperSelectedValue;
    private bool _upperValueInitializedNonZeroValue = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="RangeBase"/> class.
    /// </summary>
    public RangeBase()
    {
    }

    /// <summary>
    /// Gets or sets the minimum value.
    /// </summary>
    public double Minimum
    {
        get
        {
            return _minimum;
        }

        set
        {
            if (!ValidateDouble(value))
            {
                return;
            }

            if (IsInitialized)
            {
                SetAndRaise(MinimumProperty, ref _minimum, value);
                Maximum = ValidateMaximum(Maximum);
                LowerSelectedValue = ValidateLowerValue(LowerSelectedValue);
                MiddleSelectedValue = MiddleSelectedValue;
                UpperSelectedValue = ValidateUpperValue(UpperSelectedValue);
            }
            else
            {
                SetAndRaise(MinimumProperty, ref _minimum, value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum value.
    /// </summary>
    public double Maximum
    {
        get
        {
            return _maximum;
        }

        set
        {
            if (!ValidateDouble(value))
            {
                return;
            }

            if (IsInitialized)
            {
                value = ValidateMaximum(value);
                SetAndRaise(MaximumProperty, ref _maximum, value);
                LowerSelectedValue = ValidateLowerValue(LowerSelectedValue);
                MiddleSelectedValue = MiddleSelectedValue;
                UpperSelectedValue = ValidateUpperValue(UpperSelectedValue);
            }
            else
            {
                SetAndRaise(MaximumProperty, ref _maximum, value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the lower selected value.
    /// </summary>
    public double LowerSelectedValue
    {
        get
        {
            return _lowerSelectedValue;
        }

        set
        {
            if (!ValidateDouble(value))
            {
                return;
            }

            if (IsInitialized)
            {
                value = ValidateLowerValue(value);
                SetAndRaise(LowerSelectedValueProperty, ref _lowerSelectedValue, value);
            }
            else
            {
                SetAndRaise(LowerSelectedValueProperty, ref _lowerSelectedValue, value);
            }
        }
    }
    /// <summary>
    /// Gets or sets the lower selected value.
    /// </summary>
    public double MiddleSelectedValue
    {
        get
        {
            return _middleSelectedValue;
        }

        set
        {
            if (!ValidateDouble(value))
            {
                return;
            }

            if (IsInitialized)
            {
                SetAndRaise(MiddleSelectedValueProperty, ref _middleSelectedValue, value);
            }
            else
            {
                SetAndRaise(MiddleSelectedValueProperty, ref _middleSelectedValue, value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the upper selected value.
    /// </summary>
    public double UpperSelectedValue
    {
        get
        {
            return _upperSelectedValue;
        }

        set
        {
            if (!ValidateDouble(value))
            {
                return;
            }

            if (IsInitialized)
            {
                value = ValidateUpperValue(value);
                _upperValueInitializedNonZeroValue = value > 0.0;
                SetAndRaise(UpperSelectedValueProperty, ref _upperSelectedValue, value);
            }
            else
            {
                SetAndRaise(UpperSelectedValueProperty, ref _upperSelectedValue, value);
            }
        }
    }

    public double SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public double LargeChange
    {
        get => GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        Maximum = ValidateMaximum(Maximum);
        LowerSelectedValue = ValidateLowerValue(LowerSelectedValue);
        MiddleSelectedValue = MiddleSelectedValue;
        UpperSelectedValue = ValidateUpperValue(UpperSelectedValue);
    }

    /// <summary>
    /// Checks if the double value is not inifinity nor NaN.
    /// </summary>
    /// <param name="value">The value.</param>
    private static bool ValidateDouble(double value)
    {
        return !double.IsInfinity(value) || !double.IsNaN(value);
    }

    /// <summary>
    /// Validates/coerces the <see cref="Maximum"/> property.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The coerced value.</returns>
    private double ValidateMaximum(double value)
    {
        return Math.Max(value, Minimum);
    }

    /// <summary>
    /// Validates/coerces the <see cref="LowerSelectedValue"/> property.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The coerced value.</returns>
    private double ValidateLowerValue(double value)
    {
        return _upperValueInitializedNonZeroValue
            ? MathUtilities.Clamp(value, Minimum, UpperSelectedValue)
            : MathUtilities.Clamp(value, Minimum, Maximum);
    }

    /// <summary>
    /// Validates/coerces the <see cref="UpperSelectedValue"/> property.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The coerced value.</returns>
    private double ValidateUpperValue(double value)
    {
        return MathUtilities.Clamp(value, LowerSelectedValue, Maximum);
    }
}
