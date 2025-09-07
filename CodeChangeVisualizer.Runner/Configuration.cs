namespace CodeChangeVisualizer.Runner;

using System.Text.Json.Serialization;

public class Configuration
{
	[JsonPropertyName("directory")]
	public string? Directory { get; set; }

	[JsonPropertyName("jsonOutput")]
	public string? JsonOutput { get; set; }

	[JsonPropertyName("visualizationOutput")]
	public string? VisualizationOutput { get; set; }

	[JsonPropertyName("outputToConsole")]
	public bool OutputToConsole { get; set; }

	[JsonPropertyName("ignorePatterns")]
	public List<string> IgnorePatterns { get; set; } = new() { "/bin", "/obj" };

	[JsonPropertyName("fileExtensions")]
	public List<string> FileExtensions { get; set; } = new() { "*.cs" };

	// Advanced git analysis start commit hash (enables git mode when provided)
	[JsonPropertyName("gitStart")]
	public string? GitStart { get; set; }

	// Hide console progress (Spectre.Console) during advanced git analysis; default false (progress shown)
	[JsonPropertyName("hideProgress")]
	public bool HideProgress { get; set; }
}