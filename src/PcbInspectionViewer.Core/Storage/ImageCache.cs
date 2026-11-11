using System.Security.Cryptography;
using PcbInspectionViewer.Core.Configuration;

namespace PcbInspectionViewer.Core.Storage;

public interface IImageCache
{
    string CacheDirectory { get; }
    Task<string> SaveDataUriPngAsync(string? dataUri, CancellationToken ct = default);
    Task<string> SaveBytesPngAsync(byte[] pngBytes, CancellationToken ct = default);
    Task<string?> TryGetCachedDataUriAsync(string? imagePath, CancellationToken ct = default);
}

public sealed class ImageCache : IImageCache
{
    private readonly string _cacheDirectory;

    public ImageCache(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            JsonSettingsService.GetDefaultDataDirectory(),
            "cache");

        Directory.CreateDirectory(_cacheDirectory);
    }

    public string CacheDirectory => _cacheDirectory;

    public async Task<string> SaveDataUriPngAsync(string? dataUri, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dataUri))
        {
            return string.Empty;
        }

        int commaIdx = dataUri.IndexOf(',', StringComparison.Ordinal);
        string base64Payload = commaIdx >= 0 ? dataUri[(commaIdx + 1)..] : dataUri;
        byte[] bytes = Convert.FromBase64String(base64Payload);

        return await SaveBytesPngAsync(bytes, ct).ConfigureAwait(false);
    }

    public async Task<string> SaveBytesPngAsync(byte[] pngBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
        {
            return string.Empty;
        }

        byte[] hashBytes = SHA256.HashData(pngBytes);
        string hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        string targetPath = Path.Combine(_cacheDirectory, $"{hashHex}.png");

        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        // Atomic write to prevent concurrent write collisions (Major 1)
        string tempPath = Path.Combine(_cacheDirectory, $"{hashHex}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tempPath, pngBytes, ct).ConfigureAwait(false);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch (IOException)
        {
            // Another thread/process completed the move simultaneously; safe to ignore
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        return targetPath;
    }

    public async Task<string?> TryGetCachedDataUriAsync(string? imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        byte[] bytes = await File.ReadAllBytesAsync(imagePath, ct).ConfigureAwait(false);
        string b64 = Convert.ToBase64String(bytes);
        return $"data:image/png;base64,{b64}";
    }
}
