using System.Net;
using System.Text;
using FluentAssertions;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Api.Dtos;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Api;

public sealed class InspectionApiClientTests
{
    private static string GetFixturePath()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "fixtures", "upload_triage_sample.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("Could not locate tests/fixtures/upload_triage_sample.json");
    }

    [Fact]
    public async Task UploadTriageAsync_WithRealRecordedFixture_SendsExactMultipartFieldsAndParsesResponse()
    {
        string fixtureJson = await File.ReadAllTextAsync(GetFixturePath());
        var capturedFields = new Dictionary<string, string>(StringComparer.Ordinal);
        var capturedFileNames = new Dictionary<string, string>(StringComparer.Ordinal);

        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().Be("/api/upload-triage");
            var multipart = req.Content.Should().BeOfType<MultipartFormDataContent>().Subject;

            foreach (HttpContent part in multipart)
            {
                string? name = part.Headers.ContentDisposition?.Name?.Trim('"');
                string? fileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                if (name is null)
                {
                    continue;
                }

                if (fileName is not null)
                {
                    capturedFileNames[name] = fileName;
                }
                else
                {
                    capturedFields[name] = await part.ReadAsStringAsync(ct);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fixtureJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        var sut = new InspectionApiClient(httpClient, retryDelayProvider: _ => TimeSpan.Zero);

        var request = new UploadTriageRequest(
            TemplateBytes: [1, 2, 3, 4],
            TemplateFileName: "20085000_temp.jpg",
            TestBytes: [5, 6, 7, 8],
            TestFileName: "20085000_test.jpg",
            BatchId: "BATCH-FIXTURE",
            IpcClass: "Class 2",
            SimulateRogueVlm: false,
            ColorizePcb: false,
            ReturnImages: true);

        UploadTriageEnvelope result = await sut.UploadTriageAsync(request);

        capturedFileNames.Should().ContainKey("template_file").WhoseValue.Should().Be("20085000_temp.jpg");
        capturedFileNames.Should().ContainKey("test_file").WhoseValue.Should().Be("20085000_test.jpg");
        capturedFields["batch_id"].Should().Be("BATCH-FIXTURE");
        capturedFields["ipc_class"].Should().Be("Class 2");
        capturedFields["simulate_rogue_vlm"].Should().Be("false");
        capturedFields["colorize_pcb"].Should().Be("false");
        capturedFields["return_images"].Should().Be("true");

        result.Response.SampleId.Should().Be("UPLOAD:20085000_test");
        result.Response.BoardDisposition.Should().Be("SCRAP");
        result.Response.LineHalted.Should().BeFalse();
        result.Response.Report.Stage1LatencyMs.Should().BeApproximately(86.3474, 0.001);
        result.Response.Report.FastPathPass.Should().BeFalse();
        result.Response.Report.Detections.Should().HaveCount(3);
        result.Response.Report.Detections[0].DefectClass.Should().Be("Short");
        result.Response.Report.Detections[0].X1.Should().Be(177);
        result.Response.Report.Detections[0].Y1.Should().Be(101);
        result.Response.Report.Detections[0].X2.Should().Be(226);
        result.Response.Report.Detections[0].Y2.Should().Be(196);
        result.Response.Report.Detections[0].AreaPx.Should().Be(4655);
        result.Response.Report.Decisions.Should().HaveCount(3);
        result.Response.Report.Decisions[0].DetectionIndex.Should().Be(0);
        result.Response.Report.Decisions[0].Disposition.Should().Be("SCRAP");
        result.Response.Report.Decisions[0].Severity.Should().Be("CRITICAL");
        result.Response.TemplateB64.Should().StartWith("data:image/png;base64,");
        result.RoundTripMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task UploadTriageAsync_OnTransient5xx_RetriesUpToTwoTimesAndSucceedsOnThirdAttempt(HttpStatusCode transientCode)
    {
        string fixtureJson = await File.ReadAllTextAsync(GetFixturePath());
        int attempts = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            attempts++;
            if (attempts <= 2)
            {
                return Task.FromResult(new HttpResponseMessage(transientCode));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fixtureJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        var sut = new InspectionApiClient(httpClient, retryDelayProvider: _ => TimeSpan.Zero);

        var request = new UploadTriageRequest([1], "tpl.png", [2], "tst.png", "BATCH-1", "Class 2");
        UploadTriageEnvelope result = await sut.UploadTriageAsync(request);

        attempts.Should().Be(3);
        result.Response.BoardDisposition.Should().Be("SCRAP");
    }

    [Fact]
    public async Task UploadTriageAsync_OnTransientHttpRequestException_RetriesUpToTwoTimesAndSucceedsOnThirdAttempt()
    {
        string fixtureJson = await File.ReadAllTextAsync(GetFixturePath());
        int attempts = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            attempts++;
            if (attempts <= 2)
            {
                throw new HttpRequestException("Connection reset by peer");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fixtureJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        var sut = new InspectionApiClient(httpClient, retryDelayProvider: _ => TimeSpan.Zero);

        var request = new UploadTriageRequest([1], "tpl.png", [2], "tst.png", "BATCH-1", "Class 2");
        UploadTriageEnvelope result = await sut.UploadTriageAsync(request);

        attempts.Should().Be(3);
        result.Response.BoardDisposition.Should().Be("SCRAP");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"detail":"Failed to decode uploaded image files."}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"detail":[{"msg":"Field required"}]}""")]
    public async Task UploadTriageAsync_On400Or422_DoesNotRetryAndThrowsInspectionApiExceptionWithServerDetail(
        HttpStatusCode statusCode,
        string errorPayload)
    {
        int attempts = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(errorPayload, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        var sut = new InspectionApiClient(httpClient, retryDelayProvider: _ => TimeSpan.Zero);

        var request = new UploadTriageRequest([1], "tpl.png", [2], "tst.png", "BATCH-1", "Class 2");
        Func<Task> act = () => sut.UploadTriageAsync(request);

        var ex = await act.Should().ThrowAsync<InspectionApiException>();
        ex.Which.StatusCode.Should().Be(statusCode);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task UploadTriageAsync_WhenCancelledMidRequest_PropagatesOperationCanceledWithoutRetry()
    {
        int attempts = 0;
        using var cts = new CancellationTokenSource();

        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            attempts++;
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        var sut = new InspectionApiClient(httpClient, retryDelayProvider: _ => TimeSpan.Zero);

        var request = new UploadTriageRequest([1], "tpl.png", [2], "tst.png", "BATCH-1", "Class 2");
        Func<Task> act = () => sut.UploadTriageAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
