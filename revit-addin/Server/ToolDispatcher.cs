using System.Collections.Concurrent;
using System.Text.Json;
using Autodesk.Revit.UI;
using RevitMcp.Tools;

namespace RevitMcp.Server;

/// <summary>
/// Encola las peticiones que llegan por el socket y las ejecuta en el hilo
/// principal de Revit mediante un ExternalEvent, único contexto donde la API es válida.
/// </summary>
public sealed class ToolDispatcher : IExternalEventHandler
{
    private readonly IReadOnlyDictionary<string, IMcpTool> _tools;
    private readonly ConcurrentQueue<PendingRequest> _queue = new();
    private readonly ExternalEvent _externalEvent;

    public ToolDispatcher(IEnumerable<IMcpTool> tools)
    {
        _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        _externalEvent = ExternalEvent.Create(this);
    }

    public async Task<object?> DispatchAsync(string name, JsonElement parameters)
    {
        if (!_tools.TryGetValue(name, out var tool))
            throw new InvalidOperationException($"Herramienta desconocida: '{name}'.");

        var request = new PendingRequest(tool, new ToolParams(parameters));
        _queue.Enqueue(request);
        _externalEvent.Raise();

        try
        {
            return await request.Completion.Task.WaitAsync(BridgeSettings.RequestTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Revit no respondió a tiempo. Revisa si hay un diálogo abierto o un comando en curso.");
        }
    }

    public void Execute(UIApplication app)
    {
        var context = new ToolContext(app);
        while (_queue.TryDequeue(out var request))
        {
            try
            {
                request.Completion.SetResult(request.Tool.Execute(context, request.Parameters));
            }
            catch (Exception ex)
            {
                request.Completion.SetException(ex);
            }
        }
    }

    public string GetName() => "Revit MCP";

    private sealed record PendingRequest(IMcpTool Tool, ToolParams Parameters)
    {
        public TaskCompletionSource<object?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
