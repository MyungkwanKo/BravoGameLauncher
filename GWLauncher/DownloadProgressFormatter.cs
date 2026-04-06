using System;

namespace BravoGameLauncherGui
{
    /// <summary>다운로드 진행 UI용: 현재/총 바이트를 동일 단위(총 ≥1GiB면 GB, 아니면 MB)로 표기.</summary>
    public static class DownloadProgressFormatter
    {
        private const long GiB = 1024L * 1024 * 1024;
        private const long MiB = 1024L * 1024;

        /// <param name="totalBytes">총 크기를 모르면 null 또는 0 이하.</param>
        public static string FormatCurrentOverTotal(long readBytes, long? totalBytes)
        {
            long total = totalBytes ?? 0;
            if (total <= 0)
                return $"{readBytes / (double)MiB:F2} MB / ?";

            if (total >= GiB)
            {
                return $"{readBytes / (double)GiB:F2} GB / {total / (double)GiB:F2} GB";
            }

            return $"{readBytes / (double)MiB:F2} MB / {total / (double)MiB:F2} MB";
        }
    }
}
