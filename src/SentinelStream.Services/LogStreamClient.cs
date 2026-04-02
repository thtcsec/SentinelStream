using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SentinelStream.Models;

namespace SentinelStream.Services;

/// <summary>
/// WebSocket client that connects to the Python log_exporter agent
/// and streams LogEntry objects to the application.
/// </summary>
public class LogStreamClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event EventHandler<LogEntry>? LogReceived;
    public event EventHandler<string>? ConnectionStatusChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    /// <summary>
    /// Connects to the log server and begins streaming logs.
    /// </summary>
    public async Task ConnectAsync(string serverUrl)
    {
        _cts = new CancellationTokenSource();
        _webSocket = new ClientWebSocket();

        try
        {
            ConnectionStatusChanged?.Invoke(this, $"Connecting to {serverUrl}...");
            await _webSocket.ConnectAsync(new Uri(serverUrl), _cts.Token);
            ConnectionStatusChanged?.Invoke(this, "Connected to log server.");

            // Start listening in background
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            ConnectionStatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    ConnectionStatusChanged?.Invoke(this, "Server closed the connection.");
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                // Try to parse as JSON LogEntry, otherwise create a raw text entry
                var logEntry = TryParseLogEntry(message);
                LogReceived?.Invoke(this, logEntry);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
            ConnectionStatusChanged?.Invoke(this, $"Connection lost: {ex.Message}");
        }
    }

    private static LogEntry TryParseLogEntry(string message)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<LogEntry>(message, JsonOptions);
            if (parsed != null) return parsed;
        }
        catch
        {
            // Not JSON - parse as raw log text
        }

        // Parse raw syslog-style text
        var severity = LogSeverity.Info;
        if (message.Contains("ERR", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            severity = LogSeverity.Error;
        else if (message.Contains("WARN", StringComparison.OrdinalIgnoreCase))
            severity = LogSeverity.Warning;
        else if (message.Contains("CRIT", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("ALERT", StringComparison.OrdinalIgnoreCase))
            severity = LogSeverity.Critical;

        return new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Severity = severity,
            Source = "Agent",
            Message = message,
            RawData = message
        };
    }

    /// <summary>
    /// Disconnects from the log server.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            _cts?.Cancel();
            await _webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
        }

        ConnectionStatusChanged?.Invoke(this, "Disconnected from log server.");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _webSocket?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
