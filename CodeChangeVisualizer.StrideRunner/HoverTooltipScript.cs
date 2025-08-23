namespace CodeChangeVisualizer.StrideRunner;

using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Scripts;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;

/// <summary>
/// Displays the file name of a skyscraper (tower) when the mouse cursor hovers over any of its blocks.
/// Uses a simple ray vs AABB test against blocks that have BlockDescriptorComponent.
/// Renders the tooltip using Game.DebugTextSystem, so no UI assets are required.
/// </summary>
public class HoverTooltipScript : SyncScript
{
	private CameraComponent? _camera;
	private readonly List<Entity> _blocks = new();

	/// <summary>
	/// Ray vs AABB test. Returns true if intersects and outputs the nearest positive distance t along the ray.
	/// </summary>
	private static bool RayIntersectsAABB(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float tNear)
	{
		tNear = 0f;
		float tFar = float.MaxValue;

		// For each axis, update tNear/tFar
		for (int i = 0; i < 3; i++)
		{
			float o = i == 0 ? origin.X : i == 1 ? origin.Y : origin.Z;
			float d = i == 0 ? dir.X : i == 1 ? dir.Y : dir.Z;
			float minA = i == 0 ? min.X : i == 1 ? min.Y : min.Z;
			float maxA = i == 0 ? max.X : i == 1 ? max.Y : max.Z;

			if (Math.Abs(d) < 1e-6f)
			{
				// Ray parallel to slab; no hit if origin not within slab
				if (o < minA || o > maxA)
				{
					return false;
				}
			}
			else
			{
				float t1 = (minA - o) / d;
				float t2 = (maxA - o) / d;
				if (t1 > t2)
				{
					(t1, t2) = (t2, t1);
				}

				tNear = Math.Max(tNear, t1);
				tFar = Math.Min(tFar, t2);
				if (tNear > tFar)
				{
					return false;
				}

				if (tFar < 0)
				{
					return false;
				}
			}
		}

		return true;
	}

	public override void Start()
	{
		// Find a camera in the scene (created by SetupBase3DScene)
		this._camera = this.SceneSystem.SceneInstance?.RootScene.Entities
			.Select(e => e.Get<CameraComponent>())
			.FirstOrDefault(c => c != null);

		// Cache all blocks (entities that have BlockDescriptorComponent)
		foreach (Entity entity in this.SceneSystem.SceneInstance?.RootScene.Entities ?? Enumerable.Empty<Entity>())
		{
			this.CollectBlocksRecursive(entity);
		}
	}

	public override void Update()
	{
		this.RefreshBlocks();
		if (this._camera == null || this._blocks.Count == 0)
		{
			return;
		}

		// Ensure we have mouse input
		if (this.Input == null || !this.Input.HasMouse)
		{
			return;
		}

		// Build a ray from the current mouse position
		// MousePosition is normalized (0..1). Use toolkit extension to get world ray segment
		Vector2 mouse = this.Input.MousePosition;
		// Early out if mouse is not over window
		if (mouse.X < 0 || mouse.X > 1 || mouse.Y < 0 || mouse.Y > 1)
		{
			return;
		}

		// Near/Far clip in world units
		// Use CommunityToolkit's helper to get a world-space ray segment from screen coordinates
		Vector2 mousePos = mouse; // create variable so it can be passed by ref if required by API
		RaySegment segment;
		this._camera.ScreenToWorldRaySegment(ref mousePos, out segment);
		Vector3 rayOrigin = segment.Start;
		Vector3 rayDir = Vector3.Normalize(segment.End - segment.Start);

		// Find closest intersected block
		Entity? closestBlock = null;
		float closestT = float.MaxValue;
		foreach (Entity block in this._blocks)
		{
			BlockDescriptorComponent? desc = block.Get<BlockDescriptorComponent>();
			if (desc == null)
			{
				continue;
			}

			Vector3 pos = block.Transform.WorldMatrix.TranslationVector;
			Vector3 half = desc.Size * 0.5f;
			Vector3 min = pos - half;
			Vector3 max = pos + half;
			if (HoverTooltipScript.RayIntersectsAABB(rayOrigin, rayDir, min, max, out float t) && t >= 0 &&
			    t < closestT)
			{
				closestT = t;
				closestBlock = block;
			}
		}

		if (closestBlock != null)
		{
			// Resolve the tower root by walking up the parents; fall back to block's own entity if necessary
			Entity? towerRoot = closestBlock;
			while (towerRoot?.GetParent() != null)
			{
				towerRoot = towerRoot.GetParent();
			}

			string fileName = towerRoot?.Name ?? closestBlock.Name;
			// As a safety, strip any line-range suffix like " [start-end] ..." if present
			int idx = fileName.IndexOf(" [");
			if (idx >= 0)
			{
				fileName = fileName.Substring(0, idx);
			}

			// Compute the world hit position and project to screen space (normalized 0..1, top-left origin)
			Vector3 hitPoint = rayOrigin + rayDir * closestT;
			Vector3 screenNorm = this._camera.WorldToScreenPoint(hitPoint);

			// Convert to pixel coordinates without Y inversion (DebugText expects top-left origin)
			Texture? backBuffer = this.GraphicsDevice.Presenter?.BackBuffer;
			int width = backBuffer?.Width ?? 0;
			int height = backBuffer?.Height ?? 0;
			Int2 screenPos = new Int2((int)(screenNorm.X * width) + 16, (int)(screenNorm.Y * height) + 16);

			// Print for this frame (call every frame while hovering)
			(this.Game as Game)?.DebugTextSystem?.Print(fileName, screenPos, Color4.White);
		}
	}

	private void RefreshBlocks()
	{
		// Rebuild block cache each frame to include newly added towers/blocks
		this._blocks.Clear();
		foreach (Entity entity in this.SceneSystem.SceneInstance?.RootScene.Entities ?? Enumerable.Empty<Entity>())
		{
			this.CollectBlocksRecursive(entity);
		}
	}

	private void CollectBlocksRecursive(Entity entity)
	{
		if (entity.Get<BlockDescriptorComponent>() != null)
		{
			this._blocks.Add(entity);
		}

		// Recurse into children
		foreach (TransformComponent? child in entity.Transform.Children)
		{
			if (child.Entity != null)
			{
				this.CollectBlocksRecursive(child.Entity);
			}
		}
	}
}