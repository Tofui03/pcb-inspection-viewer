using System.Net;
using System.Text;
using FluentAssertions;
using PcbInspectionViewer.Core.Api;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Api;

public sealed class BackendHealthMonitorTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenHealthEndpointReturns200_ReturnsOnlineWithDetectorAndVersion()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/health");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "status": "ok",
                      "version": "1.2.0",
                      "detector_backend": "yolov8",
                      "execution_provider": "CPUExecutionProvider",
                      "dataset_loaded": false
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        var sut = new BackendHealthMonitor(httpClient);

        BackendHealthStatus result = await sut.CheckHealthAsync();

        result.IsOnline.Should().BeTrue();
        result.Version.Should().Be("1.2.0");
        result.DetectorBackend.Should().Be("yolov8");
        result.ExecutionProvider.Should().Be("CPUExecutionProvider");
        result.DatasetLoaded.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenHealthEndpointReturns404_FallsBackToOpenApiJsonPerBc1()
    {
        var requestedPaths = new List<string>();
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            string path = req.RequestUri!.AbsolutePath;
            requestedPaths.Add(path);

            if (path == "/api/health")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (path == "/openapi.json")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "openapi": "3.1.0",
                          "info": {
                            "title": "DeepPCB-AOI",
                            "version": "1.1.0"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        var sut = new BackendHealthMonitor(httpClient);

        BackendHealthStatus result = await sut.CheckHealthAsync();

        requestedPaths.Should().ContainInOrder("/api/health", "/openapi.json");
        result.IsOnline.Should().BeTrue();
        result.Version.Should().Be("1.1.0");
        result.DetectorBackend.Should().Be("unknown");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenNetworkRefusedOrTimeout_ReturnsOfflineWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        var sut = new BackendHealthMonitor(httpClient);

        BackendHealthStatus result = await sut.CheckHealthAsync();

        result.IsOnline.Should().BeFalse();
        result.Version.Should().Be("unknown");
        result.DetectorBackend.Should().Be("unknown");
        result.ErrorMessage.Should().Contain("Connection refused");
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
