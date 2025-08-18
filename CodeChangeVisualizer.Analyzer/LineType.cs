namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Describes the classification of a line or group of lines in a source file.
/// </summary>
public enum LineType
{
	/// <summary>Line(s) that contain only comments.</summary>
	Comment,

	/// <summary>Line(s) that likely increase cyclomatic complexity (e.g., if, for, switch).</summary>
	ComplexityIncreasing,

	/// <summary>Regular code line(s) without trailing comments.</summary>
	Code,

	/// <summary>Line(s) that contain both code and a comment.</summary>
	CodeAndComment,

	/// <summary>Blank or whitespace-only line(s).</summary>
	Empty
}