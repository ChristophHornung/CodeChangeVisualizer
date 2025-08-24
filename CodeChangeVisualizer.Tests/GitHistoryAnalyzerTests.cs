namespace CodeChangeVisualizer.Tests;

using CodeChangeVisualizer.Analyzer;

public class GitHistoryAnalyzerTests
{
	[Fact]
	public async Task AdvancedGitAnalysis_UsesPlumbingAndChangedOnly()
	{
		// Arrange fake repo with three commits
		var commits = new List<string> { "c1", "c2", "c3" };
		var filesAtCommit = new Dictionary<string, Dictionary<string, string>>
		{
			["c1"] = new()
			{
				["a.cs"] = "class A {}\n"
			},
			["c2"] = new()
			{
				["a.cs"] = "class A {}\n// comment\n"
			},
			["c3"] = new()
			{
				["b.cs"] = "class B {}\n"
			}
		};
		var changes = new Dictionary<(string prev, string curr), List<GitChange>>
		{
			[("c1", "c2")] = new() { new GitChange { Kind = GitChangeKind.Modify, Path = "a.cs" } },
			[("c2", "c3")] = new()
			{
				new GitChange { Kind = GitChangeKind.Delete, Path = "a.cs" },
				new GitChange { Kind = GitChangeKind.Add, Path = "b.cs" }
			}
		};
		var fake = new FakeGitClient(commits, filesAtCommit, changes);

		// Act
		RevisionLog log = await GitHistoryAnalyzer.RunAdvancedGitAnalysisAsync(fake, ".", "c1");

		// Assert
		Assert.Equal(3, log.Revisions.Count);
		Assert.NotNull(log.Revisions[0].Analysis);
		Assert.Single(log.Revisions[0].Analysis!);
		Assert.Equal("a.cs", log.Revisions[0].Analysis![0].File);

		Assert.NotNull(log.Revisions[1].Diff);
		var d1 = Assert.Single(log.Revisions[1].Diff!);
		Assert.Equal("a.cs", d1.File);
		Assert.Equal(FileAnalysisChangeKind.Modify, d1.Change.Kind);

		Assert.NotNull(log.Revisions[2].Diff);
		Assert.Equal(2, log.Revisions[2].Diff!.Count);
		Assert.Contains(log.Revisions[2].Diff!,
			x => x.File == "a.cs" && x.Change.Kind == FileAnalysisChangeKind.FileDelete);
		Assert.Contains(log.Revisions[2].Diff!,
			x => x.File == "b.cs" && x.Change.Kind == FileAnalysisChangeKind.FileAdd);

		// Verify we opened only needed files: a.cs at c1 and c2, b.cs at c3; no read for delete
		Assert.Equal(new List<(string sha, string path)>
		{
			("c1", "a.cs"),
			("c2", "a.cs"),
			("c3", "b.cs")
		}, fake.OpenedFiles);
	}

	[Fact]
	public async Task AdvancedGitAnalysis_AppliesFilters()
	{
		var commits = new List<string> { "c1", "c2" };
		var filesAtCommit = new Dictionary<string, Dictionary<string, string>>
		{
			["c1"] = new() { ["a.cs"] = "class A {}\n", ["b.cs"] = "class B {}\n", ["readme.md"] = "text\n" },
			["c2"] = new()
				{ ["a.cs"] = "class A {}\n// c\n", ["b.cs"] = "class B {}\n// c\n", ["readme.md"] = "text more\n" }
		};
		var changes = new Dictionary<(string prev, string curr), List<GitChange>>
		{
			[("c1", "c2")] = new()
			{
				new GitChange { Kind = GitChangeKind.Modify, Path = "a.cs" },
				new GitChange { Kind = GitChangeKind.Modify, Path = "b.cs" },
				new GitChange { Kind = GitChangeKind.Modify, Path = "readme.md" }
			}
		};
		var fake = new FakeGitClient(commits, filesAtCommit, changes);

		// Ignore b.cs explicitly and only include *.cs extensions
		var ignore = new List<string> { "^b\\.cs$" };
		RevisionLog log = await GitHistoryAnalyzer.RunAdvancedGitAnalysisAsync(fake, ".", "c1", ignorePatterns: ignore,
			fileExtensions: new List<string> { "*.cs" });

		// First analysis should include only a.cs (b.cs filtered out, readme.md excluded by glob)
		Assert.NotNull(log.Revisions[0].Analysis);
		Assert.Single(log.Revisions[0].Analysis!);
		Assert.Equal("a.cs", log.Revisions[0].Analysis![0].File);

		// Second diff should include only a.cs
		Assert.NotNull(log.Revisions[1].Diff);
		var d1 = Assert.Single(log.Revisions[1].Diff!);
		Assert.Equal("a.cs", d1.File);
		Assert.Equal(FileAnalysisChangeKind.Modify, d1.Change.Kind);

		// Verify file opens: a.cs at both commits, no b.cs/readme.md at commit 2
		Assert.Equal(new List<(string sha, string path)>
		{
			("c1", "a.cs"),
			("c2", "a.cs")
		}, fake.OpenedFiles);
	}
}