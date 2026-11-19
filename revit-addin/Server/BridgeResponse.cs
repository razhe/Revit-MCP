using System.Reflection;

namespace RevitMcp.Server;

public sealed record BridgeResponse(bool Ok, object? Result, string? Error)
{
    public static BridgeResponse Success(object? result) => new(true, result, null);

    public static BridgeResponse Failure(Exception exception)
    {
        var root = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
        return new BridgeResponse(false, null, root.Message);
    }
}
