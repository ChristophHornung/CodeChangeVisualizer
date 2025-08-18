namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Represents the analysis result for a single file.
/// </summary>
public class FileAnalysis
{
	/// <summary>
	/// Gets or sets the file path relative to the analyzed base directory, using forward slashes.
	/// </summary>
	public string File { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the contiguous groups of lines classified by type.
	/// </summary>
	public List<LineGroup> Lines { get; set; } = new();
}