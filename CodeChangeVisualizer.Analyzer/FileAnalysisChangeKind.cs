namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// The kind of change described by <see cref="FileAnalysisDiff"/>.
/// </summary>
public enum FileAnalysisChangeKind
{
	/// <summary>The file content was modified with block edits.</summary>
	Modify,
	/// <summary>The file was added.</summary>
	FileAdd,
	/// <summary>The file was deleted.</summary>
	FileDelete
}