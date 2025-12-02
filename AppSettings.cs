using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BravoGameLauncherGui
{
    public class AppSettings
    {
        /// <summary>
        /// 최근 입력한 ZIP 파일명 목록 (최대 10개)
        /// </summary>
        public List<string> RecentFileNames { get; set; } = new();

        public static string SettingsDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "BravoGameLauncherGui"); // 예: C:\Users\USER\AppData\Roaming\BravoGameLauncherGui

        public static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

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
            catch
            {
                // 실패하면 기본값으로
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 저장 실패는 치명적이지 않으니 무시
            }
        }

        /// <summary>
        /// 최근 파일명 목록 업데이트 (중복 제거 + 최대 10개 유지)
        /// </summary>
        public void AddRecentFileName(string fileName)
        {
            fileName = fileName.Trim();
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            // 중복 제거
            RecentFileNames.RemoveAll(x => x.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            // 맨 앞에 삽입
            RecentFileNames.Insert(0, fileName);

            const int max = 10;
            if (RecentFileNames.Count > max)
                RecentFileNames.RemoveRange(max, RecentFileNames.Count - max);
        }
    }
}
