using System;
using System.Collections.Generic;

namespace AvaloniaMiaDev.Models;

public class NotificationModel
{
    public readonly string Title;
    public readonly string Body;
    public readonly string? BodyImagePath;
    public readonly string? BodyAltText;
    public readonly List<(string Title, string ActionId)?> ActionButtons;
    public readonly DateTimeOffset? OptionalScheduledTime;
    public readonly DateTimeOffset? OptionalExpirationTime;

    public NotificationModel(string title, string body, string? bodyImagePath, string? bodyAltText, List<(string Title, string ActionId)?> actionButtons, DateTimeOffset? optionalScheduledTime, DateTimeOffset? optionalExpirationTime)
    {
        Title = title;
        Body = body;
        BodyImagePath = bodyImagePath;
        BodyAltText = bodyAltText;
        ActionButtons = actionButtons;
        OptionalScheduledTime = optionalScheduledTime;
        OptionalExpirationTime = optionalExpirationTime;
    }
}
