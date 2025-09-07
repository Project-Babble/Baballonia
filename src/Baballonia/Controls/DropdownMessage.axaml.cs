using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace Baballonia.Controls;


public class ButtonAction
{
    public string Text { get; set; } = string.Empty;
    public IRelayCommand? Command { get; set; }
}

public partial class DropdownMessage : UserControl
{
    public static readonly StyledProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.Register<DropdownMessage, IImage?>(nameof(ImageSource));
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<DropdownMessage, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<DropdownMessage, string>(nameof(Message), string.Empty);
    public static readonly StyledProperty<string> AcceptProperty =
        AvaloniaProperty.Register<DropdownMessage, string>(nameof(AcceptText), string.Empty);
    public static readonly StyledProperty<string> DeclineProperty =
        AvaloniaProperty.Register<DropdownMessage, string>(nameof(DeclineText), string.Empty);
    public static readonly StyledProperty<IRelayCommand?> AcceptCommandProperty =
        AvaloniaProperty.Register<DropdownMessage, IRelayCommand?>(nameof(AcceptCommand));
    public static readonly StyledProperty<IRelayCommand?> DeclineCommandProperty =
        AvaloniaProperty.Register<DropdownMessage, IRelayCommand?>(nameof(DeclineCommand));
    public IImage? ImageSource
    {
        get => GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
    public string AcceptText
    {
        get => GetValue(AcceptProperty);
        set => SetValue(AcceptProperty, value);
    }
    public string DeclineText
    {
        get => GetValue(DeclineProperty);
        set => SetValue(DeclineProperty, value);
    }

    public IRelayCommand? AcceptCommand
    {
        get => GetValue(AcceptCommandProperty);
        set => SetValue(AcceptCommandProperty, value);
    }
    public IRelayCommand? DeclineCommand
    {
        get => GetValue(DeclineCommandProperty);
        set => SetValue(DeclineCommandProperty, value);
    }

    public DropdownMessage()
    {
        InitializeComponent();
    }
}

