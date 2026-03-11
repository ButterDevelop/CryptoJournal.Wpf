using System.IO;

namespace CryptoJournal.Wpf.Storage.Common;

public static class ScenarioPaths
{
    public static string GetEnvDir(string envId)
    {
        var dir = Path.Combine(AppDataPaths.RootDir, "Scenarios", envId.Trim());
        return AppDataPaths.EnsureDir(dir);
    }

    public static string GetActiveScenarioFile(string envId)
        => Path.Combine(GetEnvDir(envId), "active.json");
}