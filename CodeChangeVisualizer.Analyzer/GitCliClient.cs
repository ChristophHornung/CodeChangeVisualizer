namespace CodeChangeVisualizer.Analyzer;

using System.Diagnostics;
using System.Text;

/// <summary>
/// Git client implementation using git plumbing commands only (no checkout mutations).
/// Provides a safe, read-only interface to git repository operations.
/// </summary>
public sealed class GitCliClient : IGitClient
{
	private const int DefaultTimeoutMs = 30000; // 30 seconds
	private const int MaxRetries = 3;
	private const int RetryDelayMs = 1000;

	/// <summary>
	/// Initializes a new instance of the <see cref="GitCliClient"/> class.
	/// </summary>
	public GitCliClient()
	{
	}

	/// <summary>
	/// Checks if the working tree is clean (no staged or unstaged changes).
	/// </summary>
	/// <param name="workingDirectory">The git repository working directory.</param>
	/// <returns>True if the working tree is clean, false otherwise.</returns>
	/// <exception cref="InvalidOperationException">Thrown when git command fails or git is not available.</exception>
	public async Task<bool> StatusCleanAsync(string workingDirectory)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		
		try
		{
			string output = await this.RunGitAsync("status --porcelain", workingDirectory);
			return string.IsNullOrWhiteSpace(output);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to check git status in '{workingDirectory}': {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Resolves a commit reference to its SHA.
	/// </summary>
	/// <param name="workingDirectory">The git repository working directory.</param>
	/// <param name="gitStartOrRef">The commit reference to resolve. Supports "initial" for the root commit.</param>
	/// <returns>The resolved commit SHA.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the reference cannot be resolved or git command fails.</exception>
	public async Task<string> ResolveCommitAsync(string workingDirectory, string gitStartOrRef)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(gitStartOrRef);

		try
		{
			if (string.Equals(gitStartOrRef, "initial", StringComparison.OrdinalIgnoreCase))
			{
				string roots = await this.RunGitAsync("rev-list --max-parents=0 HEAD", workingDirectory);
				string? rootCommit = roots.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
					.FirstOrDefault()?.Trim();
				
				if (string.IsNullOrWhiteSpace(rootCommit))
				{
					throw new InvalidOperationException("No root commit found in repository.");
				}
				
				return rootCommit;
			}

			string sha = await this.RunGitAsync($"rev-parse {gitStartOrRef}", workingDirectory);
			string trimmedSha = sha.Trim();
			
			if (string.IsNullOrWhiteSpace(trimmedSha))
			{
				throw new InvalidOperationException($"Cannot resolve git reference '{gitStartOrRef}'.");
			}
			
			return trimmedSha;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to resolve commit '{gitStartOrRef}' in '{workingDirectory}': {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Gets the current HEAD commit SHA.
	/// </summary>
	/// <param name="workingDirectory">The git repository working directory.</param>
	/// <returns>The HEAD commit SHA.</returns>
	/// <exception cref="InvalidOperationException">Thrown when git command fails or HEAD cannot be resolved.</exception>
	public async Task<string> GetHeadShaAsync(string workingDirectory)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		
		try
		{
			string sha = await this.RunGitAsync("rev-parse HEAD", workingDirectory);
			string trimmedSha = sha.Trim();
			
			if (string.IsNullOrWhiteSpace(trimmedSha))
			{
				throw new InvalidOperationException("Cannot resolve HEAD commit.");
			}
			
			return trimmedSha;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to get HEAD SHA in '{workingDirectory}': {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Gets a chronological list of commit SHAs from start (inclusive) to head (inclusive).
	/// </summary>
	/// <param name="workingDirectory">The git repository working directory.</param>
	/// <param name="startSha">The starting commit SHA (inclusive).</param>
	/// <param name="headSha">The ending commit SHA (inclusive).</param>
	/// <returns>A list of commit SHAs in chronological order.</returns>
	/// <exception cref="InvalidOperationException">Thrown when git command fails or commits cannot be resolved.</exception>
	public async Task<List<string>> GetCommitsRangeAsync(string workingDirectory, string startSha, string headSha)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(startSha);
		ArgumentNullException.ThrowIfNull(headSha);

		try
		{
			string list = await this.RunGitAsync($"rev-list --reverse {startSha}..{headSha}", workingDirectory);
			List<string> commits = list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => s.Trim())
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.ToList();
			
			// Include the start commit if it's not already in the list
			if (!commits.Contains(startSha, StringComparer.OrdinalIgnoreCase))
			{
				commits.Insert(0, startSha);
			}
			
			return commits;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to get commit range from '{startSha}' to '{headSha}' in '{workingDirectory}': {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Lists all files in the repository at the specified commit, restricted to the current directory.
	/// </summary>
	/// <param name="workingDirectory">The git repository working directory.</param>
	/// <param name="sha">The commit SHA to list files from.</param>
	/// <returns>A list of repository-relative file paths.</returns>
	/// <exception cref="InvalidOperationException">Thrown when git command fails or commit cannot be accessed.</exception>
	public async Task<List<string>> ListFilesAtCommitAsync(string workingDirectory, string sha)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(sha);

		try
		{
			// Limit to current directory pathspec to avoid scanning whole repo
			string output = await this.RunGitAsync($"ls-tree -r --name-only {sha} -- .", workingDirectory);
			List<string> files = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(GitCliClient.NormalizePath)
				.Where(f => !string.IsNullOrWhiteSpace(f))
				.ToList();
			return files;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to list files at commit '{sha}' in '{workingDirectory}': {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Gets the changes between two commits, restricted to the current directory.
	/// </summary>
	/// <param name="workingDirectory">The git repository working directory.</param>
	/// <param name="prevSha">The previous commit SHA.</param>
	/// <param name="sha">The current commit SHA.</param>
	/// <returns>A list of file changes between the commits.</returns>
	/// <exception cref="InvalidOperationException">Thrown when git command fails or commits cannot be compared.</exception>
	public async Task<List<GitChange>> GetChangesAsync(string workingDirectory, string prevSha, string sha)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(prevSha);
		ArgumentNullException.ThrowIfNull(sha);

		try
		{
			string output = await this.RunGitAsync($"diff --name-status {prevSha} {sha} -- .", workingDirectory);
			List<GitChange> changes = new();
			
			foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string line = raw.Trim();
				if (string.IsNullOrWhiteSpace(line))
					continue;

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
							_ => GitChangeKind.Modify // Default to modify for unknown status
						};
						changes.Add(new GitChange { Kind = kind, Path = GitCliClient.NormalizePath(parts[1]) });
					}
				}
			}

			return changes;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to get changes between '{prevSha}' and '{sha}' in '{workingDirectory}': {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Opens a readable stream for the file contents at the given commit and repo-relative path.
	/// </summary>
	/// <param name="workingDirectory">The git repository working directory.</param>
	/// <param name="sha">The commit SHA to read the file from.</param>
	/// <param name="repoRelativePath">The repository-relative path to the file.</param>
	/// <returns>A readable stream containing the file contents.</returns>
	/// <exception cref="InvalidOperationException">Thrown when git command fails or file cannot be accessed.</exception>
	/// <exception cref="FileNotFoundException">Thrown when the file does not exist at the specified commit.</exception>
	public async Task<Stream> OpenFileAsync(string workingDirectory, string sha, string repoRelativePath)
	{
		ArgumentNullException.ThrowIfNull(workingDirectory);
		ArgumentNullException.ThrowIfNull(sha);
		ArgumentNullException.ThrowIfNull(repoRelativePath);

		try
		{
			string path = repoRelativePath.Replace('\\', '/');
			// Quote the path component to support spaces and special characters
			string quoted = path.Replace("\"", "\\\"");
			string arguments = $"show --textconv {sha}:\"{quoted}\"";
			string content;
			try
			{
				content = await this.RunGitAsync(arguments, workingDirectory);
			}
			catch (Exception firstEx)
			{
				// Determine current subdirectory prefix relative to repo root and try alternative path variant
				string prefix = string.Empty;
				try
				{
					prefix = (await this.RunGitAsync("rev-parse --show-prefix", workingDirectory)).Trim()
						.Replace('\\', '/');
				}
				catch
				{
					prefix = string.Empty;
				}

				if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith('/'))
				{
					prefix += "/";
				}

				string altPath = path;
				if (!string.IsNullOrEmpty(prefix))
				{
					if (altPath.StartsWith(prefix, StringComparison.Ordinal))
					{
						altPath = altPath.Substring(prefix.Length);
					}
					else
					{
						altPath = prefix + altPath;
					}
				}

				string altQuoted = altPath.Replace("\"", "\\\"");
				string altArgs = $"show --textconv {sha}:\"{altQuoted}\"";
				try
				{
					content = await this.RunGitAsync(altArgs, workingDirectory);
				}
				catch (Exception secondEx)
				{
					// Attempt a case-insensitive lookup within the directory at this commit (Windows-friendly)
					try
					{
						string dirPart = altPath;
						int lastSlash = altPath.LastIndexOf('/') >= 0
							? altPath.LastIndexOf('/')
							: altPath.LastIndexOf('\\');
						string fileName = altPath;
						if (lastSlash >= 0)
						{
							dirPart = altPath.Substring(0, lastSlash);
							fileName = altPath.Substring(lastSlash + 1);
						}

						string dirArg = string.IsNullOrWhiteSpace(dirPart) ? "." : dirPart.Replace('\\', '/');
						// Use a full-tree listing to avoid missing files due to subdirectory pathspec mismatches
						string list = await this.RunGitAsync($"ls-tree -r --name-only --full-tree {sha}",
							workingDirectory);
						var entries = list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
							.Select(GitCliClient.NormalizePath)
							.ToList();
						// Try to find exact path ignoring case
						string? ciMatch = entries.FirstOrDefault(e =>
							string.Equals(e, altPath, StringComparison.OrdinalIgnoreCase));
						if (ciMatch != null)
						{
							string ciQuoted = ciMatch.Replace("\"", "\\\"");
							string ciArgs = $"show --textconv {sha}:\"{ciQuoted}\"";
							content = await this.RunGitAsync(ciArgs, workingDirectory);
						}
						else
						{
							// Try a unique suffix match (handles when the workingDirectory is a subdir and entries are repo-root relative)
							var suffixMatches = entries
								.Where(e => e.EndsWith("/" + altPath, StringComparison.OrdinalIgnoreCase)).ToList();
							if (suffixMatches.Count == 1)
							{
								string chosen = suffixMatches[0];
								string chosenQuoted = chosen.Replace("\"", "\\\"");
								string chosenArgs = $"show --textconv {sha}:\"{chosenQuoted}\"";
								content = await this.RunGitAsync(chosenArgs, workingDirectory);
							}
							else
							{
								// Build a few similar candidates to help diagnostics
								var similar = entries.Where(e =>
										e.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) ||
										string.Equals(Path.GetFileName(e), fileName,
											StringComparison.OrdinalIgnoreCase))
									.Take(5)
									.ToList();
								string similarList = similar.Count > 0 ? string.Join(", ", similar) : "<none>";
								throw new FileNotFoundException(
									$"File '{repoRelativePath}' not found at commit '{sha}'. Tried paths: '{path}', '{altPath}'. Similar in commit: {similarList}",
									secondEx);
							}
						}
					}
					catch (FileNotFoundException)
					{
						throw;
					}
					catch (Exception diagEx)
					{
						throw new FileNotFoundException(
							$"File '{repoRelativePath}' not found at commit '{sha}'. Tried paths: '{path}', '{altPath}'. Additional lookup failed: {diagEx.Message}",
							secondEx);
					}
				}
			}

			return new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
		}
		catch (FileNotFoundException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to open file '{repoRelativePath}' at commit '{sha}' in '{workingDirectory}': {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Normalizes a file path to use forward slashes.
	/// </summary>
	/// <param name="path">The path to normalize.</param>
	/// <returns>The normalized path with forward slashes.</returns>
	private static string NormalizePath(string path) => path.Replace('\\', '/');

	/// <summary>
	/// Runs a git command with retry logic and timeout.
	/// </summary>
	/// <param name="arguments">The git command arguments.</param>
	/// <param name="workingDirectory">The working directory for the git command.</param>
	/// <returns>The command output as a string.</returns>
	/// <exception cref="InvalidOperationException">Thrown when git command fails after retries or git is not available.</exception>
	private async Task<string> RunGitAsync(string arguments, string workingDirectory)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		ArgumentNullException.ThrowIfNull(workingDirectory);

		Exception? lastException = null;
		
		for (int attempt = 1; attempt <= MaxRetries; attempt++)
		{
			try
			{
				using var cts = new CancellationTokenSource(DefaultTimeoutMs);
				
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
				
				// Read output asynchronously with timeout
				var stdoutTask = proc.StandardOutput.ReadToEndAsync();
				var stderrTask = proc.StandardError.ReadToEndAsync();
				var exitTask = proc.WaitForExitAsync(cts.Token);

				await Task.WhenAll(stdoutTask, stderrTask, exitTask);

				string stdout = await stdoutTask;
				string stderr = await stderrTask;

				if (proc.ExitCode != 0)
				{
					string errorMessage = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : $"git {arguments} failed with exit code {proc.ExitCode}";
					throw new InvalidOperationException(errorMessage);
				}

				return stdout;
			}
			catch (OperationCanceledException) when (attempt < MaxRetries)
			{
				lastException = new TimeoutException($"Git command timed out after {DefaultTimeoutMs}ms: git {arguments}");
				await Task.Delay(RetryDelayMs * attempt);
			}
			catch (Exception ex) when (attempt < MaxRetries && IsRetryableError(ex))
			{
				lastException = ex;
				await Task.Delay(RetryDelayMs * attempt);
			}
			catch (Exception ex)
			{
				// Non-retryable error or last attempt
				throw new InvalidOperationException($"Git command failed: git {arguments}", ex);
			}
		}

		// All retries exhausted
		throw new InvalidOperationException($"Git command failed after {MaxRetries} attempts: git {arguments}", lastException);
	}

	/// <summary>
	/// Determines if an error is retryable.
	/// </summary>
	/// <param name="ex">The exception to check.</param>
	/// <returns>True if the error is retryable, false otherwise.</returns>
	private static bool IsRetryableError(Exception ex)
	{
		// Retry on common transient errors
		return ex.Message.Contains("fatal:") && 
		       (ex.Message.Contains("packet") || 
		        ex.Message.Contains("connection") || 
		        ex.Message.Contains("timeout") ||
		        ex.Message.Contains("remote"));
	}
}