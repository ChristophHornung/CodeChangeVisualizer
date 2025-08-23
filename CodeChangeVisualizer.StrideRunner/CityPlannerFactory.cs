namespace CodeChangeVisualizer.StrideRunner;

public static class CityPlannerFactory
{
	/// <summary>
	/// Creates a planner. If environment variable CCV_PLANNER is set to "folder"
	/// (case-insensitive), returns FolderGridCityPlanner; otherwise GridCityPlanner.
	/// </summary>
	public static ICityPlanner CreateFromEnv()
	{
		string? mode = System.Environment.GetEnvironmentVariable("CCV_PLANNER");
		if (!string.IsNullOrWhiteSpace(mode) && mode.Trim().Equals("folder", System.StringComparison.OrdinalIgnoreCase))
		{
			return new FolderGridCityPlanner();
		}
		return new GridCityPlanner();
	}
}