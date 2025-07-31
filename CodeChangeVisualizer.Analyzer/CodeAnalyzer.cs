namespace CodeChangeVisualizer.Analyzer;

public class CodeAnalyzer
{
	public async Task<List<FileAnalysis>> AnalyzeDirectoryAsync(string directoryPath, List<string>? ignorePatterns = null, List<string>? fileExtensions = null)
	{
		List<FileAnalysis> results = [];
		
		// Default to C# files if no extensions specified
		fileExtensions ??= new List<string> { "*.cs" };
		
		// Get all files matching the specified extensions
		List<string> allFiles = new();
		foreach (string extension in fileExtensions)
		{
			allFiles.AddRange(Directory.GetFiles(directoryPath, extension, SearchOption.AllDirectories));
		}

		// Filter files based on ignore patterns
		List<string> filteredFiles = this.FilterFiles(allFiles, directoryPath, ignorePatterns);

		foreach (string filePath in filteredFiles)
		{
			string relativePath = Path.GetRelativePath(directoryPath, filePath);
			FileAnalysis fileAnalysis = await this.AnalyzeFileAsync(filePath, relativePath);
			results.Add(fileAnalysis);
		}

		return results;
	}

	private List<string> FilterFiles(List<string> files, string baseDirectory, List<string>? ignorePatterns)
	{
		if (ignorePatterns == null || ignorePatterns.Count == 0)
		{
			return files;
		}

		List<string> filteredFiles = new();
		
		foreach (string filePath in files)
		{
			string relativePath = Path.GetRelativePath(baseDirectory, filePath).Replace('\\', '/');
			bool shouldInclude = true;

			foreach (string pattern in ignorePatterns)
			{
				try
				{
					if (System.Text.RegularExpressions.Regex.IsMatch(relativePath, pattern))
					{
						shouldInclude = false;
						break;
					}
				}
				catch (ArgumentException)
				{
					// Invalid regex pattern, skip it
					continue;
				}
			}

			if (shouldInclude)
			{
				filteredFiles.Add(filePath);
			}
		}

		return filteredFiles;
	}

	public async Task<FileAnalysis> AnalyzeFileAsync(Stream stream, string relativePath)
	{
		using StreamReader reader = new(stream);
		string[] lines = (await reader.ReadToEndAsync()).Split([Environment.NewLine], StringSplitOptions.None);
		return await this.AnalyzeFileAsync(relativePath, lines);
	}

	public async Task<FileAnalysis> AnalyzeFileAsync(string filePath, string relativePath)
	{
		if (!File.Exists(filePath))
		{
			throw new FileNotFoundException($"The file '{filePath}' does not exist.");
		}

		string[] lines = await File.ReadAllLinesAsync(filePath);
		return await this.AnalyzeFileAsync(relativePath, lines);
	}

	private Task<FileAnalysis> AnalyzeFileAsync(string relativePath, string[] lines)
	{
		List<LineGroup> lineGroups = [];

		LineType currentType = LineType.Empty;
		int currentStart = 1;
		int currentLength = 0;

		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			LineType lineType = this.DetermineLineType(line);

			if (lineType == currentType && currentLength > 0)
			{
				currentLength++;
			}
			else
			{
				if (currentLength > 0)
				{
					lineGroups.Add(new LineGroup()
					{
						Start = currentStart,
						Length = currentLength,
						Type = currentType
					});
				}

				currentType = lineType;
				currentStart = i + 1;
				currentLength = 1;
			}
		}

		// Add the last group
		if (currentLength > 0)
		{
			lineGroups.Add(new()
			{
				Start = currentStart,
				Length = currentLength,
				Type = currentType
			});
		}

		return Task.FromResult(new FileAnalysis
		{
			File = relativePath.Replace('\\', '/'),
			Lines = lineGroups
		});
	}

	private LineType DetermineLineType(string line)
	{
		string trimmedLine = line.Trim();

		if (string.IsNullOrWhiteSpace(trimmedLine))
		{
			return LineType.Empty;
		}

		// Check if it's a pure comment line (starts with comment markers)
		if (this.IsPureCommentLine(trimmedLine))
		{
			return LineType.Comment;
		}

		// Check if it's a complexity-increasing line (including those with comments)
		if (this.IsComplexityIncreasingLine(trimmedLine))
		{
			return LineType.ComplexityIncreasing;
		}

		// Check if it has both code and comments
		if (this.HasCodeAndComments(trimmedLine))
		{
			return LineType.CodeAndComment;
		}

		// Must be regular code
		return LineType.Code;
	}

	private bool IsPureCommentLine(string line)
	{
		string trimmed = line.Trim();
		return trimmed.StartsWith("//") ||
		       trimmed.StartsWith("/*") ||
		       trimmed.StartsWith("*") ||
		       trimmed.StartsWith("///") ||
		       trimmed.StartsWith("*/");
	}

	private bool HasCodeAndComments(string line)
	{
		string trimmed = line.Trim();

		// Check if the line contains both code and single-line comments
		if (trimmed.Contains("//") && !trimmed.TrimStart().StartsWith("//"))
		{
			return true;
		}

		return false;
	}

	private bool IsComplexityIncreasingLine(string line)
	{
		string trimmed = line.Trim();
		string[] complexityKeywords =
		[
			"if", "else", "for", "foreach", "while", "do", "switch", "case", "catch", "finally",
			"try", "throw", "return", "break", "continue", "goto", "yield", "await", "lock"
		];

		// Remove comments for keyword detection
		string codeOnly = this.RemoveComments(trimmed);
		string trimmedCodeOnly = codeOnly.Trim();

		foreach (string keyword in complexityKeywords)
		{
			if (trimmedCodeOnly.StartsWith(keyword + " ") ||
			    trimmedCodeOnly.StartsWith(keyword + "(") ||
			    trimmedCodeOnly.StartsWith(keyword + "\t") ||
			    trimmedCodeOnly == keyword ||
			    trimmedCodeOnly.StartsWith(keyword + ";"))
			{
				return true;
			}
		}

		return false;
	}


	private string RemoveComments(string line)
	{
		// Remove single-line comments
		int singleLineCommentIndex = line.IndexOf("//");
		if (singleLineCommentIndex >= 0)
		{
			line = line.Substring(0, singleLineCommentIndex);
		}

		// Remove XML documentation comments
		int xmlCommentIndex = line.IndexOf("///");
		if (xmlCommentIndex >= 0)
		{
			line = line.Substring(0, xmlCommentIndex);
		}

		return line.Trim();
	}
}