namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;

/// <summary>
/// Provides a testable, engine-agnostic projection of DiffPlaybackScript's sequencing logic.
/// It simulates how the script decides which blocks become the new sequence (pass-through, resize, insert)
/// and which old blocks are removed, using only lengths/types (no Stride entities).
/// </summary>
public static class DiffPlaybackPlanner
{
	/// <summary>
	/// For add/delete steps, synthesize edit operations to be consumed by the same merge logic.
	/// Mirrors DiffPlaybackScript.BuildEditsForAddOrDelete.
	/// </summary>
	public static List<DiffEdit> BuildEditsForAddOrDelete(List<LineGroup> oldLines, List<LineGroup> newLines)
	{
		if (oldLines.Count == 0 && newLines.Count > 0)
		{
			List<DiffEdit> list = new();
			for (int i = 0; i < newLines.Count; i++)
			{
				LineGroup g = newLines[i];
				list.Add(new DiffEdit { Kind = DiffOpType.Insert, Index = i, LineType = g.Type, NewLength = g.Length });
			}

			return list;
		}

		if (oldLines.Count > 0 && newLines.Count == 0)
		{
			List<DiffEdit> list = new();
			for (int i = 0; i < oldLines.Count; i++)
			{
				LineGroup g = oldLines[i];
				list.Add(new DiffEdit { Kind = DiffOpType.Remove, Index = i, LineType = g.Type, OldLength = g.Length });
			}

			return list;
		}

		// fallback to Differ to compute edits between old and new
		return Differ.Diff(new FileAnalysis { File = "", Lines = oldLines },
			new FileAnalysis { File = "", Lines = newLines });
	}

	/// <summary>
	/// Compute a plan for a single file change, using the same index semantics as the DiffPlaybackScript merge loop.
	/// The returned TargetAnalysis is computed by the authoritative FileAnalysisApplier.
	/// </summary>
	public static PlanResult CreatePlan(FileAnalysis current, FileAnalysisDiff change, string file)
	{
		current ??= new FileAnalysis { File = file, Lines = new List<LineGroup>() };
		FileAnalysis target = FileAnalysisApplier.Apply(current, change, file);

		List<LineGroup> oldLines = current.Lines;
		List<LineGroup> newLines = target.Lines;
		List<DiffEdit> edits = change.Kind == FileAnalysisChangeKind.Modify
			? (change.Edits ?? new())
			: DiffPlaybackPlanner.BuildEditsForAddOrDelete(oldLines, newLines);

		int iOld = 0;
		int iOp = 0;
		List<BlockPlan> newSeq = new();
		List<BlockPlan> removed = new();

		while (iOld < oldLines.Count || iOp < edits.Count)
		{
			if (iOp < edits.Count)
			{
				DiffEdit op = edits[iOp];
				if (op.Kind == DiffOpType.Remove && iOld < oldLines.Count && op.Index == iOld)
				{
					int startLen = oldLines[iOld].Length;
					removed.Add(new BlockPlan
						{ LineType = oldLines[iOld].Type, StartLength = startLen, EndLength = 0, IsNew = false });
					iOld++;
					iOp++;
					continue;
				}

				if (op.Kind == DiffOpType.Insert && op.Index == newSeq.Count)
				{
					LineGroup g;
					if (op.Index >= 0 && op.Index < newLines.Count)
					{
						g = newLines[op.Index];
					}
					else
					{
						g = new LineGroup { Type = op.LineType, Length = op.NewLength ?? 0 };
					}

					int endLen = op.NewLength ?? g.Length;
					newSeq.Add(new BlockPlan { LineType = g.Type, StartLength = 0, EndLength = endLen, IsNew = true });
					iOp++;
					continue;
				}

				if (op.Kind == DiffOpType.Resize && op.Index == newSeq.Count && iOld < oldLines.Count)
				{
					int startLen = oldLines[iOld].Length;
					int endLen = op.NewLength ?? startLen;
					newSeq.Add(new BlockPlan
						{ LineType = oldLines[iOld].Type, StartLength = startLen, EndLength = endLen, IsNew = false });
					iOld++;
					iOp++;
					continue;
				}
			}

			if (iOld < oldLines.Count)
			{
				int len = oldLines[iOld].Length;
				newSeq.Add(new BlockPlan
					{ LineType = oldLines[iOld].Type, StartLength = len, EndLength = len, IsNew = false });
				iOld++;
				continue;
			}

			if (iOp < edits.Count && edits[iOp].Kind == DiffOpType.Insert && edits[iOp].Index == newSeq.Count)
			{
				DiffEdit op = edits[iOp];
				LineGroup g;
				if (op.Index >= 0 && op.Index < newLines.Count)
				{
					g = newLines[op.Index];
				}
				else
				{
					g = new LineGroup { Type = op.LineType, Length = op.NewLength ?? 0 };
				}

				int endLen = op.NewLength ?? g.Length;
				newSeq.Add(new BlockPlan { LineType = g.Type, StartLength = 0, EndLength = endLen, IsNew = true });
				iOp++;
				continue;
			}

			break;
		}

		return new PlanResult { NewSequence = newSeq, Removed = removed, TargetAnalysis = target };
	}

	public sealed class BlockPlan
	{
		public LineType LineType { get; init; }
		public int StartLength { get; init; } // length in lines before the step
		public int EndLength { get; init; } // length in lines after the step
		public bool IsNew { get; init; }
	}

	public sealed class PlanResult
	{
		public List<BlockPlan> NewSequence { get; init; } = new();
		public List<BlockPlan> Removed { get; init; } = new();
		public FileAnalysis TargetAnalysis { get; init; } = new();
	}
}