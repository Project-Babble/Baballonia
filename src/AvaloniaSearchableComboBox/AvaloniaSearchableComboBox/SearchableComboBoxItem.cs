using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using System;
using System.Reactive.Linq;

namespace AvaloniaSearchableComboBox
{
    /// <summary>
    /// A selectable item in a <see cref="SearchableComboBox"/>.
    /// </summary>
    public class SearchableComboBoxItem : ListBoxItem
    {
        public SearchableComboBoxItem()
        {
            this.GetPropertyChangedObservable(SearchableComboBoxItem.IsFocusedProperty)
                .Subscribe(args =>
                {
                    if (args.NewValue is bool focused && focused)
                    {
                        (Parent as SearchableComboBox)?.ItemFocused(this);
                    }
                });
        }

        static SearchableComboBoxItem()
        {
            AutomationProperties.ControlTypeOverrideProperty.OverrideDefaultValue<SearchableComboBoxItem>(AutomationControlType.ComboBoxItem);
        }

        public override string ToString()
        {
            return Content?.ToString() ?? base.ToString();
        }
    }
}