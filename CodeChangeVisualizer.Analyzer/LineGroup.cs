namespace CodeChangeVisualizer.Analyzer;

using System.Text.Json.Serialization;

public class LineGroup
{
	public int Start { get; set; }
	public int Length { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public LineType Type { get; set; }
}