namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Represents a single block-level edit operation between two file analyses.
/// </summary>
public record DiffEdit
{
	/// <summary>
	/// Gets the kind of edit operation.
	/// </summary>
	public DiffOpType Kind { get; init; }

	/// <summary>
	/// The index interpretation depends on <see cref="Kind"/>:
	/// Insert/Resize use the index in the NEW sequence; Remove uses the index in the OLD sequence.
	/// </summary>
	public int Index { get; init; }

	/// <summary>
	/// The line type of the affected block. A block never changes its type; type changes are represented as remove+insert.
	/// </summary>
	public LineType LineType { get; init; }

	/// <summary>
	/// For Resize/Remove, the original length (lines). For Insert, null.
	/// </summary>
	public int? OldLength { get; init; }

	/// <summary>
	/// For Resize/Insert, the new length (lines). For Remove, null.
	/// </summary>
	public int? NewLength { get; init; }

	/// <summary>
	/// Convenience difference: NewLength - OldLength (missing values treated as 0).
	/// </summary>
	public int Delta => (this.NewLength ?? 0) - (this.OldLength ?? 0);
}