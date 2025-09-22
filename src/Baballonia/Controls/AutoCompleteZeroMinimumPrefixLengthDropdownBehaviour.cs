using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;

namespace Baballonia.Controls
{
    // https://github.com/AvaloniaUI/Avalonia/discussions/13301
    public class AutoCompleteZeroMinimumPrefixLengthDropdownBehaviour : Behavior<AutoCompleteBox>
    {
        // Cached reflection members
        private static readonly PropertyInfo? TextBoxProperty;
        private static readonly MethodInfo? PopulateDropDownMethod;
        private static readonly MethodInfo? OpeningDropDownMethod;
        private static readonly FieldInfo? IgnorePropertyChangeField;

        private readonly Path _chevron = new()
        {
            Data = Avalonia.Media.Geometry.Parse("M8.36612 16.1161C7.87796 16.6043 7.87796 17.3957 8.36612 17.8839L23.1161 32.6339C23.6043 33.122 24.3957 33.122 24.8839 32.6339L39.6339 17.8839C40.122 17.3957 40.122 16.6043 39.6339 16.1161C39.1457 15.628 38.3543 15.628 37.8661 16.1161L24 29.9822L10.1339 16.1161C9.64573 15.628 8.85427 15.628 8.36612 16.1161Z"),
            Fill = Avalonia.Media.Brushes.Gray,
            StrokeThickness = 0.5,
            Width = 12,
            Height = 12,
            Stretch = Avalonia.Media.Stretch.Uniform
        };

        static AutoCompleteZeroMinimumPrefixLengthDropdownBehaviour()
        {
            var autoCompleteBoxType = typeof(AutoCompleteBox);
            var bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;

            // Cache all reflection members
            TextBoxProperty = autoCompleteBoxType.GetProperty("TextBox", bindingFlags);
            PopulateDropDownMethod = autoCompleteBoxType.GetMethod("PopulateDropDown", bindingFlags);
            OpeningDropDownMethod = autoCompleteBoxType.GetMethod("OpeningDropDown", bindingFlags);
            IgnorePropertyChangeField = autoCompleteBoxType.GetField("_ignorePropertyChange", bindingFlags);
        }

        protected override void OnAttached()
        {
            if (AssociatedObject is not null)
            {
                AssociatedObject.KeyUp += OnKeyUp;
                AssociatedObject.DropDownOpening += DropDownOpening;
                AssociatedObject.GotFocus += OnGotFocus;
                AssociatedObject.PointerReleased += PointerReleased;

                Task.Delay(500).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Invoke(() => { CreateDropdownButton(); }));
            }

            base.OnAttached();
        }

        protected override void OnDetaching()
        {
            if (AssociatedObject is not null)
            {
                AssociatedObject.KeyUp -= OnKeyUp;
                AssociatedObject.DropDownOpening -= DropDownOpening;
                AssociatedObject.GotFocus -= OnGotFocus;
                AssociatedObject.PointerReleased -= PointerReleased;
            }

            base.OnDetaching();
        }

        //have to use KeyUp as AutoCompleteBox eats some of the KeyDown events
        private void OnKeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if ((e.Key == Avalonia.Input.Key.Down || e.Key == Avalonia.Input.Key.F4))
            {
                if (string.IsNullOrEmpty(AssociatedObject?.Text))
                {
                    ShowDropdown();
                }
            }
        }

        private void DropDownOpening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var tb = GetTextBox();
            if (tb is not null && tb.IsReadOnly)
            {
                e.Cancel = true;
            }
        }

        private void PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            if (string.IsNullOrEmpty(AssociatedObject?.Text))
            {
                ShowDropdown();
            }
        }

        private TextBox? GetTextBox()
        {
            if (AssociatedObject is null || TextBoxProperty is null)
                return null;

            return (TextBox?)TextBoxProperty.GetValue(AssociatedObject);
        }

        private void ShowDropdown()
        {
            if (AssociatedObject is not null && !AssociatedObject.IsDropDownOpen)
            {
                // Use cached reflection methods
                PopulateDropDownMethod?.Invoke(AssociatedObject, new object[] { AssociatedObject, EventArgs.Empty });
                OpeningDropDownMethod?.Invoke(AssociatedObject, new object[] { false });

                if (AssociatedObject.IsDropDownOpen) return;

                //We *must* set the field and not the property as we need to avoid the changed event being raised (which prevents the dropdown opening).
                if (IgnorePropertyChangeField is not null)
                {
                    var currentValue = (bool?)IgnorePropertyChangeField.GetValue(AssociatedObject);
                    if (currentValue == false)
                    {
                        IgnorePropertyChangeField.SetValue(AssociatedObject, true);
                    }
                }

                AssociatedObject.SetCurrentValue(AutoCompleteBox.IsDropDownOpenProperty, true);
            }
        }

        private void CreateDropdownButton()
        {
            if (AssociatedObject == null) return;

            var tb = GetTextBox();
            if (tb is null || tb.InnerRightContent is Button) return;
            var btn = new Button
            {
                Content = _chevron,
                Padding = new Thickness(6, 6, 6, 2),
                Margin = new(3),
                ClickMode = ClickMode.Press,
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            btn.Click += (s, e) =>
            {
                ShowDropdown();
            };

            tb.InnerRightContent = btn;
        }

        private void OnGotFocus(object? sender, RoutedEventArgs e)
        {
            if (AssociatedObject != null)
            {
                CreateDropdownButton();
            }
        }
    }
}
