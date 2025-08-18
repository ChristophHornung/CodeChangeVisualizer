namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Represents a single revision entry: either a full analysis (first) or a diff.
/// </summary>
public class RevisionEntry
{
	/// <summary>
	/// The commit SHA for this entry.
	/// </summary>
	public string Commit { get; set; } = string.Empty;

	/// <summary>
	/// The full analysis for the first commit; null otherwise.
	/// </summary>
	public List<FileAnalysis>? Analysis { get; set; }

	/// <summary>
	/// The diff to the previous commit for subsequent entries; null for the first.
	/// </summary>
	public List<FileChangeEntry>? Diff { get; set; }
}