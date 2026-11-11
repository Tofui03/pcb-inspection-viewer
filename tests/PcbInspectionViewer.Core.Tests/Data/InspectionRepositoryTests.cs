using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Data.Entities;
using PcbInspectionViewer.Core.Storage;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Data;

public sealed class InspectionRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly ImageCache _imageCache;
    private readonly DbContextOptions<InspectionDbContext> _dbOptions;

    public InspectionRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pcb-repo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "inspections_test.db");
        string cacheDir = Path.Combine(_tempDir, "cache");

        _imageCache = new ImageCache(cacheDir);

        _dbOptions = new DbContextOptionsBuilder<InspectionDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        using var db = new InspectionDbContext(_dbOptions);
        db.EnsureDatabaseAndViewsCreatedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task SaveInspectionResultAsync_SavesBatchInspectionDefectsAndDecisions_WithAreaAndCache()
    {
        var sut = new InspectionRepository(() => new InspectionDbContext(_dbOptions), _imageCache);

        var sampleDetection = new BoundingBoxDetectionDto
        {
            X1 = 10,
            Y1 = 20,
            X2 = 60,
            Y2 = 80,
            Confidence = 0.95,
            DefectClass = "Short"
        };

        var sampleDecision = new InspectionDecisionDto
        {
            DetectionIndex = 0,
            DefectClass = "Short",
            Severity = "CRITICAL",
            Disposition = "SCRAP",
            IpcStandardClause = "Clause 3.1.2",
            ElectricalFailureRisk = "High",
            RootCauseHypothesis = "Etching bridge",
            RecommendedCorrectiveAction = "Clean bath",
            GuardrailOverridden = false
        };

        var report = new BoardInspectionReportDto
        {
            Stage1LatencyMs = 45.2,
            Detections = [sampleDetection],
            Decisions = [sampleDecision]
        };

        // Base64 1x1 png:
        string fakeB64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var dto = new UploadTriageResponseDto
        {
            BoardDisposition = "SCRAP",
            Report = report,
            LineHalted = true,
            TestB64 = fakeB64,
            TemplateB64 = fakeB64,
            DiffB64 = fakeB64
        };

        var cmd = new SaveInspectionCommand(
            BatchId: "BATCH-SMT-A",
            FileName: "20085000_test.jpg",
            TemplateImagePath: "/mock/template.jpg",
            TestImagePath: "/mock/test.jpg",
            IpcClass: "Class 2",
            Envelope: new UploadTriageEnvelope(dto, RoundTripMs: 88.5));

        InspectionEntity saved = await sut.SaveInspectionResultAsync(cmd);

        saved.Id.Should().BeGreaterThan(0);
        saved.BoardDisposition.Should().Be("SCRAP");
        saved.LineHalted.Should().BeTrue();
        saved.DefectCount.Should().Be(1);
        saved.CachedTestImagePath.Should().NotBeNullOrEmpty();
        File.Exists(saved.CachedTestImagePath).Should().BeTrue();

        // Verify Defect Area Calculation
        saved.Defects.Should().HaveCount(1);
        DefectEntity defect = saved.Defects[0];
        defect.DefectClass.Should().Be("Short");
        defect.AreaPx.Should().Be((60.0 - 10.0) * (80.0 - 20.0)); // 50 * 60 = 3000
        defect.Severity.Should().Be("CRITICAL");

        // Verify Batch aggregation
        await using var db = new InspectionDbContext(_dbOptions);
        BatchEntity? batch = await db.Batches.FirstOrDefaultAsync(b => b.BatchId == "BATCH-SMT-A");
        batch.Should().NotBeNull();
        batch!.TotalBoards.Should().Be(1);
        batch.ScrapCount.Should().Be(1);
        batch.LineHalted.Should().BeTrue();
    }

    [Fact]
    public async Task QueryInspectionsAsync_FiltersByVerdictAndDefectClassCorrectly()
    {
        var sut = new InspectionRepository(() => new InspectionDbContext(_dbOptions), _imageCache);

        var dtoPass = new UploadTriageResponseDto
        {
            BoardDisposition = "PASS",
            Report = new BoardInspectionReportDto { Stage1LatencyMs = 20.0, Detections = [], Decisions = [] },
            LineHalted = false
        };

        await sut.SaveInspectionResultAsync(new SaveInspectionCommand("BATCH-1", "pass1.jpg", null, null, "Class 2", new UploadTriageEnvelope(dtoPass, 50.0)));

        var sampleDetection = new BoundingBoxDetectionDto { X1 = 0, Y1 = 0, X2 = 10, Y2 = 10, Confidence = 0.9, DefectClass = "Mousebite" };
        var dtoScrap = new UploadTriageResponseDto
        {
            BoardDisposition = "SCRAP",
            Report = new BoardInspectionReportDto { Stage1LatencyMs = 20.0, Detections = [sampleDetection], Decisions = [] },
            LineHalted = false
        };

        await sut.SaveInspectionResultAsync(new SaveInspectionCommand("BATCH-1", "scrap1.jpg", null, null, "Class 2", new UploadTriageEnvelope(dtoScrap, 50.0)));

        // Query by verdict
        List<InspectionEntity> passResults = await sut.QueryInspectionsAsync(new InspectionQueryFilter(Verdict: "PASS"));
        passResults.Should().HaveCount(1);
        passResults[0].FileName.Should().Be("pass1.jpg");

        // Query by defect class
        List<InspectionEntity> mousebiteResults = await sut.QueryInspectionsAsync(new InspectionQueryFilter(DefectClasses: ["Mousebite"]));
        mousebiteResults.Should().HaveCount(1);
        mousebiteResults[0].FileName.Should().Be("scrap1.jpg");

        // Query by filename substring and date range
        List<InspectionEntity> fileSearch = await sut.QueryInspectionsAsync(new InspectionQueryFilter(FileNameSubstring: "pass"));
        fileSearch.Should().HaveCount(1);
        fileSearch[0].FileName.Should().Be("pass1.jpg");

        List<InspectionEntity> dateRangeResults = await sut.QueryInspectionsAsync(new InspectionQueryFilter(
            StartDateUtc: DateTime.UtcNow.AddMinutes(-5),
            EndDateUtc: DateTime.UtcNow.AddMinutes(5)));
        dateRangeResults.Should().HaveCount(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
