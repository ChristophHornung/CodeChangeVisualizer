namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Describes the kind of block-level edit between two analyses.
/// </summary>
public enum DiffOpType
{
	/// <summary>An existing block changed its size in lines.</summary>
	Resize,

	/// <summary>A new block was inserted.</summary>
	Insert,

	/// <summary>An existing block was removed.</summary>
	Remove
}