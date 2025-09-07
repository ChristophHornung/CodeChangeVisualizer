namespace CodeChangeVisualizer.Analyzer;

public record GitAnalysisProgress
{
	// Kinds: "CommitsTotal", "CommitStarted", "CommitCompleted", "FilesTotal", "FileProcessed"
	public string Kind { get; init; } = string.Empty;
	public int? Total { get; init; }
	public int? Value { get; init; }
	public string? Commit { get; init; }
	public string? File { get; init; }
}