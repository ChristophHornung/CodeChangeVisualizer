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
	/// <exception cref="ArgumentNullException">Thrown when workingDirectory or gitStart is null.</exception>
	/// <exception cref="ArgumentException">Thrown when workingDirectory or gitStart is empty or whitespace.</exception>
	/// <exception cref="InvalidOperationException">Thrown when git operations fail or working tree is not clean.</exception>
	public static async Task<RevisionLog> RunAdvancedGitAnalysisAsync(
		string workingDirectory,
		string gitStart,
		List<string>? ignorePatterns = null,
		List<string>? fileExtensions = null,
		IProgress<GitAnalysisProgress>? progress = null)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(gitStart);
		
		if (string.IsNullOrWhiteSpace(workingDirectory))
			throw new ArgumentException("Working directory cannot be empty or whitespace.", nameof(workingDirectory));
		if (string.IsNullOrWhiteSpace(gitStart))
			throw new ArgumentException("Git start reference cannot be empty or whitespace.", nameof(gitStart));

		IGitClient git = new GitCliClient();
		return await GitHistoryAnalyzer.RunAdvancedGitAnalysisAsync(git, workingDirectory, gitStart, ignorePatterns,
			fileExtensions, progress);
	}

	/// <summary>
	/// Overload that accepts a custom IGitClient (for testing or alternate implementations).
	/// </summary>
	/// <param name="git">The git client to use for operations.</param>
	/// <param name="workingDirectory">The repository subdirectory to analyze.</param>
	/// <param name="gitStart">The starting commit hash or ref to include as the first revision.</param>
	/// <param name="ignorePatterns">Optional regex patterns to ignore files.</param>
	/// <param name="fileExtensions">Optional file extensions (globs) to include.</param>
	/// <returns>The computed <see cref="RevisionLog"/>.</returns>
	/// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
	/// <exception cref="ArgumentException">Thrown when workingDirectory or gitStart is empty or whitespace.</exception>
	/// <exception cref="InvalidOperationException">Thrown when git operations fail or working tree is not clean.</exception>
	public static async Task<RevisionLog> RunAdvancedGitAnalysisAsync(
		IGitClient git,
		string workingDirectory,
		string gitStart,
		List<string>? ignorePatterns = null,
		List<string>? fileExtensions = null,
		IProgress<GitAnalysisProgress>? progress = null)
	{
		ArgumentNullException.ThrowIfNull(git);
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(gitStart);
		
		if (string.IsNullOrWhiteSpace(workingDirectory))
			throw new ArgumentException("Working directory cannot be empty or whitespace.", nameof(workingDirectory));
		if (string.IsNullOrWhiteSpace(gitStart))
			throw new ArgumentException("Git start reference cannot be empty or whitespace.", nameof(gitStart));

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
		if (string.IsNullOrWhiteSpace(headSha))
		{
			throw new InvalidOperationException("Cannot resolve HEAD commit.");
		}

		List<string> commits = await git.GetCommitsRangeAsync(workDir, startSha, headSha);
		if (commits.Count == 0)
		{
			throw new InvalidOperationException($"No commits found between '{startSha}' and '{headSha}'.");
		}

		progress?.Report(new GitAnalysisProgress { Kind = "CommitsTotal", Total = commits.Count });
		RevisionLog log = new RevisionLog();

		// Normalize filters
		List<string> exts = (fileExtensions != null && fileExtensions.Count > 0) ? fileExtensions : ["*.cs"]; // default
		List<string>? ignores = (ignorePatterns != null && ignorePatterns.Count > 0) ? ignorePatterns : null;

		// Snapshot map of previous commit analyses for incremental diffs
		Dictionary<string, FileAnalysis>? prevMap = null;

		for (int idx = 0; idx < commits.Count; idx++)
		{
			string sha = commits[idx];
			CodeAnalyzer analyzer = new CodeAnalyzer();
			progress?.Report(new GitAnalysisProgress { Kind = "CommitStarted", Commit = sha, Value = idx + 1 });

			if (prevMap == null)
			{
				// First commit: full analysis from plumbing
				List<string> allFiles = await git.ListFilesAtCommitAsync(workDir, sha);
				List<string> filesToAnalyze = GitHistoryAnalyzer.FilterByExtensionsAndIgnores(allFiles, exts, ignores);
				List<FileAnalysis> current = new List<FileAnalysis>(filesToAnalyze.Count);
				progress?.Report(new GitAnalysisProgress
					{ Kind = "FilesTotal", Commit = sha, Total = filesToAnalyze.Count });
				
				foreach (string repoPath in filesToAnalyze)
				{
					try
					{
						using Stream stream = await git.OpenFileAsync(workDir, sha, repoPath);
						FileAnalysis fa = await analyzer.AnalyzeFileAsync(stream, repoPath);
						fa.File = repoPath.Replace('\\', '/');
						current.Add(fa);
					}
					catch (Exception ex)
					{
						// Log the error but continue with other files
						Console.WriteLine($"Warning: Failed to analyze file '{repoPath}' at commit '{sha}': {ex.Message}");
					}
					finally
					{
						// Always advance progress regardless of success to keep counts consistent
						progress?.Report(new GitAnalysisProgress
							{ Kind = "FileProcessed", Commit = sha, File = repoPath });
					}
				}

				// Deterministic ordering
				current = current.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase).ToList();
				log.Revisions.Add(new RevisionEntry { Commit = sha, Analysis = current });
				prevMap = current.ToDictionary(f => f.File, f => f, StringComparer.OrdinalIgnoreCase);
				progress?.Report(new GitAnalysisProgress { Kind = "CommitCompleted", Commit = sha, Value = idx + 1 });
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
				progress?.Report(new GitAnalysisProgress
					{ Kind = "FilesTotal", Commit = sha, Total = filteredPaths.Count });
				
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
						try
						{
							using Stream stream = await git.OpenFileAsync(workDir, sha, path);
							newFa = await analyzer.AnalyzeFileAsync(stream, path);
						}
						catch (Exception ex)
						{
							// Log the error but continue with other files
							Console.WriteLine($"Warning: Failed to analyze file '{path}' at commit '{sha}': {ex.Message}");
							continue;
						}
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

					progress?.Report(new GitAnalysisProgress { Kind = "FileProcessed", Commit = sha, File = path });
				}
				
				// Deterministic ordering
				diffEntries = diffEntries.OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase).ToList();
				log.Revisions.Add(new RevisionEntry { Commit = sha, Diff = diffEntries });
				prevMap = nextMap;
				progress?.Report(new GitAnalysisProgress { Kind = "CommitCompleted", Commit = sha, Value = idx + 1 });
			}
		}

		return log;
	}

	/// <summary>
	/// Computes the difference between two sets of file analyses.
	/// </summary>
	/// <param name="oldAnalyses">The previous file analyses.</param>
	/// <param name="newAnalyses">The current file analyses.</param>
	/// <returns>A list of file change entries representing the differences.</returns>
	private static List<FileChangeEntry> ComputeDiff(List<FileAnalysis> oldAnalyses, List<FileAnalysis> newAnalyses)
	{
		ArgumentNullException.ThrowIfNull(oldAnalyses);
		ArgumentNullException.ThrowIfNull(newAnalyses);

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

	/// <summary>
	/// Filters a list of file paths based on extension patterns and ignore patterns.
	/// </summary>
	/// <param name="paths">The list of file paths to filter.</param>
	/// <param name="fileExtensions">Optional file extension patterns (e.g., "*.cs").</param>
	/// <param name="ignorePatterns">Optional regex patterns to ignore files.</param>
	/// <returns>The filtered list of file paths.</returns>
	private static List<string> FilterByExtensionsAndIgnores(List<string> paths, List<string>? fileExtensions,
		List<string>? ignorePatterns)
	{
		ArgumentNullException.ThrowIfNull(paths);

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
				catch (ArgumentException ex)
				{
					// Log invalid regex patterns but continue
					Console.WriteLine($"Warning: Invalid ignore pattern '{pat}': {ex.Message}");
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

	/// <summary>
	/// Checks if a filename matches a glob pattern.
	/// </summary>
	/// <param name="input">The filename to check.</param>
	/// <param name="pattern">The glob pattern to match against.</param>
	/// <returns>True if the filename matches the pattern, false otherwise.</returns>
	private static bool GlobIsMatch(string input, string pattern)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(pattern);

		try
		{
			// Translate glob to regex: escape regex chars, then replace \* -> .*, \? -> .
			string escaped = Regex.Escape(pattern)
				.Replace("\\*", ".*")
				.Replace("\\?", ".");
			string regex = "^" + escaped + "$";
			return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
		}
		catch (ArgumentException)
		{
			// Return false for invalid patterns
			return false;
		}
	}
}