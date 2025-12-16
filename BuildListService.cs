using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BravoGameLauncherGui
{
    // v2: 플랫폼별 builds 구조
    public class PlatformBuildGroup
    {
        public List<BuildItem> Builds { get; set; } = new();
    }

    public class BuildListResponseV2
    {
        public string Project { get; set; } = "";
        public int SchemaVersion { get; set; } = 2;
        public Dictionary<string, PlatformBuildGroup> Platforms { get; set; } = new();
    }

    // v1: 기존 구조 (platform + builds[])
    public class BuildListResponseV1
    {
        public string Project { get; set; } = "";
        public string Platform { get; set; } = "";
        public List<BuildItem> Builds { get; set; } = new();
    }

    // 런처에서 실제로 사용하는 통합 형태
    public class BuildListResponse
    {
        public string Project { get; set; } = "";
        public string Platform { get; set; } = ""; // v1 호환용, v2에서는 "MULTI" 등으로 사용
        public List<BuildItem> Builds { get; set; } = new();
    }

    public class BuildItem
    {
        public string FileName { get; set; } = "";
        public string Version { get; set; } = "";
        public int Cl { get; set; }
        public string Config { get; set; } = "";
        public DateTime? BuildTime { get; set; }
        public int JenkinsBuildNumber { get; set; }
        public long SizeBytes { get; set; }

        // JSON에는 없고, 런처에서 채워 넣는 필드
        [JsonIgnore]
        public string Platform { get; set; } = "";
    }

    public static class BuildListService
    {
        // 서버 builds.json 위치
        public const string BuildListUrl =
            "http://bravo-build.omnicraftlabs.co.kr:8000/GameBuilds/builds.json";

        public static async Task<BuildListResponse?> FetchBuildListAsync()
        {
            using var client = new HttpClient();

            var json = await client.GetStringAsync(BuildListUrl);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // v2: platforms 노드가 있는 경우
            if (root.TryGetProperty("platforms", out var platformsElement)
                && platformsElement.ValueKind == JsonValueKind.Object)
            {
                var v2 = JsonSerializer.Deserialize<BuildListResponseV2>(json, options);
                if (v2 == null)
                    return null;

                var merged = new BuildListResponse
                {
                    Project = v2.Project,
                    Platform = "MULTI"
                };

                foreach (var kv in v2.Platforms)
                {
                    var platformName = kv.Key; // WIN, DS, LINUX, ...
                    var group = kv.Value;
                    if (group?.Builds == null)
                        continue;

                    foreach (var item in group.Builds)
                    {
                        if (item == null)
                            continue;

                        item.Platform = platformName;
                        merged.Builds.Add(item);
                    }
                }

                return merged;
            }
            else
            {
                // v1: 기존 구조 (project + platform + builds[])
                var v1 = JsonSerializer.Deserialize<BuildListResponseV1>(json, options);
                if (v1 == null)
                    return null;

                var merged = new BuildListResponse
                {
                    Project = v1.Project,
                    Platform = v1.Platform
                };

                if (v1.Builds != null)
                {
                    foreach (var item in v1.Builds)
                    {
                        if (item == null)
                            continue;

                        item.Platform = v1.Platform;
                        merged.Builds.Add(item);
                    }
                }

                return merged;
            }
        }
    }
}
