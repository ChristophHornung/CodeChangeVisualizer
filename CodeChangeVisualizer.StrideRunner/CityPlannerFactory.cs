namespace CodeChangeVisualizer.StrideRunner;

public static class CityPlannerFactory
{
	/// <summary>
	/// Creates a planner. Defaults to FolderGridCityPlanner.
	/// To use the legacy grid planner, set environment variable CCV_PLANNER to "grid" (case-insensitive).
	/// </summary>
	public static ICityPlanner CreateFromEnv()
	{
		string? mode = System.Environment.GetEnvironmentVariable("CCV_PLANNER");
		if (!string.IsNullOrWhiteSpace(mode) && mode.Trim().Equals("grid", System.StringComparison.OrdinalIgnoreCase))
		{
			return new GridCityPlanner();
		}
		return new FolderGridCityPlanner();
	}
}