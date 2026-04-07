using System.Security.Cryptography;
using System.Text;

namespace SentinelStream.Core.Forensics;

/// <summary>
/// Provides forensic integrity verification for recordings and evidence files.
/// Uses SHA-256 with a configurable salt to create tamper-evident hashes.
/// </summary>
public static class ForensicHasher
{
    /// <summary>
    /// Computes a SHA-256 hash of the given file, prepended with the forensic salt.
    /// This ensures the hash cannot be forged without knowledge of the salt.
    /// </summary>
    /// <param name="filePath">Path to the file to hash.</param>
    /// <param name="salt">Forensic salt from configuration.</param>
    /// <returns>Hex string of the salted SHA-256 hash.</returns>
    public static async Task<string> ComputeFileHashAsync(string filePath, string salt)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Evidence file not found.", filePath);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        incrementalHash.AppendData(saltBytes);

        var buffer = new byte[81920]; // 80 KB
        using var fileStream = File.OpenRead(filePath);
        int bytesRead;
        while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
        {
            incrementalHash.AppendData(buffer, 0, bytesRead);
        }

        var hash = incrementalHash.GetHashAndReset();
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Verifies the integrity of a file against a known hash.
    /// </summary>
    public static async Task<bool> VerifyFileIntegrityAsync(string filePath, string salt, string expectedHash)
    {
        var computedHash = await ComputeFileHashAsync(filePath, salt);
        return string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Generates a forensic report string for a recording session.
    /// </summary>
    public static async Task<string> GenerateForensicReportAsync(string filePath, string salt, string sessionId)
    {
        var hash = await ComputeFileHashAsync(filePath, salt);
        var fileInfo = new FileInfo(filePath);

        return $"""
        ═══════════════════════════════════════════════════
        SENTINELSTREAM FORENSIC INTEGRITY REPORT
        ═══════════════════════════════════════════════════
        Session ID  : {sessionId}
        File        : {fileInfo.Name}
        File Size   : {fileInfo.Length:N0} bytes
        SHA-256     : {hash}
        Generated   : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
        ═══════════════════════════════════════════════════
        This hash was computed with a secured forensic salt.
        Any modification to the file will invalidate this hash.
        ═══════════════════════════════════════════════════
        """;
    }
}
