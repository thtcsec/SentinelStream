namespace SentinelStream.Models;

/// <summary>
/// Runtime options for log feeds (built from <see cref="AppConfig"/>).
/// Keeps the view model free of env key names and parsing rules.
/// </summary>
public sealed class WarRoomFeedOptions
{
    /// <summary>WebSocket URL for the Python log agent, or null if not configured.</summary>
    public Uri? LogAgentWebSocketUri { get; init; }

    /// <summary>When true, injects simulated SOC-style lines for demos or offline use.</summary>
    public bool EnableDemoLogFeed { get; init; } = true;
}
