using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SentinelStream.Core.Agora;
using SentinelStream.Core.Forensics;
using SentinelStream.Models;
using SentinelStream.Services;

namespace SentinelStream.App.ViewModels;

/// <summary>
/// ViewModel for the War Room dashboard — displays logs, participants, chat, and connection status.
/// </summary>
public class WarRoomViewModel : ViewModelBase, IDisposable
{
    private readonly AgoraWarRoomClient _agoraClient;
    private readonly WarRoomFeedOptions _feedOptions;
    private readonly SessionArtifactOptions _artifactOptions;
    private LogStreamClient? _logStreamClient;
    private string _connectionStatus = "Disconnected";
    private string _channelName = string.Empty;
    private string _chatInput = string.Empty;
    private string _displayName = string.Empty;
    private string _logFeedSummary = "Log agent: not configured (set LOG_SERVER_URL in .env).";
    private string _sessionModeFootnote =
        "RTC channel is a local stub until Agora SDK is integrated. Keys in .env are for future use.";
    private string _logFilterText = string.Empty;
    private readonly ListCollectionView _logMessagesView;

    public ObservableCollection<string> LogMessages { get; } = new();

    /// <summary>UI binds here; backing store is <see cref="LogMessages"/> (export uses full list).</summary>
    public ICollectionView LogMessagesView => _logMessagesView;
    public ObservableCollection<string> ChatMessages { get; } = new();
    public ObservableCollection<string> Participants { get; } = new();

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public string ChannelName
    {
        get => _channelName;
        set => SetProperty(ref _channelName, value);
    }

    public string ChatInput
    {
        get => _chatInput;
        set => SetProperty(ref _chatInput, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string LogFeedSummary
    {
        get => _logFeedSummary;
        set => SetProperty(ref _logFeedSummary, value);
    }

    public string SessionModeFootnote
    {
        get => _sessionModeFootnote;
        set => SetProperty(ref _sessionModeFootnote, value);
    }

    /// <summary>Substring filter applied to displayed log lines (case-insensitive).</summary>
    public string LogFilterText
    {
        get => _logFilterText;
        set
        {
            if (SetProperty(ref _logFilterText, value))
                _logMessagesView.Refresh();
        }
    }

    public ICommand SendChatCommand { get; }
    public ICommand LeaveCommand { get; }

    public event EventHandler? LeaveRequested;

    public WarRoomViewModel(
        AgoraWarRoomClient agoraClient,
        WarRoomFeedOptions feedOptions,
        SessionArtifactOptions? sessionArtifacts = null)
    {
        _agoraClient = agoraClient;
        _feedOptions = feedOptions;
        _artifactOptions = sessionArtifacts ?? new SessionArtifactOptions();

        _logMessagesView = new ListCollectionView(LogMessages);
        _logMessagesView.Filter = LogLineFilter;

        SendChatCommand = new RelayCommand(
            execute: _ => OnSendChat(),
            canExecute: _ => !string.IsNullOrWhiteSpace(ChatInput)
        );

        LeaveCommand = new RelayCommand(
            execute: _ => OnLeave(),
            canExecute: _ => true
        );

        _agoraClient.StateChanged += OnStateChanged;
        _agoraClient.ParticipantChanged += OnParticipantChanged;
        _agoraClient.LogReceived += OnLogReceived;
        _agoraClient.ChatMessageReceived += OnChatReceived;

        RefreshLogFeedSummaryStaticPart();
    }

    private bool LogLineFilter(object obj)
    {
        if (obj is not string line) return false;
        if (string.IsNullOrWhiteSpace(_logFilterText)) return true;
        return line.Contains(_logFilterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshLogFeedSummaryStaticPart()
    {
        if (_feedOptions.LogAgentWebSocketUri == null)
            LogFeedSummary = "Log agent: not configured (set LOG_SERVER_URL in .env).";
        else
            LogFeedSummary = $"Log agent: connecting to {_feedOptions.LogAgentWebSocketUri}…";
    }

    public async Task JoinAsync(string channelName, string displayName)
    {
        ChannelName = channelName;
        DisplayName = displayName;
        var userId = Guid.NewGuid().ToString("N")[..8];

        await _agoraClient.JoinWarRoomAsync(channelName, userId, displayName);

        if (_feedOptions.LogAgentWebSocketUri != null)
            StartLogAgentClient(_feedOptions.LogAgentWebSocketUri);

        if (_feedOptions.EnableDemoLogFeed)
            _ = Task.Run(SimulateLogsAsync);
    }

    private void StartLogAgentClient(Uri uri)
    {
        _logStreamClient?.Dispose();
        _logStreamClient = new LogStreamClient();
        _logStreamClient.LogReceived += OnAgentLogReceived;
        _logStreamClient.ConnectionStatusChanged += OnAgentConnectionStatusChanged;
        _logStreamClient.ErrorOccurred += OnAgentError;

        var url = uri.ToString();
        LogFeedSummary = $"Log agent: connecting to {uri.Host}:{uri.Port}…";
        _ = _logStreamClient.ConnectAsync(url);
    }

    private void OnAgentConnectionStatusChanged(object? sender, string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_feedOptions.LogAgentWebSocketUri != null)
            {
                var host = _feedOptions.LogAgentWebSocketUri.Host;
                LogFeedSummary = $"Log agent ({host}): {message}";
            }
            else
                LogFeedSummary = $"Log agent: {message}";
        });
    }

    private void OnAgentError(object? sender, Exception ex)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LogMessages.Add($"[AGENT ERROR] {ex.Message}");
        });
    }

    private void OnAgentLogReceived(object? sender, LogEntry entry)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            AppendLogLine(entry.Timestamp, entry.Severity.ToString(), entry.Message, prefix: "AGENT");
        });
    }

    private void AppendLogLine(DateTime timestamp, string severity, string message, string prefix = "LOG")
    {
        var severityTag = severity.ToUpperInvariant();
        LogMessages.Add($"[{timestamp:HH:mm:ss}] [{prefix}] [{severityTag}] {message}");
        TrimLogBuffer();
    }

    private void TrimLogBuffer()
    {
        while (LogMessages.Count > 200)
            LogMessages.RemoveAt(0);
    }

    private async Task SimulateLogsAsync()
    {
        var random = new Random();
        var sampleLogs = new[]
        {
            ("Critical", "ALERT: Unauthorized SSH login detected from 10.0.0.55"),
            ("Error", "ERROR: Firewall rule bypass attempt on port 443"),
            ("Warning", "WARN: Unusual outbound traffic spike to 185.220.101.x (TOR Exit Node)"),
            ("Info", "INF: IDS signature update completed successfully"),
            ("Critical", "ALERT: Malware C2 beacon detected — hash: a3f2b8c1d9e..."),
            ("Warning", "WARN: Failed login attempt #47 for admin@corp-dc01"),
            ("Info", "INF: Forensic snapshot initiated for /var/log/auth.log"),
            ("Error", "ERROR: Memory dump failed on endpoint WS-PC-0042"),
            ("Info", "INF: Network segment 10.0.2.0/24 quarantined successfully"),
            ("Critical", "ALERT: Lateral movement detected — PsExec from DC01 to FS02"),
        };

        while (_agoraClient.CurrentState == SessionState.Connected)
        {
            await Task.Delay(random.Next(1500, 4000));

            var (severity, message) = sampleLogs[random.Next(sampleLogs.Length)];
            var timestamp = DateTime.UtcNow;

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                AppendLogLine(timestamp, severity, message, prefix: "DEMO");
            });
        }
    }

    private void OnStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            ConnectionStatus = e.NewState.ToString();
            LogMessages.Add($"[SYSTEM] State: {e.OldState} → {e.NewState} | {e.Message}");
            TrimLogBuffer();
        });
    }

    private void OnParticipantChanged(object? sender, ParticipantChangedEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (e.Action == ParticipantAction.Joined)
                Participants.Add($"🟢 {e.DisplayName}");
            else
                Participants.Remove($"🟢 {e.DisplayName}");
        });
    }

    private void OnLogReceived(object? sender, LogReceivedEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            AppendLogLine(e.Log.Timestamp, e.Log.Severity.ToString(), e.Log.Message, prefix: "ROOM");
        });
    }

    private void OnChatReceived(object? sender, string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            ChatMessages.Add($"[{DisplayName}] {message}");
        });
    }

    private async void OnSendChat()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;

        var msg = ChatInput;
        ChatInput = string.Empty;

        ChatMessages.Add($"[{DisplayName}] {msg}");
        await _agoraClient.SendChatMessageAsync(msg);
    }

    private async void OnLeave()
    {
        await TryExportSessionArtifactAsync();

        if (_logStreamClient != null)
        {
            try
            {
                await _logStreamClient.DisconnectAsync();
            }
            catch
            {
                /* ignore */
            }

            _logStreamClient.LogReceived -= OnAgentLogReceived;
            _logStreamClient.ConnectionStatusChanged -= OnAgentConnectionStatusChanged;
            _logStreamClient.ErrorOccurred -= OnAgentError;
            _logStreamClient.Dispose();
            _logStreamClient = null;
        }

        await _agoraClient.LeaveWarRoomAsync();
        LeaveRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task TryExportSessionArtifactAsync()
    {
        if (!_artifactOptions.ShouldExport || _artifactOptions.ForensicSalt is not { Length: > 0 } salt)
            return;

        List<string> lines;
        string channelSnap;
        try
        {
            (lines, channelSnap) = await Application.Current!.Dispatcher.InvokeAsync(() =>
                (LogMessages.ToList(), ChannelName));
        }
        catch
        {
            return;
        }

        var dir = string.IsNullOrWhiteSpace(_artifactOptions.ExportDirectory)
            ? Path.GetTempPath()
            : _artifactOptions.ExportDirectory!;

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    $"Could not create export directory:\n{dir}\n\n{ex.Message}",
                    "SentinelStream — session export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            return;
        }

        var safeChannel = string.Join("_", (channelSnap ?? "room").Split(Path.GetInvalidFileNameChars()));
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(dir, $"sentinelstream_{safeChannel}_{stamp}_utc.log");

        try
        {
            await File.WriteAllLinesAsync(path, lines);
            var hash = await ForensicHasher.ComputeFileHashAsync(path, salt);
            var report = await ForensicHasher.GenerateForensicReportAsync(path, salt, channelSnap ?? "");
            var reportPath = Path.Combine(dir, $"sentinelstream_{safeChannel}_{stamp}_utc_forensic.txt");
            await File.WriteAllTextAsync(reportPath, report);

            Application.Current?.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    $"Session log and forensic report saved.\n\nLog:\n{path}\n\nReport:\n{reportPath}\n\nSHA-256 (salted):\n{hash}",
                    "SentinelStream — session export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information));
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    $"Session export failed:\n{ex.Message}",
                    "SentinelStream — session export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
        }
    }

    public void Dispose()
    {
        _agoraClient.StateChanged -= OnStateChanged;
        _agoraClient.ParticipantChanged -= OnParticipantChanged;
        _agoraClient.LogReceived -= OnLogReceived;
        _agoraClient.ChatMessageReceived -= OnChatReceived;

        if (_logStreamClient != null)
        {
            _logStreamClient.LogReceived -= OnAgentLogReceived;
            _logStreamClient.ConnectionStatusChanged -= OnAgentConnectionStatusChanged;
            _logStreamClient.ErrorOccurred -= OnAgentError;
            _logStreamClient.Dispose();
            _logStreamClient = null;
        }

        _agoraClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
