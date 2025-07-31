namespace CodeChangeVisualizer.Analyzer;

public class FileAnalysis
{
	public string File { get; set; } = string.Empty;
	public List<LineGroup> Lines { get; set; } = new();
}