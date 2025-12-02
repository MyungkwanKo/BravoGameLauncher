using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace BravoGameLauncherGui
{
    public partial class MainWindow : Window
    {
        private readonly GameBuildLauncher _launcher;
        private readonly AppSettings _settings;

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _launcher = new GameBuildLauncher(AppendLog);

            TxtCachePath.Text = _launcher.RootDownloadDir;
            AppendLog("=== Bravo Game Launcher (GUI) ===");
            AppendLog($"캐시 루트 경로: {_launcher.RootDownloadDir}");
            AppendLog("");

            RefreshRecentFileNames();
        }

        /// <summary>
        /// ComboBox에 최근 파일명 리스트를 바인딩
        /// </summary>
        private void RefreshRecentFileNames()
        {
            CmbFileName.ItemsSource = null;
            CmbFileName.ItemsSource = _settings.RecentFileNames;

            if (_settings.RecentFileNames.Count > 0)
            {
                CmbFileName.Text = _settings.RecentFileNames[0];
            }
            else
            {
                // 초기 기본값
                CmbFileName.Text = "GW_v0.0.1_CL2229_Shipping_20251201220028.zip";
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            BtnRun.IsEnabled = false;

            try
            {
                string fileName = (CmbFileName.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    AppendLog("[WARN] 파일명을 입력하세요.");
                    return;
                }

                // 최근 목록에 추가 & 저장
                _settings.AddRecentFileName(fileName);
                _settings.Save();
                RefreshRecentFileNames();

                // 실제 실행 로직
                await _launcher.RunAsync(fileName);
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 예외가 발생했습니다.");
                AppendLog(ex.Message);
            }
            finally
            {
                BtnRun.IsEnabled = true;
            }
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }
    }

    /// <summary>
    /// 기존에 쓰던 GameBuildLauncher (변경 없음, RootDownloadDir은 내부에서 결정)
    /// </summary>
    public class GameBuildLauncher
    {
        private const string BaseUrl = "http://bravo-build.omnicraftlabs.co.kr:8000/GameBuilds";
        private const string Platform = "WIN";

        public string RootDownloadDir { get; }

        private readonly Action<string> _log;

        public GameBuildLauncher(Action<string> logAction)
        {
            _log = logAction;

            RootDownloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BravoGameBuilds"
            );
        }

        public async Task RunAsync(string zipFileName)
        {
            _log("[INFO] 실행 요청: " + zipFileName);

            var buildInfo = ParseBuildFileName(zipFileName);
            _log($"[INFO] 버전: {buildInfo.Version}, CL: {buildInfo.ChangeList}, Config: {buildInfo.Config}");

            string downloadUrl = BuildDownloadUrl(buildInfo);
            _log($"[INFO] 다운로드 URL: {downloadUrl}");

            string buildFolder = Path.Combine(RootDownloadDir, buildInfo.Version, buildInfo.OriginalFileNameWithoutExt);
            string zipPath = Path.Combine(buildFolder, "build.zip");
            string extractDir = Path.Combine(buildFolder, "unpacked");

            Directory.CreateDirectory(buildFolder);

            // 1) ZIP 존재 여부 확인 및 다운로드
            if (File.Exists(zipPath))
            {
                _log("[INFO] 기존 ZIP 파일을 발견했습니다. 다운로드를 생략합니다.");
            }
            else
            {
                _log("[INFO] ZIP 파일이 없습니다. 다운로드를 시작합니다...");
                await DownloadFileAsync(downloadUrl, zipPath);
                _log("[INFO] 다운로드 완료.");
            }

            // 2) 압축 해제 여부 확인 및 수행
            if (Directory.Exists(extractDir) &&
                Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).Length > 0)
            {
                _log("[INFO] 이미 압축이 풀려 있습니다. 압축 해제를 생략합니다.");
            }
            else
            {
                _log("[INFO] 압축 해제를 시작합니다...");
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, recursive: true);
                }
                ZipFile.ExtractToDirectory(zipPath, extractDir);
                _log("[INFO] 압축 해제 완료.");
            }

            // 3) 실행할 exe 찾기
            _log("[INFO] 실행 가능한 EXE 파일을 검색합니다...");
            string? exePath = FindGameExe(extractDir);

            if (exePath == null)
            {
                _log("[ERROR] 실행할 EXE 파일을 찾지 못했습니다.");
                return;
            }

            _log($"[INFO] 실행 파일: {exePath}");
            _log("[INFO] 게임을 실행합니다...");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? extractDir,
                UseShellExecute = true
            };

            Process.Start(psi);

            _log("[INFO] 실행 명령을 보냈습니다.");
        }

        #region Build Info & Parsing

        private class BuildInfo
        {
            public string Version { get; set; } = "";
            public string ChangeList { get; set; } = "";
            public string Config { get; set; } = "";
            public string BuildTime { get; set; } = "";
            public string OriginalFileName { get; set; } = "";
            public string OriginalFileNameWithoutExt { get; set; } = "";
        }

        private BuildInfo ParseBuildFileName(string fileName)
        {
            var nameOnly = Path.GetFileName(fileName);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

            if (string.IsNullOrEmpty(nameOnly))
                throw new ArgumentException("유효하지 않은 파일명입니다.", nameof(fileName));

            // 예: GW_v0.0.1_CL2229_Shipping_20251201220028.zip
            var regex = new Regex(
                @"^GW_v(?<version>\d+\.\d+\.\d+)_CL(?<cl>\d+)_?(?<config>[A-Za-z0-9]+)?_(?<buildtime>\d+)\.zip$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var match = regex.Match(nameOnly);
            if (!match.Success)
            {
                throw new FormatException(
                    $"파일명이 예상한 형식과 다릅니다: {nameOnly}\n예상 예: GW_v0.0.1_CL2229_Shipping_YYYYMMDDHHMMSS.zip");
            }

            return new BuildInfo
            {
                Version = match.Groups["version"].Value,
                ChangeList = match.Groups["cl"].Value,
                Config = match.Groups["config"].Value,
                BuildTime = match.Groups["buildtime"].Value,
                OriginalFileName = nameOnly,
                OriginalFileNameWithoutExt = nameWithoutExt
            };
        }

        private string BuildDownloadUrl(BuildInfo build)
        {
            return $"{BaseUrl}/{build.Version}/{Platform}/{build.OriginalFileName}";
        }

        #endregion

        #region Download & EXE Finder

        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            using var httpClient = new HttpClient();

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            await contentStream.CopyToAsync(fileStream);
        }

        private string? FindGameExe(string rootDir)
        {
            var gwExe = FindFirstExe(rootDir, "GW*.exe");
            if (gwExe != null) return gwExe;

            var allExe = Directory.GetFiles(rootDir, "*.exe", SearchOption.AllDirectories);
            if (allExe.Length > 0)
                return allExe[0];

            return null;
        }

        private string? FindFirstExe(string rootDir, string pattern)
        {
            var files = Directory.GetFiles(rootDir, pattern, SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }

        #endregion
    }
}
