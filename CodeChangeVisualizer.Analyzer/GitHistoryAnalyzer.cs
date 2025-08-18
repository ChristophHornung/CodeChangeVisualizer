namespace CodeChangeVisualizer.Analyzer;

using System.Diagnostics;

/// <summary>
/// Provides advanced git history analysis utilities that run the code analyzer across revisions
/// and produce a revision log containing the first full analysis followed by diffs per commit.
/// </summary>
public static class GitHistoryAnalyzer
{
	/// <summary>
	/// Runs the advanced git analysis from <paramref name="gitStart"/> up to HEAD for the given working directory.
	/// Produces a <see cref="RevisionLog"/> where the first entry contains full analysis and subsequent entries contain diffs.
	/// </summary>
	/// <param name="workingDirectory">The repository subdirectory to analyze (will also be used as git working directory).</param>
	/// <param name="gitStart">The starting commit hash or ref to include as the first revision.</param>
	/// <param name="ignorePatterns">Optional regex patterns to ignore files (relative paths from workingDirectory).</param>
	/// <param name="fileExtensions">Optional file extensions (globs) to include. Defaults to *.cs if null or empty.</param>
	/// <returns>The computed <see cref="RevisionLog"/>.</returns>
	public static async Task<RevisionLog> RunAdvancedGitAnalysisAsync(
		string workingDirectory,
		string gitStart,
		List<string>? ignorePatterns = null,
		List<string>? fileExtensions = null)
	{
		string workDir = Path.GetFullPath(workingDirectory);

		// Ensure clean working directory
		string status = await GitHistoryAnalyzer.RunGitAsync("status --porcelain", workDir);
		if (!string.IsNullOrWhiteSpace(status))
		{
			throw new InvalidOperationException(
				"Git working tree has local changes. Please commit or stash before running advanced analysis.");
		}

		string originalRef = (await GitHistoryAnalyzer.RunGitAsync("rev-parse --abbrev-ref HEAD", workDir)).Trim();
		string originalSha = (await GitHistoryAnalyzer.RunGitAsync("rev-parse HEAD", workDir)).Trim();
		bool restoreBySha = string.Equals(originalRef, "HEAD", StringComparison.OrdinalIgnoreCase) ||
		                    string.IsNullOrWhiteSpace(originalRef);

		string startSha = (await GitHistoryAnalyzer.RunGitAsync($"rev-parse {gitStart}", workDir)).Trim();
		if (string.IsNullOrWhiteSpace(startSha))
		{
			throw new InvalidOperationException($"Cannot resolve start git hash '{gitStart}'.");
		}

		// Commits after start (excluding start)
		string list = await GitHistoryAnalyzer.RunGitAsync($"rev-list --reverse {startSha}..HEAD", workDir);
		List<string> commits = list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
		// Include start as first
		commits.Insert(0, startSha);

		RevisionLog log = new RevisionLog();

		List<FileAnalysis>? prev = null;
		try
		{
			for (int idx = 0; idx < commits.Count; idx++)
			{
				string sha = commits[idx];
				await GitHistoryAnalyzer.RunGitAsync($"checkout --quiet {sha}", workDir);

				CodeAnalyzer analyzer = new CodeAnalyzer();
				DirectoryAnalysis currentAll = await analyzer.AnalyzeDirectoryAsync(
					workDir,
					ignorePatterns != null && ignorePatterns.Count > 0 ? ignorePatterns : null,
					fileExtensions != null && fileExtensions.Count > 0 ? fileExtensions : null);
				List<FileAnalysis> current = currentAll.Files;

				if (prev == null)
				{
					log.Revisions.Add(new RevisionEntry { Commit = sha, Analysis = current });
				}
				else
				{
					List<FileChangeEntry> diff = GitHistoryAnalyzer.ComputeDiff(prev, current);
					log.Revisions.Add(new RevisionEntry { Commit = sha, Diff = diff });
				}

				prev = current;
			}
		}
		finally
		{
			// Restore original ref
			string target = restoreBySha ? originalSha : originalRef;
			await GitHistoryAnalyzer.RunGitAsync($"checkout --quiet {target}", workDir);
		}

		return log;
	}

	private static List<FileChangeEntry> ComputeDiff(List<FileAnalysis> oldAnalyses, List<FileAnalysis> newAnalyses)
	{
		List<FileChangeEntry> result = new List<FileChangeEntry>();
		Dictionary<string, FileAnalysis> oldMap = oldAnalyses.ToDictionary(f => f.File, f => f);
		Dictionary<string, FileAnalysis> newMap = newAnalyses.ToDictionary(f => f.File, f => f);

		// Removed files
		foreach (KeyValuePair<string, FileAnalysis> kv in oldMap)
		{
			if (!newMap.ContainsKey(kv.Key))
			{
				FileDiff fd = FileDiffer.DiffFile(kv.Value,
					new FileAnalysis { File = kv.Key, Lines = new List<LineGroup>() });
				result.Add(new FileChangeEntry { File = kv.Key, Change = FileAnalysisDiff.FromFileDiff(fd) });
			}
		}

		// Added and modified files
		foreach (KeyValuePair<string, FileAnalysis> kv in newMap)
		{
			if (!oldMap.TryGetValue(kv.Key, out FileAnalysis? oldFa))
			{
				FileDiff fd = FileDiffer.DiffFile(new FileAnalysis { File = kv.Key, Lines = new List<LineGroup>() },
					kv.Value);
				result.Add(new FileChangeEntry { File = kv.Key, Change = FileAnalysisDiff.FromFileDiff(fd) });
			}
			else
			{
				FileDiff fd = FileDiffer.DiffFile(oldFa, kv.Value);
				// Skip no-op modify (no edits)
				if (fd.Kind != FileChangeKind.Modify || (fd.Edits != null && fd.Edits.Count > 0))
				{
					result.Add(new FileChangeEntry { File = kv.Key, Change = FileAnalysisDiff.FromFileDiff(fd) });
				}
			}
		}

		// Order by file for deterministic output
		return result.OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static async Task<string> RunGitAsync(string arguments, string workingDirectory)
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