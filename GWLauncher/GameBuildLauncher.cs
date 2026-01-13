using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;

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
        public Task RunAsync(string zipFileName, bool useWindowed)
            => RunAsync(zipFileName, "WIN", useWindowed);

        // 플랫폼까지 명시하는 버전
        public async Task RunAsync(string zipFileName, string platform, bool useWindowed)
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

                using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
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

            // ✅ Local 실행에서도 address 인자 제거: -log (+ windowed 옵션)만 전달
            string args = "-log";

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

        private static string? FindExecutable(string rootDir, string preferredExeName)
        {
            if (!Directory.Exists(rootDir))
                return null;

            // 1순위: preferred exe
            var preferred = Directory.GetFiles(rootDir, preferredExeName, SearchOption.AllDirectories);
            if (preferred.Length > 0)
                return preferred[0];

            // 2순위: 첫 번째 exe
            var exes = Directory.GetFiles(rootDir, "*.exe", SearchOption.AllDirectories);
            if (exes.Length > 0)
                return exes[0];

            return null;
        }

        // 기존 호환용 오버로드 (RunAsync에서 사용)
        private static string? FindExecutable(string rootDir)
        {
            // 클라이언트 기본 우선순위: GW.exe
            return FindExecutable(rootDir, "GW.exe");
        }


        public void ChangeRootDownloadDir(string newRootDownloadDir)
        {
            if (string.IsNullOrWhiteSpace(newRootDownloadDir))
                return;

            RootDownloadDir = newRootDownloadDir;
            Directory.CreateDirectory(RootDownloadDir);
        }

        public async Task RunDedicatedServerAsync(string dsZipFileName)
        {
            // ✅ v5: DS만 실행에서도 기존 DS가 떠 있으면 종료
            KillRunningDedicatedServer();

            if (string.IsNullOrWhiteSpace(dsZipFileName))
            {
                _log("[ERROR] DS ZIP 파일명이 비어 있습니다.");
                return;
            }

            var (version, _, _) = ParseBuildInfoFromFileName(dsZipFileName);
            if (string.IsNullOrWhiteSpace(version))
            {
                _log("[ERROR] DS 파일명에서 버전 정보를 파싱하지 못했습니다.");
                return;
            }

            string buildName = Path.GetFileNameWithoutExtension(dsZipFileName);
            string downloadUrl = $"{BuildServerBaseUrl}/{version}/DS/{dsZipFileName}";

            _log("[INFO] Dedicated Server 실행 준비");
            _log($"       Version : {version}");
            _log($"       File    : {dsZipFileName}");
            _log($"       URL     : {downloadUrl}");

            // 캐시 구조 동일
            string versionDir = Path.Combine(RootDownloadDir, version);
            string buildDir   = Path.Combine(versionDir, buildName);
            string zipPath    = Path.Combine(buildDir, "build.zip");
            string unpackDir  = Path.Combine(buildDir, "unpacked");

            Directory.CreateDirectory(buildDir);

            // 1️⃣ ZIP 다운로드
            if (!File.Exists(zipPath))
            {
                _log("[INFO] DS ZIP 다운로드 시작...");

                using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);

                _log("[INFO] DS ZIP 다운로드 완료.");
            }
            else
            {
                _log("[INFO] 캐시된 DS ZIP 파일 사용.");
            }

            // 2️⃣ 압축 해제
            if (Directory.Exists(unpackDir))
            {
                _log("[INFO] 기존 DS unpacked 폴더 삭제.");
                Directory.Delete(unpackDir, recursive: true);
            }

            _log("[INFO] DS ZIP 압축 해제 중...");
            ZipFile.ExtractToDirectory(zipPath, unpackDir);
            _log("[INFO] DS 압축 해제 완료.");

            // 3️⃣ GWServer.exe 탐색
            StartDedicatedServer(unpackDir);
        }

        private async Task<string> DownloadAndExtractAsync(string zipFileName, string platform)
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

            // 캐시 구조: {Root}/{version}/{buildName}/build.zip + unpacked/
            string versionDir = Path.Combine(RootDownloadDir, version);
            string buildDir   = Path.Combine(versionDir, buildName);
            string zipPath    = Path.Combine(buildDir, "build.zip");
            string unpackDir  = Path.Combine(buildDir, "unpacked");

            Directory.CreateDirectory(buildDir);

            // ZIP 다운로드
            if (!File.Exists(zipPath))
            {
                _log($"[INFO] ({platform}) ZIP 다운로드 시작...");
                _log($"       URL: {downloadUrl}");

                using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);

                _log($"[INFO] ({platform}) ZIP 다운로드 완료.");
            }
            else
            {
                _log($"[INFO] ({platform}) 캐시된 ZIP 파일 사용.");
            }

            // 압축 해제 (항상 새로)
            if (Directory.Exists(unpackDir))
            {
                _log($"[INFO] ({platform}) 기존 unpacked 폴더 삭제.");
                Directory.Delete(unpackDir, recursive: true);
            }

            _log($"[INFO] ({platform}) ZIP 압축 해제 중...");
            ZipFile.ExtractToDirectory(zipPath, unpackDir);
            _log($"[INFO] ({platform}) 압축 해제 완료.");

            return unpackDir;
        }

        private void KillRunningDedicatedServer()
        {
            try
            {
                var procs = Process.GetProcessesByName("GWServer");
                if (procs.Length == 0)
                    return;

                _log($"[INFO] 기존 DS 프로세스 {procs.Length}개 감지 → 종료 시도");

                foreach (var p in procs)
                {
                    try
                    {
                        _log($"       Kill PID={p.Id}");
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        _log($"[WARN] DS 프로세스 종료 실패(PID={p.Id}): {ex.Message}");
                    }
                }

                _log("[INFO] 기존 DS 프로세스 종료 처리 완료.");
            }
            catch (Exception ex)
            {
                _log("[WARN] DS 프로세스 탐색/종료 중 예외: " + ex.Message);
            }
        }

        public async Task RunLocalWithDedicatedServerAsync(
            string clientZipFileName,
            string dsZipFileName,
            bool useWindowed)
        {
            // ✅ 0) DS가 실행 중이면 먼저 종료 (압축해제/삭제 전에!)
            KillRunningDedicatedServer();

            // 1) Client(WIN) + DS(DS) 다운로드/압축해제 병렬
            _log("[INFO] Local 실행: Client/DS 준비 병렬 시작");

            var clientTask = DownloadAndExtractAsync(clientZipFileName, "WIN");
            var dsTask     = DownloadAndExtractAsync(dsZipFileName, "DS");

            await Task.WhenAll(clientTask, dsTask);

            string clientUnpackDir = clientTask.Result;
            string dsUnpackDir     = dsTask.Result;

            // 2) 실행 파일 탐색
            string? dsExePath = FindExecutable(dsUnpackDir, "GWServer.exe");
            if (string.IsNullOrWhiteSpace(dsExePath) || !File.Exists(dsExePath))
            {
                _log("[ERROR] DS 실행 파일(GWServer.exe)을 찾지 못했습니다.");
                return;
            }

            string? clientExePath = FindExecutable(clientUnpackDir, "GW.exe");
            if (string.IsNullOrWhiteSpace(clientExePath) || !File.Exists(clientExePath))
            {
                _log("[ERROR] Client 실행 파일(GW.exe)을 찾지 못했습니다.");
                return;
            }

            // 3) DS 실행 (커맨드 고정)
            StartDedicatedServer(dsUnpackDir);

            // 4) Client 실행 (DS 실행 후)
            string clientArgs = "-log";
            if (useWindowed)
                clientArgs += " -windowed -ResX=1920 -ResY=1080";

            _log($"[INFO] Client 실행: {clientExePath} {clientArgs}");

            Process.Start(new ProcessStartInfo
            {
                FileName         = clientExePath,
                Arguments        = clientArgs,
                WorkingDirectory = Path.GetDirectoryName(clientExePath) ?? clientUnpackDir,
                UseShellExecute  = false
            });
        }

        private void StartDedicatedServer(string dsUnpackDir)
        {
            // GWServer.exe 탐색
            string? dsExePath = FindExecutable(dsUnpackDir, "GWServer.exe");
            if (string.IsNullOrWhiteSpace(dsExePath) || !File.Exists(dsExePath))
            {
                _log("[ERROR] DS 실행 파일(GWServer.exe)을 찾지 못했습니다.");
                return;
            }

            string dsArgs = "/GWBattleRoyale/Maps/L_BR_Proto?port=7778 -log";
            _log($"[INFO] DS 실행: {dsExePath} {dsArgs}");

            Process.Start(new ProcessStartInfo
            {
                FileName         = dsExePath,
                Arguments        = dsArgs,
                WorkingDirectory = Path.GetDirectoryName(dsExePath) ?? dsUnpackDir,
                UseShellExecute  = false
            });
        }
    }
}
