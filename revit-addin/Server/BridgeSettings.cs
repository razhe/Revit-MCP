using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitMcp.Server;

internal static class BridgeSettings
{
    private const int DefaultPort = 8765;

    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("REVIT_MCP_PORT"), out var port) ? port : DefaultPort;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
}
