using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BravoGameLauncherGui
{
    public class BuildListResponse
    {
        public string Project { get; set; } = "";
        public string Platform { get; set; } = "";
        public List<BuildItem> Builds { get; set; } = new();
    }

    public class BuildItem
    {
        public string FileName { get; set; } = "";
        public string Version { get; set; } = "";
        public int Cl { get; set; }
        public string Config { get; set; } = "";
        public DateTime? BuildTime { get; set; }
        public long SizeBytes { get; set; }
    }

    public static class BuildListService
    {
        // TODO: 필요하면 AppSettings에서 읽도록 바꿀 수 있음
        private const string BuildListUrl =
            "http://bravo-build.omnicraftlabs.co.kr:8000/GameBuilds/builds.json";

        public static async Task<BuildListResponse?> FetchBuildListAsync()
        {
            using var client = new HttpClient();

            var json = await client.GetStringAsync(BuildListUrl);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<BuildListResponse>(json, options);
        }
    }
}
