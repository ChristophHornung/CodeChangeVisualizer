namespace CodeChangeVisualizer.StrideRunner;

using Stride.Core.Mathematics;
using Stride.Engine;

public class BlockDescriptorComponent : EntityComponent
{
	public Vector3 Size { get; set; }
	public Color4 Color { get; set; }
}