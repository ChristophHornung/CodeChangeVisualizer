namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Describes the kind of change between two versions of a file.
/// </summary>
public enum FileChangeKind
{
	/// <summary>The file existed in both versions and changed at the block level.</summary>
	Modify,
	/// <summary>The file did not exist previously and was added in the new version.</summary>
	FileAdd,
	/// <summary>The file existed previously and was deleted in the new version.</summary>
	FileDelete
}