namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Abstraction over git operations required by GitHistoryAnalyzer.
/// Implementations must be side-effect free with respect to the working tree (no checkouts).
/// </summary>
public interface IGitClient
{
	/// <summary>
	/// Returns true if the working tree is clean (no staged or unstaged changes).
	/// </summary>
	Task<bool> StatusCleanAsync(string workingDirectory);

	/// <summary>
	/// Resolves a ref/spec to a commit SHA.
	/// Supports special ref "initial" to return the root commit.
	/// </summary>
	Task<string> ResolveCommitAsync(string workingDirectory, string gitStartOrRef);

	/// <summary>
	/// Returns the HEAD commit SHA.
	/// </summary>
	Task<string> GetHeadShaAsync(string workingDirectory);

	/// <summary>
	/// Returns a chronological list of commit SHAs from start (inclusive) to head (inclusive).
	/// </summary>
	Task<List<string>> GetCommitsRangeAsync(string workingDirectory, string startSha, string headSha);

	/// <summary>
	/// Lists repository-root-relative file paths at the given commit, restricted to the current directory (".").
	/// </summary>
	Task<List<string>> ListFilesAtCommitAsync(string workingDirectory, string sha);

	/// <summary>
	/// Returns changed files between prevSha and sha, restricted to the current directory (".").
	/// Renames are surfaced as Kind = Rename with OldPath and Path populated.
	/// </summary>
	Task<List<GitChange>> GetChangesAsync(string workingDirectory, string prevSha, string sha);

	/// <summary>
	/// Opens a readable stream for the file contents at the given commit and repo-relative path.
	/// Caller is responsible for disposing the returned stream.
	/// </summary>
	Task<Stream> OpenFileAsync(string workingDirectory, string sha, string repoRelativePath);
}

/// <summary>
/// Type of change between two commits.
/// </summary>
public enum GitChangeKind
{
	Add,
	Modify,
	Delete,
	Rename
}

public sealed class GitChange
{
	public GitChangeKind Kind { get; set; }

	/// <summary>
	/// New path (for Add/Modify/Rename new path); repository-root relative with forward slashes.
	/// </summary>
	public string Path { get; set; } = string.Empty;

	/// <summary>
	/// Old path for Rename; repository-root relative with forward slashes.
	/// </summary>
	public string? OldPath { get; set; }
}