namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Represents a per-file change within a revision, including block-level or file-level diffs.
/// </summary>
public class FileChangeEntry
{
	/// <summary>
	/// The relative file path.
	/// </summary>
	public string File { get; set; } = string.Empty;

	/// <summary>
	/// The change description for the file.
	/// </summary>
	public FileAnalysisDiff Change { get; set; } = new();
}