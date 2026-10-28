using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Baballonia.Models;

/// <summary>
/// Raised when the user toggles Settings &gt; Advanced &gt; "Show Debug menu", so the navigation
/// sidebar (<see cref="ViewModels.MainViewModel"/>) can add or remove the Debug page entry live.
/// </summary>
public sealed class ShowDebugMenuChangedMessage(bool value) : ValueChangedMessage<bool>(value);
