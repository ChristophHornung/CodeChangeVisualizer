namespace CodeChangeVisualizer.Analyzer;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a contiguous block of lines with the same <see cref="LineType"/>.
/// </summary>
public class LineGroup
{
	/// <summary>
	/// Gets or sets the 1-based line index where this group starts.
	/// </summary>
	public int Start { get; set; }

	/// <summary>
	/// Gets or sets the number of lines contained in this group.
	/// </summary>
	public int Length { get; set; }

	/// <summary>
	/// Gets or sets the classification of the lines in this group.
	/// Serialized as a string via <see cref="JsonStringEnumConverter"/>.
	/// </summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public LineType Type { get; set; }
}