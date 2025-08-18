namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Represents the analysis result of a directory containing multiple files.
/// </summary>
public class DirectoryAnalysis
{
	/// <summary>
	/// Gets or sets the analyzed directory path (optional; informational).
	/// </summary>
	public string? Directory { get; set; }

	/// <summary>
	/// Gets or sets the collection of analyzed files.
	/// </summary>
	public List<FileAnalysis> Files { get; set; } = new();
}
