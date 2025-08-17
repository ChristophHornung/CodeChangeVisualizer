namespace CodeChangeVisualizer.StrideRunner;

public sealed class FileAnalysis
{
	public string File { get; set; } = string.Empty;
	public List<LineGroup> Lines { get; set; } = new();
}