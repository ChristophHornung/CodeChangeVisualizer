namespace CodeChangeVisualizer.StrideRunner;

using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.CommunityToolkit.Engine;

/// <summary>
/// Displays the file name of a skyscraper (tower) when the mouse cursor hovers over any of its blocks.
/// Uses a simple ray vs AABB test against blocks that have BlockDescriptorComponent.
/// Renders the tooltip using Game.DebugTextSystem, so no UI assets are required.
/// </summary>
public class HoverTooltipScript : SyncScript
{
    private CameraComponent? _camera;
    private readonly List<Entity> _blocks = new();

    public override void Start()
    {
        // Find a camera in the scene (created by SetupBase3DScene)
        _camera = SceneSystem.SceneInstance?.RootScene.Entities
            .Select(e => e.Get<CameraComponent>())
            .FirstOrDefault(c => c != null);

        // Cache all blocks (entities that have BlockDescriptorComponent)
        foreach (var entity in SceneSystem.SceneInstance?.RootScene.Entities ?? Enumerable.Empty<Entity>())
        {
            CollectBlocksRecursive(entity);
        }
    }

    public override void Update()
    {
        if (_camera == null || _blocks.Count == 0)
            return;

        // Ensure we have mouse input
        if (Input == null || !Input.HasMouse)
            return;

        // Build a ray from the current mouse position
        // MousePosition is normalized (0..1). Use toolkit extension to get world ray segment
        var mouse = Input.MousePosition;
        // Early out if mouse is not over window
        if (mouse.X < 0 || mouse.X > 1 || mouse.Y < 0 || mouse.Y > 1)
            return;

        // Near/Far clip in world units
        // Use CommunityToolkit's helper to get a world-space ray segment from screen coordinates
        var mousePos = mouse; // create variable so it can be passed by ref if required by API
        Stride.CommunityToolkit.Scripts.RaySegment segment;
        _camera.ScreenToWorldRaySegment(ref mousePos, out segment);
        var rayOrigin = segment.Start;
        var rayDir = Vector3.Normalize(segment.End - segment.Start);

        // Find closest intersected block
        Entity? closestBlock = null;
        float closestT = float.MaxValue;
        foreach (var block in _blocks)
        {
            var desc = block.Get<BlockDescriptorComponent>();
            if (desc == null)
                continue;

            var pos = block.Transform.WorldMatrix.TranslationVector;
            var half = desc.Size * 0.5f;
            var min = pos - half;
            var max = pos + half;
            if (RayIntersectsAABB(rayOrigin, rayDir, min, max, out float t) && t >= 0 && t < closestT)
            {
                closestT = t;
                closestBlock = block;
            }
        }

        if (closestBlock != null)
        {
            // The tower root is the parent of the block; its Name is the file name/path
            var towerRoot = closestBlock.GetParent();
            string fileName = towerRoot?.Name ?? closestBlock.Name;

            // Compute the world hit position and project to screen space (normalized 0..1, top-left origin)
            var hitPoint = rayOrigin + rayDir * closestT;
            Vector3 screenNorm = _camera.WorldToScreenPoint(hitPoint);

            // Convert to pixel coordinates without Y inversion (DebugText expects top-left origin)
            var backBuffer = GraphicsDevice.Presenter?.BackBuffer;
            int width = backBuffer?.Width ?? 0;
            int height = backBuffer?.Height ?? 0;
            var screenPos = new Int2((int)(screenNorm.X * width) + 16, (int)(screenNorm.Y * height) + 16);

            // Print for this frame (call every frame while hovering)
            (this.Game as Stride.Engine.Game)?.DebugTextSystem?.Print(fileName, screenPos, Color4.White);
        }
    }

    private void CollectBlocksRecursive(Entity entity)
    {
        if (entity.Get<BlockDescriptorComponent>() != null)
        {
            _blocks.Add(entity);
        }

        // Recurse into children
        foreach (var child in entity.Transform.Children)
        {
            if (child.Entity != null)
            {
                CollectBlocksRecursive(child.Entity);
            }
        }
    }

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
                    return false;
            }
            else
            {
                float t1 = (minA - o) / d;
                float t2 = (maxA - o) / d;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tNear = Math.Max(tNear, t1);
                tFar = Math.Min(tFar, t2);
                if (tNear > tFar)
                    return false;
                if (tFar < 0)
                    return false;
            }
        }

        return true;
    }
}
