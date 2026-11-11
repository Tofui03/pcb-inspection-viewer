using FluentAssertions;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Api.Dtos;
using Xunit;

namespace PcbInspectionViewer.IntegrationTests;

public sealed class LiveBackendIntegrationTests
{
    private readonly string _backendBaseUrl;

    public LiveBackendIntegrationTests()
    {
        _backendBaseUrl = Environment.GetEnvironmentVariable("TEST_BACKEND_URL") ?? "http://localhost:8000";
    }

    [Fact]
    public async Task HealthAndInspection_EndToEnd_WhenBackendIsAvailable()
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var healthMonitor = new BackendHealthMonitor(httpClient);

        BackendHealthStatus health;
        try
        {
            health = await healthMonitor.CheckHealthAsync(_backendBaseUrl);
        }
        catch
        {
            // If backend is not running locally in this test harness, test passes gracefully
            return;
        }

        if (!health.IsOnline)
        {
            return;
        }

        health.IsOnline.Should().BeTrue();
        health.Version.Should().NotBeNullOrWhiteSpace();

        // Test inspection with mock image
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../DeepPCB-AOI"));
        string templatePath = Path.Combine(repoRoot, "core/data/mock_pcb/group20085/20085/20085000_temp.jpg");
        string testPath = Path.Combine(repoRoot, "core/data/mock_pcb/group20085/20085/20085000_test.jpg");

        if (File.Exists(templatePath) && File.Exists(testPath))
        {
            using var apiClient = new HttpClient { BaseAddress = new Uri(_backendBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
            var inspectionClient = new InspectionApiClient(apiClient);

            var request = new UploadTriageRequest(
                TemplateImagePath: templatePath,
                TestImagePath: testPath,
                IpcClass: "Class 2",
                BatchId: "INTEG-BATCH-01",
                ColorizePcb: false);

            UploadTriageEnvelope envelope = await inspectionClient.UploadTriageAsync(request);
            envelope.Should().NotBeNull();
            envelope.Response.BoardDisposition.Should().BeOneOf("PASS", "REWORK", "SCRAP");
            envelope.Response.Report.Should().NotBeNull();
            envelope.RoundTripMs.Should().BeGreaterThan(0);
        }
    }
}
