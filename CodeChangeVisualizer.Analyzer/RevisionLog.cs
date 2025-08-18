namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Root container for the list of analyzed revisions.
/// </summary>
public class RevisionLog
{
	/// <summary>
	/// The chronological list of revisions. The first contains full analysis; others contain diffs.
	/// </summary>
	public List<RevisionEntry> Revisions { get; set; } = new();
}