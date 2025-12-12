using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BravoGameLauncherGui
{
    public class GameBuildLauncher
    {
        private readonly Action<string> _log;
        private static readonly HttpClient _httpClient = new HttpClient();

        // 서버 빌드 베이스 URL (버전/플랫폼/파일명 붙여서 사용)
        private const string BuildServerBaseUrl = "http://bravo-build.omnicraftlabs.co.kr:8000/GameBuilds";

        public string RootDownloadDir { get; private set; }

        public GameBuildLauncher(Action<string> logCallback, string rootDownloadDir)
        {
            _log = logCallback ?? (_ => { });
            RootDownloadDir = string.IsNullOrWhiteSpace(rootDownloadDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BravoGameBuilds")
                : rootDownloadDir;

            Directory.CreateDirectory(RootDownloadDir);
        }

        public void ChangeRootDownloadDir(string newRoot)
        {
            if (string.IsNullOrWhiteSpace(newRoot))
                return;

            RootDownloadDir = newRoot;
            Directory.CreateDirectory(RootDownloadDir);
            _log($"[INFO] GameBuildLauncher RootDownloadDir 변경: {RootDownloadDir}");
        }

        /// <summary>
        /// 기존 호환용 (IP/창모드 기본값)
        /// </summary>
        public Task RunAsync(string zipFileName)
        {
            return RunAsync(zipFileName, "localhost", false);
        }

        /// <summary>
        /// 지정한 ZIP 빌드를 다운로드/압축해제 후
        /// IP 및 창모드 옵션에 맞춰 실행
        /// </summary>
        public async Task RunAsync(string zipFileName, string ipAddress, bool useWindowed)
        {
            if (string.IsNullOrWhiteSpace(zipFileName))
            {
                _log("[ERROR] ZIP 파일명이 비어 있습니다.");
                return;
            }

            var buildInfo = ParseBuildInfoFromFileName(zipFileName);
            string version = string.IsNullOrWhiteSpace(buildInfo.Version)
                ? "Unknown"
                : buildInfo.Version;

            string buildName = Path.GetFileNameWithoutExtension(zipFileName);

            string versionRoot = Path.Combine(RootDownloadDir, version);
            string buildRoot   = Path.Combine(versionRoot, buildName);
            string zipPath     = Path.Combine(buildRoot, "build.zip");
            string unpackDir   = Path.Combine(buildRoot, "unpacked");

            Directory.CreateDirectory(buildRoot);

            // 1) ZIP 다운로드 (없으면)
            if (!File.Exists(zipPath))
            {
                string url = $"{BuildServerBaseUrl}/{version}/WIN/{zipFileName}";
                _log($"[INFO] ZIP 파일이 없어 서버에서 다운로드합니다.");
                _log($"       URL: {url}");
                try
                {
                    using var resp = await _httpClient.GetAsync(url);
                    resp.EnsureSuccessStatusCode();

                    await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await resp.Content.CopyToAsync(fs);

                    _log($"[INFO] 다운로드 완료 → {zipPath}");
                }
                catch (Exception ex)
                {
                    _log("[ERROR] ZIP 다운로드 중 오류가 발생했습니다.");
                    _log(ex.Message);
                    return;
                }
            }
            else
            {
                _log($"[INFO] 기존 ZIP 파일 사용 → {zipPath}");
            }

            // 2) 압축 해제 (없으면)
            if (!Directory.Exists(unpackDir) || Directory.GetFileSystemEntries(unpackDir).Length == 0)
            {
                _log($"[INFO] ZIP 압축 해제 중... → {unpackDir}");

                try
                {
                    if (Directory.Exists(unpackDir))
                        Directory.Delete(unpackDir, recursive: true);

                    ZipFile.ExtractToDirectory(zipPath, unpackDir);
                    _log("[INFO] 압축 해제 완료.");
                }
                catch (Exception ex)
                {
                    _log("[ERROR] 압축 해제 중 오류가 발생했습니다.");
                    _log(ex.Message);
                    return;
                }
            }
            else
            {
                _log($"[INFO] 이미 압축 해제된 폴더 사용 → {unpackDir}");
            }

            // 3) 실행 파일 찾기 (우선 GW.exe, 없으면 첫 번째 .exe)
            string? exePath = FindExecutable(unpackDir);
            if (exePath == null)
            {
                _log("[ERROR] 실행 가능한 exe 파일을 찾지 못했습니다.");
                return;
            }

            // 4) 실행 인자 구성
            if (string.IsNullOrWhiteSpace(ipAddress))
                ipAddress = "localhost";

            string args = $"{ipAddress}:7777 -log";
            if (useWindowed)
            {
                args += " -windowed -ResX=1920 -ResY=1080";
            }

            _log("[INFO] 빌드 실행을 시작합니다.");
            _log($"       EXE : {exePath}");
            _log($"       Args: {args}");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? unpackDir,
                    UseShellExecute = false
                };

                Process.Start(psi);
                _log("[INFO] 게임 실행 명령을 호출했습니다.");
            }
            catch (Exception ex)
            {
                _log("[ERROR] 게임 실행 중 오류가 발생했습니다.");
                _log(ex.Message);
            }
        }

        private static (string Version, int CL, DateTime? Timestamp) ParseBuildInfoFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return (string.Empty, 0, null);

            // 예: GW_v0.0.1_CL2301_Shipping_20251205123010.zip
            var pattern = @"^GW_v(?<ver>\d+\.\d+\.\d+)_CL(?<cl>\d+)_.*_(?<ts>\d{14})\.zip$";
            var m = Regex.Match(fileName, pattern, RegexOptions.IgnoreCase);
            if (!m.Success)
                return (string.Empty, 0, null);

            string ver = m.Groups["ver"].Value;
            int cl = int.TryParse(m.Groups["cl"].Value, out var clVal) ? clVal : 0;

            string ts = m.Groups["ts"].Value; // yyyyMMddHHmmss
            if (DateTime.TryParseExact(ts, "yyyyMMddHHmmss", null,
                    System.Globalization.DateTimeStyles.None, out var dt))
            {
                return (ver, cl, dt);
            }

            return (ver, cl, null);
        }

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
    }
}
