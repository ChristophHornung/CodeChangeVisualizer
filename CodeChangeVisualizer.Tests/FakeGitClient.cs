namespace CodeChangeVisualizer.Tests;

using System.Text;
using CodeChangeVisualizer.Analyzer;

/// <summary>
/// Fake implementation of IGitClient for testing purposes.
/// Provides a controlled environment for testing git operations without requiring a real git repository.
/// </summary>
internal sealed class FakeGitClient : IGitClient
{
	private readonly List<string> _commits;
	private readonly Dictionary<string, Dictionary<string, string>> _filesAtCommit; // commit -> (path -> content)
	private readonly Dictionary<(string prev, string curr), List<GitChange>> _changes;

	/// <summary>
	/// Gets the list of files that were opened during testing.
	/// </summary>
	public List<(string sha, string path)> OpenedFiles { get; } = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="FakeGitClient"/> class.
	/// </summary>
	/// <param name="commits">The list of commit SHAs in chronological order.</param>
	/// <param name="filesAtCommit">Dictionary mapping commit SHAs to file contents.</param>
	/// <param name="changes">Dictionary mapping commit pairs to file changes.</param>
	public FakeGitClient(List<string> commits,
		Dictionary<string, Dictionary<string, string>> filesAtCommit,
		Dictionary<(string prev, string curr), List<GitChange>> changes)
	{
		this._commits = commits ?? throw new ArgumentNullException(nameof(commits));
		this._filesAtCommit = filesAtCommit ?? throw new ArgumentNullException(nameof(filesAtCommit));
		this._changes = changes ?? throw new ArgumentNullException(nameof(changes));
	}

	/// <summary>
	/// Always returns true for clean status in tests.
	/// </summary>
	public Task<bool> StatusCleanAsync(string workingDirectory) => Task.FromResult(true);

	/// <summary>
	/// Resolves commit references, including the special "initial" reference.
	/// </summary>
	public Task<string> ResolveCommitAsync(string workingDirectory, string gitStartOrRef)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(gitStartOrRef);

		if (string.Equals(gitStartOrRef, "initial", StringComparison.OrdinalIgnoreCase))
		{
			if (this._commits.Count == 0)
			{
				throw new InvalidOperationException("No commits available for 'initial' reference.");
			}
			return Task.FromResult(this._commits.First());
		}

		return Task.FromResult(gitStartOrRef);
	}

	/// <summary>
	/// Returns the last commit in the test data.
	/// </summary>
	public Task<string> GetHeadShaAsync(string workingDirectory)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		
		if (this._commits.Count == 0)
		{
			throw new InvalidOperationException("No commits available for HEAD reference.");
		}
		
		return Task.FromResult(this._commits.Last());
	}

	/// <summary>
	/// Returns a range of commits between start and head SHAs.
	/// </summary>
	public Task<List<string>> GetCommitsRangeAsync(string workingDirectory, string startSha, string headSha)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(startSha);
		ArgumentNullException.ThrowIfNull(headSha);

		int si = this._commits.IndexOf(startSha);
		int ei = this._commits.IndexOf(headSha);
		if (si < 0 || ei < 0 || ei < si)
		{
			return Task.FromResult(new List<string>());
		}

		return Task.FromResult(this._commits.GetRange(si, ei - si + 1));
	}

	/// <summary>
	/// Lists files available at the specified commit.
	/// </summary>
	public Task<List<string>> ListFilesAtCommitAsync(string workingDirectory, string sha)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(sha);

		if (this._filesAtCommit.TryGetValue(sha, out var map))
		{
			return Task.FromResult(map.Keys.Select(FakeGitClient.Normalize).ToList());
		}

		return Task.FromResult(new List<string>());
	}

	/// <summary>
	/// Returns changes between two commits.
	/// </summary>
	public Task<List<GitChange>> GetChangesAsync(string workingDirectory, string prevSha, string sha)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(prevSha);
		ArgumentNullException.ThrowIfNull(sha);

		if (this._changes.TryGetValue((prevSha, sha), out var list))
		{
			return Task.FromResult(list);
		}

		return Task.FromResult(new List<GitChange>());
	}

	/// <summary>
	/// Opens a file stream for the specified commit and path.
	/// </summary>
	public Task<Stream> OpenFileAsync(string workingDirectory, string sha, string repoRelativePath)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(sha);
		ArgumentNullException.ThrowIfNull(repoRelativePath);

		string p = FakeGitClient.Normalize(repoRelativePath);
		this.OpenedFiles.Add((sha, p));
		
		if (this._filesAtCommit.TryGetValue(sha, out var map) && map.TryGetValue(p, out string? content))
		{
			if (content == null)
			{
				throw new FileNotFoundException($"File {p} at commit {sha} has null content.");
			}
			
			return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
		}

		throw new FileNotFoundException($"No file {p} at commit {sha}");
	}

	/// <summary>
	/// Normalizes a file path to use forward slashes.
	/// </summary>
	/// <param name="path">The path to normalize.</param>
	/// <returns>The normalized path.</returns>
	private static string Normalize(string path) => path.Replace('\\', '/');
}