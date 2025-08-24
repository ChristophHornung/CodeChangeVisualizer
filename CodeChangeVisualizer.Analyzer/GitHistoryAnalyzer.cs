namespace CodeChangeVisualizer.Analyzer;

using System.Text.RegularExpressions;

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
		IGitClient git = new GitCliClient();
		return await GitHistoryAnalyzer.RunAdvancedGitAnalysisAsync(git, workingDirectory, gitStart, ignorePatterns,
			fileExtensions);
	}

	/// <summary>
	/// Overload that accepts a custom IGitClient (for testing or alternate implementations).
	/// </summary>
	public static async Task<RevisionLog> RunAdvancedGitAnalysisAsync(
		IGitClient git,
		string workingDirectory,
		string gitStart,
		List<string>? ignorePatterns = null,
		List<string>? fileExtensions = null)
	{
		string workDir = Path.GetFullPath(workingDirectory);

		// Ensure clean working directory (safety)
		bool clean = await git.StatusCleanAsync(workDir);
		if (!clean)
		{
			throw new InvalidOperationException(
				"Git working tree has local changes. Please commit or stash before running advanced analysis.");
		}

		string startSha = await git.ResolveCommitAsync(workDir, gitStart);
		if (string.IsNullOrWhiteSpace(startSha))
		{
			throw new InvalidOperationException($"Cannot resolve start git hash '{gitStart}'.");
		}

		string headSha = await git.GetHeadShaAsync(workDir);
		List<string> commits = await git.GetCommitsRangeAsync(workDir, startSha, headSha);

		RevisionLog log = new RevisionLog();

		// Normalize filters
		List<string>?
			exts = (fileExtensions != null && fileExtensions.Count > 0) ? fileExtensions : ["*.cs"]; // default
		List<string>? ignores = (ignorePatterns != null && ignorePatterns.Count > 0) ? ignorePatterns : null;

		// Snapshot map of previous commit analyses for incremental diffs
		Dictionary<string, FileAnalysis>? prevMap = null;

		for (int idx = 0; idx < commits.Count; idx++)
		{
			string sha = commits[idx];
			CodeAnalyzer analyzer = new CodeAnalyzer();

			if (prevMap == null)
			{
				// First commit: full analysis from plumbing
				List<string> allFiles = await git.ListFilesAtCommitAsync(workDir, sha);
				List<string> filesToAnalyze = GitHistoryAnalyzer.FilterByExtensionsAndIgnores(allFiles, exts, ignores);
				List<FileAnalysis> current = new List<FileAnalysis>(filesToAnalyze.Count);
				foreach (string repoPath in filesToAnalyze)
				{
					using Stream stream = await git.OpenFileAsync(workDir, sha, repoPath);
					FileAnalysis fa = await analyzer.AnalyzeFileAsync(stream, repoPath);
					fa.File = repoPath.Replace('\\', '/');
					current.Add(fa);
				}

				// Deterministic ordering
				current = current.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase).ToList();
				log.Revisions.Add(new RevisionEntry { Commit = sha, Analysis = current });
				prevMap = current.ToDictionary(f => f.File, f => f, StringComparer.OrdinalIgnoreCase);
			}
			else
			{
				string prevSha = commits[idx - 1];
				List<GitChange> rawChanges = await git.GetChangesAsync(workDir, prevSha, sha);
				// Expand renames into delete+add to keep downstream simple
				List<GitChange> changes = new();
				foreach (var ch in rawChanges)
				{
					if (ch.Kind == GitChangeKind.Rename)
					{
						if (!string.IsNullOrWhiteSpace(ch.OldPath))
						{
							changes.Add(new GitChange { Kind = GitChangeKind.Delete, Path = ch.OldPath! });
						}

						changes.Add(new GitChange { Kind = GitChangeKind.Add, Path = ch.Path });
					}
					else
					{
						changes.Add(ch);
					}
				}

				// Keep only files we care about
				List<string> changePaths =
					changes.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				List<string> filteredPaths =
					GitHistoryAnalyzer.FilterByExtensionsAndIgnores(changePaths, exts, ignores);

				List<FileChangeEntry> diffEntries = new();
				Dictionary<string, FileAnalysis> nextMap = new(prevMap, StringComparer.OrdinalIgnoreCase);

				foreach (string path in filteredPaths)
				{
					GitChangeKind lastKind = changes
						.Last(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)).Kind;

					prevMap.TryGetValue(path, out FileAnalysis? oldFa);
					FileAnalysis newFa;
					if (lastKind == GitChangeKind.Delete)
					{
						newFa = new FileAnalysis { File = path, Lines = new List<LineGroup>() };
					}
					else
					{
						using Stream stream = await git.OpenFileAsync(workDir, sha, path);
						newFa = await analyzer.AnalyzeFileAsync(stream, path);
					}

					oldFa ??= new FileAnalysis { File = path, Lines = new List<LineGroup>() };
					FileDiff fd = FileDiffer.DiffFile(oldFa, newFa);

					// Skip no-op modify (no edits)
					if (fd.Kind != FileChangeKind.Modify || (fd.Edits != null && fd.Edits.Count > 0))
					{
						diffEntries.Add(new FileChangeEntry
							{ File = path, Change = FileAnalysisDiff.FromFileDiff(fd) });
					}

					// Update next map according to kind
					if (fd.Kind == FileChangeKind.FileDelete)
					{
						nextMap.Remove(path);
					}
					else
					{
						nextMap[path] = newFa;
					}
				}

				// Deterministic ordering
				diffEntries = diffEntries.OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase).ToList();
				log.Revisions.Add(new RevisionEntry { Commit = sha, Diff = diffEntries });
				prevMap = nextMap;
			}
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

	private static List<string> FilterByExtensionsAndIgnores(List<string> paths, List<string>? fileExtensions,
		List<string>? ignorePatterns)
	{
		// Normalize to forward slashes
		IEnumerable<string> seq = paths.Select(p => p.Replace('\\', '/'));

		// Apply ignore regex patterns first
		if (ignorePatterns != null && ignorePatterns.Count > 0)
		{
			List<Regex> regs = new();
			foreach (string pat in ignorePatterns)
			{
				try
				{
					regs.Add(new Regex(pat, RegexOptions.IgnoreCase));
				}
				catch
				{
					/* skip invalid regex */
				}
			}

			seq = seq.Where(p => !regs.Any(r => r.IsMatch(p)));
		}

		// Apply extension globs (*.cs etc.) using simple Regex-based glob matching on the file name
		if (fileExtensions != null && fileExtensions.Count > 0)
		{
			seq = seq.Where(p =>
			{
				string name = Path.GetFileName(p);
				foreach (string glob in fileExtensions)
				{
					if (GitHistoryAnalyzer.GlobIsMatch(name, glob))
					{
						return true;
					}
				}

				return false;
			});
		}

		return seq.ToList();
	}

	private static bool GlobIsMatch(string input, string pattern)
	{
		// Translate glob to regex: escape regex chars, then replace \* -> .*, \? -> .
		string escaped = Regex.Escape(pattern)
			.Replace("\\*", ".*")
			.Replace("\\?", ".");
		string regex = "^" + escaped + "$";
		return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
	}
}