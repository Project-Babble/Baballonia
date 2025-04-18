using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace AvaloniaMiaDev.Controls;

public partial class WarningBlock : UserControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<WarningBlock, string>(nameof(Header));

    public static readonly StyledProperty<string> SubheaderProperty =
        AvaloniaProperty.Register<WarningBlock, string>(nameof(Subheader));

    public static readonly StyledProperty<string> CaptionProperty =
        AvaloniaProperty.Register<WarningBlock, string>(nameof(Caption));

    public static readonly StyledProperty<ICommand> NavigateCommandProperty =
        AvaloniaProperty.Register<WarningBlock, ICommand>(nameof(NavigateCommand));

    public static readonly StyledProperty<ICommand> DismissCommandProperty =
        AvaloniaProperty.Register<WarningBlock, ICommand>(nameof(DismissCommand));

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Subheader
    {
        get => GetValue(SubheaderProperty);
        set => SetValue(SubheaderProperty, value);
    }

    public string Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public ICommand NavigateCommand
    {
        get => GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    public ICommand DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public WarningBlock()
    {
        InitializeComponent();
    }
}
