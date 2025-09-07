namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

/// <summary>
/// Plays back a sequence of diffs (from advanced git analysis) step-by-step.
/// On each Space key press, applies the next diff with a 2-second animation:
/// - Tower deletion: sink into ground, then remove entity from scene.
/// - Tower addition: create and grow blocks from zero height.
/// - Tower modify: resize/insert/remove blocks with smooth height transitions; recompute Y positions each frame.
/// </summary>
public class DiffPlaybackScript : SyncScript
{
	// Constants should match SkyscraperVisualizer for visual consistency
	private const float UnitsPerLine = LayoutCalculator.Constants.UnitsPerLine; // same as visualizer
	private const float TowerSpacing = LayoutCalculator.Constants.TowerSpacing;
	private const float BlockWidth = LayoutCalculator.Constants.BlockWidth;
	private const float BlockDepth = LayoutCalculator.Constants.BlockDepth;
	private const float AnimationDuration = 2.0f; // seconds per step
	private const float SinkDistance = 3.0f; // how far to sink deleted towers below ground

	private static readonly Dictionary<LineType, Color4> LineTypeColors = new()
	{
		[LineType.Empty] = new Color4(1f, 1f, 1f, 1f),
		[LineType.Comment] = new Color4(0f, 1f, 0f, 1f),
		[LineType.ComplexityIncreasing] = new Color4(1f, 0f, 0f, 1f),
		[LineType.Code] = new Color4(0.5f, 0.5f, 0.5f, 1f),
		[LineType.CodeAndComment] = new Color4(0.56f, 0.93f, 0.56f, 1f)
	};

	// Runtime state
	private readonly Dictionary<string, FileAnalysis> currentAnalyses = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Entity> towers = new(StringComparer.OrdinalIgnoreCase);
	private int nextGridIndex;
	private int diffIndex; // index of the next diff step to play (we do not mutate Diffs)

	// Animation state per step
	private bool animating;
	private float elapsed;

	private StepAnimation? currentStep;

	// Autoplay flag: when true, we keep advancing steps automatically when not animating
	private bool autoPlay;

	private Model? templateModel;

	[DataMemberIgnore]
	public List<FileAnalysis> InitialAnalysis { get; set; } = new();

	[DataMemberIgnore]
	public List<List<FileChangeEntry>> Diffs { get; set; } = new();

	private static List<DiffEdit> BuildEditsForAddOrDelete(List<LineGroup> oldLines, List<LineGroup> newLines)
	{
		if (oldLines.Count == 0 && newLines.Count > 0)
		{
			// pure add => inserts for each new block
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
			// pure delete => removes for each old block
			List<DiffEdit> list = new();
			for (int i = 0; i < oldLines.Count; i++)
			{
				LineGroup g = oldLines[i];
				list.Add(new DiffEdit { Kind = DiffOpType.Remove, Index = i, LineType = g.Type, OldLength = g.Length });
			}

			return list;
		}

		// fallback: compute via Differ if needed
		return Differ.Diff(new FileAnalysis { File = "", Lines = oldLines },
			new FileAnalysis { File = "", Lines = newLines });
	}

	public override void Start()
	{
		// Build lookup of existing towers/entities based on the InitialAnalysis that has already been built by SkyscraperVisualizer
		// Find tower roots by name (file path used as entity name)
		var roots = this.SceneSystem.SceneInstance?.RootScene.Entities;
		foreach (var file in this.InitialAnalysis)
		{
			this.currentAnalyses[file.File] = new FileAnalysis
			{
				File = file.File,
				Lines = file.Lines.Select(g => new LineGroup { Type = g.Type, Length = g.Length, Start = g.Start })
					.ToList()
			};
			Entity? root =
				roots.FirstOrDefault(e => string.Equals(e.Name, file.File, StringComparison.OrdinalIgnoreCase));
			if (root != null)
			{
				this.towers[file.File] = root;
			}
		}

		this.nextGridIndex = this.towers.Count; // append for newly added towers
		this.diffIndex = 0; // start from the first diff step
	}

	public override void Update()
	{
		if (this.Input == null)
		{
			return;
		}

		if (!this.animating)
		{
			// Reset to first visualization on 'R'
			if (this.Input.IsKeyPressed(Keys.R))
			{
				this.ResetToInitialVisualization();
			}

			// Trigger next step on Space key
			if (this.Input.IsKeyPressed(Keys.Space))
			{
				this.StartNextStep();
			}

			// Toggle autoplay with 'L' (play all remaining steps, sequentially)
			if (this.Input.IsKeyPressed(Keys.L))
			{
				this.autoPlay = !this.autoPlay;
				Console.WriteLine(this.autoPlay
					? "[Playback] Autoplay ON (will advance with 2s per step)."
					: "[Playback] Autoplay OFF.");
			}

			// If autoplay is enabled and not animating, attempt to start the next step
			if (this.autoPlay)
			{
				this.StartNextStep();
			}

			return;
		}

		// Animate current step
		this.elapsed += (float)this.Game.UpdateTime.Elapsed.TotalSeconds;
		float t = Math.Min(1f, this.elapsed / DiffPlaybackScript.AnimationDuration);

		if (this.currentStep == null)
		{
			this.animating = false;
			return;
		}

		foreach (TowerAnim tower in this.currentStep.Towers)
		{
			if (tower.IsDelete)
			{
				if (tower.Root != null)
				{
					float y = MathUtil.Lerp(tower.StartRootPos.Y,
						tower.StartRootPos.Y - DiffPlaybackScript.SinkDistance, t);
					tower.Root.Transform.Position = new Vector3(tower.StartRootPos.X, y, tower.StartRootPos.Z);
				}

				continue;
			}

			// Animate tower root movement when layout changes
			if (tower.Root != null && tower.MoveRoot)
			{
				Vector3 p = Vector3.Lerp(tower.StartRootPos, tower.EndRootPos, t);
				tower.Root.Transform.Position = p;
			}

			// Animate blocks for adds/modifies
			// Update heights for blocks that will exist in the resulting state
			foreach (BlockAnim b in tower.NewSequence)
			{
				b.CurrentHeight = MathUtil.Lerp(b.StartHeight, b.EndHeight, t);
				this.ApplyBlockSize(b.Entity!, b.CurrentHeight);
			}

			// Update heights for blocks that will be removed (shrink to zero)
			foreach (BlockAnim b in tower.Removed)
			{
				b.CurrentHeight = MathUtil.Lerp(b.StartHeight, 0f, t);
				if (b.Entity != null)
				{
					this.ApplyBlockSize(b.Entity, b.CurrentHeight);
				}
			}

			// Recompute Y positions for new sequence (target stack)
			float currentY = 0f;
			foreach (BlockAnim b in tower.NewSequence)
			{
				if (b.Entity == null)
				{
					continue;
				}

				float h = b.CurrentHeight;
				b.Entity.Transform.Position = new Vector3(0f, currentY + h / 2f, 0f);
				currentY += h;
			}

			// Recompute Y positions for old stack (only apply to removed blocks so they visibly shrink in place)
			if (tower.OldSequence != null && tower.OldSequence.Count > 0 && tower.Removed.Count > 0)
			{
				var removedSet = new HashSet<BlockAnim>(tower.Removed);
				float oldY = 0f;
				foreach (BlockAnim b in tower.OldSequence)
				{
					float h = Math.Max(0f, b.CurrentHeight);
					if (removedSet.Contains(b) && b.Entity != null)
					{
						b.Entity.Transform.Position = new Vector3(0f, oldY + h / 2f, 0f);
					}
					oldY += h;
				}
			}
		}

		if (t >= 1f)
		{
			// Finalize: apply end state, cleanup removes and deletes, reset scales
			foreach (TowerAnim tower in this.currentStep.Towers)
			{
				if (tower.IsDelete)
				{
					if (tower.Root != null)
					{
						// remove from scene and maps
						this.SceneSystem.SceneInstance?.RootScene.Entities.Remove(tower.Root);
					}

					this.towers.Remove(tower.File);
					this.currentAnalyses.Remove(tower.File);
					continue;
				}

				// Cleanup removed blocks
				foreach (BlockAnim b in tower.Removed)
				{
					if (b.Entity != null)
					{
						Entity parent = b.Entity.GetParent() ?? tower.Root!;
						parent.RemoveChild(b.Entity);
					}
				}

				// Normalize new sequence blocks to have absolute scale matching final size
				foreach (BlockAnim b in tower.NewSequence)
				{
					if (b.Entity == null)
					{
						continue;
					}

					BlockDescriptorComponent? desc = b.Entity.Get<BlockDescriptorComponent>();
					if (desc != null)
					{
						desc.Size = new Vector3(DiffPlaybackScript.BlockWidth, b.EndHeight,
							DiffPlaybackScript.BlockDepth);
					}

					// Set absolute transform scale to match final block size (consistent with SkyscraperVisualizer)
					b.Entity.Transform.Scale = new Vector3(DiffPlaybackScript.BlockWidth, b.EndHeight,
						DiffPlaybackScript.BlockDepth);
				}

				// Update current analysis
				if (tower.TargetAnalysis != null)
				{
					this.currentAnalyses[tower.File] = tower.TargetAnalysis;
				}
			}

			Console.WriteLine($"[Playback] Step {this.diffIndex} completed.");
			this.animating = false;
			this.currentStep = null;
		}
	}

	private void ResetToInitialVisualization()
	{
		// Stop any ongoing animation
		this.animating = false;
		this.elapsed = 0f;
		this.currentStep = null;

		// Remove all current tower entities from the scene
		var rootEntities = this.SceneSystem.SceneInstance?.RootScene.Entities;
		if (rootEntities != null)
		{
			foreach (var kv in this.towers.ToList())
			{
				if (kv.Value != null)
				{
					rootEntities.Remove(kv.Value);
				}
			}
		}

		// Clear runtime maps
		this.towers.Clear();
		this.currentAnalyses.Clear();

		// Reset playback to the beginning and rebuild the towers using the same layout logic as creation
		this.diffIndex = 0;

		// Rebuild the towers using the same layout logic as creation via the planner
		int total = this.InitialAnalysis.Count;
		this.planner.SetFiles(this.InitialAnalysis);
		for (int index = 0; index < total; index++)
		{
			var file = this.InitialAnalysis[index];
			Vector3 pos = this.planner.GetPosition(index);
			Entity fileRoot = new Entity(file.File);
			fileRoot.Transform.Position = new Vector3(pos.X, 0f, pos.Z);
			rootEntities?.Add(fileRoot);
			this.towers[file.File] = fileRoot;
			this.currentAnalyses[file.File] = new FileAnalysis
			{
				File = file.File,
				Lines = file.Lines.Select(g => new LineGroup { Type = g.Type, Length = g.Length, Start = g.Start })
					.ToList()
			};
			
			// Build blocks
			float currentY = 0f;
			foreach (var group in file.Lines)
			{
				var block = this.CreateBlock(file.File, group, currentY);
				fileRoot.AddChild(block);
    currentY += LayoutCalculator.ComputeBlockHeight(group.Length);
			}
		}

		this.nextGridIndex = this.towers.Count;
	}

	private void StartNextStep()
	{
		if (this.Diffs.Count == 0)
		{
			Console.WriteLine("[Playback] No diffs available.");
			this.autoPlay = false;
			return;
		}

		if (this.diffIndex >= this.Diffs.Count)
		{
			Console.WriteLine("[Playback] No more steps to play. Reached the end of the revision log.");
			// Stop autoplay if it was active
			this.autoPlay = false;
			return;
		}

		Console.WriteLine($"[Playback] SPACE pressed: starting step {this.diffIndex + 1}/{this.Diffs.Count}");
		List<FileChangeEntry> diff = this.Diffs[this.diffIndex];
		Console.WriteLine($"[Playback] Step {this.diffIndex + 1}: {diff.Count} file change(s)");
		this.diffIndex++;

		// Compute current and future layouts to animate tower movements
		var currentList = this.currentAnalyses.Values.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase).ToList();
		var currentIndex = currentList.Select((f, i) => new { f.File, i }).ToDictionary(x => x.File, x => x.i, StringComparer.OrdinalIgnoreCase);
		this.planner.SetFiles(currentList);
		Dictionary<string, Vector3> currentPositions = currentList.ToDictionary(f => f.File,
			f => this.planner.GetPosition(currentIndex[f.File]), StringComparer.OrdinalIgnoreCase);
		
		// Apply diffs to get future list
		Dictionary<string, FileAnalysis> futureMap = this.currentAnalyses.ToDictionary(kv => kv.Key,
			kv => new FileAnalysis
			{
				File = kv.Value.File,
				Lines = kv.Value.Lines.Select(g => new LineGroup { Type = g.Type, Length = g.Length, Start = g.Start })
					.ToList()
			}, StringComparer.OrdinalIgnoreCase);
		foreach (var change in diff)
		{
			string file = change.File;
			FileAnalysisDiff fad = change.Change;
			futureMap.TryGetValue(file, out FileAnalysis? oldFa);
			oldFa ??= new FileAnalysis { File = file, Lines = new List<LineGroup>() };
			FileAnalysis patched = FileAnalysisApplier.Apply(oldFa, fad, file);
			if (fad.Kind == FileAnalysisChangeKind.FileDelete)
			{
				futureMap.Remove(file);
			}
			else
			{
				futureMap[file] = patched;
			}
		}
		var futureList = futureMap.Values.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase).ToList();
		var futureIndex = futureList.Select((f, i) => new { f.File, i }).ToDictionary(x => x.File, x => x.i, StringComparer.OrdinalIgnoreCase);
		this.planner.SetFiles(futureList);
		Dictionary<string, Vector3> futurePositions = futureList.ToDictionary(f => f.File,
			f => this.planner.GetPosition(futureIndex[f.File]), StringComparer.OrdinalIgnoreCase);
		
		StepAnimation step = new StepAnimation();
		
		foreach (FileChangeEntry change in diff)
		{
			string file = change.File;
			FileAnalysisDiff fad = change.Change;
			if (fad.Kind == FileAnalysisChangeKind.Modify)
			{
				int total = fad.Edits?.Count ?? 0;
				int ins = fad.Edits?.Count(e => e.Kind == DiffOpType.Insert) ?? 0;
				int rem = fad.Edits?.Count(e => e.Kind == DiffOpType.Remove) ?? 0;
				int res = fad.Edits?.Count(e => e.Kind == DiffOpType.Resize) ?? 0;
				Console.WriteLine($"  [Playback] MODIFY {file}: edits={total} (ins={ins}, rem={rem}, res={res})");
			}
			else if (fad.Kind == FileAnalysisChangeKind.FileAdd)
			{
				int blocks = fad.NewFileLines?.Count ?? 0;
				Console.WriteLine($"  [Playback] ADD {file}: {blocks} block(s)");
			}
			else if (fad.Kind == FileAnalysisChangeKind.FileDelete)
			{
				Console.WriteLine($"  [Playback] DELETE {file}");
			}

			this.currentAnalyses.TryGetValue(file, out FileAnalysis? current);
			current ??= new FileAnalysis { File = file, Lines = new List<LineGroup>() };

			// Compute target analysis using unified applier
			FileAnalysis target = FileAnalysisApplier.Apply(current, fad, file);

			TowerAnim towerAnim = new TowerAnim { File = file, TargetAnalysis = target };

			bool hasRoot = this.towers.TryGetValue(file, out Entity? root);
			if (fad.Kind == FileAnalysisChangeKind.FileDelete)
			{
				// Sink the tower and remove
				if (hasRoot && root != null)
				{
					towerAnim.Root = root;
					towerAnim.IsDelete = true;
					towerAnim.StartRootPos = root.Transform.Position;
					step.Towers.Add(towerAnim);
				}

				// If no root (already absent), nothing to animate
				continue;
			}

			if (!hasRoot || root == null)
			{
				// Add new tower: create with blocks at 0 height and grow
				root = this.CreateTowerRoot(file, this.nextGridIndex++);
				this.SceneSystem.SceneInstance?.RootScene.Entities.Add(root);
				this.towers[file] = root;
				towerAnim.Root = root;
				towerAnim.IsAdd = true;
				// Place at future layout position
				if (futurePositions.TryGetValue(file, out var endPos))
				{
					root.Transform.Position = endPos;
					towerAnim.StartRootPos = endPos;
					towerAnim.EndRootPos = endPos;
				}

				// Create blocks with base size equal to target and start height 0
				foreach (LineGroup g in target.Lines)
				{
     float endH = LayoutCalculator.ComputeBlockHeight(g.Length);
					Entity block = this.CreateBlock(file, g, 0f);
					// Initialize with scale 0 (we will grow to 1)
					this.ApplyBlockSize(block, 0f, endH);
					root.AddChild(block);
					towerAnim.NewSequence.Add(new BlockAnim
					{
						Entity = block,
						Color = DiffPlaybackScript.LineTypeColors[g.Type],
						StartHeight = 0f,
						EndHeight = endH,
						CurrentHeight = 0f,
						IsNew = true
					});
				}

				step.Towers.Add(towerAnim);
				continue;
			}

			// Modify or add with existing root: we need to animate block-level changes
			towerAnim.Root = root;
			// Animate root movement if layout changed
			Vector3 actualStart = root.Transform.Position;
			towerAnim.StartRootPos = actualStart;
			if (futurePositions.TryGetValue(file, out var endP))
			{
				towerAnim.EndRootPos = endP;
				towerAnim.MoveRoot = (Vector3.DistanceSquared(actualStart, endP) > 1e-4f);
			}

			// Get current blocks/entities in order by Y
			List<Entity> oldBlocks = root.Transform.Children.Select(c => c.Entity).Where(e => e != null)
				.Select(e => e!)
				.OrderBy(e => e.Transform.Position.Y)
				.ToList();

			List<LineGroup> oldLines = current.Lines;
			List<LineGroup> newLines = target.Lines;
			List<DiffEdit> edits = fad.Kind == FileAnalysisChangeKind.Modify
				? (fad.Edits ?? new())
				: DiffPlaybackScript.BuildEditsForAddOrDelete(oldLines, newLines);

			int iOld = 0; // index in old
			int iOp = 0; // index in edits
			List<BlockAnim> newSeq = new();
			List<BlockAnim> removed = new();

			while (iOld < oldLines.Count || iOp < edits.Count)
			{
				if (iOp < edits.Count)
				{
					DiffEdit op = edits[iOp];
					if (op.Kind == DiffOpType.Remove && iOld < oldLines.Count && op.Index == iOld)
					{
						// mark old block to shrink to zero (if entity exists)
						Entity? ent = iOld < oldBlocks.Count ? oldBlocks[iOld] : null;
						float startH = LayoutCalculator.ComputeBlockHeight(oldLines[iOld].Length);
						if (ent != null)
						{
 						var remBlock = new BlockAnim
 						{
 							Entity = ent, Color = DiffPlaybackScript.LineTypeColors[oldLines[iOld].Type],
 							StartHeight = startH, EndHeight = 0f, CurrentHeight = startH, IsNew = false
 						};
 						removed.Add(remBlock);
 						towerAnim.OldSequence.Add(remBlock);
						}

						// Even if entity is missing, advance to keep indices consistent
						iOld++;
						iOp++;
						continue;
					}

					if (op.Kind == DiffOpType.Insert && op.Index == newSeq.Count)
					{
						// create a new block entity with base of target height and 0 current height
						LineGroup g;
						if (op.Index >= 0 && op.Index < newLines.Count)
						{
							g = newLines[op.Index];
						}
						else
						{
							g = new LineGroup { Type = op.LineType, Length = op.NewLength ?? 0, Start = 0 };
						}

      float endH = LayoutCalculator.ComputeBlockHeight(op.NewLength ?? g.Length);
						Entity block = this.CreateBlock(file, g, 0f);
						this.ApplyBlockSize(block, 0f, endH);
						root.AddChild(block);
						newSeq.Add(new BlockAnim
						{
							Entity = block, Color = DiffPlaybackScript.LineTypeColors[g.Type], StartHeight = 0f,
							EndHeight = endH, CurrentHeight = 0f, IsNew = true
						});
						iOp++;
						continue;
					}

					if (op.Kind == DiffOpType.Resize && op.Index == newSeq.Count && iOld < oldLines.Count)
					{
						Entity? ent = iOld < oldBlocks.Count ? oldBlocks[iOld] : null;
      float startH = LayoutCalculator.ComputeBlockHeight(oldLines[iOld].Length);
      float endH = LayoutCalculator.ComputeBlockHeight(op.NewLength ?? oldLines[iOld].Length);
						if (ent == null)
						{
							// if missing entity, create one to animate resize correctly
							var gOld = oldLines[iOld];
							ent = this.CreateBlock(file, gOld, 0f);
							this.ApplyBlockSize(ent, startH, startH);
							root.AddChild(ent);
						}

						var resized = new BlockAnim
						{
							Entity = ent, Color = DiffPlaybackScript.LineTypeColors[oldLines[iOld].Type],
							StartHeight = startH, EndHeight = endH, CurrentHeight = startH, IsNew = false
						};
						newSeq.Add(resized);
						towerAnim.OldSequence.Add(resized);
						iOld++;
						iOp++;
						continue;
					}
				}

				if (iOld < oldLines.Count)
				{
					// passthrough unchanged block
					Entity? ent = iOld < oldBlocks.Count ? oldBlocks[iOld] : null;
     float h = LayoutCalculator.ComputeBlockHeight(oldLines[iOld].Length);
					if (ent == null)
					{
						// recreate missing entity
						var gOld = oldLines[iOld];
						ent = this.CreateBlock(file, gOld, 0f);
						this.ApplyBlockSize(ent, h, h);
						root.AddChild(ent);
					}

					var passthrough = new BlockAnim
					{
						Entity = ent, Color = DiffPlaybackScript.LineTypeColors[oldLines[iOld].Type], StartHeight = h,
						EndHeight = h, CurrentHeight = h, IsNew = false
					};
					newSeq.Add(passthrough);
					towerAnim.OldSequence.Add(passthrough);
					iOld++;
					continue;
				}

				// trailing inserts at end
				if (iOp < edits.Count && edits[iOp].Kind == DiffOpType.Insert && edits[iOp].Index == newSeq.Count)
				{
					var op = edits[iOp];
					LineGroup g;
					if (op.Index >= 0 && op.Index < newLines.Count)
					{
						g = newLines[op.Index];
					}
					else
					{
						g = new LineGroup { Type = op.LineType, Length = op.NewLength ?? 0, Start = 0 };
					}

     float endH = LayoutCalculator.ComputeBlockHeight(op.NewLength ?? g.Length);
					Entity block = this.CreateBlock(file, g, 0f);
					this.ApplyBlockSize(block, 0f, endH);
					root.AddChild(block);
					newSeq.Add(new BlockAnim
					{
						Entity = block, Color = DiffPlaybackScript.LineTypeColors[g.Type], StartHeight = 0f,
						EndHeight = endH, CurrentHeight = 0f, IsNew = true
					});
					iOp++;
					continue;
				}

				break;
			}

			towerAnim.NewSequence = newSeq;
			towerAnim.Removed = removed;
			step.Towers.Add(towerAnim);
		}

		// Also add pure movement animations for towers not in diff but whose position changes
		foreach (var kv in this.towers)
		{
			string file = kv.Key;
			// Skip if this tower already has an entry in this step
			if (step.Towers.Any(t => string.Equals(t.File, file, StringComparison.OrdinalIgnoreCase)))
				continue;
			if (!futurePositions.ContainsKey(file) || !currentPositions.ContainsKey(file))
				continue;
			Vector3 startP = kv.Value.Transform.Position; // actual current
			Vector3 endP = futurePositions[file];
			if (Vector3.DistanceSquared(startP, endP) > 1e-4f)
			{
				step.Towers.Add(new TowerAnim
				{
					File = file,
					Root = kv.Value,
					StartRootPos = startP,
					EndRootPos = endP,
					MoveRoot = true,
					TargetAnalysis = this.currentAnalyses[file]
				});
			}
		}

		int addCount = step.Towers.Count(t => t.IsAdd);
		int delCount = step.Towers.Count(t => t.IsDelete);
		int modCount = step.Towers.Count - addCount - delCount;
		Console.WriteLine(
			$"[Playback] Step summary: towers affected={step.Towers.Count} (add={addCount}, delete={delCount}, modify={modCount})");

		this.currentStep = step;
		this.elapsed = 0f;
		this.animating = true;
	}

	private readonly ICityPlanner planner = CityPlannerFactory.CreateFromEnv();

 private Entity CreateTowerRoot(string file, int index)
 {
 	// Build a file list representing current files plus this new file to determine position.
    var filesList = this.currentAnalyses.Values.ToList();
 	if (!filesList.Any(f => string.Equals(f.File, file, StringComparison.OrdinalIgnoreCase)))
 	{
 		// If we have a target analysis for this file in the current step, prefer that; otherwise, create a stub.
 		filesList.Add(new FileAnalysis { File = file, Lines = new List<LineGroup>() });
 	}

    this.planner.SetFiles(filesList);
    Vector3 pos = this.planner.GetPosition(index);
 	Entity fileRoot = new Entity(file);
 	fileRoot.Transform.Position = new Vector3(pos.X, 0f, pos.Z);
 	return fileRoot;
 }

	private void EnsureTemplateModel()
	{
		if (this.templateModel != null)
		{
			return;
		}

		// First, try to capture a model from any existing block in the scene
		var all = this.SceneSystem.SceneInstance?.RootScene.Entities;
		if (all != null)
		{
			foreach (var e in all)
			{
				var modelComp = e.Get<ModelComponent>();
				if (modelComp != null && modelComp.Model != null && e.Get<BlockDescriptorComponent>() != null)
				{
					this.templateModel = modelComp.Model;
					break;
				}
			}
		}

		// If still null, synthesize a 1x1x1 cube model procedurally and keep its Model
		if (this.templateModel == null)
		{
			var game = this.Game as Game;
			if (game != null)
			{
				var createOptions = new Primitive3DCreationOptions
					{ Size = new Vector3(1f, 1f, 1f), IncludeCollider = false };
				Entity temp = game.Create3DPrimitive(PrimitiveModelType.Cube, createOptions);
				var modelComp = temp.Get<ModelComponent>();
				if (modelComp != null)
				{
					this.templateModel = modelComp.Model;
				}
				// We don't add 'temp' to the scene; it will be GC'ed. We only need the shared Model
			}
		}
	}

	private Entity CreateBlock(string file, LineGroup group, float currentY)
	{
  float height = LayoutCalculator.ComputeBlockHeight(group.Length);
		Color4 color = DiffPlaybackScript.LineTypeColors[group.Type];

		// Ensure we have a template model to attach to new blocks, even if scene started empty
		this.EnsureTemplateModel();

		Entity cube = new Entity(file);
		if (this.templateModel != null)
		{
			cube.Add(new ModelComponent { Model = this.templateModel });
		}

		// Position so the base sits at currentY (cube is centered, so add half-height)
		cube.Transform.Position = new Vector3(0f, currentY + height / 2f, 0f);
		// Scale to the intended block size so it visually matches SkyscraperVisualizer
		cube.Transform.Scale = new Vector3(DiffPlaybackScript.BlockWidth, height, DiffPlaybackScript.BlockDepth);
		cube.Add(new BlockDescriptorComponent
		{
			Size = new Vector3(DiffPlaybackScript.BlockWidth, height, DiffPlaybackScript.BlockDepth), Color = color
		});

		// Ensure per-entity material override so colors don't bleed across shared model instances
		this.ApplyColorToCube(cube, color);
		return cube;
	}

	private void ApplyBlockSize(Entity block, float currentHeight, float? baseHeightOverride = null)
	{
		BlockDescriptorComponent? desc = block.Get<BlockDescriptorComponent>();
		if (desc == null)
		{
			return;
		}

		// Use absolute scaling so it matches SkyscraperVisualizer and avoids X/Z scale loss.
		float clampedH = Math.Max(0f, currentHeight);
		block.Transform.Scale = new Vector3(DiffPlaybackScript.BlockWidth, clampedH, DiffPlaybackScript.BlockDepth);
		// Keep descriptor Size in sync with the actual world size for picking/metadata
		desc.Size = new Vector3(DiffPlaybackScript.BlockWidth, clampedH, DiffPlaybackScript.BlockDepth);
	}

	private void ApplyColorToCube(Entity cube, Color4 color)
	{
		ModelComponent? modelComponent = cube.Get<ModelComponent>();
		if (modelComponent == null)
		{
			return;
		}

		var game = this.Game as Game;
		if (game?.GraphicsDevice == null)
		{
			return;
		}

		MaterialDescriptor materialDesc = new()
		{
			Attributes = new MaterialAttributes
			{
				Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color)),
				DiffuseModel = new MaterialDiffuseLambertModelFeature(),
			}
		};
		Material? material = Material.New(game.GraphicsDevice, materialDesc);

		// Assign per-entity material override (do not touch the shared Model's materials)
		var materials = modelComponent.Materials;
		if (materials.Count > 0)
		{
			materials[0] = material;
		}
		else
		{
			materials.Add(new KeyValuePair<int, Material>(0, material));
		}
	}

	private class StepAnimation
	{
		public List<TowerAnim> Towers = new();
	}

		private class TowerAnim
		{
			public string File = string.Empty;
			public Entity? Root; // may be null for newly added until created
			public bool IsDelete;
			public bool IsAdd;
			public bool MoveRoot; // when true, lerp Root from StartRootPos to EndRootPos
			public List<BlockAnim> NewSequence = new(); // blocks that exist in the resulting state
			public List<BlockAnim> Removed = new(); // blocks that vanish
			public List<BlockAnim> OldSequence = new(); // blocks in the original stack order (pre-step), includes removed and pass-through/resized
			public FileAnalysis? TargetAnalysis; // resulting analysis for this tower after the step
			public Vector3 StartRootPos; // for sinking / movement
			public Vector3 EndRootPos; // for movement
		}

	private class BlockAnim
	{
		public Entity? Entity;
		public Color4 Color;
		public float StartHeight;
		public float EndHeight;
		public float CurrentHeight;
		public bool IsNew; // true if inserted this step
	}
}