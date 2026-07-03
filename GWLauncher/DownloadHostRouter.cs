using System;

namespace BravoGameLauncherGui
{
    public static class DownloadHostRouter
    {
        public const string MasterHost = "bravo-build.omnicraftlabs.co.kr";
        public const string AgentHost = "bravo-agent.omnicraftlabs.co.kr";

        /// <summary>Master 고정 URL (JSON 매니페스트 조회용)</summary>
        public static string MasterBuildsBaseUrl => BuildBaseUrl(MasterHost, "builds");

        public static string MasterInstalledBaseUrl => BuildBaseUrl(MasterHost, "installed");

        public static string PickPrimaryHost()
        {
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return (ms % 2 == 0) ? MasterHost : AgentHost;
        }

        public static string GetFallbackHost(string primaryHost)
            => primaryHost == MasterHost ? AgentHost : MasterHost;

        public static string BuildBaseUrl(string host, string pathSegment)
            => $"http://{host}/{pathSegment.TrimStart('/')}";

        /// <summary>ZIP 경로 조립 — primary/fallback URL 쌍 반환</summary>
        public static (string Primary, string Fallback) BuildZipUrls(string pathSegment, string relativePath)
        {
            string primaryHost = PickPrimaryHost();
            string fallbackHost = GetFallbackHost(primaryHost);
            string rel = relativePath.TrimStart('/');
            string primary = $"{BuildBaseUrl(primaryHost, pathSegment)}/{rel}";
            string fallback = $"{BuildBaseUrl(fallbackHost, pathSegment)}/{rel}";
            return (primary, fallback);
        }

        public static string ExtractHost(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Host;
            return url;
        }
    }
}
