using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;

namespace BravoGameLauncherGui
{
    public class GameBuildLauncher
    {
        private readonly Action<string> _log;
        private static readonly HttpClient HttpClient = new();

        // 서버 빌드 베이스 URL (버전/플랫폼/파일명 붙여서 사용)
        private const string BuildServerBaseUrl =
            "http://bravo-build.omnicraftlabs.co.kr:8000/GameBuilds";

        public string RootDownloadDir { get; private set; }

        public GameBuildLauncher(Action<string> log, string rootDownloadDir)
        {
            _log = log ?? (_ => { });

            if (string.IsNullOrWhiteSpace(rootDownloadDir))
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                RootDownloadDir = Path.Combine(localAppData, "GWLauncher", "Cache");
            }
            else
            {
                RootDownloadDir = rootDownloadDir;
            }

            Directory.CreateDirectory(RootDownloadDir);
        }

        // 기존 호출과 호환용: 기본 플랫폼은 WIN
        public Task RunAsync(string zipFileName, string ipAddress, bool useWindowed)
            => RunAsync(zipFileName, "WIN", ipAddress, useWindowed);

        // 플랫폼까지 명시하는 버전
        public async Task RunAsync(string zipFileName, string platform, string ipAddress, bool useWindowed)
        {
            if (string.IsNullOrWhiteSpace(zipFileName))
                throw new ArgumentException("zip 파일명이 비어 있습니다.", nameof(zipFileName));

            if (string.IsNullOrWhiteSpace(platform))
                platform = "WIN";

            var (version, _, _) = ParseBuildInfoFromFileName(zipFileName);
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException($"파일명에서 버전 정보를 파싱하지 못했습니다: {zipFileName}");

            string buildName = Path.GetFileNameWithoutExtension(zipFileName);

            string downloadUrl = $"{BuildServerBaseUrl}/{version}/{platform}/{zipFileName}";

            _log($"[INFO] 빌드 실행 준비");
            _log($"       Version : {version}");
            _log($"       Platform: {platform}");
            _log($"       File    : {zipFileName}");
            _log($"       URL     : {downloadUrl}");

            // 캐시 구조: {Root}/{version}/{buildName}/build.zip + unpacked/
            string versionDir = Path.Combine(RootDownloadDir, version);
            string buildDir   = Path.Combine(versionDir, buildName);
            string zipPath    = Path.Combine(buildDir, "build.zip");
            string unpackDir  = Path.Combine(buildDir, "unpacked");

            Directory.CreateDirectory(buildDir);

            // ZIP 다운로드 (이미 있으면 재사용)
            if (!File.Exists(zipPath))
            {
                _log("[INFO] ZIP 다운로드 시작...");

                using var response = await HttpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                await using var fs = File.Create(zipPath);
                await response.Content.CopyToAsync(fs);

                _log("[INFO] ZIP 다운로드 완료.");
            }
            else
            {
                _log("[INFO] 캐시된 ZIP 파일 사용.");
            }

            // 압축 해제 (unpacked 폴더 비우고 다시 풀기)
            if (Directory.Exists(unpackDir))
            {
                _log("[INFO] 기존 unpacked 폴더 삭제.");
                Directory.Delete(unpackDir, recursive: true);
            }

            _log("[INFO] ZIP 압축 해제 중...");
            ZipFile.ExtractToDirectory(zipPath, unpackDir);
            _log("[INFO] 압축 해제 완료.");

            // 실행 파일 찾기
            string? exePath = FindExecutable(unpackDir);
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                _log("[ERROR] 실행 가능한 exe를 찾지 못했습니다.");
                return;
            }

            // 실행 인자 구성: GW.exe {IP}:7777 -log [windowed 옵션]
            string address = $"{ipAddress}:7777";
            string args    = $"{address} -log";

            if (useWindowed)
            {
                args += " -windowed -ResX=1920 -ResY=1080";
            }

            _log($"[INFO] 실행: {exePath} {args}");

            var psi = new ProcessStartInfo
            {
                FileName         = exePath,
                Arguments        = args,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? unpackDir,
                UseShellExecute  = false
            };

            Process.Start(psi);
        }

        /// <summary>
        /// ZIP 빌드를 다운로드 및 압축 해제만 수행하고 실행은 하지 않습니다.
        /// 주로 Dedicated Server 테스트용으로 사용합니다.
        /// </summary>
        /// <param name="fileUrl">직접 접근 가능한 ZIP 파일 전체 URL</param>
        /// <returns>압축이 풀린 폴더 경로 (실패 시 null)</returns>
        public async Task<string?> DownloadAndExtractOnlyAsync(string fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
            {
                _log("[ERROR] DS 빌드 URL 이 비어 있습니다.");
                return null;
            }

            // URL에서 파일명 추출
            string fileName;
            try
            {
                if (Uri.TryCreate(fileUrl, UriKind.Absolute, out Uri? uri) && uri != null)
                    fileName = Path.GetFileName(uri.LocalPath);
                else
                    fileName = Path.GetFileName(fileUrl);
            }
            catch
            {
                fileName = Path.GetFileName(fileUrl);
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                _log("[ERROR] DS 빌드 URL 에서 파일명을 추출하지 못했습니다.");
                return null;
            }

            var buildInfo = ParseBuildInfoFromFileName(fileName);
            string version = string.IsNullOrWhiteSpace(buildInfo.Version)
                ? "Unknown"
                : buildInfo.Version;

            string buildName = Path.GetFileNameWithoutExtension(fileName);

            string versionRoot = Path.Combine(RootDownloadDir, version);
            string buildRoot   = Path.Combine(versionRoot, buildName);
            string zipPath     = Path.Combine(buildRoot, "build.zip");
            string unpackDir   = Path.Combine(buildRoot, "unpacked");

            Directory.CreateDirectory(buildRoot);

            // 1) ZIP 다운로드 (없으면)
            if (!File.Exists(zipPath))
            {
                _log("[INFO] DS ZIP 파일이 없어 서버에서 다운로드합니다.");
                _log("       URL: " + fileUrl);

                HttpResponseMessage? resp = null;
                try
                {
                    resp = await HttpClient.GetAsync(fileUrl);
                    resp.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await resp.Content.CopyToAsync(fs);
                    }

                    _log("[INFO] DS 다운로드 완료 → " + zipPath);
                }
                catch (Exception ex)
                {
                    _log("[ERROR] DS ZIP 다운로드 중 오류가 발생했습니다.");
                    _log(ex.Message);
                    return null;
                }
                finally
                {
                    resp?.Dispose();
                }
            }
            else
            {
                _log("[INFO] 기존 DS ZIP 파일 사용 → " + zipPath);
            }

            // 2) 압축 해제 (없으면)
            if (!Directory.Exists(unpackDir) || Directory.GetFileSystemEntries(unpackDir).Length == 0)
            {
                _log("[INFO] DS ZIP 압축 해제 중... → " + unpackDir);

                try
                {
                    if (Directory.Exists(unpackDir))
                        Directory.Delete(unpackDir, true);

                    ZipFile.ExtractToDirectory(zipPath, unpackDir);
                    _log("[INFO] DS 압축 해제 완료.");
                }
                catch (Exception ex)
                {
                    _log("[ERROR] DS 압축 해제 중 오류가 발생했습니다.");
                    _log(ex.Message);
                    return null;
                }
            }
            else
            {
                _log("[INFO] 이미 압축 해제된 DS 폴더 사용 → " + unpackDir);
            }

            return unpackDir;
        }

        // 파일명 파싱: GW_v{ver}_CL{cl}_..._{yyyyMMddHHmmss}[_DS].zip
        private static (string Version, int CL, DateTime? Timestamp) ParseBuildInfoFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return (string.Empty, 0, null);

            // 예:
            //  GW_v0.0.1_CL2301_Shipping_20251205123010.zip
            //  GW_v0.0.1_CL2351_Development_20251212220043_DS.zip
            var pattern = @"^GW_v(?<ver>\d+\.\d+\.\d+)_CL(?<cl>\d+)_.*_(?<ts>\d{14})(?:_DS)?\.zip$";

            var m = Regex.Match(fileName, pattern, RegexOptions.IgnoreCase);
            if (!m.Success)
                return (string.Empty, 0, null);

            string ver = m.Groups["ver"].Value;
            int cl = int.TryParse(m.Groups["cl"].Value, out var clVal) ? clVal : 0;

            string ts = m.Groups["ts"].Value; // yyyyMMddHHmmss
            if (DateTime.TryParseExact(
                    ts,
                    "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dt))
            {
                return (ver, cl, dt);
            }

            return (ver, cl, null);
        }

        // 실행 파일 탐색
        // 1순위: GW.exe
        // 2순위: 첫 번째 exe
        private static string? FindExecutable(string rootDir)
        {
            if (!Directory.Exists(rootDir))
                return null;

            // 1순위: GW.exe
            var gwExe = Directory.GetFiles(rootDir, "GW.exe", SearchOption.AllDirectories);
            if (gwExe.Length > 0)
                return gwExe[0];

            // 2순위: 첫 번째 exe
            var exes = Directory.GetFiles(rootDir, "*.exe", SearchOption.AllDirectories);
            if (exes.Length > 0)
                return exes[0];

            return null;
        }

        public void ChangeRootDownloadDir(string newRootDownloadDir)
        {
            if (string.IsNullOrWhiteSpace(newRootDownloadDir))
                return;

            RootDownloadDir = newRootDownloadDir;
            Directory.CreateDirectory(RootDownloadDir);
        }
    }
}
