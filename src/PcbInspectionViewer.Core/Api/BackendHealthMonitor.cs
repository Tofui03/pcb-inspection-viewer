using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcbInspectionViewer.Core.Api;

public sealed record BackendHealthStatus(
    bool IsOnline,
    string Version,
    string DetectorBackend,
    string ExecutionProvider,
    bool DatasetLoaded,
    string? ErrorMessage)
{
    public static BackendHealthStatus Offline(string errorMessage) =>
        new(
            IsOnline: false,
            Version: "unknown",
            DetectorBackend: "unknown",
            ExecutionProvider: "unknown",
            DatasetLoaded: false,
            ErrorMessage: errorMessage);
}

public interface IBackendHealthMonitor
{
    Task<BackendHealthStatus> CheckHealthAsync(string? baseUrlOverride = null, CancellationToken ct = default);
}

public sealed class BackendHealthMonitor : IBackendHealthMonitor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public BackendHealthMonitor(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<BackendHealthStatus> CheckHealthAsync(string? baseUrlOverride = null, CancellationToken ct = default)
    {
        try
        {
            Uri baseUri = ResolveBaseUri(baseUrlOverride);
            var healthUri = new Uri(baseUri, "/api/health");

            using HttpResponseMessage response = await _httpClient.GetAsync(healthUri, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                HealthEndpointDto? dto = await JsonSerializer.DeserializeAsync<HealthEndpointDto>(stream, JsonOptions, ct)
                    .ConfigureAwait(false);

                if (dto is not null && string.Equals(dto.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return new BackendHealthStatus(
                        IsOnline: true,
                        Version: string.IsNullOrWhiteSpace(dto.Version) ? "unknown" : dto.Version,
                        DetectorBackend: string.IsNullOrWhiteSpace(dto.DetectorBackend) ? "unknown" : dto.DetectorBackend,
                        ExecutionProvider: string.IsNullOrWhiteSpace(dto.ExecutionProvider) ? "unknown" : dto.ExecutionProvider,
                        DatasetLoaded: dto.DatasetLoaded ?? false,
                        ErrorMessage: null);
                }
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return await FallbackToOpenApiJsonAsync(baseUri, ct).ConfigureAwait(false);
            }

            return BackendHealthStatus.Offline($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BackendHealthStatus.Offline(ex.Message);
        }
    }

    private async Task<BackendHealthStatus> FallbackToOpenApiJsonAsync(Uri baseUri, CancellationToken ct)
    {
        var openApiUri = new Uri(baseUri, "/openapi.json");
        using HttpResponseMessage response = await _httpClient.GetAsync(openApiUri, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return BackendHealthStatus.Offline($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        string version = "unknown";
        if (doc.RootElement.TryGetProperty("info", out JsonElement infoElement) &&
            infoElement.TryGetProperty("version", out JsonElement versionElement) &&
            versionElement.ValueKind == JsonValueKind.String)
        {
            version = versionElement.GetString() ?? "unknown";
        }

        return new BackendHealthStatus(
            IsOnline: true,
            Version: version,
            DetectorBackend: "unknown",
            ExecutionProvider: "unknown",
            DatasetLoaded: false,
            ErrorMessage: null);
    }

    private Uri ResolveBaseUri(string? baseUrlOverride)
    {
        if (!string.IsNullOrWhiteSpace(baseUrlOverride) &&
            Uri.TryCreate(baseUrlOverride.TrimEnd('/'), UriKind.Absolute, out Uri? customUri))
        {
            return customUri;
        }

        if (_httpClient.BaseAddress is not null)
        {
            return _httpClient.BaseAddress;
        }

        return new Uri("http://localhost:8000");
    }

    private sealed class HealthEndpointDto
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("detector_backend")]
        public string? DetectorBackend { get; init; }

        [JsonPropertyName("execution_provider")]
        public string? ExecutionProvider { get; init; }

        [JsonPropertyName("dataset_loaded")]
        public bool? DatasetLoaded { get; init; }
    }
}
