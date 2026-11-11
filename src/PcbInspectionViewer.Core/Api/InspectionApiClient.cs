using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PcbInspectionViewer.Core.Api.Dtos;

namespace PcbInspectionViewer.Core.Api;

public sealed class InspectionApiException : Exception
{
    public InspectionApiException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public interface IInspectionApiClient
{
    Task<UploadTriageEnvelope> UploadTriageAsync(UploadTriageRequest request, CancellationToken ct = default);
}

public sealed class InspectionApiClient : IInspectionApiClient
{
    private const int MaxTransientRetries = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Func<int, TimeSpan> _retryDelayProvider;

    public InspectionApiClient(HttpClient httpClient, Func<int, TimeSpan>? retryDelayProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _retryDelayProvider = retryDelayProvider ?? DefaultRetryDelay;
    }

    public async Task<UploadTriageEnvelope> UploadTriageAsync(
        UploadTriageRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ResolveBaseUri(request.BaseUrlOverride);
        var endpointUri = new Uri(baseUri, "api/upload-triage");

        int attempt = 0;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            attempt++;
            ct.ThrowIfCancellationRequested();

            try
            {
                using MultipartFormDataContent form = BuildMultipartContent(request);
                using HttpResponseMessage response = await _httpClient.PostAsync(endpointUri, form, ct)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    await using Stream contentStream = await response.Content.ReadAsStreamAsync(ct)
                        .ConfigureAwait(false);
                    UploadTriageResponseDto? dto = await JsonSerializer.DeserializeAsync<UploadTriageResponseDto>(
                        contentStream,
                        JsonOptions,
                        ct).ConfigureAwait(false);

                    stopwatch.Stop();

                    if (dto is null)
                    {
                        throw new InspectionApiException(
                            HttpStatusCode.InternalServerError,
                            "Server returned an empty or null JSON payload.");
                    }

                    return new UploadTriageEnvelope(dto, stopwatch.Elapsed.TotalMilliseconds);
                }

                if (IsTransientStatus(response.StatusCode) && attempt <= MaxTransientRetries)
                {
                    await Task.Delay(_retryDelayProvider(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                string detail = await ExtractErrorDetailAsync(response, ct).ConfigureAwait(false);
                throw new InspectionApiException(response.StatusCode, detail);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (InspectionApiException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (attempt <= MaxTransientRetries)
                {
                    await Task.Delay(_retryDelayProvider(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                throw new InspectionApiException(
                    HttpStatusCode.ServiceUnavailable,
                    $"Backend request failed after {attempt} attempt(s): {ex.Message}",
                    ex);
            }
        }
    }

    private static MultipartFormDataContent BuildMultipartContent(UploadTriageRequest request)
    {
        var form = new MultipartFormDataContent();

        var templateContent = new ByteArrayContent(request.TemplateBytes);
        templateContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMimeType(request.TemplateFileName));
        form.Add(templateContent, "template_file", request.TemplateFileName);

        var testContent = new ByteArrayContent(request.TestBytes);
        testContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMimeType(request.TestFileName));
        form.Add(testContent, "test_file", request.TestFileName);

        form.Add(new StringContent(request.BatchId), "batch_id");
        form.Add(new StringContent(request.IpcClass), "ipc_class");
        form.Add(new StringContent(request.SimulateRogueVlm ? "true" : "false"), "simulate_rogue_vlm");
        form.Add(new StringContent(request.ColorizePcb ? "true" : "false"), "colorize_pcb");
        form.Add(new StringContent(request.ReturnImages ? "true" : "false"), "return_images");

        return form;
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.RequestTimeout;

    private static async Task<string> ExtractErrorDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("detail", out JsonElement detailProp))
            {
                return detailProp.ValueKind == JsonValueKind.String
                    ? detailProp.GetString() ?? raw
                    : detailProp.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Fallback to raw text.
        }

        return raw;
    }

    private static string GuessMimeType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
    }

    private static TimeSpan DefaultRetryDelay(int attempt)
    {
        int baseMs = attempt == 1 ? 200 : 500;
        int jitterMs = Random.Shared.Next(20, 120);
        return TimeSpan.FromMilliseconds(baseMs + jitterMs);
    }

    private Uri ResolveBaseUri(string? baseUrlOverride)
    {
        if (!string.IsNullOrWhiteSpace(baseUrlOverride) &&
            Uri.TryCreate(EnsureTrailingSlash(baseUrlOverride.Trim()), UriKind.Absolute, out Uri? customUri))
        {
            return customUri;
        }

        if (_httpClient.BaseAddress is not null)
        {
            return new Uri(EnsureTrailingSlash(_httpClient.BaseAddress.ToString()));
        }

        return new Uri("http://localhost:8000/");
    }

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : $"{url}/";
}
