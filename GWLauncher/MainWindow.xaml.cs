using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms; // FolderBrowserDialog
using MessageBox = System.Windows.MessageBox;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text;
using System.Windows.Navigation;

namespace BravoGameLauncherGui
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly GameBuildLauncher _launcher;

        // 서버에서 받은 전체 빌드 목록 (WIN 기준)
        private List<ServerBuildItem> _allBuilds = new();

        private class ServerBuildItem
        {
            public int    BuildNo   { get; set; }               // Jenkins build number
            public string FileName  { get; set; } = string.Empty;
            public string DsFileName { get; set; } = string.Empty;
            public string Config    { get; set; } = string.Empty;
            public string Version   { get; set; } = string.Empty;
            public int    CL        { get; set; }
            public string BuildDate { get; set; } = string.Empty; // yyyy-MM-dd
            public string BuildTime { get; set; } = string.Empty; // HH:mm:ss
            public string DS        { get; set; } = "x";           // O / X
            public DateTime SortKey { get; set; }                 // 내림차순 정렬용
        }

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _launcher = new GameBuildLauncher(AppendLog, _settings.RootDownloadDir);

            // Engine 버전 드롭다운 초기화
            CbEngineVersion.ItemsSource = SupportedEngineVersions;

            // 저장된 선택 버전 반영
            if (!string.IsNullOrWhiteSpace(_settings.SelectedEngineVersion))
                _engineVersion = _settings.SelectedEngineVersion;

            if (!SupportedEngineVersions.Contains(_engineVersion))
                _engineVersion = SupportedEngineVersions.First(); // 안전

            CbEngineVersion.SelectedItem = _engineVersion;
            
            // Engine BasePath 기본값 보정
            if (string.IsNullOrWhiteSpace(_settings.InstalledBuildBasePath))
                _settings.InstalledBuildBasePath = AppSettings.DefaultInstalledBuildBasePath;

            // UI 반영
            ApplyEngineBasePathToUi(_settings.InstalledBuildBasePath);

            // Setup p4 - P4USER 기본 선택: gw_developer
            CbP4UserDeveloper.IsChecked = true;
            CbP4UserEngine.IsChecked = false;
            CbP4UserGuest.IsChecked = false;

            TbP4Workspace.Text = new DirectoryInfo(Environment.CurrentDirectory).Name;

            TxtCachePath.Text = _launcher.RootDownloadDir;
            AppendLog("=== GW Launcher (GUI) ===");
            AppendLog($"캐시 루트 경로: {_launcher.RootDownloadDir}");
            AppendLog(string.Empty);

            CmbBuildType.SelectionChanged += (_, __) => RefreshBuildListUI();
            _engineUiReady = true;
            Loaded += async (_, __) =>
            {
                try { await RefreshFromServerAsync(); }
                catch (Exception ex) { AppendLog("[ERROR] 초기 서버 로드 실패: " + ex.Message); }
            };
        }

        private void RefreshBuildListUI()
        {
            if (_allBuilds == null || _allBuilds.Count == 0)
            {
                LvBuilds.ItemsSource = null;
                return;
            }

            string selectedType = GetSelectedBuildType();

            var filtered = _allBuilds
                .Where(b => string.Equals(selectedType, "All", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(b.Config, selectedType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(b => b.SortKey)
                .ToList();

            if (filtered.Count == 0)
            {
                LvBuilds.ItemsSource = null;
                AppendLog($"[WARN] 서버 빌드 리스트 중 '{selectedType}' 타입 빌드가 없습니다.");
                return;
            }

            LvBuilds.ItemsSource = filtered;
            LvBuilds.SelectedIndex = 0;

            AppendLog($"[INFO] '{selectedType}' 타입 빌드 {filtered.Count}개 표시.");
        }

        private string GetSelectedBuildType()
        {
            if (CmbBuildType?.SelectedItem is ComboBoxItem cbi &&
                cbi.Content is string content &&
                !string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
            return "All";
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            await RunSelectedBuildAsync();
        }

        /// <summary>지정한 TextBox에 로그 메시지를 추가하고 맨 아래로 스크롤합니다.</summary>
        private void AppendToLog(System.Windows.Controls.TextBox textBox, string message)
        {
            if (textBox == null) return;
            Dispatcher.Invoke(() =>
            {
                textBox.AppendText(message + Environment.NewLine);
                textBox.ScrollToEnd();
            });
        }

        private void AppendLog(string message) => AppendToLog(TxtLog, message);
        private void AppendEngineLog(string message) => AppendToLog(TxtEngineLog, message);
        private void AppendSetupP4Log(string message) => AppendToLog(TxtSetupP4Log, message);           

        private void MenuChangeCachePath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "게임 빌드 캐시를 저장할 폴더를 선택하세요.",
                SelectedPath = _launcher.RootDownloadDir,
                ShowNewFolderButton = true
            };

            var result = dialog.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK) return;

            string newPath = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(newPath)) return;

            _settings.RootDownloadDir = newPath;
            _settings.Save();

            _launcher.ChangeRootDownloadDir(newPath);

            TxtCachePath.Text = newPath;
            AppendLog($"[INFO] 캐시 경로가 변경되었습니다 → {newPath}");
        }

        private void MenuClearCache_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"캐시 폴더 전체를 삭제합니다.\n\n경로: {_launcher.RootDownloadDir}\n\n계속하시겠습니까?",
                "캐시 삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (Directory.Exists(_launcher.RootDownloadDir))
                {
                    Directory.Delete(_launcher.RootDownloadDir, recursive: true);
                    AppendLog("[INFO] 캐시 폴더를 삭제했습니다.");
                }
                else
                {
                    AppendLog("[INFO] 삭제할 캐시 폴더가 없습니다.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 캐시 삭제 중 오류가 발생했습니다.");
                AppendLog(ex.Message);
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>파일 메뉴 - 런처가 설치된 폴더를 탐색기로 연다.</summary>
        private void MenuOpenLauncherFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? dir = Path.GetDirectoryName(Environment.ProcessPath);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    dir = AppContext.BaseDirectory;
                if (string.IsNullOrWhiteSpace(dir))
                {
                    MessageBox.Show("런처 실행 경로를 확인할 수 없습니다.", "경로 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"탐색기 열기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>GameStarter 탭 - 로컬 캐시 경로를 탐색기로 연다.</summary>
        private void BtnOpenCachePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = _launcher.RootDownloadDir;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] 캐시 경로 열기 실패: {ex.Message}");
                MessageBox.Show($"탐색기 열기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRefreshFromServer_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFromServerAsync();
        }

        // ====== (1번 기능 핵심) 서버 목록 로드 + DS 존재 여부 계산 ======
        private async Task RefreshFromServerAsync()
        {
            if (BtnRefreshFromServer != null)
                BtnRefreshFromServer.IsEnabled = false;

            AppendLog("[INFO] 서버에서 빌드 리스트를 가져오는 중...");

            try
            {
                var result = await BuildListService.FetchBuildListAsync();
                if (result == null || result.Platforms == null || result.Platforms.Count == 0)
                {
                    AppendLog("[WARN] 서버에서 가져온 빌드 정보가 없습니다.");
                    _allBuilds.Clear();
                    RefreshBuildListUI();
                    return;
                }

                // 클라이언트(WIN) / DS 목록 추출
                var winBuilds = result.Platforms.TryGetValue("WIN", out var win) ? win.Builds : new List<BuildItem>();
                var dsBuilds  = result.Platforms.TryGetValue("DS", out var ds)  ? ds.Builds  : new List<BuildItem>();

                if (winBuilds == null || winBuilds.Count == 0)
                {
                    AppendLog("[WARN] 서버 WIN 빌드 리스트가 비어 있습니다.");
                    _allBuilds.Clear();
                    RefreshBuildListUI();
                    return;
                }

                // DS 존재 여부 빠른 조회용.
                // 1) Jenkins BuildNo 매칭 우선: 클라이언트/DS Config가 달라도 같은 빌드 번호면 DS 존재로 판단.
                // 2) 레거시 fallback: 파일명 기반(baseName + "_DS") 매칭.
                var dsByBuildNo = dsBuilds
                    .Where(b => b.JenkinsBuildNumber > 0 && !string.IsNullOrWhiteSpace(b.FileName))
                    .GroupBy(b => b.JenkinsBuildNumber)
                    .ToDictionary(g => g.Key, g => g.First().FileName);

                var dsNameSet = new HashSet<string>(
                    dsBuilds
                        .Where(b => !string.IsNullOrWhiteSpace(b.FileName))
                        .Select(b => Path.GetFileNameWithoutExtension(b.FileName)),
                    StringComparer.OrdinalIgnoreCase
                );

                // WIN: buildTime 기준 내림차순
                winBuilds.Sort((a, b) =>
                {
                    var ta = a.BuildTime ?? DateTime.MinValue;
                    var tb = b.BuildTime ?? DateTime.MinValue;
                    return tb.CompareTo(ta);
                });

                _allBuilds = new List<ServerBuildItem>();

                foreach (var item in winBuilds)
                {
                    if (string.IsNullOrWhiteSpace(item.FileName))
                        continue;

                    var parse = ParseBuildInfoFromFileName(item.FileName);
                    var dt = item.BuildTime ?? parse.Timestamp ?? DateTime.MinValue;

                    // DS 매칭 (파일명 기반)
                    var baseName = Path.GetFileNameWithoutExtension(item.FileName);
                    var expectedDsBaseName = baseName + "_DS";
                    bool hasDs = (item.JenkinsBuildNumber > 0 && dsByBuildNo.ContainsKey(item.JenkinsBuildNumber))
                              || dsNameSet.Contains(expectedDsBaseName);
                    string matchedDsFileName = (item.JenkinsBuildNumber > 0 && dsByBuildNo.TryGetValue(item.JenkinsBuildNumber, out var byNo))
                        ? byNo
                        : (dsNameSet.Contains(expectedDsBaseName) ? expectedDsBaseName + ".zip" : string.Empty);

                    _allBuilds.Add(new ServerBuildItem
                    {
                        BuildNo   = item.JenkinsBuildNumber,
                        FileName  = item.FileName,
                        DsFileName = matchedDsFileName,
                        Config    = !string.IsNullOrWhiteSpace(item.Config) ? item.Config : GetBuildConfigFromFileName(item.FileName),
                        Version   = !string.IsNullOrWhiteSpace(item.Version) ? item.Version : (parse.Version ?? string.Empty),
                        CL        = item.Cl != 0 ? item.Cl : parse.CL,
                        BuildDate = dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd"),
                        BuildTime = dt == DateTime.MinValue ? "" : dt.ToString("HH:mm:ss"),
                        DS        = hasDs ? "O" : "X",
                        SortKey   = dt
                    });
                }

                AppendLog($"[INFO] 서버 빌드 리스트 {_allBuilds.Count}개 로드 완료.");
                RefreshBuildListUI();
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 서버 빌드 리스트 로드 실패.");
                AppendLog(ex.Message);
            }
            finally
            {
                if (BtnRefreshFromServer != null)
                    BtnRefreshFromServer.IsEnabled = true;
            }
        }

        // ====== 파일명 파싱 유틸 (기존 유지 + DS/확장자 유연화) ======
        private static (string Version, int CL, DateTime? Timestamp) ParseBuildInfoFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return (string.Empty, 0, null);

            // 예:
            //  - GW_v0.0.1_CL2301_Shipping_20251205123010.zip
            //  - GW_v0.0.1_CL2301_Shipping_20251205123010_DS.zip
            //  - (확장자 없는 케이스도 대비) ..._20251205123010_DS
            var pattern = @"^GW_v(?<ver>\d+\.\d+\.\d+)_CL(?<cl>\d+)_.*_(?<ts>\d{14})(?:_DS)?(?:\.zip)?$";
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

        private static string GetBuildConfigFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "Unknown";

            if (fileName.IndexOf("_Shipping_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Shipping";

            if (fileName.IndexOf("_Development_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Development";

            return "Unknown";
        }

        private static ProcessStartInfo CreateProcessStartInfo(string fileName, string arguments, string? workingDirectory)
        {
            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
        }

        private async Task<int> RunProcessAsync(
            string fileName,
            string arguments,
            Action<string> log,
            string? workingDirectory = null)
        {
            try
            {
                var psi = CreateProcessStartInfo(fileName, arguments, workingDirectory);
                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) log(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) log("[ERR] " + e.Data); };

                if (!proc.Start())
                    throw new InvalidOperationException("프로세스를 시작할 수 없습니다.");

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await proc.WaitForExitAsync();
                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                // (정책 3번) 실행 자체가 불가한 치명 오류 → 팝업 + 로그
                log("[FATAL] 프로세스 실행 실패: " + ex.Message);
                MessageBox.Show(
                    $"프로세스 실행 실패\n\n{fileName} {arguments}\n\n{ex.Message}",
                    "실행 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return -1;
            }
        }

        private async void BtnSetupP4Apply_Click(object sender, RoutedEventArgs e)
        {
            BtnSetupP4Apply.IsEnabled = false;

            try
            {
                TxtSetupP4Log.Clear();

                string ws = (TbP4Workspace.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(ws))
                {
                    // 입력 누락은 사용자 실수이므로: 로그 + 팝업(친절)
                    AppendSetupP4Log("[WARN] Workspace 이름을 입력하세요.");
                    MessageBox.Show("Workspace(P4CLIENT) 이름을 입력하세요.", "입력 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                AppendSetupP4Log("=== setup_p4 시작 ===");
                AppendSetupP4Log($"Workspace(P4CLIENT): {ws}");
                AppendSetupP4Log("");

                // 배치와 동일한 설정(값 그대로)
                // p4 set P4IGNORE=.p4ignore
                // p4 set P4CHARSET=utf8
                // p4 set P4PORT=bravo-repo.omnicraftlabs.co.kr:1666
                // p4 set P4CLIENT=<workspace>
                // p4 set (확인 출력)

                int code;

                code = await RunProcessAsync("p4", "set P4IGNORE=.p4ignore", AppendSetupP4Log);
                if (code != 0) AppendSetupP4Log($"[WARN] ExitCode={code}");

                code = await RunProcessAsync("p4", "set P4CHARSET=utf8", AppendSetupP4Log);
                if (code != 0) AppendSetupP4Log($"[WARN] ExitCode={code}");

                var p4user = GetSelectedP4User();
                AppendSetupP4Log($"P4USER: {p4user}");
                code = await RunProcessAsync("p4", $"set P4USER={p4user}", AppendSetupP4Log);
                if (code != 0) AppendSetupP4Log($"[WARN] ExitCode={code}");

                code = await RunProcessAsync("p4", "set P4PORT=bravo-repo.omnicraftlabs.co.kr:1666", AppendSetupP4Log);
                if (code != 0) AppendSetupP4Log($"[WARN] ExitCode={code}");

                code = await RunProcessAsync("p4", $"set P4CLIENT={ws}", AppendSetupP4Log);
                if (code != 0) AppendSetupP4Log($"[WARN] ExitCode={code}");

                AppendSetupP4Log("");
                AppendSetupP4Log("===== Perforce 환경 변수 확인 =====");

                code = await RunProcessAsync("p4", "set", AppendSetupP4Log);
                if (code != 0) AppendSetupP4Log($"[WARN] ExitCode={code}");

                AppendSetupP4Log("===== P4 Info 확인 =====");

                code = await RunProcessAsync("p4", "info", AppendSetupP4Log);
                if (code != 0) AppendSetupP4Log($"[WARN] ExitCode={code}");

                AppendSetupP4Log("==================================");
                AppendSetupP4Log("=== setup_p4 완료 ===");
            }
            finally
            {
                BtnSetupP4Apply.IsEnabled = true;
            }
        }

        /// <summary>P4 사용자 체크박스 단일 선택(라디오처럼 동작) 처리.</summary>
        private void OnP4UserChecked(System.Windows.Controls.CheckBox current, System.Windows.Controls.CheckBox other1, System.Windows.Controls.CheckBox other2)
        {
            if (current.IsChecked == true)
            {
                other1.IsChecked = false;
                other2.IsChecked = false;
            }
            else
            {
                if (other1.IsChecked != true && other2.IsChecked != true)
                    current.IsChecked = true;
            }
        }

        private void CbP4UserDeveloper_Click(object sender, RoutedEventArgs e) => OnP4UserChecked(CbP4UserDeveloper, CbP4UserEngine, CbP4UserGuest);
        private void CbP4UserEngine_Click(object sender, RoutedEventArgs e) => OnP4UserChecked(CbP4UserEngine, CbP4UserDeveloper, CbP4UserGuest);
        private void CbP4UserGuest_Click(object sender, RoutedEventArgs e) => OnP4UserChecked(CbP4UserGuest, CbP4UserDeveloper, CbP4UserEngine);

        private string GetSelectedP4User()
        {
            // 체크된 체크박스(Tag)를 그대로 사용 (XAML이 단일 소스)
            if (CbP4UserDeveloper.IsChecked == true) return CbP4UserDeveloper.Tag?.ToString() ?? "gw_developer";
            if (CbP4UserEngine.IsChecked == true) return CbP4UserEngine.Tag?.ToString() ?? "gw_engine";
            if (CbP4UserGuest.IsChecked == true) return CbP4UserGuest.Tag?.ToString() ?? "gw_guest";

            // 혹시 모를 예외 케이스(전부 해제 방지 로직이 있어도 안전장치)
            return "gw_developer";
        }

        private void AppendGWEditorLog(string message) => AppendToLog(TxtGWEditorLog, message);

        private static (string clientName, string clientRoot, string clientStream) ParseP4ZtagInfo(string stdout)
        {
            string name = "", root = "", stream = "";
            if (string.IsNullOrWhiteSpace(stdout)) return (name, root, stream);
            foreach (var line in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("... clientName ", StringComparison.OrdinalIgnoreCase))
                    name = line.Substring("... clientName ".Length).Trim();
                else if (line.StartsWith("... clientRoot ", StringComparison.OrdinalIgnoreCase))
                    root = line.Substring("... clientRoot ".Length).Trim();
                else if (line.StartsWith("... clientStream ", StringComparison.OrdinalIgnoreCase))
                    stream = line.Substring("... clientStream ".Length).Trim();
            }
            return (name, root, stream);
        }

        private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessCaptureAsync(
            string fileName,
            string arguments,
            string? workingDirectory = null,
            Action<string>? onFatalLog = null)
        {
            try
            {
                var psi = CreateProcessStartInfo(fileName, arguments, workingDirectory);
                using var proc = new Process { StartInfo = psi };
                if (!proc.Start())
                    throw new InvalidOperationException("프로세스를 시작할 수 없습니다.");

                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                return (proc.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                onFatalLog?.Invoke("[FATAL] 프로세스 실행 실패: " + ex.Message);
                MessageBox.Show(
                    $"프로세스 실행 실패\n\n{fileName} {arguments}\n\n{ex.Message}",
                    "실행 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return (-1, "", ex.ToString());
            }
        }

        private async Task RefreshGWEditorP4InfoAsync()
        {
            TxtGWEditorLog.Clear();
            AppendGWEditorLog("=== GWEditor: Workspace / Project / Editor 경로 확인 ===");

            var (exit, stdout, stderr) = await RunProcessCaptureAsync("p4", "-ztag info", onFatalLog: AppendGWEditorLog);
            if (exit != 0)
            {
                AppendGWEditorLog($"[WARN] p4 -ztag info 실패 (ExitCode={exit})");
                if (!string.IsNullOrWhiteSpace(stderr))
                    AppendGWEditorLog(stderr.Trim());

                TbGWEditorWorkspace.Text = "";
                TbGWEditorProjectPath.Text = "";
                TbGWEditorEditorExe.Text = "";
                SetGWEditorSyncStatus("", 0);
                return;
            }

            var (ws, clientRoot, _) = ParseP4ZtagInfo(stdout);
            TbGWEditorWorkspace.Text = ws;

            // Project(.uproject) 경로는 항상 P4 clientRoot 기준
            string uproject = Path.Combine(clientRoot, "GW", "GW.uproject");
            TbGWEditorProjectPath.Text = uproject;

            // Editor exe 경로는 Engine 탭 InstallRoot 기준 (Installed Build)
            string engineInstallRoot = TbEngineInstallRoot.Text?.Trim() ?? "";
            string editorExe = Path.Combine(engineInstallRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
            TbGWEditorEditorExe.Text = editorExe;

            AppendGWEditorLog($"Workspace: {ws}");
            AppendGWEditorLog($"Project: {uproject}");
            AppendGWEditorLog($"Editor : {editorExe}");

            // p4 sync 필요 여부 + Local CL / GW_ProjectBuild CL 표시 및 버튼 활성화
            _gwEditorClientRoot = clientRoot;
            var (localCL, buildCL, needSync, status, statusKind) = await QueryP4SyncClStateAsync(ws, clientRoot, log: null);
            SetGWEditorSyncStatus(status, statusKind);
            SetGWEditorClAndButtons(localCL, buildCL, needSync);
        }

        /// <param name="statusKind">0=없음(회색), 1=동기화필요(빨강), 2=동일(초록), 3=주의(주황)</param>
        private void SetGWEditorSyncStatus(string statusText, int statusKind)
        {
            Dispatcher.Invoke(() =>
            {
                if (TbGWEditorSyncStatus != null)
                    TbGWEditorSyncStatus.Text = statusText ?? "";
                if (GWEditorSyncStatusIndicator != null)
                {
                    GWEditorSyncStatusIndicator.Fill = statusKind switch
                    {
                        1 => System.Windows.Media.Brushes.Crimson,
                        2 => System.Windows.Media.Brushes.MediumSeaGreen,
                        3 => System.Windows.Media.Brushes.Orange,
                        _ => System.Windows.Media.Brushes.Gray
                    };
                }
            });
        }

        /// <summary>GWEditor 통합 탭: Local CL / Build CL 표시, Sync / Local Rollback 버튼 활성화.</summary>
        private void SetGWEditorClAndButtons(int localCL, int buildCL, bool needSync)
        {
            Dispatcher.Invoke(() =>
            {
                if (TbGWEditorLocalCL != null)
                    TbGWEditorLocalCL.Text = localCL > 0 ? localCL.ToString() : "0";
                if (TbGWEditorBuildCL != null)
                    TbGWEditorBuildCL.Text = buildCL > 0 ? buildCL.ToString() : "-";
                if (BtnGWEditorSync != null)
                    BtnGWEditorSync.IsEnabled = needSync && buildCL > 0;
                if (BtnGWEditorLocalRollback != null)
                    BtnGWEditorLocalRollback.IsEnabled = buildCL > 0 && localCL > buildCL;
            });
        }

        private string? _gwEditorClientRoot;


        private async void BtnGWEditorRefresh_Click(object sender, RoutedEventArgs e)
        {
            BtnGWEditorRefresh.IsEnabled = false;
            try { await RefreshGWEditorP4InfoAsync(); }
            finally { BtnGWEditorRefresh.IsEnabled = true; }
        }
       
        private void BtnRunGWEditor_Click(object sender, RoutedEventArgs e)
        {
            // UI에 표시된 "프로젝트 파일 경로"를 기준으로 실행
            string uproject = TbGWEditorProjectPath.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(uproject) || !File.Exists(uproject))
            {
                AppendGWEditorLog("[WARN] 프로젝트 파일(GW.uproject) 경로가 유효하지 않습니다: " + uproject);
                MessageBox.Show(
                    "프로젝트 파일(GW.uproject)을 찾을 수 없습니다.\n\n" + uproject +
                    "\n\n[새로고침] 후 다시 시도하세요.",
                    "실행 불가",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // UI에 표시된 "에디터 실행 파일 경로"를 기준으로 실행
            string editorExe = TbGWEditorEditorExe.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(editorExe) || !File.Exists(editorExe))
            {
                AppendGWEditorLog("[WARN] UnrealEditor.exe 경로가 유효하지 않습니다: " + editorExe);
                MessageBox.Show(
                    "UnrealEditor.exe를 찾을 수 없습니다.\n\n" + editorExe +
                    "\n\nEngine 탭에서 Installed Build 설치/경로를 확인하세요.",
                    "실행 불가",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string opts = (TbGWEditorArgs?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(opts)) opts = DefaultGWEditorArgs;
            string args = $"\"{uproject}\" {opts}";

            AppendGWEditorLog("=== UnrealEditor 실행 요청 ===");
            AppendGWEditorLog("Editor : " + editorExe);
            AppendGWEditorLog("Project: " + uproject);
            AppendGWEditorLog("Args   : " + args);

            try
            {
                var workDir = Path.GetDirectoryName(editorExe);
                if (string.IsNullOrWhiteSpace(workDir))
                    workDir = Environment.CurrentDirectory;

                Process.Start(new ProcessStartInfo
                {
                    FileName = editorExe,
                    Arguments = args,
                    WorkingDirectory = workDir,
                    UseShellExecute = true,
                });

                AppendGWEditorLog("실행 요청 완료.");
            }
            catch (Exception ex)
            {
                AppendGWEditorLog("[FATAL] 실행 실패: " + ex.Message);
                MessageBox.Show($"UnrealEditor 실행 실패\n\n{ex.Message}", "실행 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private const string DefaultGWEditorArgs = "-nocompile";

        /// <summary>DS 실행 옵션 멀티라인에서 줄바꿈만 공백으로 합칩니다(프로세스 인자는 한 줄).</summary>
        private static string NormalizeMultilineLauncherArgs(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var t = text.Trim();
            return t.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        }

        private void BtnGWEditorArgsReset_Click(object sender, RoutedEventArgs e)
        {
            TbGWEditorArgs.Text = DefaultGWEditorArgs;
        }

        private void BtnGameStarterArgsReset_Click(object sender, RoutedEventArgs e)
        {
            TbGameStarterArgs.Text = string.Empty;
        }

        private void BtnGameStarterDsArgsReset_Click(object sender, RoutedEventArgs e)
        {
            TbGameStarterDsArgs.Text = GameBuildLauncher.DefaultDedicatedServerArgs;
        }

        private bool _gwEditorRefreshing = false;

        private async void MainTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 탭 내부 컨트롤에서도 버블링될 수 있어 TabControl에서 발생한 것만 처리
            if (!ReferenceEquals(e.OriginalSource, sender))
                return;

            if (sender is not System.Windows.Controls.TabControl tc)
                return;

            if (tc.SelectedItem is not TabItem tab)
                return;

            string header = tab.Header?.ToString() ?? "";

            if (header == "GWEditor")
            {
                await AutoRefreshGWEditorAsync();
                return;
            }

            if (header == "Engine")
            {
                await AutoRefreshEngineAsync();
                return;
            }

        }

        private async Task AutoRefreshEngineAsync()
        {
            if (_engineRefreshing) return;
            _engineRefreshing = true;

            try
            {
                if (BtnEngineRefresh != null) BtnEngineRefresh.IsEnabled = false;
                await RefreshEngineStatusAsync();
            }
            finally
            {
                if (BtnEngineRefresh != null) BtnEngineRefresh.IsEnabled = true;
                _engineRefreshing = false;
            }
        }

        private async Task AutoRefreshGWEditorAsync()
        {
            if (_gwEditorRefreshing)
                return;

            _gwEditorRefreshing = true;
            try
            {
                // 버튼은 유지하되, 자동 갱신 중엔 잠깐 비활성(선택)
                if (BtnGWEditorRefresh != null) BtnGWEditorRefresh.IsEnabled = false;
                if (BtnRunGWEditor != null) BtnRunGWEditor.IsEnabled = false;

                await RefreshGWEditorP4InfoAsync();

                if (BtnRunGWEditor != null) BtnRunGWEditor.IsEnabled = true;
            }
            finally
            {
                if (BtnGWEditorRefresh != null) BtnGWEditorRefresh.IsEnabled = true;
                _gwEditorRefreshing = false;
            }
        }

        // ========================
        // GWEditor 통합 탭: CL 표시/버튼 제어 (QueryP4SyncClStateAsync 공용)
        // ========================
        private const string P4SYNC_TARGET_DEPOT   = "//GW/dev/...";
        private const string P4SYNC_JENKINS_USER   = "gw_build";
        private const string P4SYNC_JENKINS_CLIENT = "jenkins-Agent-Win-GW_ProjectBuild";
        private const string P4SYNC_TAG            = "#JenkinsBuild";

        /// <summary>
        /// Run 버튼에서 쓰는 방식 그대로: local 최신 CL 조회 + GW_ProjectBuild CL 조회/태그 스캔 + 비교.
        /// 단, 여기서는 "sync 실행"은 하지 않고 "표시/판단"만 한다.
        /// </summary>
        private async Task<(int localCL, int buildCL, bool needSync, string status, int statusKind)> QueryP4SyncClStateAsync(
            string ws, string root, Action<string>? log = null)
        {
            void LogMsg(string msg) => log?.Invoke(msg);

            if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(root))
                return (0, -1, false, "Workspace/Client Root 확인 불가", 0);

            // [1] 로컬 최신 CL
            LogMsg("");
            LogMsg("[INFO] Local CL 조회 중...");
            var (exitLocal, outLocal, errLocal) = await RunProcessCaptureAsync("p4", $"changes -m1 @{ws}", root);

            int localCL = 0;
            if (exitLocal != 0)
            {
                LogMsg($"[WARN] 로컬 최신 CL 조회 실패 (ExitCode={exitLocal}). LOCAL_CL=0 가정");
                if (!string.IsNullOrWhiteSpace(errLocal)) LogMsg("[ERR] " + errLocal.Trim());
                localCL = 0;
            }
            else
            {
                localCL = ParseChangeNumber(outLocal);
                if (localCL < 0) localCL = 0;
            }
            LogMsg($"- Local CL: {localCL}");

            // [2] GW_ProjectBuild CL scan (최근 5개)
            LogMsg("");
            LogMsg("[INFO] GW_ProjectBuild CL scan (최근 5개)...");
            var (exitCandidates, outCandidates, errCandidates) =
                await RunProcessCaptureAsync("p4", $"changes -u {P4SYNC_JENKINS_USER} -c {P4SYNC_JENKINS_CLIENT} -m5 {P4SYNC_TARGET_DEPOT}", root);

            if (exitCandidates != 0)
            {
                LogMsg($"[WARN] GW_ProjectBuild CL 조회 실패 (ExitCode={exitCandidates})");
                if (!string.IsNullOrWhiteSpace(errCandidates)) LogMsg("[ERR] " + errCandidates.Trim());
                return (localCL, -1, false, "GW_ProjectBuild CL 조회 실패", 0);
            }

            var candidateCLs = new List<int>();
            foreach (var line in outCandidates.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int cl = ParseChangeNumber(line);
                if (cl > 0) candidateCLs.Add(cl);
            }

            if (candidateCLs.Count == 0)
            {
                LogMsg("[WARN] GW_ProjectBuild CL이 없습니다.");
                return (localCL, -1, false, "GW_ProjectBuild CL 없음", 0);
            }

            int buildCL = -1;

            foreach (var cl in candidateCLs)
            {
                var (exitDesc, outDesc, errDesc) = await RunProcessCaptureAsync("p4", $"describe -s {cl}", root);
                if (exitDesc != 0)
                {
                    LogMsg($"[WARN] GW_ProjectBuild describe 실패 (CL={cl}, ExitCode={exitDesc})");
                    if (!string.IsNullOrWhiteSpace(errDesc)) LogMsg("[ERR] " + errDesc.Trim());
                    continue;
                }

                if (outDesc.IndexOf(P4SYNC_TAG, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    buildCL = cl;
                    break;
                }
                else
                {
                    LogMsg($"[DEBUG] 태그 없음 (CL={cl})");
                }
            }

            if (buildCL <= 0)
            {
                LogMsg($"[WARN] 최근 5개 CL에서 태그({P4SYNC_TAG})를 찾지 못했습니다.");
                return (localCL, -1, false, "태그 탐지 실패", 0);
            }

            LogMsg($"[INFO] GW_ProjectBuild CL: {buildCL}");

            // [3] 비교 (statusKind: 1=동기화필요, 2=동일, 3=주의)
            bool needSync = buildCL > localCL;
            string status;
            int statusKind;
            if (needSync)
            {
                status = "배포된 프로젝트 빌드가 있습니다. 동기화 필요 합니다.";
                statusKind = 1;
            }
            else if (buildCL == localCL)
            {
                status = "안정된 CL 상태입니다. 동기화 필요 없습니다.";
                statusKind = 2;
            }
            else
            {
                status = "최신 프로젝트 빌드이나 Editor 실행 시 에러 발생할 수 있음.\n에러 시 Local Rollback 또는 #bravo_all 채널로 Project 빌드 요청.";
                statusKind = 3;
            }

            LogMsg($"[INFO] 비교 결과: {status} (Local={localCL}, Build={buildCL})");
            return (localCL, buildCL, needSync, status, statusKind);
        }

        private async void BtnGWEditorSync_Click(object sender, RoutedEventArgs e)
        {
            BtnGWEditorSync.IsEnabled = false;
            BtnGWEditorRefresh.IsEnabled = false;

            try
            {
                TxtGWEditorLog.Clear();
                string ws = (TbGWEditorWorkspace.Text ?? "").Trim();
                string root = _gwEditorClientRoot ?? "";

                if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(root))
                {
                    AppendGWEditorLog("[WARN] Workspace/Client Root를 확인할 수 없습니다. [새로고침] 후 다시 시도하세요.");
                    MessageBox.Show("Workspace/Client Root를 확인할 수 없습니다.\n\n[새로고침] 후 다시 시도하세요.",
                        "실행 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var confirm = MessageBox.Show(
                    $"아래 워크스페이스 기준으로 Sync를 진행합니다.\n\nWorkspace: {ws}\nClient Root: {root}\n\n진행할까요?",
                    "p4 sync 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    AppendGWEditorLog("[INFO] 사용자가 취소했습니다.");
                    return;
                }

                AppendGWEditorLog("=== p4 sync 시작 ===");
                var (localCL, buildCL, needSync, status, _) = await QueryP4SyncClStateAsync(ws, root, AppendGWEditorLog);

                if (!needSync || buildCL <= 0)
                {
                    if (buildCL > 0 && !needSync)
                        AppendGWEditorLog($"[INFO] 로컬 CL({localCL})이 최신 GW_ProjectBuild CL({buildCL})보다 새롭거나 같아 sync 생략.");
                    return;
                }

                AppendGWEditorLog("");
                AppendGWEditorLog($"[4/4] p4 sync ...@{buildCL} 실행");
                int code = await RunProcessAsync("p4", $"sync ...@{buildCL}", AppendGWEditorLog, root);
                if (code != 0)
                {
                    AppendGWEditorLog($"[ERROR] p4 sync 실패 (ExitCode={code})");
                    return;
                }

                AppendGWEditorLog("");
                AppendGWEditorLog($"[OK] 로컬 워크스페이스가 최신 GW_ProjectBuild CL {buildCL} 까지 동기화되었습니다.");
                AppendGWEditorLog("=== p4 sync 완료 ===");
            }
            finally
            {
                BtnGWEditorRefresh.IsEnabled = true;
                try { await RefreshGWEditorP4InfoAsync(); }
                catch { if (BtnGWEditorSync != null) BtnGWEditorSync.IsEnabled = false; }
            }
        }

        private async void BtnGWEditorLocalRollback_Click(object sender, RoutedEventArgs e)
        {
            string buildCLText = (TbGWEditorBuildCL?.Text ?? "").Trim();
            if (!int.TryParse(buildCLText, out int buildCL) || buildCL <= 0)
            {
                AppendGWEditorLog("[WARN] GW_ProjectBuild CL이 유효하지 않습니다. [새로고침] 후 다시 시도하세요.");
                return;
            }

            string root = _gwEditorClientRoot ?? "";
            if (string.IsNullOrWhiteSpace(root))
            {
                AppendGWEditorLog("[WARN] Client Root를 확인할 수 없습니다. [새로고침] 후 다시 시도하세요.");
                MessageBox.Show("Client Root를 확인할 수 없습니다.\n\n[새로고침] 후 다시 시도하세요.", "실행 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string localCLText = (TbGWEditorLocalCL?.Text ?? "0").Trim();
            int localCL = int.TryParse(localCLText, out int lcl) ? lcl : 0;
            if (localCL <= buildCL)
            {
                AppendGWEditorLog("[WARN] Local Rollback은 Local CL이 GW_ProjectBuild CL보다 클 때만 사용할 수 있습니다.");
                return;
            }

            var confirm = MessageBox.Show(
                $"로컬 워크스페이스를 GW_ProjectBuild CL {buildCL} 상태로 되돌립니다.\n\n열려 있는 파일이 있으면 진행이 되지 않습니다.\n\n진행할까요?",
                "Local Rollback 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                AppendGWEditorLog("[INFO] 사용자가 취소했습니다.");
                return;
            }

            BtnGWEditorLocalRollback.IsEnabled = false;
            BtnGWEditorRefresh.IsEnabled = false;

            try
            {
                AppendGWEditorLog($"=== Local Rollback: p4 sync //...@{buildCL} ===");
                int code = await RunProcessAsync("p4", $"sync //...@{buildCL}", AppendGWEditorLog, root);
                if (code != 0)
                    AppendGWEditorLog($"[ERROR] p4 sync 실패 (ExitCode={code})");
                else
                    AppendGWEditorLog("=== Local Rollback 완료 ===");
            }
            finally
            {
                BtnGWEditorRefresh.IsEnabled = true;
                try { await RefreshGWEditorP4InfoAsync(); }
                catch { }
            }
        }

        private static int ParseChangeNumber(string p4ChangesOutput)
        {
            // 일반적으로: "Change 12345 on ..."
            if (string.IsNullOrWhiteSpace(p4ChangesOutput))
                return -1;

            foreach (var line in p4ChangesOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Change ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int cl))
                        return cl;
                }
            }
            return -1;
        }

        private async Task RunSelectedBuildAsync()
        {
            if (LvBuilds.SelectedItem is not ServerBuildItem selected)
            {
                AppendLog("[WARN] 실행할 빌드를 선택하세요.");
                return;
            }

            bool runClient = CbRunClient.IsChecked == true;
            bool runDS = CbRunDS.IsChecked == true;

            if (!runClient && !runDS)
            {
                AppendLog("[WARN] 클라이언트 또는 DS 중 하나 이상을 선택한 뒤 게임 실행을 눌러주세요.");
                MessageBox.Show("클라이언트 또는 DS 중 하나 이상을 선택한 뒤 실행해주세요.", "실행 대상 선택", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 게임 실행 전까지 버튼 비활성화 (중복 실행 방지)
            BtnRun.IsEnabled = false;

            try
            {
                bool useWindowed = CbWindowed.IsChecked == true;
                string winZip = selected.FileName;
                string dsZip = !string.IsNullOrWhiteSpace(selected.DsFileName)
                    ? selected.DsFileName
                    : Path.GetFileNameWithoutExtension(winZip) + "_DS.zip";
                string clientArgs = (TbGameStarterArgs?.Text ?? "").Trim();

                string dsArgs = NormalizeMultilineLauncherArgs(TbGameStarterDsArgs?.Text);
                if (string.IsNullOrWhiteSpace(dsArgs)) dsArgs = GameBuildLauncher.DefaultDedicatedServerArgs;

                if (runClient && runDS)
                {
                    await _launcher.RunLocalWithDedicatedServerAsync(winZip, dsZip, useWindowed, ReportGameStarterProgress, clientArgs, dsArgs);
                }
                else if (runClient)
                {
                    await _launcher.RunLocalClientOnlyAsync(winZip, useWindowed, ReportGameStarterProgress, clientArgs);
                }
                else
                {
                    await _launcher.RunDedicatedServerAsync(dsZip, ReportGameStarterProgress, dsArgs);
                }
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 게임 실행 중 오류가 발생했습니다.");
                AppendLog(ex.Message);
                MessageBox.Show(
                    $"게임 실행 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "실행 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                BtnRun.IsEnabled = true;
                SetGameStarterProgress(false, 0, null);
            }
        }

        /// <summary>Engine 탭과 동일한 방식으로 GameStarter 진행율 표시.</summary>
        private void ReportGameStarterProgress(double percent, string? message)
        {
            Dispatcher.Invoke(() =>
            {
                if (PbGameStarter != null)
                {
                    PbGameStarter.IsIndeterminate = double.IsNaN(percent) || percent < 0;
                    if (!PbGameStarter.IsIndeterminate)
                        PbGameStarter.Value = Math.Min(100, Math.Max(0, percent));
                }
                if (TxtGameStarterProgress != null)
                    TxtGameStarterProgress.Text = message ?? "";
            });
        }

        private void SetGameStarterProgress(bool indeterminate, double value, string? text)
        {
            Dispatcher.Invoke(() =>
            {
                if (PbGameStarter != null)
                {
                    PbGameStarter.IsIndeterminate = indeterminate;
                    PbGameStarter.Value = value;
                }
                if (TxtGameStarterProgress != null)
                    TxtGameStarterProgress.Text = text ?? "";
            });
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        // Engine(Installed Build) 버전 
        private string _engineVersion = "UE5.6";
        private static readonly string[] SupportedEngineVersions = { "UE5.6"/*, "UE5.7" 등 추가 */ };
        private bool _engineUiReady = false;

        // Engine 탭 상태
        private InstalledBuildLatest? _engineLatest;
        private InstalledBuildMeta? _engineLocalMeta;
        private bool _engineRefreshing;
        private bool _engineWorking;

        private static string GetInstallRoot(string basePath, string ueVersion)
        {
            basePath = (basePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(basePath))
                basePath = AppSettings.DefaultInstalledBuildBasePath;

            // 사용자가 GW_Engine 자체를 고른 경우도 허용
            string leaf = new DirectoryInfo(basePath.TrimEnd(Path.DirectorySeparatorChar)).Name;
            string gwEngineRoot = leaf.Equals("GW_Engine", StringComparison.OrdinalIgnoreCase)
                ? basePath
                : Path.Combine(basePath, "GW_Engine");

            // GW_Engine 폴더가 있으면 그대로 사용, 없으면 생성(InstallRoot 생성 시점에 만들도록)
            return Path.Combine(gwEngineRoot, ueVersion);
        }

        private void ApplyEngineBasePathToUi(string basePath)
        {
            TbEngineBasePath.Text = basePath;
            TbEngineInstallRoot.Text = GetInstallRoot(basePath, _engineVersion);
        }

        private async void BtnEngineRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshEngineStatusAsync();
        }
        private async void BtnEngineDownload_Click(object sender, RoutedEventArgs e)
        {
            await DownloadInstalledBuildAsync(installAfterDownload: false);
        }
        private async void BtnEngineDownloadInstall_Click(object sender, RoutedEventArgs e)
        {
            await DownloadInstalledBuildAsync(installAfterDownload: true);
        }

        private async Task RefreshEngineStatusAsync()
        {
            try
            {
                PbEngine.IsIndeterminate = true;
                TxtEngineProgress.Text = "상태 확인 중...";
                BtnEngineDownload.IsEnabled = false;
                BtnEngineDownloadInstall.IsEnabled = false;

                string basePath = TbEngineBasePath.Text;
                string installRoot = GetInstallRoot(basePath, _engineVersion);

                Directory.CreateDirectory(installRoot);

                // 서버 latest.json
                _engineLatest = await InstalledBuildServices.GetLatestAsync(_engineVersion, AppendEngineLog);

                // 로컬 meta
                _engineLocalMeta = InstalledBuildServices.TryLoadLocalMeta(installRoot);

                // UI 표시
                TxtEngineServerInfo.Text = _engineLatest == null
                    ? "서버 latest.json을 불러오지 못했습니다."
                    : $"Label: {_engineLatest.label}\nCL: {_engineLatest.cl}\nCreated: {_engineLatest.createdAt}";

                if (_engineLocalMeta == null)
                {
                    TxtEngineLocalInfo.Text =
                        $"InstallRoot: {installRoot}\n로컬 설치: 없음 (meta 없음)";
                }
                else
                {
                    TxtEngineLocalInfo.Text =
                        $"InstallRoot: {installRoot}\n로컬 Label: {_engineLocalMeta.label}\n설치일: {_engineLocalMeta.installedAt}";
                }


                bool needUpdate = _engineLatest != null &&
                                (_engineLocalMeta == null || !string.Equals(_engineLocalMeta.label, _engineLatest.label, StringComparison.OrdinalIgnoreCase));

                BtnEngineDownload.IsEnabled = _engineLatest != null && !_engineWorking;
                BtnEngineDownloadInstall.IsEnabled = _engineLatest != null && needUpdate && !_engineWorking;

                TxtEngineProgress.Text = needUpdate ? "업데이트 필요" : "최신 상태";
            }
            catch (Exception ex)
            {
                AppendEngineLog($"[ERROR] 상태 확인 실패: {ex.Message}");
                TxtEngineProgress.Text = "오류";
            }
            finally
            {
                PbEngine.IsIndeterminate = false;
            }
        }

        private async Task DownloadInstalledBuildAsync(bool installAfterDownload)
        {
            if (_engineWorking) return;
            _engineWorking = true;

            try
            {
                if (_engineLatest == null)
                    _engineLatest = await InstalledBuildServices.GetLatestAsync(_engineVersion, AppendEngineLog);

                if (_engineLatest == null)
                {
                    AppendEngineLog("[ERROR] 서버 latest.json 로드 실패");
                    return;
                }

                string basePath = TbEngineBasePath.Text;
                string installRoot = GetInstallRoot(basePath, _engineVersion);
                Directory.CreateDirectory(installRoot);

                // zip 저장 위치: InstallRoot\{label}.zip
                string zipPath = Path.Combine(installRoot, $"{_engineLatest.label}.zip");

                BtnEngineDownload.IsEnabled = false;
                BtnEngineDownloadInstall.IsEnabled = false;

                // 다운로드 (이미 존재하면 size/sha256로 재사용 가능)
                await InstalledBuildServices.DownloadZipAsync(
                    url: _engineLatest.zip.url,
                    destZipPath: zipPath,
                    expectedSize: _engineLatest.zip.size,
                    log: AppendEngineLog,
                    progress: (p, readBytes, totalBytes) =>
                    {
                        bool unknownTotal = totalBytes <= 0 && p < 100;
                        PbEngine.IsIndeterminate = unknownTotal;
                        if (!unknownTotal)
                            PbEngine.Value = p;
                        var sz = DownloadProgressFormatter.FormatCurrentOverTotal(readBytes, totalBytes > 0 ? totalBytes : null);
                        TxtEngineProgress.Text = unknownTotal
                            ? $"다운로드 … ({sz})"
                            : $"다운로드 {p:0}% ({sz})";
                    });

                // 검증
                TxtEngineProgress.Text = "무결성 검증 중...";
                PbEngine.IsIndeterminate = true;

                bool ok = await InstalledBuildServices.VerifyZipAsync(
                    zipPath,
                    _engineLatest.zip.size,
                    _engineLatest.zip.sha256,
                    AppendEngineLog);

                if (!ok)
                {
                    TxtEngineProgress.Text = "검증 실패";
                    return;
                }

                if (!installAfterDownload)
                {
                    TxtEngineProgress.Text = "다운로드 완료";
                    return;
                }

                // 언팩(.NET) + 적용(Engine 폴더만)
                TxtEngineProgress.Text = "압축 해제/적용 중...";
                PbEngine.IsIndeterminate = true;

                await InstalledBuildServices.ExtractAndApplyAsync(
                    zipPath: zipPath,
                    installRoot: installRoot,
                    log: AppendEngineLog);

                // meta 갱신
                InstalledBuildServices.SaveLocalMeta(installRoot, new InstalledBuildMeta
                {
                    engineVersion = _engineLatest.engineVersion,
                    label = _engineLatest.label,
                    installedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    zipFileName = Path.GetFileName(zipPath),
                    zipSize = _engineLatest.zip.size,
                    zipSha256 = _engineLatest.zip.sha256,
                    zipUrl = _engineLatest.zip.url
                });

                AppendEngineLog("[SUCCESS] 설치 완료 및 meta 갱신");
                TxtEngineProgress.Text = "설치 완료";

                await RefreshEngineStatusAsync();
            }
            catch (Exception ex)
            {
                AppendEngineLog($"[ERROR] 다운로드/설치 실패: {ex.Message}");
                TxtEngineProgress.Text = "오류";
            }
            finally
            {
                PbEngine.IsIndeterminate = false;
                PbEngine.Value = 0;
                _engineWorking = false;
                BtnEngineDownload.IsEnabled = true;
                BtnEngineDownloadInstall.IsEnabled = true;
            }
        }

        private async void BtnEngineChangePath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Installed Build를 설치할 기본 경로(BasePath)를 선택하세요. (예: D:\\)",
                SelectedPath = TbEngineBasePath.Text,
                ShowNewFolderButton = true
            };

            var result = dialog.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK) return;

            var newBase = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(newBase)) return;

            _settings.InstalledBuildBasePath = newBase;
            _settings.Save();

            ApplyEngineBasePathToUi(newBase);

            await RefreshEngineStatusAsync();
        }

        private async void CbEngineVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_engineUiReady) return;

            if (CbEngineVersion.SelectedItem is not string v || string.IsNullOrWhiteSpace(v))
                return;

            _engineVersion = v.Trim();

            // 선택값 저장(옵션)
            _settings.SelectedEngineVersion = _engineVersion;
            _settings.Save();

            // InstallRoot 갱신
            ApplyEngineBasePathToUi(TbEngineBasePath.Text);

            // 서버 최신/로컬상태 갱신
            await RefreshEngineStatusAsync();
        }

    }
}
