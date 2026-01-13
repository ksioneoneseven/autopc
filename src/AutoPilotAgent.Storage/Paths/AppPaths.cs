namespace AutoPilotAgent.Storage.Paths;

public static class AppPaths
{
    public static string GetAppDataRoot()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoPilotAgent");

        Directory.CreateDirectory(root);
        return root;
    }

    public static string GetDatabasePath()
    {
        return Path.Combine(GetAppDataRoot(), "autopilot.db");
    }

    public static string GetSecretsPath()
    {
        return Path.Combine(GetAppDataRoot(), "secrets.bin");
    }

    public static string GetLogFilePath()
    {
        return Path.Combine(GetAppDataRoot(), "autopilot.log");
    }
}
