using SentinelStream.Models;

namespace SentinelStream.Services;

/// <summary>
/// Loads configuration from .env file into AppConfig.
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Resolves .env from the given path, the current working directory, or next to the executable.
    /// </summary>
    public static string? ResolveEnvFilePath(string envFilePath = ".env")
    {
        if (File.Exists(envFilePath))
            return Path.GetFullPath(envFilePath);

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), envFilePath);
        if (File.Exists(cwd))
            return cwd;

        var exeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, envFilePath);
        if (File.Exists(exeDir))
            return exeDir;

        return null;
    }

    /// <summary>
    /// Loads .env when present; otherwise returns defaults (no throw).
    /// </summary>
    public static AppConfig LoadFromEnvFileOrDefault(string envFilePath = ".env")
    {
        var resolved = ResolveEnvFilePath(envFilePath);
        return resolved != null ? ParseEnvFile(resolved) : new AppConfig();
    }

    /// <summary>
    /// Loads the .env file from the given path and returns an AppConfig.
    /// </summary>
    /// <exception cref="FileNotFoundException">When no .env file can be resolved.</exception>
    public static AppConfig LoadFromEnvFile(string envFilePath = ".env")
    {
        var resolved = ResolveEnvFilePath(envFilePath)
            ?? throw new FileNotFoundException(
                "Configuration file .env not found. Run setup_env.ps1 or copy .env.example to .env.",
                envFilePath);
        return ParseEnvFile(resolved);
    }

    private static AppConfig ParseEnvFile(string fullPath)
    {
        var config = new AppConfig();
        var lines = File.ReadAllLines(fullPath);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0) continue;

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();

            switch (key)
            {
                case "AGORA_APP_ID":
                    config.AgoraAppId = value;
                    break;
                case "AGORA_APP_CERTIFICATE":
                    config.AgoraAppCertificate = value;
                    break;
                case "ENCRYPTION_KEY":
                    config.EncryptionKey = value;
                    break;
                case "FORENSIC_SALT":
                    config.ForensicSalt = value;
                    break;
                case "LOG_SERVER_URL":
                    config.LogServerUrl = value;
                    break;
                case "DEMO_LOG_FEED":
                    config.EnableDemoLogFeed = ParseBool(value, defaultValue: true);
                    break;
            }
        }

        return config;
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => defaultValue
        };
    }
}
