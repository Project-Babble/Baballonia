using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Utilities;
using System;

namespace Baballonia.Controls.Primitives;

[PseudoClasses(":vertical", ":horizontal")]
public class RangeTrack : Control
{
    public static readonly DirectProperty<RangeTrack, double> MinimumProperty =
        RangeBase.MinimumProperty.AddOwner<RangeTrack>(o => o.Minimum, (o, v) => o.Minimum = v);

    public static readonly DirectProperty<RangeTrack, double> MaximumProperty =
        RangeBase.MaximumProperty.AddOwner<RangeTrack>(o => o.Maximum, (o, v) => o.Maximum = v);

    public static readonly DirectProperty<RangeTrack, double> LowerSelectedValueProperty =
        RangeBase.LowerSelectedValueProperty.AddOwner<RangeTrack>(o => o.LowerSelectedValue, (o, v) => o.LowerSelectedValue = v);

    public static readonly DirectProperty<RangeTrack, double> MiddleSelectedValueProperty =
        RangeBase.MiddleSelectedValueProperty.AddOwner<RangeTrack>(o => o.MiddleSelectedValue, (o, v) => o.MiddleSelectedValue = v);

    public static readonly DirectProperty<RangeTrack, double> UpperSelectedValueProperty =
        RangeBase.UpperSelectedValueProperty.AddOwner<RangeTrack>(o => o.UpperSelectedValue, (o, v) => o.UpperSelectedValue = v);

    public static readonly StyledProperty<double> ViewportSizeProperty =
        ScrollBar.ViewportSizeProperty.AddOwner<RangeTrack>();

    public static readonly StyledProperty<Orientation> OrientationProperty =
        ScrollBar.OrientationProperty.AddOwner<RangeTrack>();

    public static readonly StyledProperty<Thumb> LowerThumbProperty =
        AvaloniaProperty.Register<RangeTrack, Thumb>(nameof(Thumb));

    public static readonly StyledProperty<Thumb> MiddleThumbProperty =
        AvaloniaProperty.Register<RangeTrack, Thumb>(nameof(Thumb));

    public static readonly StyledProperty<Thumb> UpperThumbProperty =
        AvaloniaProperty.Register<RangeTrack, Thumb>(nameof(Thumb));

    public static readonly StyledProperty<RepeatButton> BackgroundButtonProperty =
        AvaloniaProperty.Register<RangeTrack, RepeatButton>(nameof(BackgroundButton));

    public static readonly StyledProperty<RepeatButton> ForegroundButtonProperty =
        AvaloniaProperty.Register<RangeTrack, RepeatButton>(nameof(ForegroundButton));

    public static readonly StyledProperty<bool> IsDirectionReversedProperty =
        AvaloniaProperty.Register<RangeTrack, bool>(nameof(IsDirectionReversed));

    public static readonly StyledProperty<bool> IsThumbOverlapProperty =
        AvaloniaProperty.Register<RangeTrack, bool>(nameof(IsThumbOverlap));

    private double _minimum;
    private double _maximum = 100.0;
    private double _lowerSelectedValue;
    private double _middleSelectedValue;
    private double _upperSelectedValue;

    static RangeTrack()
    {
        LowerThumbProperty.Changed.AddClassHandler<RangeTrack>((x, e) => x.ThumbChanged(e));
        MiddleThumbProperty.Changed.AddClassHandler<RangeTrack>((x, e) => x.ThumbChanged(e));
        UpperThumbProperty.Changed.AddClassHandler<RangeTrack>((x, e) => x.ThumbChanged(e));
        BackgroundButtonProperty.Changed.AddClassHandler<RangeTrack>((x, e) => x.ButtonChanged(e));
        ForegroundButtonProperty.Changed.AddClassHandler<RangeTrack>((x, e) => x.ButtonChanged(e));
        AffectsArrange<RangeTrack>(
            MinimumProperty,
            MaximumProperty,
            LowerSelectedValueProperty,
            MiddleSelectedValueProperty,
            UpperSelectedValueProperty,
            IsThumbOverlapProperty,
            OrientationProperty);
    }

    public RangeTrack()
    {
        UpdatePseudoClasses(Orientation);
    }

    public double Minimum
    {
        get { return _minimum; }
        set { SetAndRaise(MinimumProperty, ref _minimum, value); }
    }

    public double Maximum
    {
        get { return _maximum; }
        set { SetAndRaise(MaximumProperty, ref _maximum, value); }
    }

    public double LowerSelectedValue
    {
        get { return _lowerSelectedValue; }
        set { SetAndRaise(LowerSelectedValueProperty, ref _lowerSelectedValue, value); }
    }
    public double MiddleSelectedValue
    {
        get { return _middleSelectedValue; }
        set { SetAndRaise(MiddleSelectedValueProperty, ref _middleSelectedValue, value); }
    }

    public double UpperSelectedValue
    {
        get { return _upperSelectedValue; }
        set { SetAndRaise(UpperSelectedValueProperty, ref _upperSelectedValue, value); }
    }

    public double ViewportSize
    {
        get { return GetValue(ViewportSizeProperty); }
        set { SetValue(ViewportSizeProperty, value); }
    }

    public Orientation Orientation
    {
        get { return GetValue(OrientationProperty); }
        set { SetValue(OrientationProperty, value); }
    }

    public Thumb LowerThumb
    {
        get { return GetValue(LowerThumbProperty); }
        set { SetValue(LowerThumbProperty, value); }
    }
    public Thumb MiddleThumb
    {
        get { return GetValue(MiddleThumbProperty); }
        set { SetValue(MiddleThumbProperty, value); }
    }

    public Thumb UpperThumb
    {
        get { return GetValue(UpperThumbProperty); }
        set { SetValue(UpperThumbProperty, value); }
    }

    public RepeatButton BackgroundButton
    {
        get { return GetValue(BackgroundButtonProperty); }
        set { SetValue(BackgroundButtonProperty, value); }
    }

    public RepeatButton ForegroundButton
    {
        get { return GetValue(ForegroundButtonProperty); }
        set { SetValue(ForegroundButtonProperty, value); }
    }

    public bool IsDirectionReversed
    {
        get { return GetValue(IsDirectionReversedProperty); }
        set { SetValue(IsDirectionReversedProperty, value); }
    }

    public bool IsThumbOverlap
    {
        get { return GetValue(IsThumbOverlapProperty); }
        set { SetValue(IsThumbOverlapProperty, value); }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredSize = new Size(0.0, 0.0);

        // Only measure thumbs.
        // Repeat buttons will be sized based on thumbs
        if (LowerThumb != null && UpperThumb != null)
        {
            LowerThumb.Measure(availableSize);
            MiddleThumb?.Measure(availableSize);
            UpperThumb.Measure(availableSize);

            if (Orientation == Orientation.Horizontal)
            {
                var maxHeight = Math.Max(LowerThumb.DesiredSize.Height, UpperThumb.DesiredSize.Height);
                if (MiddleThumb != null)
                    maxHeight = Math.Max(maxHeight, MiddleThumb.DesiredSize.Height);

                desiredSize = new Size(
                    LowerThumb.DesiredSize.Width + UpperThumb.Width + (MiddleThumb?.Width ?? 0),
                    maxHeight);
            }
            else
            {
                var maxWidth = Math.Max(LowerThumb.DesiredSize.Width, UpperThumb.DesiredSize.Width);
                if (MiddleThumb != null)
                    maxWidth = Math.Max(maxWidth, MiddleThumb.DesiredSize.Width);

                desiredSize = new Size(
                    maxWidth,
                    LowerThumb.DesiredSize.Height + UpperThumb.Height + (MiddleThumb?.Height ?? 0));
            }
        }

        if (!double.IsNaN(ViewportSize))
        {
            // ScrollBar can shrink to 0 in the direction of scrolling
            if (Orientation == Orientation.Vertical)
                desiredSize = desiredSize.WithHeight(0.0);
            else
                desiredSize = desiredSize.WithWidth(0.0);
        }

        return desiredSize;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        double thumbLength, backgroundButtonLength, foregroundButtonLength, lowerThumbOffset, upperThumbOffset;
        var isVertical = Orientation == Orientation.Vertical;
        var viewportSize = Math.Max(0.0, ViewportSize);

        // If viewport is NaN, compute thumb's size based on its desired size,
        // otherwise compute the thumb base on the viewport and extent properties
        if (double.IsNaN(ViewportSize))
        {
            ComputeSliderLengths(arrangeSize, isVertical, out thumbLength,
                out backgroundButtonLength, out foregroundButtonLength,
                out lowerThumbOffset, out upperThumbOffset);
        }
        else
        {
            // Don't arrange if there's not enough content or the track is too small
            if (!ComputeScrollBarLengths(arrangeSize, viewportSize, isVertical, out thumbLength,
                out backgroundButtonLength, out foregroundButtonLength,
                out lowerThumbOffset, out upperThumbOffset))
            {
                return arrangeSize;
            }
        }

        // Layout the pieces of track
        var offset = new Point();
        var pieceSize = arrangeSize;
        var isDirectionReversed = IsDirectionReversed;

        if (isVertical)
        {
            CoerceLength(ref backgroundButtonLength, arrangeSize.Height);
            CoerceLength(ref foregroundButtonLength, arrangeSize.Height);
            CoerceLength(ref thumbLength, arrangeSize.Height);
            var halfThumbLength = thumbLength / 2.0;

            offset = offset.WithY(isDirectionReversed ? 0.0 : halfThumbLength);
            pieceSize = pieceSize.WithHeight(backgroundButtonLength);

            BackgroundButton?.Arrange(new Rect(offset, pieceSize));

            offset = offset.WithY(isDirectionReversed ? 0.0 : upperThumbOffset + halfThumbLength);
            pieceSize = pieceSize.WithHeight(foregroundButtonLength);

            ForegroundButton?.Arrange(new Rect(offset, pieceSize));

            offset = offset.WithY(isDirectionReversed ? 0.0 : lowerThumbOffset);
            pieceSize = pieceSize.WithHeight(thumbLength);

            LowerThumb?.Arrange(new Rect(offset, pieceSize));

            if (MiddleThumb != null)
            {
                var middleThumbOffset = ComputeMiddleThumbOffset(arrangeSize, true, thumbLength);
                offset = offset.WithY(isDirectionReversed ? 0.0 : middleThumbOffset);
                pieceSize = pieceSize.WithHeight(thumbLength);
                MiddleThumb.Arrange(new Rect(offset, pieceSize));
            }

            offset = offset.WithY(isDirectionReversed ? 0.0 : upperThumbOffset);
            pieceSize = pieceSize.WithHeight(thumbLength);

            UpperThumb?.Arrange(new Rect(offset, pieceSize));
        }
        else
        {
            CoerceLength(ref backgroundButtonLength, arrangeSize.Width);
            CoerceLength(ref foregroundButtonLength, arrangeSize.Width);
            CoerceLength(ref thumbLength, arrangeSize.Width);
            var halfThumbLength = thumbLength / 2.0;

            offset = offset.WithX(isDirectionReversed ? 0.0 : halfThumbLength);
            pieceSize = pieceSize.WithWidth(backgroundButtonLength);

            BackgroundButton?.Arrange(new Rect(offset, pieceSize));

            offset = offset.WithX(isDirectionReversed ? 0.0 : lowerThumbOffset + halfThumbLength);
            pieceSize = pieceSize.WithWidth(foregroundButtonLength);

            ForegroundButton?.Arrange(new Rect(offset, pieceSize));

            offset = offset.WithX(isDirectionReversed ? 0.0 : lowerThumbOffset);
            pieceSize = pieceSize.WithWidth(thumbLength);

            LowerThumb?.Arrange(new Rect(offset, pieceSize));

            if (MiddleThumb != null)
            {
                var middleThumbOffset = ComputeMiddleThumbOffset(arrangeSize, false, thumbLength);
                offset = offset.WithX(isDirectionReversed ? 0.0 : middleThumbOffset);
                pieceSize = pieceSize.WithWidth(thumbLength);
                MiddleThumb.Arrange(new Rect(offset, pieceSize));
            }

            offset = offset.WithX(isDirectionReversed ? 0.0 : upperThumbOffset);
            pieceSize = pieceSize.WithWidth(thumbLength);

            UpperThumb?.Arrange(new Rect(offset, pieceSize));
        }

        return arrangeSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        var e = change as AvaloniaPropertyChangedEventArgs<Orientation>;

        if (e is null)
            return;

        var value = e.NewValue.GetValueOrDefault();

        if (change.Property == OrientationProperty)
        {
            UpdatePseudoClasses(value);
        }
    }

    private static void CoerceLength(ref double componentLength, double trackLength)
    {
        if (componentLength < 0)
            componentLength = 0.0;
        else if (componentLength > trackLength || double.IsNaN(componentLength))
            componentLength = trackLength;
    }

    private void ComputeSliderLengths(Size arrangeSize, bool isVertical, out double thumbLength,
        out double backgroundButtonLength, out double foregroundButtonLength,
        out double lowerThumbOffset, out double upperThumbOffset)
    {
        var range = Math.Max(0.0, Maximum - Minimum);
        var offsetLower = Math.Min(range, LowerSelectedValue - Minimum);
        var offsetUpper = Math.Min(range, Maximum - UpperSelectedValue);

        // Compute thumbs size
        var sliderLength = isVertical ? arrangeSize.Height : arrangeSize.Width;
        thumbLength = isVertical ? LowerThumb?.DesiredSize.Height ?? 0.0 : LowerThumb?.DesiredSize.Width ?? 0.0;

        CoerceLength(ref thumbLength, sliderLength);

        // Compute lengths of increase, middle and decrease button
        var trackLength = sliderLength - thumbLength;
        var effectiveTrackLength = sliderLength - (IsThumbOverlap ? thumbLength : 2.0 * thumbLength);

        backgroundButtonLength = trackLength;
        CoerceLength(ref backgroundButtonLength, trackLength);

        if (isVertical)
        {
            lowerThumbOffset = effectiveTrackLength - (effectiveTrackLength * offsetLower / range);
            upperThumbOffset = effectiveTrackLength * offsetUpper / range;
            lowerThumbOffset += IsThumbOverlap ? 0 : thumbLength;
        }
        else
        {
            lowerThumbOffset = effectiveTrackLength * offsetLower / range;
            upperThumbOffset = effectiveTrackLength - (effectiveTrackLength * offsetUpper / range);
            upperThumbOffset += IsThumbOverlap ? 0 : thumbLength;
        }

        foregroundButtonLength = Math.Abs(upperThumbOffset - lowerThumbOffset);
        CoerceLength(ref foregroundButtonLength, trackLength);
    }

    private bool ComputeScrollBarLengths(Size arrangeSize, double viewportSize, bool isVertical,
        out double thumbLength,out double backgroundButtonLength, out double foregroundButtonLength,
        out double lowerThumbOffset, out double upperThumbOffset)
    {
        var range = Math.Max(0.0, Maximum - Minimum);
        var offsetLower = Math.Min(range, LowerSelectedValue - Minimum);
        var offsetUpper = Math.Min(range, Maximum - UpperSelectedValue);
        var extent = Math.Max(0.0, range) + viewportSize;
        var sliderLength = isVertical ? arrangeSize.Height : arrangeSize.Width;
        var thumbMinLength = 10.0;

        var minLengthProperty = isVertical ? MinHeightProperty : MinWidthProperty;

        if (LowerThumb != null && LowerThumb.IsSet(minLengthProperty))
        {
            thumbMinLength = LowerThumb.GetValue(minLengthProperty);
        }

        thumbLength = sliderLength * viewportSize / extent;
        CoerceLength(ref thumbLength, sliderLength);
        thumbLength = Math.Max(thumbMinLength, thumbLength);

        // If we don't have enough content to scroll, disable the track.
        var notEnoughContentToScroll = MathUtilities.LessThanOrClose(range, 0.0);
        var thumbLongerThanTrack = thumbLength > sliderLength;

        // if there's not enough content or the thumb is longer than the track,
        // hide the track and don't arrange the pieces
        if (notEnoughContentToScroll || thumbLongerThanTrack)
        {
            ShowChildren(false);
            backgroundButtonLength = 0.0;
            foregroundButtonLength = 0.0;
            lowerThumbOffset = 0.0;
            upperThumbOffset = 0.0;
            return false; // don't arrange
        }
        else
        {
            ShowChildren(true);
        }

        // Compute lengths of increase, middle and decrease button
        var trackLength = sliderLength - thumbLength;
        var effectiveTrackLength = sliderLength - (IsThumbOverlap ? thumbLength : 2.0 * thumbLength);

        backgroundButtonLength = trackLength;
        CoerceLength(ref backgroundButtonLength, trackLength);

        if (isVertical)
        {
            lowerThumbOffset = effectiveTrackLength - (effectiveTrackLength * offsetLower / range);
            upperThumbOffset = effectiveTrackLength * offsetUpper / range;
            lowerThumbOffset += IsThumbOverlap ? 0 : thumbLength;
        }
        else
        {
            lowerThumbOffset = effectiveTrackLength * offsetLower / range;
            upperThumbOffset = effectiveTrackLength - (effectiveTrackLength * offsetUpper / range);
            upperThumbOffset += IsThumbOverlap ? 0 : thumbLength;
        }

        foregroundButtonLength = Math.Abs(upperThumbOffset - lowerThumbOffset);
        CoerceLength(ref foregroundButtonLength, trackLength);

        return true;
    }

    private void ThumbChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is Thumb oldThumb)
        {
            oldThumb.DragDelta -= DummyThumbDragged;

            LogicalChildren.Remove(oldThumb);
            VisualChildren.Remove(oldThumb);
        }

        if (e.NewValue is Thumb newThumb)
        {
            newThumb.DragDelta += DummyThumbDragged;
            LogicalChildren.Add(newThumb);
            VisualChildren.Add(newThumb);
        }
    }

    private void DummyThumbDragged(object? sender, VectorEventArgs e)
    { }

    private void ButtonChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is Button oldButton)
        {
            LogicalChildren.Remove(oldButton);
            VisualChildren.Remove(oldButton);
        }

        if (e.NewValue is Button newButton)
        {
            LogicalChildren.Add(newButton);
            VisualChildren.Add(newButton);
        }
    }

    private void ShowChildren(bool visible)
    {
        // WPF sets Visible = Hidden here but we don't have that, and setting IsVisible = false
        // will cause us to stop being laid out. Instead show/hide the child controls.
        if (LowerThumb != null)
            LowerThumb.IsVisible = visible;

        if (MiddleThumb != null)
            MiddleThumb.IsVisible = visible;

        if (UpperThumb != null)
            UpperThumb.IsVisible = visible;

        if (BackgroundButton != null)
            BackgroundButton.IsVisible = visible;

        if (ForegroundButton != null)
            ForegroundButton.IsVisible = visible;
    }

    private void UpdatePseudoClasses(Orientation o)
    {
        PseudoClasses.Set(":vertical", o == Orientation.Vertical);
        PseudoClasses.Set(":horizontal", o == Orientation.Horizontal);
    }

    private double ComputeMiddleThumbOffset(Size arrangeSize, bool isVertical, double thumbLength)
    {
        var range = Math.Max(0.0, Maximum - Minimum);
        var middleOffset = Math.Min(range, MiddleSelectedValue - Minimum);
        var sliderLength = isVertical ? arrangeSize.Height : arrangeSize.Width;
        var effectiveTrackLength = sliderLength - (IsThumbOverlap ? thumbLength : 2.0 * thumbLength);

        if (isVertical)
        {
            return effectiveTrackLength - (effectiveTrackLength * middleOffset / range) + (IsThumbOverlap ? 0 : thumbLength);
        }
        else
        {
            return effectiveTrackLength * middleOffset / range;
        }
    }
}
