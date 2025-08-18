namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Unified diff model for a single file analysis. Supports block-level edits (Modify),
/// whole-file addition (FileAdd), and whole-file deletion (FileDelete).
/// </summary>
public class FileAnalysisDiff
{
	public FileAnalysisChangeKind Kind { get; init; }

	// For Modify: Edits is populated
	public List<DiffEdit>? Edits { get; init; }

	// For FileAdd: NewFileLines carries all blocks of the new file
	public List<LineGroup>? NewFileLines { get; init; }

	// File names (optional helpers)
	public string? OldFileName { get; init; }
	public string? NewFileName { get; init; }

	/// <summary>
	/// Convenience converter from existing FileDiff to FileAnalysisDiff for interop.
	/// </summary>
	public static FileAnalysisDiff FromFileDiff(FileDiff fileDiff)
	{
		if (fileDiff == null)
		{
			throw new ArgumentNullException(nameof(fileDiff));
		}

		FileAnalysisChangeKind kind = fileDiff.Kind switch
		{
			FileChangeKind.Modify => FileAnalysisChangeKind.Modify,
			FileChangeKind.FileAdd => FileAnalysisChangeKind.FileAdd,
			FileChangeKind.FileDelete => FileAnalysisChangeKind.FileDelete,
			_ => FileAnalysisChangeKind.Modify
		};
		return new FileAnalysisDiff
		{
			Kind = kind,
			Edits = fileDiff.Edits,
			NewFileLines = fileDiff.NewFileLines,
			OldFileName = fileDiff.OldFileName,
			NewFileName = fileDiff.NewFileName
		};
	}
}