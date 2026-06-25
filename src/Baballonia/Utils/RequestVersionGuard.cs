using Baballonia.Attributes;
using System;
using System.Reflection;

namespace Baballonia;

public class RequestVersionGuard
{
    public static void ValidateRequestForVersion(object request, Version apiVersion)
    {
        var attr = request.GetType().GetCustomAttribute<ApiVersionRangeAttribute>();
        if (attr == null)
            throw new InvalidOperationException($"Request {request.GetType().Name} has no version metadata.");
        if (!attr.IsAllowed(apiVersion))
            throw new NotSupportedException($"{request.GetType().Name} not valid for API v{apiVersion}");
    }

    /// <summary>
    /// Non-throwing version check: true if the request is allowed for the given firmware version.
    /// Lets callers skip unsupported requests (e.g. SetPausedRequest on v2 firmware) instead of
    /// catching a NotSupportedException.
    /// </summary>
    public static bool IsSupported(object request, Version apiVersion)
    {
        var attr = request.GetType().GetCustomAttribute<ApiVersionRangeAttribute>();
        return attr != null && attr.IsAllowed(apiVersion);
    }
}
