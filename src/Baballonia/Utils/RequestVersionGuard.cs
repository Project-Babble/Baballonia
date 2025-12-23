using System;
using System.Reflection;
using Baballonia.Attributes;

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
}
