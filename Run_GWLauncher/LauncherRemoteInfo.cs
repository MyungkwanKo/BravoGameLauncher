using System.Text.Json.Serialization;

namespace Run_GWLauncher
{
    public class LauncherRemoteInfo
    {
        [JsonPropertyName("latestVersion")]
        public int LatestVersion { get; set; }

        [JsonPropertyName("minSupportedVersion")]
        public int MinSupportedVersion { get; set; }

        [JsonPropertyName("package")]
        public PackageInfo Package { get; set; } = new();

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; } = string.Empty;
    }

    public class PackageInfo
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
