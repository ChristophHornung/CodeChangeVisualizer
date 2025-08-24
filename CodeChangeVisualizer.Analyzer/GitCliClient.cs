namespace CodeChangeVisualizer.Analyzer;

using System.Diagnostics;
using System.Text;

/// <summary>
/// Git client implementation using git plumbing commands only (no checkout mutations).
/// </summary>
public sealed class GitCliClient : IGitClient
{
	public async Task<bool> StatusCleanAsync(string workingDirectory)
	{
		string output = await this.RunGitAsync("status --porcelain", workingDirectory);
		return string.IsNullOrWhiteSpace(output);
	}

	public async Task<string> ResolveCommitAsync(string workingDirectory, string gitStartOrRef)
	{
		if (string.Equals(gitStartOrRef, "initial", StringComparison.OrdinalIgnoreCase))
		{
			string roots = await this.RunGitAsync("rev-list --max-parents=0 HEAD", workingDirectory);
			return roots.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ??
			       string.Empty;
		}

		string sha = await this.RunGitAsync($"rev-parse {gitStartOrRef}", workingDirectory);
		return sha.Trim();
	}

	public async Task<string> GetHeadShaAsync(string workingDirectory)
	{
		string sha = await this.RunGitAsync("rev-parse HEAD", workingDirectory);
		return sha.Trim();
	}

	public async Task<List<string>> GetCommitsRangeAsync(string workingDirectory, string startSha, string headSha)
	{
		string list = await this.RunGitAsync($"rev-list --reverse {startSha}..{headSha}", workingDirectory);
		List<string> commits = list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(s => s.Trim()).ToList();
		commits.Insert(0, startSha);
		return commits;
	}

	public async Task<List<string>> ListFilesAtCommitAsync(string workingDirectory, string sha)
	{
		// Limit to current directory pathspec to avoid scanning whole repo
		string output = await this.RunGitAsync($"ls-tree -r --name-only {sha} -- .", workingDirectory);
		List<string> files = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(GitCliClient.NormalizePath)
			.ToList();
		return files;
	}

	public async Task<List<GitChange>> GetChangesAsync(string workingDirectory, string prevSha, string sha)
	{
		string output = await this.RunGitAsync($"diff --name-status {prevSha} {sha} -- .", workingDirectory);
		List<GitChange> changes = new();
		foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string line = raw.Trim();
			// Examples: "A\tpath", "M\tpath", "D\tpath", "R100\told\tnew"
			string[] parts = line.Split('\t');
			if (parts.Length >= 2)
			{
				string status = parts[0];
				if (status.StartsWith("R", StringComparison.Ordinal))
				{
					if (parts.Length >= 3)
					{
						changes.Add(new GitChange
						{
							Kind = GitChangeKind.Rename,
							OldPath = GitCliClient.NormalizePath(parts[1]),
							Path = GitCliClient.NormalizePath(parts[2])
						});
					}
				}
				else
				{
					GitChangeKind kind = status switch
					{
						"A" => GitChangeKind.Add,
						"M" => GitChangeKind.Modify,
						"D" => GitChangeKind.Delete,
						_ => GitChangeKind.Modify
					};
					changes.Add(new GitChange { Kind = kind, Path = GitCliClient.NormalizePath(parts[1]) });
				}
			}
		}

		return changes;
	}

	public async Task<Stream> OpenFileAsync(string workingDirectory, string sha, string repoRelativePath)
	{
		string path = repoRelativePath.Replace('\\', '/');
		string content = await this.RunGitAsync($"show {sha}:{path}", workingDirectory);
		return new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
	}

	private static string NormalizePath(string p) => p.Replace('\\', '/');

	private async Task<string> RunGitAsync(string arguments, string workingDirectory)
	{
		ProcessStartInfo psi = new()
		{
			FileName = "git",
			Arguments = arguments,
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		using Process proc = Process.Start(psi)!;
		string stdout = await proc.StandardOutput.ReadToEndAsync();
		string stderr = await proc.StandardError.ReadToEndAsync();
		await proc.WaitForExitAsync();
		if (proc.ExitCode != 0)
		{
			throw new InvalidOperationException($"git {arguments} failed: {stderr}");
		}

		return stdout;
	}
}