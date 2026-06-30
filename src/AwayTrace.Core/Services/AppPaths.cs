namespace AwayTrace.Core.Services;

public static class AppPaths
{
    public const string AppFolderName = "AwayTrace";
    public const string DatabaseFileName = "awaytrace.db";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
}
