namespace PcbInspectionViewer.Core.Batch;

public sealed record BatchInspectionWorkItem(
    int Index,
    string TestImagePath,
    string TemplateImagePath,
    string DisplayFileName);

public static class BatchFolderScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    public static IReadOnlyList<BatchInspectionWorkItem> ScanFolder(
        string folderPath,
        string? globalTemplatePath,
        bool useDeepPcbAutoPairing)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return [];
        }

        string[] allFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);

        var supportedFiles = allFiles
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f)
            .ToList();

        var workItems = new List<BatchInspectionWorkItem>();
        int index = 0;

        if (useDeepPcbAutoPairing)
        {
            // DeepPCB convention: <id>_test.jpg paired with <id>_temp.jpg
            var testFiles = supportedFiles.Where(f =>
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(f);
                return nameWithoutExt.EndsWith("_test", StringComparison.OrdinalIgnoreCase);
            }).ToList();

            foreach (string testFile in testFiles)
            {
                string dir = Path.GetDirectoryName(testFile)!;
                string testBase = Path.GetFileNameWithoutExtension(testFile);
                string prefix = testBase[..^5]; // remove "_test"
                string ext = Path.GetExtension(testFile);

                // Look for <prefix>_temp<ext> first, then any supported extension
                string tempCandidate = Path.Combine(dir, $"{prefix}_temp{ext}");
                if (!File.Exists(tempCandidate))
                {
                    string? matched = supportedFiles.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).Equals($"{prefix}_temp", StringComparison.OrdinalIgnoreCase));
                    if (matched is not null)
                    {
                        tempCandidate = matched;
                    }
                }

                index++;
                workItems.Add(new BatchInspectionWorkItem(
                    Index: index,
                    TestImagePath: testFile,
                    TemplateImagePath: tempCandidate,
                    DisplayFileName: Path.GetFileName(testFile)));
            }

            return workItems;
        }

        // Default mode: use global template, skip template file and any *_temp.*
        string? normalizedTemplatePath = string.IsNullOrWhiteSpace(globalTemplatePath) ? null : Path.GetFullPath(globalTemplatePath);

        foreach (string file in supportedFiles)
        {
            string fullPath = Path.GetFullPath(file);
            if (normalizedTemplatePath is not null && string.Equals(fullPath, normalizedTemplatePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string nameWithoutExt = Path.GetFileNameWithoutExtension(file);
            if (nameWithoutExt.EndsWith("_temp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            index++;
            workItems.Add(new BatchInspectionWorkItem(
                Index: index,
                TestImagePath: fullPath,
                TemplateImagePath: normalizedTemplatePath ?? string.Empty,
                DisplayFileName: Path.GetFileName(file)));
        }

        return workItems;
    }
}
