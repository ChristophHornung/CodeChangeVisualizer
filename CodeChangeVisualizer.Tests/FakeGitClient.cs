namespace CodeChangeVisualizer.Tests;

using System.Text;
using CodeChangeVisualizer.Analyzer;

internal sealed class FakeGitClient : IGitClient
{
	private readonly List<string> _commits;
	private readonly Dictionary<string, Dictionary<string, string>> _filesAtCommit; // commit -> (path -> content)
	private readonly Dictionary<(string prev, string curr), List<GitChange>> _changes;

	public List<(string sha, string path)> OpenedFiles { get; } = new();

	public FakeGitClient(List<string> commits,
		Dictionary<string, Dictionary<string, string>> filesAtCommit,
		Dictionary<(string prev, string curr), List<GitChange>> changes)
	{
		this._commits = commits;
		this._filesAtCommit = filesAtCommit;
		this._changes = changes;
	}

	public Task<bool> StatusCleanAsync(string workingDirectory) => Task.FromResult(true);

	public Task<string> ResolveCommitAsync(string workingDirectory, string gitStartOrRef)
	{
		if (string.Equals(gitStartOrRef, "initial", StringComparison.OrdinalIgnoreCase))
		{
			return Task.FromResult(this._commits.First());
		}

		return Task.FromResult(gitStartOrRef);
	}

	public Task<string> GetHeadShaAsync(string workingDirectory) => Task.FromResult(this._commits.Last());

	public Task<List<string>> GetCommitsRangeAsync(string workingDirectory, string startSha, string headSha)
	{
		int si = this._commits.IndexOf(startSha);
		int ei = this._commits.IndexOf(headSha);
		if (si < 0 || ei < 0 || ei < si)
		{
			return Task.FromResult(new List<string>());
		}

		return Task.FromResult(this._commits.GetRange(si, ei - si + 1));
	}

	public Task<List<string>> ListFilesAtCommitAsync(string workingDirectory, string sha)
	{
		if (this._filesAtCommit.TryGetValue(sha, out var map))
		{
			return Task.FromResult(map.Keys.Select(FakeGitClient.Normalize).ToList());
		}

		return Task.FromResult(new List<string>());
	}

	public Task<List<GitChange>> GetChangesAsync(string workingDirectory, string prevSha, string sha)
	{
		if (this._changes.TryGetValue((prevSha, sha), out var list))
		{
			return Task.FromResult(list);
		}

		return Task.FromResult(new List<GitChange>());
	}

	public Task<Stream> OpenFileAsync(string workingDirectory, string sha, string repoRelativePath)
	{
		string p = FakeGitClient.Normalize(repoRelativePath);
		this.OpenedFiles.Add((sha, p));
		if (this._filesAtCommit.TryGetValue(sha, out var map) && map.TryGetValue(p, out string content))
		{
			return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
		}

		throw new FileNotFoundException($"No file {p} at commit {sha}");
	}

	private static string Normalize(string p) => p.Replace('\\', '/');
}