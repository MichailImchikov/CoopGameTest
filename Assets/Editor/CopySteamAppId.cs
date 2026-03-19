using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;
using UnityEngine;

public class CopySteamAppId : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        string buildPath = Path.GetDirectoryName(report.summary.outputPath);
        string sourceFile = Path.Combine(Application.dataPath, "..", "steam_appid.txt");
        string destFile = Path.Combine(buildPath, "steam_appid.txt");

        if (File.Exists(sourceFile))
        {
            File.Copy(sourceFile, destFile, true);
            Debug.Log($"[CopySteamAppId] Copied steam_appid.txt to {destFile}");
        }
        else
        {
            // Создаём файл если его нет
            File.WriteAllText(destFile, "480");
            Debug.Log($"[CopySteamAppId] Created steam_appid.txt at {destFile}");
        }
    }
}
