namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using System.Linq;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.CommunityToolkit.Bepu;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Core;

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

	[DataMemberIgnore]
	public List<FileAnalysis> InitialAnalysis { get; set; } = new();
	[DataMemberIgnore]
	public List<List<FileChangeEntry>> Diffs { get; set; } = new();

	// Runtime state
	private readonly Dictionary<string, FileAnalysis> _currentAnalyses = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Entity> _towers = new(StringComparer.OrdinalIgnoreCase);
	private int _nextGridIndex;
	private int _diffIndex; // index of the next diff step to play (we do not mutate Diffs)

	// Animation state per step
	private bool _animating;
	private float _elapsed;
	private StepAnimation? _currentStep;
	// Autoplay flag: when true, we keep advancing steps automatically when not animating
	private bool _autoPlay;

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
		public List<BlockAnim> NewSequence = new(); // blocks that exist in the resulting state
		public List<BlockAnim> Removed = new(); // blocks that vanish
		public FileAnalysis? TargetAnalysis; // resulting analysis for this tower after the step
		public Vector3 StartRootPos; // for sinking
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

	public override void Start()
	{
		// Build lookup of existing towers/entities based on the InitialAnalysis that has already been built by SkyscraperVisualizer
		// Find tower roots by name (file path used as entity name)
		var roots = this.SceneSystem.SceneInstance?.RootScene.Entities;
		foreach (var file in this.InitialAnalysis)
		{
			this._currentAnalyses[file.File] = new FileAnalysis { File = file.File, Lines = file.Lines.Select(g => new LineGroup { Type = g.Type, Length = g.Length, Start = g.Start }).ToList() };
			Entity? root = roots.FirstOrDefault(e => string.Equals(e.Name, file.File, StringComparison.OrdinalIgnoreCase));
			if (root != null)
			{
				this._towers[file.File] = root;
			}
		}

		this._nextGridIndex = this._towers.Count; // append for newly added towers
		this._diffIndex = 0; // start from the first diff step
	}

	public override void Update()
	{
		if (this.Input == null) return;

		if (!this._animating)
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
				this._autoPlay = !this._autoPlay;
				Console.WriteLine(this._autoPlay
					? "[Playback] Autoplay ON (will advance with 2s per step)."
					: "[Playback] Autoplay OFF.");
			}
			// If autoplay is enabled and not animating, attempt to start the next step
			if (this._autoPlay)
			{
				this.StartNextStep();
			}
			return;
		}

		// Animate current step
		this._elapsed += (float)this.Game.UpdateTime.Elapsed.TotalSeconds;
		float t = Math.Min(1f, this._elapsed / DiffPlaybackScript.AnimationDuration);

		if (this._currentStep == null)
		{
			this._animating = false;
			return;
		}

		foreach (TowerAnim tower in this._currentStep.Towers)
		{
			if (tower.IsDelete)
			{
				if (tower.Root != null)
				{
					float y = MathUtil.Lerp(tower.StartRootPos.Y, tower.StartRootPos.Y - DiffPlaybackScript.SinkDistance, t);
					tower.Root.Transform.Position = new Vector3(tower.StartRootPos.X, y, tower.StartRootPos.Z);
				}
				continue;
			}

			// Animate blocks for adds/modifies
			// Update heights
			foreach (BlockAnim b in tower.NewSequence)
			{
				b.CurrentHeight = MathUtil.Lerp(b.StartHeight, b.EndHeight, t);
				this.ApplyBlockSize(b.Entity!, b.CurrentHeight);
			}
			foreach (BlockAnim b in tower.Removed)
			{
				b.CurrentHeight = MathUtil.Lerp(b.StartHeight, 0f, t);
				if (b.Entity != null)
				{
					this.ApplyBlockSize(b.Entity, b.CurrentHeight);
				}
			}

			// Recompute Y positions for new sequence (unchanged blocks move as neighbors change)
			float currentY = 0f;
			foreach (BlockAnim b in tower.NewSequence)
			{
				if (b.Entity == null) continue;
				float h = b.CurrentHeight;
				b.Entity.Transform.Position = new Vector3(0f, currentY + h / 2f, 0f);
				currentY += h;
			}
		}

		if (t >= 1f)
		{
			// Finalize: apply end state, cleanup removes and deletes, reset scales
			foreach (TowerAnim tower in this._currentStep.Towers)
			{
				if (tower.IsDelete)
				{
					if (tower.Root != null)
					{
						// remove from scene and maps
						this.SceneSystem.SceneInstance?.RootScene.Entities.Remove(tower.Root);
					}
					this._towers.Remove(tower.File);
					this._currentAnalyses.Remove(tower.File);
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
					if (b.Entity == null) continue;
					BlockDescriptorComponent? desc = b.Entity.Get<BlockDescriptorComponent>();
					if (desc != null)
					{
						desc.Size = new Vector3(DiffPlaybackScript.BlockWidth, b.EndHeight, DiffPlaybackScript.BlockDepth);
					}
					// Set absolute transform scale to match final block size (consistent with SkyscraperVisualizer)
					b.Entity.Transform.Scale = new Vector3(DiffPlaybackScript.BlockWidth, b.EndHeight, DiffPlaybackScript.BlockDepth);
				}

				// Update current analysis
				if (tower.TargetAnalysis != null)
				{
					this._currentAnalyses[tower.File] = tower.TargetAnalysis;
				}
			}

			Console.WriteLine($"[Playback] Step {this._diffIndex} completed.");
			this._animating = false;
			this._currentStep = null;
		}
	}

	private void ResetToInitialVisualization()
	{
		// Stop any ongoing animation
		this._animating = false;
		this._elapsed = 0f;
		this._currentStep = null;

		// Remove all current tower entities from the scene
		var rootEntities = this.SceneSystem.SceneInstance?.RootScene.Entities;
		if (rootEntities != null)
		{
			foreach (var kv in this._towers.ToList())
			{
				if (kv.Value != null)
				{
					rootEntities.Remove(kv.Value);
				}
			}
		}

		// Clear runtime maps
		this._towers.Clear();
		this._currentAnalyses.Clear();

		// Reset playback to the beginning and rebuild the towers using the same layout logic as creation
		this._diffIndex = 0;

		// Rebuild the towers using the same layout logic as creation
		int index = 0;
		foreach (var file in this.InitialAnalysis)
		{
			Entity fileRoot = this.CreateTowerRoot(file.File, index);
			rootEntities?.Add(fileRoot);
			this._towers[file.File] = fileRoot;
			this._currentAnalyses[file.File] = new FileAnalysis { File = file.File, Lines = file.Lines.Select(g => new LineGroup { Type = g.Type, Length = g.Length, Start = g.Start }).ToList() };

			// Build blocks
			float currentY = 0f;
			foreach (var group in file.Lines)
			{
				var block = this.CreateBlock(file.File, group, currentY);
				fileRoot.AddChild(block);
				currentY += group.Length * DiffPlaybackScript.UnitsPerLine;
			}
			index++;
		}

		this._nextGridIndex = this._towers.Count;
	}

	private void StartNextStep()
	{
		if (this.Diffs.Count == 0)
		{
			Console.WriteLine("[Playback] No diffs available.");
			this._autoPlay = false;
			return;
		}
		if (this._diffIndex >= this.Diffs.Count)
		{
			Console.WriteLine("[Playback] No more steps to play. Reached the end of the revision log.");
			// Stop autoplay if it was active
			this._autoPlay = false;
			return;
		}
		Console.WriteLine($"[Playback] SPACE pressed: starting step {this._diffIndex + 1}/{this.Diffs.Count}");
		List<FileChangeEntry> diff = this.Diffs[this._diffIndex];
		Console.WriteLine($"[Playback] Step {this._diffIndex + 1}: {diff.Count} file change(s)");
		this._diffIndex++;

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

			this._currentAnalyses.TryGetValue(file, out FileAnalysis? current);
			current ??= new FileAnalysis { File = file, Lines = new List<LineGroup>() };

			// Compute target analysis using unified applier
			FileAnalysis target = FileAnalysisApplier.Apply(current, fad, file);

			TowerAnim towerAnim = new TowerAnim { File = file, TargetAnalysis = target };

			bool hasRoot = this._towers.TryGetValue(file, out Entity? root);
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
				root = this.CreateTowerRoot(file, this._nextGridIndex++);
				this.SceneSystem.SceneInstance?.RootScene.Entities.Add(root);
				this._towers[file] = root;
				towerAnim.Root = root;
				towerAnim.IsAdd = true;

				// Create blocks with base size equal to target and start height 0
				foreach (LineGroup g in target.Lines)
				{
					float endH = g.Length * DiffPlaybackScript.UnitsPerLine;
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

			// Get current blocks/entities in order by Y
			List<Entity> oldBlocks = root.Transform.Children.Select(c => c.Entity).Where(e => e != null)
				.Select(e => e!)
				.OrderBy(e => e.Transform.Position.Y)
				.ToList();

			List<LineGroup> oldLines = current.Lines;
			List<LineGroup> newLines = target.Lines;
			List<DiffEdit> edits = fad.Kind == FileAnalysisChangeKind.Modify ? (fad.Edits ?? new()) : DiffPlaybackScript.BuildEditsForAddOrDelete(oldLines, newLines);

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
						float startH = oldLines[iOld].Length * DiffPlaybackScript.UnitsPerLine;
						if (ent != null)
						{
							removed.Add(new BlockAnim { Entity = ent, Color = DiffPlaybackScript.LineTypeColors[oldLines[iOld].Type], StartHeight = startH, EndHeight = 0f, CurrentHeight = startH, IsNew = false });
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
						float endH = (op.NewLength ?? g.Length) * DiffPlaybackScript.UnitsPerLine;
						Entity block = this.CreateBlock(file, g, 0f);
						this.ApplyBlockSize(block, 0f, endH);
						root.AddChild(block);
						newSeq.Add(new BlockAnim { Entity = block, Color = DiffPlaybackScript.LineTypeColors[g.Type], StartHeight = 0f, EndHeight = endH, CurrentHeight = 0f, IsNew = true });
						iOp++;
						continue;
					}
					if (op.Kind == DiffOpType.Resize && op.Index == newSeq.Count && iOld < oldLines.Count)
					{
						Entity? ent = iOld < oldBlocks.Count ? oldBlocks[iOld] : null;
						float startH = oldLines[iOld].Length * DiffPlaybackScript.UnitsPerLine;
						float endH = (op.NewLength ?? oldLines[iOld].Length) * DiffPlaybackScript.UnitsPerLine;
						if (ent == null)
						{
							// if missing entity, create one to animate resize correctly
							var gOld = oldLines[iOld];
							ent = this.CreateBlock(file, gOld, 0f);
							this.ApplyBlockSize(ent, startH, startH);
							root.AddChild(ent);
						}
						newSeq.Add(new BlockAnim { Entity = ent, Color = DiffPlaybackScript.LineTypeColors[oldLines[iOld].Type], StartHeight = startH, EndHeight = endH, CurrentHeight = startH, IsNew = false });
						iOld++;
						iOp++;
						continue;
					}
				}
				if (iOld < oldLines.Count)
				{
					// passthrough unchanged block
					Entity? ent = iOld < oldBlocks.Count ? oldBlocks[iOld] : null;
					float h = oldLines[iOld].Length * DiffPlaybackScript.UnitsPerLine;
					if (ent == null)
					{
						// recreate missing entity
						var gOld = oldLines[iOld];
						ent = this.CreateBlock(file, gOld, 0f);
						this.ApplyBlockSize(ent, h, h);
						root.AddChild(ent);
					}
					newSeq.Add(new BlockAnim { Entity = ent, Color = DiffPlaybackScript.LineTypeColors[oldLines[iOld].Type], StartHeight = h, EndHeight = h, CurrentHeight = h, IsNew = false });
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
					float endH = (op.NewLength ?? g.Length) * DiffPlaybackScript.UnitsPerLine;
					Entity block = this.CreateBlock(file, g, 0f);
					this.ApplyBlockSize(block, 0f, endH);
					root.AddChild(block);
					newSeq.Add(new BlockAnim { Entity = block, Color = DiffPlaybackScript.LineTypeColors[g.Type], StartHeight = 0f, EndHeight = endH, CurrentHeight = 0f, IsNew = true });
					iOp++;
					continue;
				}
				break;
			}

			towerAnim.NewSequence = newSeq;
			towerAnim.Removed = removed;
			step.Towers.Add(towerAnim);
		}

		int addCount = step.Towers.Count(t => t.IsAdd);
		int delCount = step.Towers.Count(t => t.IsDelete);
		int modCount = step.Towers.Count - addCount - delCount;
		Console.WriteLine($"[Playback] Step summary: towers affected={step.Towers.Count} (add={addCount}, delete={delCount}, modify={modCount})");

		this._currentStep = step;
		this._elapsed = 0f;
		this._animating = true;
	}

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
		return Differ.Diff(new FileAnalysis { File = "", Lines = oldLines }, new FileAnalysis { File = "", Lines = newLines });
	}

	private Entity CreateTowerRoot(string file, int index)
	{
		int gridSize = (int)Math.Ceiling(Math.Sqrt(index + 1));
		int row = index / gridSize;
		int col = index % gridSize;
		float x = col * DiffPlaybackScript.TowerSpacing;
		float z = row * DiffPlaybackScript.TowerSpacing;
		Entity fileRoot = new Entity(file);
		fileRoot.Transform.Position = new Vector3(x, 0f, z);
		return fileRoot;
	}

	private Model? _templateModel;

	private void EnsureTemplateModel()
	{
		if (this._templateModel != null)
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
					this._templateModel = modelComp.Model;
					break;
				}
			}
		}

		// If still null, synthesize a 1x1x1 cube model procedurally and keep its Model
		if (this._templateModel == null)
		{
			var game = this.Game as Game;
			if (game != null)
			{
				var createOptions = new Primitive3DCreationOptions { Size = new Vector3(1f, 1f, 1f), IncludeCollider = false };
				Entity temp = game.Create3DPrimitive(PrimitiveModelType.Cube, createOptions);
				var modelComp = temp.Get<ModelComponent>();
				if (modelComp != null)
				{
					this._templateModel = modelComp.Model;
				}
				// We don't add 'temp' to the scene; it will be GC'ed. We only need the shared Model
			}
		}
	}

	private Entity CreateBlock(string file, LineGroup group, float currentY)
	{
		float height = group.Length * DiffPlaybackScript.UnitsPerLine;
		Color4 color = DiffPlaybackScript.LineTypeColors[group.Type];

		// Ensure we have a template model to attach to new blocks, even if scene started empty
		this.EnsureTemplateModel();

		Entity cube = new Entity(file);
		if (this._templateModel != null)
		{
			cube.Add(new ModelComponent { Model = this._templateModel });
		}
		cube.Transform.Position = new Vector3(0f, currentY + height / 2f, 0f);
		cube.Add(new BlockDescriptorComponent { Size = new Vector3(DiffPlaybackScript.BlockWidth, height, DiffPlaybackScript.BlockDepth), Color = color });

		// Ensure per-entity material override so colors don't bleed across shared model instances
		this.ApplyColorToCube(cube, color);
		return cube;
	}

	private void ApplyBlockSize(Entity block, float currentHeight, float? baseHeightOverride = null)
	{
		BlockDescriptorComponent? desc = block.Get<BlockDescriptorComponent>();
		if (desc == null) return;

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
}
