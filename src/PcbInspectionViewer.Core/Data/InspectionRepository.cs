using Microsoft.EntityFrameworkCore;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Data.Entities;
using PcbInspectionViewer.Core.Geometry;
using PcbInspectionViewer.Core.Storage;

namespace PcbInspectionViewer.Core.Data;

public sealed record SaveInspectionCommand(
    string BatchId,
    string FileName,
    string? TemplateImagePath,
    string? TestImagePath,
    string IpcClass,
    UploadTriageEnvelope Envelope);

public interface IInspectionRepository
{
    Task<InspectionEntity> SaveInspectionResultAsync(SaveInspectionCommand command, CancellationToken ct = default);
    Task<List<InspectionEntity>> QueryInspectionsAsync(InspectionQueryFilter filter, CancellationToken ct = default);
    Task<InspectionEntity?> GetInspectionDetailsAsync(long id, CancellationToken ct = default);
    Task<List<string>> GetAllBatchIdsAsync(CancellationToken ct = default);
}

public sealed record InspectionQueryFilter(
    string? Verdict = null,
    IReadOnlyList<string>? DefectClasses = null,
    string? BatchId = null,
    DateTime? StartDateUtc = null,
    DateTime? EndDateUtc = null,
    string? FileNameSubstring = null,
    int Limit = 500);

public sealed class InspectionRepository : IInspectionRepository
{
    private readonly Func<InspectionDbContext> _contextFactory;
    private readonly IImageCache _imageCache;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public InspectionRepository(Func<InspectionDbContext> contextFactory, IImageCache imageCache)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
    }

    public async Task<InspectionEntity> SaveInspectionResultAsync(SaveInspectionCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UploadTriageResponseDto dto = command.Envelope.Response;

        // 1. Cache PNGs to disk
        string cachedTemplatePath = await _imageCache.SaveDataUriPngAsync(dto.TemplateB64, ct).ConfigureAwait(false);
        string cachedTestPath = await _imageCache.SaveDataUriPngAsync(dto.TestB64, ct).ConfigureAwait(false);
        string cachedDiffPath = await _imageCache.SaveDataUriPngAsync(dto.DiffB64, ct).ConfigureAwait(false);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using InspectionDbContext db = _contextFactory();

            // 2. Ensure or update Batch record (serialized under writeLock)
            string batchId = string.IsNullOrWhiteSpace(command.BatchId) ? "BATCH-DEFAULT" : command.BatchId.Trim();
            BatchEntity? batch = await db.Batches.FirstOrDefaultAsync(b => b.BatchId == batchId, ct).ConfigureAwait(false);
            if (batch is null)
            {
                batch = new BatchEntity
                {
                    BatchId = batchId,
                    CreatedAtUtc = DateTime.UtcNow,
                    TotalBoards = 0,
                    PassCount = 0,
                    ReworkCount = 0,
                    ScrapCount = 0,
                    LineHalted = false
                };
                db.Batches.Add(batch);
            }

            batch.TotalBoards++;
            string disposition = dto.BoardDisposition.Trim().ToUpperInvariant();
            if (disposition == "PASS") batch.PassCount++;
            else if (disposition == "REWORK") batch.ReworkCount++;
            else if (disposition == "SCRAP") batch.ScrapCount++;

            if (dto.LineHalted)
            {
                batch.LineHalted = true;
            }

            // 3. Build Defect and Decision Entities
            IReadOnlyList<BoundingBoxDetectionDto> rawDetections = dto.Report?.Detections ?? [];
            IReadOnlyList<InspectionDecisionDto> rawDecisions = dto.Report?.Decisions ?? [];
            IReadOnlyList<AlignedDefectDecision> aligned = OverlayGeometryCalculator.PairDetectionsWithDecisions(rawDetections, rawDecisions);

            var defectEntities = new List<DefectEntity>(rawDetections.Count);
            foreach (AlignedDefectDecision item in aligned)
            {
                double width = Math.Max(0.0, item.Detection.X2 - item.Detection.X1);
                double height = Math.Max(0.0, item.Detection.Y2 - item.Detection.Y1);

                defectEntities.Add(new DefectEntity
                {
                    DetectionIndex = item.Index,
                    DefectClass = item.Detection.DefectClass,
                    Confidence = item.Detection.Confidence,
                    X1 = item.Detection.X1,
                    Y1 = item.Detection.Y1,
                    X2 = item.Detection.X2,
                    Y2 = item.Detection.Y2,
                    AreaPx = width * height,
                    Severity = item.Decision?.Severity,
                    Disposition = item.Decision?.Disposition,
                    IpcClause = item.Decision?.IpcStandardClause
                });
            }

            var decisionEntities = new List<DecisionEntity>(rawDecisions.Count);
            foreach (InspectionDecisionDto dec in rawDecisions)
            {
                decisionEntities.Add(new DecisionEntity
                {
                    DetectionIndex = dec.DetectionIndex,
                    DefectClass = dec.DefectClass ?? string.Empty,
                    Severity = dec.Severity,
                    Disposition = dec.Disposition,
                    IpcStandardClause = dec.IpcStandardClause,
                    ElectricalFailureRisk = dec.ElectricalFailureRisk,
                    RootCauseHypothesis = dec.RootCauseHypothesis,
                    RecommendedCorrectiveAction = dec.RecommendedCorrectiveAction,
                    GuardrailOverridden = dec.GuardrailOverridden,
                    OverrideReason = dec.OverrideReason
                });
            }

            string defectSummary = string.Join(", ", rawDetections.Select(d => d.DefectClass).Distinct());

            var inspection = new InspectionEntity
            {
                BatchId = batchId,
                FileName = string.IsNullOrWhiteSpace(command.FileName) ? "unnamed.jpg" : Path.GetFileName(command.FileName),
                TemplateImagePath = command.TemplateImagePath,
                TestImagePath = command.TestImagePath,
                CachedTemplateImagePath = cachedTemplatePath,
                CachedTestImagePath = cachedTestPath,
                CachedDiffImagePath = cachedDiffPath,
                BoardDisposition = disposition,
                IpcClass = command.IpcClass,
                LineHalted = dto.LineHalted,
                DefectCount = rawDetections.Count,
                DefectClassesSummary = defectSummary,
                Stage1LatencyMs = dto.Report?.Stage1LatencyMs ?? 0,
                RoundTripMs = command.Envelope.RoundTripMs,
                InspectedAtUtc = DateTime.UtcNow,
                Defects = defectEntities,
                Decisions = decisionEntities
            };

            db.Inspections.Add(inspection);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return inspection;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<InspectionEntity>> QueryInspectionsAsync(InspectionQueryFilter filter, CancellationToken ct = default)
    {
        await using InspectionDbContext db = _contextFactory();

        IQueryable<InspectionEntity> query = db.Inspections.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Verdict) && filter.Verdict != "ALL")
        {
            string v = filter.Verdict.Trim().ToUpperInvariant();
            query = query.Where(i => i.BoardDisposition == v);
        }

        if (!string.IsNullOrWhiteSpace(filter.BatchId) && filter.BatchId != "ALL")
        {
            query = query.Where(i => i.BatchId == filter.BatchId);
        }

        if (!string.IsNullOrWhiteSpace(filter.FileNameSubstring))
        {
            string sub = filter.FileNameSubstring.Trim();
            query = query.Where(i => i.FileName.Contains(sub));
        }

        if (filter.StartDateUtc.HasValue)
        {
            query = query.Where(i => i.InspectedAtUtc >= filter.StartDateUtc.Value);
        }

        if (filter.EndDateUtc.HasValue)
        {
            query = query.Where(i => i.InspectedAtUtc <= filter.EndDateUtc.Value);
        }

        if (filter.DefectClasses is not null && filter.DefectClasses.Count > 0)
        {
            // Filter inspections containing any of the selected defect classes
            query = query.Where(i => db.Defects.Any(d => d.InspectionId == i.Id && filter.DefectClasses.Contains(d.DefectClass)));
        }

        return await query
            .OrderByDescending(i => i.InspectedAtUtc)
            .Take(filter.Limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<InspectionEntity?> GetInspectionDetailsAsync(long id, CancellationToken ct = default)
    {
        await using InspectionDbContext db = _contextFactory();

        return await db.Inspections
            .AsNoTracking()
            .Include(i => i.Defects)
            .Include(i => i.Decisions)
            .Include(i => i.LlmSummary)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<string>> GetAllBatchIdsAsync(CancellationToken ct = default)
    {
        await using InspectionDbContext db = _contextFactory();
        return await db.Batches
            .AsNoTracking()
            .Select(b => b.BatchId)
            .OrderBy(b => b)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
