using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PcbInspectionViewer.Core.Configuration;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Configuration;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFilePath;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pcb-viewer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsFilePath = Path.Combine(_tempDir, "settings.json");
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsSpecDefaults()
    {
        var sut = new JsonSettingsService(_settingsFilePath);

        StationSettings settings = await sut.LoadAsync();

        settings.Api.BaseUrl.Should().Be("http://localhost:8000");
        settings.Api.TimeoutSeconds.Should().Be(10);
        settings.Batch.Concurrency.Should().Be(2);
        settings.Batch.DefaultIpcClass.Should().Be("Class 2");
        settings.Batch.DefaultBatchId.Should().Be("BATCH-UPLOAD");
        settings.Batch.ColorizePcb.Should().BeFalse();
        settings.Llm.Provider.Should().Be("None");
        settings.Llm.ModelName.Should().Be("gemini-2.5-flash");
        settings.Llm.SummaryLanguage.Should().Be("en");
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsModifiedSettingsWithoutSecrets()
    {
        var sut = new JsonSettingsService(_settingsFilePath);
        var modified = new StationSettings
        {
            Api = new ApiOptions
            {
                BaseUrl = "https://aoi.factory.internal:8443",
                TimeoutSeconds = 25
            },
            Batch = new BatchOptions
            {
                Concurrency = 6,
                DefaultIpcClass = "Class 3",
                DefaultBatchId = "BATCH-SMT-99",
                ColorizePcb = true
            },
            Llm = new LlmOptions
            {
                Provider = "Gemini",
                ModelName = "gemini-2.5-pro",
                SummaryLanguage = "en+zh"
            }
        };

        SettingsValidationResult validation = await sut.SaveAsync(modified);
        validation.IsValid.Should().BeTrue();
        validation.RequiresHttpsWarning.Should().BeFalse();

        string rawJson = await File.ReadAllTextAsync(_settingsFilePath);
        rawJson.Should().NotContainEquivalentOf("ApiKey");
        rawJson.Should().NotContainEquivalentOf("Secret");

        StationSettings reloaded = await sut.LoadAsync();
        reloaded.Should().BeEquivalentTo(modified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-2)]
    public void Validate_WhenConcurrencyOutside1To8_ReturnsInvalid(int invalidConcurrency)
    {
        var sut = new JsonSettingsService(_settingsFilePath);
        var settings = new StationSettings
        {
            Batch = new BatchOptions { Concurrency = invalidConcurrency }
        };

        SettingsValidationResult result = sut.Validate(settings);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Concurrency"));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://localhost:8000")]
    [InlineData("")]
    public void Validate_WhenBaseUrlNotHttpOrHttps_ReturnsInvalid(string invalidUrl)
    {
        var sut = new JsonSettingsService(_settingsFilePath);
        var settings = new StationSettings
        {
            Api = new ApiOptions { BaseUrl = invalidUrl }
        };

        SettingsValidationResult result = sut.Validate(settings);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("URL"));
    }

    [Theory]
    [InlineData("http://192.168.1.50:8000", true)]
    [InlineData("http://aoi-server.local:8000", true)]
    [InlineData("http://localhost:8000", false)]
    [InlineData("http://127.0.0.1:8000", false)]
    [InlineData("https://192.168.1.50:8443", false)]
    public void Validate_WhenPlainHttpOnNonLocalhost_FlagsHttpsWarning(string url, bool expectedWarning)
    {
        var sut = new JsonSettingsService(_settingsFilePath);
        var settings = new StationSettings
        {
            Api = new ApiOptions { BaseUrl = url }
        };

        SettingsValidationResult result = sut.Validate(settings);

        result.IsValid.Should().BeTrue();
        result.RequiresHttpsWarning.Should().Be(expectedWarning);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
