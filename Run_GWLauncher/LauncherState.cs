using System.Text.Json.Serialization;

namespace Run_GWLauncher
{
    public class LauncherState
    {
        [JsonPropertyName("installedVersion")]
        public int InstalledVersion { get; set; }

        [JsonPropertyName("installedPath")]
        public string InstalledPath { get; set; } = string.Empty;

        [JsonPropertyName("lastCheckedAt")]
        public string LastCheckedAt { get; set; } = string.Empty;
    }
}
