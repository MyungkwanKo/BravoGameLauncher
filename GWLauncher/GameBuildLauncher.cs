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

        /// <summary>GameStarter 클라이언트(GW.exe) 기본 실행 인자.</summary>
        public const string DefaultClientLaunchArgs = "";

        /// <summary>GameStarter DS(GWServer.exe) 기본 실행 인자.</summary>
        public const string DefaultDedicatedServerArgs =
            "-port=7778 -MapBaseId=10111 -log -LogCmds=\"LogGW Verbose\"";

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

        public void ChangeRootDownloadDir(string newRootDownloadDir)
        {
            if (string.IsNullOrWhiteSpace(newRootDownloadDir))
                return;

            RootDownloadDir = newRootDownloadDir;
            Directory.CreateDirectory(RootDownloadDir);
        }

        /// <summary>
        /// zip 파일명으로부터 로컬 캐시의 빌드 폴더 경로({캐시}\{version}\{buildName})를 계산한다.
        /// 파일명에서 버전을 파싱하지 못하면 null. 다운로드/압축 해제와 크래시 로그 전송이 이 규칙을 공유한다.
        /// </summary>
        public string? GetBuildDir(string zipFileName)
        {
            if (string.IsNullOrWhiteSpace(zipFileName))
                return null;

            var (version, _, _) = ParseBuildInfoFromFileName(zipFileName);
            if (string.IsNullOrWhiteSpace(version))
                return null;

            string buildName = Path.GetFileNameWithoutExtension(zipFileName);
            return Path.Combine(RootDownloadDir, version, buildName);
        }

        /// <summary>
        /// zip 파일명으로부터 압축 해제 폴더(unpacked) 경로를 계산한다. 파싱 실패 시 null.
        /// </summary>
        public string? GetClientUnpackDir(string zipFileName)
        {
            string? buildDir = GetBuildDir(zipFileName);
            return buildDir == null ? null : Path.Combine(buildDir, "unpacked");
        }

        public async Task RunDedicatedServerAsync(
            string dsZipFileName,
            Action<double, string?>? progress = null,
            string? dsArgsOverride = null)
        {
            // ✅ v5: DS만 실행에서도 기존 DS가 떠 있으면 종료
            KillRunningDedicatedServer();

            if (string.IsNullOrWhiteSpace(dsZipFileName))
            {
                _log("[ERROR] DS ZIP 파일명이 비어 있습니다.");
                return;
            }

            string dsUnpackDir = await DownloadAndExtractWithProgressAsync(dsZipFileName, "DS", progress, 0, 100);
            progress?.Invoke(-1, "DS 실행 중...");
            StartDedicatedServer(dsUnpackDir, dsArgsOverride);
        }

        /// <summary>
        /// 클라이언트/DS 선택에 따라 ZIP 다운로드·압축 해제만 수행하고 프로세스는 실행하지 않습니다.
        /// </summary>
        public async Task PrepareBuildsOnlyAsync(
            string clientZipFileName,
            string dsZipFileName,
            bool wantClient,
            bool wantDS,
            Action<double, string?>? progress = null)
        {
            if (!wantClient && !wantDS)
            {
                _log("[ERROR] 클라이언트 또는 DS 중 하나 이상을 선택하세요.");
                return;
            }

            if (wantClient && string.IsNullOrWhiteSpace(clientZipFileName))
            {
                _log("[ERROR] Client ZIP 파일명이 비어 있습니다.");
                return;
            }

            if (wantDS && string.IsNullOrWhiteSpace(dsZipFileName))
            {
                _log("[ERROR] DS ZIP 파일명이 비어 있습니다.");
                return;
            }

            if (wantClient && wantDS)
            {
                progress?.Invoke(-1, "Client 다운로드 준비...");
                await DownloadAndExtractWithProgressAsync(clientZipFileName, "WIN", progress, 0, 50);
                progress?.Invoke(-1, "DS 다운로드 준비...");
                await DownloadAndExtractWithProgressAsync(dsZipFileName, "DS", progress, 50, 100);
            }
            else if (wantClient)
            {
                progress?.Invoke(-1, "Client 다운로드 준비...");
                await DownloadAndExtractWithProgressAsync(clientZipFileName, "WIN", progress, 0, 100);
            }
            else
            {
                progress?.Invoke(-1, "DS 다운로드 준비...");
                await DownloadAndExtractWithProgressAsync(dsZipFileName, "DS", progress, 0, 100);
            }

            progress?.Invoke(100, "다운로드·압축 해제 완료");
            _log("[INFO] 다운로드·압축 해제만 완료 (실행 없음).");
        }

        /// <summary>클라이언트만 다운로드 후 실행 (DS 없음).</summary>
        public async Task RunLocalClientOnlyAsync(
            string clientZipFileName,
            bool useWindowed,
            Action<double, string?>? progress = null,
            string? clientArgsOverride = null)
        {
            if (string.IsNullOrWhiteSpace(clientZipFileName))
            {
                _log("[ERROR] Client ZIP 파일명이 비어 있습니다.");
                return;
            }

            _log("[INFO] Client만 실행: 다운로드/압축해제 시작");
            progress?.Invoke(-1, "Client 다운로드 준비...");
            string clientUnpackDir = await DownloadAndExtractWithProgressAsync(clientZipFileName, "WIN", progress, 0, 100);

            string? clientExePath = FindExecutable(clientUnpackDir, "GW.exe");
            if (string.IsNullOrWhiteSpace(clientExePath) || !File.Exists(clientExePath))
            {
                _log("[ERROR] Client 실행 파일(GW.exe)을 찾지 못했습니다.");
                return;
            }

            progress?.Invoke(-1, "게임 실행 중...");
            string clientArgs = clientArgsOverride?.Trim() ?? "";
            if (useWindowed)
                clientArgs = string.IsNullOrEmpty(clientArgs)
                    ? "-windowed -ResX=1920 -ResY=1080"
                    : clientArgs + " -windowed -ResX=1920 -ResY=1080";

            _log($"[INFO] Client 실행: {clientExePath} {clientArgs}");
            Process.Start(new ProcessStartInfo
            {
                FileName         = clientExePath,
                Arguments        = clientArgs,
                WorkingDirectory = Path.GetDirectoryName(clientExePath) ?? clientUnpackDir,
                UseShellExecute  = false
            });
        }

        /// <summary>진행률을 progress(0~100 또는 -1=비결정)로 보고하며 다운로드 후 압축 해제. progressStart~progressEnd 구간에 매핑.</summary>
        private async Task<string> DownloadAndExtractWithProgressAsync(
            string zipFileName,
            string platform,
            Action<double, string?>? progress,
            double progressStart,
            double progressEnd)
        {
            if (string.IsNullOrWhiteSpace(zipFileName))
                throw new ArgumentException("zip 파일명이 비어 있습니다.", nameof(zipFileName));

            if (string.IsNullOrWhiteSpace(platform))
                platform = "WIN";

            var (version, _, _) = ParseBuildInfoFromFileName(zipFileName);
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException($"파일명에서 버전 정보를 파싱하지 못했습니다: {zipFileName}");

            var (primaryUrl, fallbackUrl) = DownloadHostRouter.BuildZipUrls(
                "builds", $"{version}/{platform}/{zipFileName}");

            // 경로 규칙은 GetBuildDir/GetClientUnpackDir와 공유한다(크래시 로그 전송도 같은 규칙을 사용).
            string buildDir   = GetBuildDir(zipFileName)
                                ?? throw new InvalidOperationException($"빌드 폴더 경로를 계산하지 못했습니다: {zipFileName}");
            string zipPath    = Path.Combine(buildDir, "build.zip");
            string unpackDir  = Path.Combine(buildDir, "unpacked");

            Directory.CreateDirectory(buildDir);

            double MapProgress(double pct)
            {
                if (pct < 0) return -1;
                return progressStart + (progressEnd - progressStart) * Math.Min(100, pct) / 100.0;
            }

            // ZIP 다운로드 (진행률 지원)
            if (!File.Exists(zipPath))
            {
                _log($"[INFO] ({platform}) ZIP 다운로드 시작...");
                _log($"       URL: {primaryUrl}");

                string SizeLabel(long read, long? tot)
                    => DownloadProgressFormatter.FormatCurrentOverTotal(read, tot);

                progress?.Invoke(
                    MapProgress(0),
                    $"{platform} 다운로드 0% ({SizeLabel(0, null)})");

                long? totalNullable = null;

                await DownloadWithFailover.DownloadToFileWithFailoverAsync(
                    HttpClient,
                    primaryUrl,
                    fallbackUrl,
                    zipPath,
                    _log,
                    (pct, readTotal, totalBytes) =>
                    {
                        if (totalBytes > 0)
                            totalNullable = totalBytes;

                        if (progress == null)
                            return;

                        if (totalBytes > 0)
                        {
                            progress(
                                MapProgress(Math.Min(100, pct)),
                                $"{platform} 다운로드 {pct:0}% ({SizeLabel(readTotal, totalBytes)})");
                        }
                        else
                        {
                            progress(
                                -1,
                                $"{platform} 다운로드 ({SizeLabel(readTotal, null)})");
                        }
                    });

                long readTotal = new FileInfo(zipPath).Length;
                long effectiveTotal = (totalNullable is long t && t > 0) ? t : readTotal;
                progress?.Invoke(
                    MapProgress(100),
                    $"{platform} 다운로드 완료 ({SizeLabel(readTotal, effectiveTotal)})");
                _log($"[INFO] ({platform}) ZIP 다운로드 완료.");
            }
            else
            {
                _log($"[INFO] ({platform}) 캐시된 ZIP 파일 사용.");
                long len = new FileInfo(zipPath).Length;
                progress?.Invoke(
                    MapProgress(100),
                    $"{platform} 캐시 사용 ({DownloadProgressFormatter.FormatCurrentOverTotal(len, len)})");
            }

            // unpacked 폴더가 이미 있으면 삭제/언팩 없이 바로 사용 (실행 시간 단축)
            if (Directory.Exists(unpackDir) && Directory.GetFileSystemEntries(unpackDir).Length > 0)
            {
                _log($"[INFO] ({platform}) 기존 unpacked 폴더 사용 → 바로 실행");
                progress?.Invoke(progressEnd, $"{platform} 캐시 사용");
                return unpackDir;
            }

            // 압축 해제 (unpacked 없거나 비어 있을 때만)
            if (Directory.Exists(unpackDir))
            {
                _log($"[INFO] ({platform}) 기존 unpacked 폴더 삭제.");
                Directory.Delete(unpackDir, recursive: true);
            }

            progress?.Invoke(-1, $"{platform} 압축 해제 중...");
            _log($"[INFO] ({platform}) ZIP 압축 해제 중...");
            ZipFile.ExtractToDirectory(zipPath, unpackDir);
            _log($"[INFO] ({platform}) 압축 해제 완료.");
            progress?.Invoke(progressEnd, $"{platform} 압축 해제 완료");

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
            bool useWindowed,
            Action<double, string?>? progress = null,
            string? clientArgsOverride = null,
            string? dsArgsOverride = null)
        {
            // ✅ 0) DS가 실행 중이면 먼저 종료 (압축해제/삭제 전에!)
            KillRunningDedicatedServer();

            // 1) Client(WIN) → DS(DS) 순차 다운로드/압축해제 (진행률 0-50%, 50-100%)
            _log("[INFO] Local 실행: Client/DS 준비 시작");

            progress?.Invoke(-1, "Client 다운로드 준비...");
            string clientUnpackDir = await DownloadAndExtractWithProgressAsync(clientZipFileName, "WIN", progress, 0, 50);

            progress?.Invoke(-1, "DS 다운로드 준비...");
            string dsUnpackDir = await DownloadAndExtractWithProgressAsync(dsZipFileName, "DS", progress, 50, 100);

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

            progress?.Invoke(-1, "게임 실행 중...");

            // 3) DS 실행 (실행 옵션은 호출측에서 전달 또는 기본값)
            StartDedicatedServer(dsUnpackDir, dsArgsOverride);

            // 4) Client 실행 (DS 실행 후, 실행 옵션은 호출측 그대로; 비어 있으면 인자 없음)
            string clientArgs = clientArgsOverride?.Trim() ?? "";
            if (useWindowed)
                clientArgs = string.IsNullOrEmpty(clientArgs)
                    ? "-windowed -ResX=1920 -ResY=1080"
                    : clientArgs + " -windowed -ResX=1920 -ResY=1080";

            _log($"[INFO] Client 실행: {clientExePath} {clientArgs}");

            Process.Start(new ProcessStartInfo
            {
                FileName         = clientExePath,
                Arguments        = clientArgs,
                WorkingDirectory = Path.GetDirectoryName(clientExePath) ?? clientUnpackDir,
                UseShellExecute  = false
            });
        }

        private void StartDedicatedServer(string dsUnpackDir, string? dsArgsOverride = null)
        {
            // GWServer.exe 탐색
            string? dsExePath = FindExecutable(dsUnpackDir, "GWServer.exe");
            if (string.IsNullOrWhiteSpace(dsExePath) || !File.Exists(dsExePath))
            {
                _log("[ERROR] DS 실행 파일(GWServer.exe)을 찾지 못했습니다.");
                return;
            }

            string dsArgs = !string.IsNullOrWhiteSpace(dsArgsOverride) ? dsArgsOverride.Trim() : DefaultDedicatedServerArgs;
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
