namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Unified applier that can apply either block-level edits (IEnumerable&lt;DiffEdit&gt;)
/// or a higher-level FileAnalysisDiff (FileAdd/FileDelete/Modify).
/// This centralizes the logic into a single class as requested.
/// </summary>
public static class FileAnalysisApplier
{
	/// <summary>
	/// Applies a sequence of diff edits to the given <paramref name="oldFile"/> and produces a new FileAnalysis.
	/// Notes on index meanings (as produced by Differ):
	/// - Remove.Index: index in OLD sequence (consumes one old block, does not advance new index).
	/// - Insert.Index: index in NEW sequence (adds a new block at current new index).
	/// - Resize.Index: index in NEW sequence (consumes one old block and outputs a resized block).
	///
	/// This method uses a left-to-right merge guided by those indices.
	/// It also recomputes the LineGroup.Start fields to be contiguous starting at 0.
	/// </summary>
	public static FileAnalysis Apply(FileAnalysis oldFile, IEnumerable<DiffEdit> edits, string? newFileName = null)
	{
		if (oldFile == null)
		{
			throw new ArgumentNullException(nameof(oldFile));
		}

		if (edits == null)
		{
			throw new ArgumentNullException(nameof(edits));
		}

		List<LineGroup> oldLines = oldFile.Lines;
		List<LineGroup> result = new();
		List<DiffEdit> ops = edits.ToList();

		int iOld = 0; // index in oldLines
		int iOp = 0; // index in ops

		while (iOld < oldLines.Count || iOp < ops.Count)
		{
			// If we still have an operation to try to apply at this position, attempt in priority order:
			if (iOp < ops.Count)
			{
				DiffEdit op = ops[iOp];

				// 1) Remove applies when its index matches the current old index.
				if (op.Kind == DiffOpType.Remove && iOld < oldLines.Count && op.Index == iOld)
				{
					// Validate type if possible (best-effort)
					if (op.LineType == oldLines[iOld].Type)
					{
						// consume the old block without adding to result
						iOld++;
						iOp++;
						continue;
					}

					// If types don't match, we still consume to best match index-based semantics
					iOld++;
					iOp++;
					continue;
				}

				// 2) Insert applies when its index matches the current new index (i.e., result.Count)
				if (op.Kind == DiffOpType.Insert && op.Index == result.Count)
				{
					result.Add(new LineGroup
					{
						Type = op.LineType,
						Length = op.NewLength ?? 0
					});
					iOp++;
					continue;
				}

				// 3) Resize applies when its index matches the current new index (result.Count) and consumes one old
				if (op.Kind == DiffOpType.Resize && op.Index == result.Count && iOld < oldLines.Count)
				{
					// Prefer the type from the edit, but ensure it matches the old type if possible
					LineGroup old = oldLines[iOld];
					result.Add(new LineGroup
					{
						Type = op.LineType,
						Length = op.NewLength ?? old.Length
					});
					iOld++;
					iOp++;
					continue;
				}
			}

			// If no op applied, copy through the next old block unchanged to result
			if (iOld < oldLines.Count)
			{
				LineGroup old = oldLines[iOld++];
				result.Add(new LineGroup { Type = old.Type, Length = old.Length });
				continue;
			}

			// No old blocks left; only applicable operations now are trailing inserts at the end position
			if (iOp < ops.Count && ops[iOp].Kind == DiffOpType.Insert && ops[iOp].Index == result.Count)
			{
				DiffEdit op = ops[iOp++];
				result.Add(new LineGroup { Type = op.LineType, Length = op.NewLength ?? 0 });
				continue;
			}

			// Nothing more to do
			break;
		}

		// Recompute Start indices to be contiguous starting at 0
		int start = 0;
		foreach (LineGroup g in result)
		{
			g.Start = start;
			start += g.Length;
		}

		return new FileAnalysis
		{
			File = newFileName ?? oldFile.File,
			Lines = result
		};
	}

	/// <summary>
	/// Applies a unified FileAnalysisDiff to <paramref name="oldFile"/>.
	/// </summary>
	public static FileAnalysis Apply(FileAnalysis oldFile, FileAnalysisDiff diff, string? newFileName = null)
	{
		if (oldFile == null)
		{
			throw new ArgumentNullException(nameof(oldFile));
		}

		if (diff == null)
		{
			throw new ArgumentNullException(nameof(diff));
		}

		switch (diff.Kind)
		{
			case FileAnalysisChangeKind.FileAdd:
			{
				// Use provided blocks (deep copy) and recompute Start
				List<LineGroup> lines = (diff.NewFileLines ?? new List<LineGroup>())
					.Select(g => new LineGroup { Type = g.Type, Length = g.Length })
					.ToList();
				FileAnalysisApplier.RecomputeStarts(lines);
				return new FileAnalysis
				{
					File = newFileName ?? diff.NewFileName ?? oldFile.File,
					Lines = lines
				};
			}
			case FileAnalysisChangeKind.FileDelete:
			{
				return new FileAnalysis
				{
					File = newFileName ?? diff.NewFileName ?? oldFile.File,
					Lines = new List<LineGroup>()
				};
			}
			case FileAnalysisChangeKind.Modify:
			default:
			{
				List<DiffEdit> edits = diff.Edits ?? new List<DiffEdit>();
				return FileAnalysisApplier.Apply(oldFile, edits, newFileName ?? diff.NewFileName);
			}
		}
	}

	private static void RecomputeStarts(List<LineGroup> list)
	{
		int start = 0;
		foreach (LineGroup g in list)
		{
			g.Start = start;
			start += g.Length;
		}
	}
}