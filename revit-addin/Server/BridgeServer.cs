using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RevitMcp.Server;

/// <summary>
/// Servidor TCP local que recibe una petición JSON por línea
/// ({"command": "...", "params": {...}}) y responde otra línea JSON
/// ({"ok": true, "result": ...} o {"ok": false, "error": "..."}).
/// </summary>
public sealed class BridgeServer(int port, ToolDispatcher dispatcher) : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;

    public bool IsRunning => _listener is not null;

    public void Start()
    {
        if (IsRunning)
            return;

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        _listener = listener;
        _cancellation = new CancellationTokenSource();
        _ = AcceptClientsAsync(listener, _cancellation.Token);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _listener?.Stop();
        _cancellation?.Dispose();
        _cancellation = null;
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptClientsAsync(TcpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellation);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = HandleClientAsync(client, cancellation);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellation)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            while (!cancellation.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellation);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }

                if (line is null)
                    return;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                await writer.WriteLineAsync(await ProcessAsync(line));
            }
        }
    }

    private async Task<string> ProcessAsync(string line)
    {
        BridgeResponse response;
        try
        {
            using var request = JsonDocument.Parse(line);
            var root = request.RootElement;
            var command = root.GetProperty("command").GetString() ?? string.Empty;
            var parameters = root.TryGetProperty("params", out var value) ? value.Clone() : default;
            response = BridgeResponse.Success(await dispatcher.DispatchAsync(command, parameters));
        }
        catch (Exception ex)
        {
            response = BridgeResponse.Failure(ex);
        }

        return JsonSerializer.Serialize(response, BridgeSettings.Json);
    }
}
