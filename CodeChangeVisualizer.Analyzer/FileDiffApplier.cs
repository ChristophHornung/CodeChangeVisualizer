namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Applies <see cref="FileDiff"/> changes to a <see cref="FileAnalysis"/>.
/// </summary>
/// <remarks>
/// Obsolete: prefer <see cref="FileAnalysisApplier.Apply(FileAnalysis, FileAnalysisDiff, string?)"/>.
/// </remarks>
[System.Obsolete("Use FileAnalysisApplier.Apply")]
public static class FileDiffApplier
{
    /// <summary>
    /// Applies the provided <paramref name="fileDiff"/> to <paramref name="oldFile"/> and returns the result.
    /// </summary>
    /// <param name="oldFile">The original analysis of the file.</param>
    /// <param name="fileDiff">The diff to apply.</param>
    /// <param name="newFileName">Optional override for the resulting file name.</param>
    /// <returns>The resulting <see cref="FileAnalysis"/> after applying the diff.</returns>
    public static FileAnalysis ApplyFile(FileAnalysis oldFile, FileDiff fileDiff, string? newFileName = null)
    {
        if (oldFile == null) throw new ArgumentNullException(nameof(oldFile));
        if (fileDiff == null) throw new ArgumentNullException(nameof(fileDiff));

        switch (fileDiff.Kind)
        {
            case FileChangeKind.FileAdd:
            {
                // Use provided blocks (deep copy) and recompute Start
                var lines = (fileDiff.NewFileLines ?? new List<LineGroup>())
                    .Select(g => new LineGroup { Type = g.Type, Length = g.Length })
                    .ToList();
                RecomputeStarts(lines);
                return new FileAnalysis
                {
                    File = newFileName ?? fileDiff.NewFileName ?? oldFile.File,
                    Lines = lines
                };
            }
            case FileChangeKind.FileDelete:
            {
                return new FileAnalysis
                {
                    File = newFileName ?? fileDiff.NewFileName ?? oldFile.File,
                    Lines = new List<LineGroup>()
                };
            }
            case FileChangeKind.Modify:
            default:
            {
                var edits = fileDiff.Edits ?? new List<DiffEdit>();
                return DiffApplier.Apply(oldFile, edits, newFileName ?? fileDiff.NewFileName);
            }
        }
    }

    private static void RecomputeStarts(List<LineGroup> list)
    {
        int start = 0;
        foreach (var g in list)
        {
            g.Start = start;
            start += g.Length;
        }
    }
}
