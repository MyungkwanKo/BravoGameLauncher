using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BravoGameLauncherGui
{
    public class AppSettings
    {
        public string RootDownloadDir { get; set; } = DefaultRootPath;

        // Installed builds base path (e.g. C:\BravoGameBuilds)
        public string InstalledBuildBasePath { get; set; } = DefaultInstalledBuildBasePath;

        public static string DefaultInstalledBuildBasePath =>
            Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";


        public List<string> RecentFileNames { get; set; } = new();

        public static string SettingsDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "BravoGameLauncherGui");  

        public static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

        public static string DefaultRootPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         "BravoGameBuilds");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                        return settings;
                }
            }
            catch { }
            
            return new AppSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(SettingsDir);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }

        public void AddRecentFileName(string fileName)
        {
            fileName = fileName.Trim();
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            RecentFileNames.RemoveAll(x => 
                x.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            RecentFileNames.Insert(0, fileName);

            if (RecentFileNames.Count > 10)
                RecentFileNames.RemoveRange(10, RecentFileNames.Count - 10);
        }

        public string SelectedEngineVersion { get; set; } = "UE5.6";

    }
}
