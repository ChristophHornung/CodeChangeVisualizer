namespace CodeChangeVisualizer.Viewer;

using CodeChangeVisualizer.Analyzer;
using SkiaSharp;

public class CodeVisualizer
{
	private const int PixelPerLine = 1; // Each line is 1 pixel high
	private const int StackWidth = 40; // Each stack is 40 pixels wide
	private const int Margin = 20;
	private const int FileHeaderMargin = 5;

	private static readonly SKFont Font = new() { Size = 10 };

	private static readonly Dictionary<LineType, SKColor> LineTypeColors = new()
	{
		[LineType.Empty] = SKColors.White,
		[LineType.Comment] = SKColors.Green,
		[LineType.ComplexityIncreasing] = SKColors.Red,
		[LineType.Code] = SKColors.Gray,
		[LineType.CodeAndComment] = SKColors.LightGreen
	};

	public void GenerateVisualization(List<FileAnalysis> analysis, string outputPath)
	{
		SKCanvas canvas = this.CreateCanvas(analysis);
		SKImage? image = canvas.Surface.Snapshot();

		using SKData? data = image.Encode(SKEncodedImageFormat.Png, 100);
		using FileStream stream = File.OpenWrite(outputPath);
		data.SaveTo(stream);
	}

	private SKCanvas CreateCanvas(List<FileAnalysis> analysis)
	{
		(int width, int textAreaHeight, int stackAreaHeight) = this.CalculateDimensions(analysis);
		int totalHeight = textAreaHeight + stackAreaHeight;
		SKSurface? surface = SKSurface.Create(new SKImageInfo(width, totalHeight));
		SKCanvas? canvas = surface.Canvas;

		// Clear background
		canvas.Clear(SKColors.White);

		// Draw files horizontally
		this.DrawFiles(canvas, analysis, textAreaHeight);

		return canvas;
	}

	private (int width, int textAreaHeight, int stackAreaHeight) CalculateDimensions(List<FileAnalysis> analysis)
	{
		int totalWidth = CodeVisualizer.Margin;
		int maxStackHeight = 0;
		int maxTextHeight = 0;

		foreach (FileAnalysis file in analysis)
		{
			// Calculate total lines in the file
			int totalLines = 0;
			foreach (LineGroup lineGroup in file.Lines)
			{
				totalLines += lineGroup.Length;
			}

			int fileHeight = CodeVisualizer.FileHeaderMargin + totalLines * CodeVisualizer.PixelPerLine;
			maxStackHeight = Math.Max(maxStackHeight, fileHeight);

			// Measure text dimensions for this file
			float textWidth = CodeVisualizer.Font.MeasureText(file.File);

			// Calculate the height needed for angled text
			// For -45° rotation, the height is approximately the text width
			int textHeight = (int)Math.Ceiling(textWidth * 0.707); // cos(45°) ≈ 0.707
			maxTextHeight = Math.Max(maxTextHeight, textHeight);

			// Each file stack takes StackWidth + Margin
			totalWidth += CodeVisualizer.StackWidth + CodeVisualizer.Margin;
		}

		// Add some padding to the text area
		int textAreaHeight = maxTextHeight + 10;

		return (totalWidth, textAreaHeight, maxStackHeight);
	}


	private void DrawFiles(SKCanvas canvas, List<FileAnalysis> analysis, int textAreaHeight)
	{
		int x = CodeVisualizer.Margin;

		foreach (FileAnalysis file in analysis)
		{
			// Draw file header
			this.DrawFileHeader(canvas, file.File, x, textAreaHeight);

			// Draw line groups as color blocks
			int y = textAreaHeight + CodeVisualizer.FileHeaderMargin;
			foreach (LineGroup lineGroup in file.Lines)
			{
				this.DrawLineGroup(canvas, lineGroup, x, y);
				y += lineGroup.Length * CodeVisualizer.PixelPerLine;
			}

			x += CodeVisualizer.StackWidth + CodeVisualizer.Margin;
		}
	}

	private void DrawFileHeader(SKCanvas canvas, string fileName, int x, int textAreaHeight)
	{
		SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			Color = SKColors.Black
		};

		// Save the current canvas state
		canvas.Save();

		canvas.Translate(x, textAreaHeight);

		// Rotate -45 degrees (counterclockwise from left bottom to right top)
		canvas.RotateDegrees(-45);

		// Draw file name starting from left bottom corner
		canvas.DrawText(fileName, 0, 0, SKTextAlign.Left, CodeVisualizer.Font, paint);

		// Restore the canvas state
		canvas.Restore();

		// Draw separator line
		canvas.DrawLine(x, textAreaHeight, x + CodeVisualizer.StackWidth, textAreaHeight,
			new SKPaint { Color = SKColors.Gray });
	}

	private void DrawLineGroup(SKCanvas canvas, LineGroup lineGroup, int x, int y)
	{
		SKColor color = CodeVisualizer.LineTypeColors[lineGroup.Type];
		int height = lineGroup.Length * CodeVisualizer.PixelPerLine;
		SKRect rect = new SKRect(x, y, x + CodeVisualizer.StackWidth, y + height);

		canvas.DrawRect(rect, new SKPaint { Color = color });
	}
}