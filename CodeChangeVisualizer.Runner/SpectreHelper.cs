namespace CodeChangeVisualizer.Runner;

using CodeChangeVisualizer.Analyzer;
using Spectre.Console;

internal static class SpectreHelper
{
	public static async Task<RevisionLog> RunWithProgressAsync(Configuration config)
	{
		RevisionLog? result = null;

		try
		{
			await AnsiConsole.Progress()
				.AutoClear(true)
				.Columns(new ProgressColumn[]
				{
					new TaskDescriptionColumn(),
					new ProgressBarColumn(),
					new PercentageColumn(),
					new RemainingTimeColumn(),
					new SpinnerColumn()
				})
				.StartAsync(async ctx =>
				{
					// Define tasks
					var commitsTask = ctx.AddTask("Analyzing commits",
						new ProgressTaskSettings { AutoStart = false, MaxValue = 1 });
					var filesTask = ctx.AddTask("Analyzing files",
						new ProgressTaskSettings { AutoStart = false, MaxValue = 1 });

					void SafeSetFilesDescription(string? commit, GitAnalysisProgress p)
					{
						try
						{
							// Build a plain text description without any raw markup tokens, then escape the entire string
							string raw = $"Analyzing files {commit ?? string.Empty}";
							string desc = Markup.Escape(raw);
							// Validate markup eagerly so Spectre renderer doesn't explode later
							_ = new Markup(desc);
							filesTask.Description = desc;
						}
						catch (Exception ex)
						{
							Console.WriteLine(
								"[DIAGNOSTIC] Failed to set Spectre task description. Falling back to safe text.");
							Console.WriteLine(
								$"[DIAGNOSTIC] Progress Kind={p.Kind}, Commit='{commit}', File='{p.File}'");
							Console.WriteLine($"[DIAGNOSTIC] Exception: {ex}");
							filesTask.Description = "Analyzing files (commit shown in logs)";
						}
					}

					var progress = new Progress<GitAnalysisProgress>(p =>
					{
						switch (p.Kind)
						{
							case "CommitsTotal":
								commitsTask.MaxValue = p.Total ?? 0;
								commitsTask.Value = 0;
								commitsTask.StartTask();
								break;
							case "CommitStarted":
								// Reset files task for a new commit without stopping it to avoid restart exceptions
								filesTask.Value = 0;
								SafeSetFilesDescription(p.Commit, p);
								break;
							case "FilesTotal":
								filesTask.MaxValue = p.Total ?? 0;
								filesTask.Value = 0;
								if (filesTask.MaxValue > 0 && !filesTask.IsStarted)
								{
									filesTask.StartTask();
								}

								break;
							case "FileProcessed":
								if (filesTask.IsStarted)
								{
									filesTask.Increment(1);
								}

								break;
							case "CommitCompleted":
								if (commitsTask.IsStarted)
								{
									commitsTask.Increment(1);
								}

								// Do not stop filesTask; it will be reset on next commit
								break;
						}
					});

					result = await GitHistoryAnalyzer.RunAdvancedGitAnalysisAsync(
						config.Directory!,
						config.GitStart!,
						config.IgnorePatterns.Count > 0 ? config.IgnorePatterns : null,
						config.FileExtensions.Count > 0 ? config.FileExtensions : null,
						progress);
				});
		}
		catch (Exception ex)
		{
			Console.WriteLine("[DIAGNOSTIC] Spectre progress failed. Falling back to non-progress run.");
			Console.WriteLine(ex.ToString());
			// Fallback to analysis without progress to avoid terminating the app
			result = await GitHistoryAnalyzer.RunAdvancedGitAnalysisAsync(
				config.Directory!,
				config.GitStart!,
				config.IgnorePatterns.Count > 0 ? config.IgnorePatterns : null,
				config.FileExtensions.Count > 0 ? config.FileExtensions : null,
				progress: null);
		}

		return result!;
	}
}