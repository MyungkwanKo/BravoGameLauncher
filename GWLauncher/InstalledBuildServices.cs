using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace BravoGameLauncherGui
{
    // latest.json 모델
    public class InstalledBuildLatest
    {
        public string engineVersion { get; set; } = "";
        public string label { get; set; } = "";
        public int cl { get; set; }
        public int jenkinsBuild { get; set; }
        public string createdAt { get; set; } = "";
        public InstalledBuildZip zip { get; set; } = new();
    }

    public class InstalledBuildZip
    {
        public string fileName { get; set; } = "";
        public string url { get; set; } = "";
        public long size { get; set; }
        public string sha256 { get; set; } = "";
    }

    // 로컬 meta 모델
    public class InstalledBuildMeta
    {
        public string engineVersion { get; set; } = "";
        public string label { get; set; } = "";
        public string installedAt { get; set; } = "";
        public string zipFileName { get; set; } = "";
        public long zipSize { get; set; }
        public string zipSha256 { get; set; } = "";
        public string zipUrl { get; set; } = "";
    }

    public static class InstalledBuildServices
    {
        private static readonly HttpClient Http = new HttpClient();

        /// <summary>서버 배포 루트. 하위에 엔진 버전 폴더 없이 flat 배치. JSON은 Master 고정.</summary>
        public static string InstalledBuildLatestJsonUrl =>
            $"{DownloadHostRouter.MasterInstalledBaseUrl}/latest.json";

        public static async Task<InstalledBuildLatest?> GetLatestAsync(string engineVersion, Action<string> log)
        {
            string url = InstalledBuildLatestJsonUrl;
            log($"[INFO] latest.json 요청: {url}");

            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                log($"[ERROR] latest.json 실패: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json.TrimStart('\uFEFF');

            var model = JsonSerializer.Deserialize<InstalledBuildLatest>(json);
            if (model == null)
            {
                log("[ERROR] latest.json 파싱 실패");
                return null;
            }

            string requested = engineVersion?.Trim() ?? "";
            string actual = model.engineVersion?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(requested) &&
                !string.IsNullOrWhiteSpace(actual) &&
                !string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase))
            {
                log($"[WARN] latest.json engineVersion({actual}) ≠ 선택 버전({requested})");
                return null;
            }

            string jsonZipUrl = model.zip?.url ?? "";
            string resolvedZipUrl = ResolveInstalledBuildZipUrl(model);
            if (!string.IsNullOrWhiteSpace(resolvedZipUrl))
            {
                if (!string.Equals(jsonZipUrl, resolvedZipUrl, StringComparison.OrdinalIgnoreCase))
                    log($"[INFO] ZIP URL 보정: {jsonZipUrl} → {resolvedZipUrl}");
                model.zip ??= new InstalledBuildZip();
                model.zip.url = resolvedZipUrl;
            }

            return model;
        }

        /// <summary>
        /// flat 배치: /installed/{fileName} (엔진 버전 중간 경로 없음)
        /// </summary>
        public static string ResolveInstalledBuildZipUrl(InstalledBuildLatest latest)
        {
            string fileName = latest.zip?.fileName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(latest.label))
                fileName = $"{latest.label}.zip";

            if (string.IsNullOrWhiteSpace(fileName))
                return latest.zip?.url ?? "";

            return $"{DownloadHostRouter.MasterInstalledBaseUrl}/{fileName}";
        }

        public static InstalledBuildMeta? TryLoadLocalMeta(string installRoot)
        {
            string metaPath = Path.Combine(installRoot, "installed_build.meta.json");
            try
            {
                if (!File.Exists(metaPath))
                    return null;

                var json = File.ReadAllText(metaPath);
                return JsonSerializer.Deserialize<InstalledBuildMeta>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void SaveLocalMeta(string installRoot, InstalledBuildMeta meta)
        {
            string metaPath = Path.Combine(installRoot, "installed_build.meta.json");
            Directory.CreateDirectory(installRoot);

            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json);
        }

        /// <param name="progress">(퍼센트, 받은 바이트, 총 바이트 추정). 총 크기를 모르면 totalBytes는 0.</param>
        public static async Task DownloadZipAsync(string url, string destZipPath, long expectedSize, Action<string> log, Action<double, long, long> progress)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destZipPath)!);

            // 이미 파일이 있고 size가 일치하면 재다운로드 생략
            if (File.Exists(destZipPath))
            {
                var fi = new FileInfo(destZipPath);
                if (fi.Length == expectedSize && expectedSize > 0)
                {
                    log($"[INFO] ZIP 이미 존재(크기 일치) → 다운로드 생략: {destZipPath}");
                    progress(100, fi.Length, fi.Length);
                    return;
                }

                log($"[WARN] 기존 ZIP 크기 불일치 → 삭제 후 재다운로드: {destZipPath}");
                File.Delete(destZipPath);
            }

            string fileName = ExtractZipFileName(url);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException($"ZIP 파일명을 URL에서 추출하지 못했습니다: {url}", nameof(url));

            var (primaryUrl, fallbackUrl) = DownloadHostRouter.BuildZipUrls("installed", fileName);

            await DownloadWithFailover.DownloadToFileWithFailoverAsync(
                Http,
                primaryUrl,
                fallbackUrl,
                destZipPath,
                log,
                progress);

            log($"[SUCCESS] ZIP 다운로드 완료: {destZipPath}");
        }

        private static string ExtractZipFileName(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return Path.GetFileName(uri.LocalPath);
            return Path.GetFileName(url);
        }

        public static async Task<bool> VerifyZipAsync(string zipPath, long expectedSize, string expectedSha256, Action<string> log)
        {
            if (!File.Exists(zipPath))
            {
                log("[ERROR] ZIP 파일이 없습니다.");
                return false;
            }

            var fi = new FileInfo(zipPath);
            if (expectedSize > 0 && fi.Length != expectedSize)
            {
                log($"[ERROR] ZIP size 불일치. expected={expectedSize}, actual={fi.Length}");
                return false;
            }

            string hash = await ComputeSha256Async(zipPath);
            if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                log($"[ERROR] SHA256 불일치.\n expected={expectedSha256}\n actual  ={hash}");
                return false;
            }

            log("[SUCCESS] ZIP 무결성 검증 통과");
            return true;
        }

        private static async Task<string> ComputeSha256Async(string filePath)
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(fs);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static async Task ExtractAndApplyAsync(string zipPath, string installRoot, Action<string> log)
        {
            string staging = Path.Combine(installRoot, "_staging");

            // staging 정리
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);

            Directory.CreateDirectory(staging);

            log("[INFO] ZIP 압축 해제 시작 (.NET)");
            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);

            string stagedEngine = Path.Combine(staging, "Engine");
            if (!Directory.Exists(stagedEngine))
                throw new InvalidOperationException("압축 해제 결과에 Engine 폴더가 없습니다. (ZIP 구조 확인 필요)");

            // 기존 Engine 삭제
            string targetEngine = Path.Combine(installRoot, "Engine");
            if (Directory.Exists(targetEngine))
            {
                log("[INFO] 기존 Engine 폴더 삭제");
                Directory.Delete(targetEngine, true);
            }

            // staging Engine → installRoot Engine
            log("[INFO] 새 Engine 적용");
            Directory.Move(stagedEngine, targetEngine);

            // staging 정리(선택)
            try { Directory.Delete(staging, true); } catch { }

            await Task.CompletedTask;
            log("[SUCCESS] Engine 적용 완료");
        }
    }
}
