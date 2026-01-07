﻿using System;
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

            TbP4Workspace.Text = new DirectoryInfo(Environment.CurrentDirectory).Name;

            _settings = AppSettings.Load();
            _launcher = new GameBuildLauncher(AppendLog, _settings.RootDownloadDir);

            TxtCachePath.Text = _launcher.RootDownloadDir;
            AppendLog("=== GW Launcher (GUI) ===");
            AppendLog($"캐시 루트 경로: {_launcher.RootDownloadDir}");
            AppendLog(string.Empty);

            CmbBuildType.SelectionChanged += (_, __) => RefreshBuildListUI();

            Loaded += async (_, __) => await RefreshFromServerAsync();
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
                .Where(b => string.Equals(b.Config, selectedType, StringComparison.OrdinalIgnoreCase))
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
            return "Development";
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            BtnRun.IsEnabled = false;

            try
            {
                if (LvBuilds.ItemsSource == null)
                {
                    AppendLog("[WARN] 실행할 빌드가 없습니다. 빌드목록을 먼저 새로고침하세요.");
                    return;
                }

                if (LvBuilds.SelectedItem is not ServerBuildItem selected)
                {
                    AppendLog("[WARN] 실행할 빌드를 체크하세요.");
                    return;
                }

                bool useWindowed = CbWindowed.IsChecked == true;
                string clientZip = selected.FileName;

                _settings.AddRecentFileName(clientZip);
                _settings.Save();

                // 2️⃣ Local 실행
                if (selected.DS == "O")
                {
                    string baseName = Path.GetFileNameWithoutExtension(clientZip);
                    string dsZip = baseName + "_DS.zip";

                    // ✅ 병렬 준비 + DS 먼저 실행 + Client 실행 (한 방에)
                    await _launcher.RunLocalWithDedicatedServerAsync(clientZip, dsZip, useWindowed);
                }
                else
                {
                    // DS 없으면 Client만 실행
                    await _launcher.RunAsync(clientZip, useWindowed);
                }
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 실행 중 예외 발생");
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

        private void AppendSetupP4Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtSetupP4Log.AppendText(message + Environment.NewLine);
                TxtSetupP4Log.ScrollToEnd();
            });
        }           

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

                // DS 존재 여부 빠른 조회용 (파일명 기반)
                // 규칙: <클라이언트빌드명(확장자제거)> + "_DS" 가 DS 플랫폼에 존재하면 O
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
                    bool hasDs = dsNameSet.Contains(expectedDsBaseName);

                    _allBuilds.Add(new ServerBuildItem
                    {
                        BuildNo   = item.JenkinsBuildNumber,
                        FileName  = item.FileName,
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

        private async Task<int> RunProcessAsync(
            string fileName,
            string arguments,
            Action<string> log,
            string? workingDirectory = null)
        {
            try
            {
                var psi = new ProcessStartInfo
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

        private void AppendGWEditorLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtGWEditorLog.AppendText(message + Environment.NewLine);
                TxtGWEditorLog.ScrollToEnd();
            });
        }

        private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessCaptureAsync(
            string fileName,
            string arguments,
            string? workingDirectory = null)
        {
            try
            {
                var psi = new ProcessStartInfo
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
                // (정책 3번) 실행 자체가 불가한 치명 오류만 팝업 + 로그
                AppendP4SyncLog("[FATAL] 프로세스 실행 실패: " + ex.Message);
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
            AppendGWEditorLog("=== GWEditor: Workspace/ClientRoot 확인 ===");

            var (exit, stdout, stderr) = await RunProcessCaptureAsync("p4", "-ztag info");
            if (exit != 0)
            {
                AppendGWEditorLog($"[WARN] p4 -ztag info 실패 (ExitCode={exit})");
                if (!string.IsNullOrWhiteSpace(stderr))
                    AppendGWEditorLog(stderr.Trim());

                TbGWEditorWorkspace.Text = "";
                TbGWEditorClientRoot.Text = "";
                return;
            }

            string ws = "";
            string root = "";

            foreach (var line in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("... clientName ", StringComparison.OrdinalIgnoreCase))
                    ws = line.Substring("... clientName ".Length).Trim();
                else if (line.StartsWith("... clientRoot ", StringComparison.OrdinalIgnoreCase))
                    root = line.Substring("... clientRoot ".Length).Trim();
            }

            TbGWEditorWorkspace.Text = ws;
            TbGWEditorClientRoot.Text = root;

            AppendGWEditorLog($"Workspace: {ws}");
            AppendGWEditorLog($"Client Root: {root}");
        }

        private async void BtnGWEditorRefresh_Click(object sender, RoutedEventArgs e)
        {
            BtnGWEditorRefresh.IsEnabled = false;
            try { await RefreshGWEditorP4InfoAsync(); }
            finally { BtnGWEditorRefresh.IsEnabled = true; }
        }

        private void BtnRunGWEditor_Click(object sender, RoutedEventArgs e)
        {
            string root = (TbGWEditorClientRoot.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                AppendGWEditorLog("[WARN] Client Root가 비어있거나 유효하지 않습니다. 새로고침 후 다시 시도하세요.");
                MessageBox.Show("Client Root를 확인할 수 없습니다.\n\n[새로고침] 후 다시 시도하세요.", "실행 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string editorExe = System.IO.Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
            string uproject = System.IO.Path.Combine(root, "GW", "GW.uproject");

            if (!File.Exists(editorExe))
            {
                AppendGWEditorLog("[WARN] UnrealEditor.exe를 찾지 못했습니다: " + editorExe);
                MessageBox.Show($"UnrealEditor.exe를 찾지 못했습니다.\n\n{editorExe}", "실행 불가", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!File.Exists(uproject))
            {
                AppendGWEditorLog("[WARN] GW.uproject를 찾지 못했습니다: " + uproject);
                MessageBox.Show($"GW.uproject를 찾지 못했습니다.\n\n{uproject}", "실행 불가", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string args = $"\"{uproject}\" -nocompile -ddc=noshared";

            AppendGWEditorLog("=== UnrealEditor 실행 ===");
            AppendGWEditorLog(editorExe);
            AppendGWEditorLog(args);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = editorExe,
                    Arguments = args,
                    WorkingDirectory = root,
                    UseShellExecute = true,   // start "" 와 유사하게 별도 프로세스로 런치
                });

                AppendGWEditorLog("실행 요청 완료.");
            }
            catch (Exception ex)
            {
                AppendGWEditorLog("[FATAL] 실행 실패: " + ex.Message);
                MessageBox.Show($"UnrealEditor 실행 실패\n\n{ex.Message}", "실행 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            if (header == "p4 sync")
            {
                await AutoRefreshP4SyncAsync();
                return;
            }
        }

        private async Task AutoRefreshP4SyncAsync()
        {
            if (_p4SyncRefreshing) return;
            _p4SyncRefreshing = true;

            try
            {
                if (BtnP4SyncRefresh != null) BtnP4SyncRefresh.IsEnabled = false;
                if (BtnP4SyncRun != null) BtnP4SyncRun.IsEnabled = false;

                await RefreshP4SyncInfoAsync();

                if (BtnP4SyncRun != null) BtnP4SyncRun.IsEnabled = true;
            }
            finally
            {
                if (BtnP4SyncRefresh != null) BtnP4SyncRefresh.IsEnabled = true;
                _p4SyncRefreshing = false;
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

        private bool _p4SyncRefreshing = false;

        private void AppendP4SyncLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtP4SyncLog.AppendText(message + Environment.NewLine);
                TxtP4SyncLog.ScrollToEnd();
            });
        }

        private async Task RefreshP4SyncInfoAsync()
        {
            TxtP4SyncLog.Clear();
            AppendP4SyncLog("=== p4 sync: Workspace/ClientRoot 확인 ===");

            var (exit, stdout, stderr) = await RunProcessCaptureAsync("p4", "-ztag info");
            if (exit != 0)
            {
                AppendP4SyncLog($"[WARN] p4 -ztag info 실패 (ExitCode={exit})");
                if (!string.IsNullOrWhiteSpace(stderr))
                    AppendP4SyncLog(stderr.Trim());

                TbP4SyncWorkspace.Text = "";
                TbP4SyncClientRoot.Text = "";
                TbP4SyncStream.Text = "";
                return;
            }

            string ws = "";
            string root = "";
            string stream = "";

            foreach (var line in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("... clientName ", StringComparison.OrdinalIgnoreCase))
                    ws = line.Substring("... clientName ".Length).Trim();
                else if (line.StartsWith("... clientRoot ", StringComparison.OrdinalIgnoreCase))
                    root = line.Substring("... clientRoot ".Length).Trim();
                else if (line.StartsWith("... clientStream ", StringComparison.OrdinalIgnoreCase))
                    stream = line.Substring("... clientStream ".Length).Trim();
            }

            TbP4SyncWorkspace.Text = ws;
            TbP4SyncClientRoot.Text = root;
            TbP4SyncStream.Text = stream;

            AppendP4SyncLog($"Workspace: {ws}");
            AppendP4SyncLog($"Client Root: {root}");
            if (!string.IsNullOrWhiteSpace(stream))
                AppendP4SyncLog($"Stream: {stream}");
        }

        private async void BtnP4SyncRefresh_Click(object sender, RoutedEventArgs e)
        {
            BtnP4SyncRefresh.IsEnabled = false;
            try { await RefreshP4SyncInfoAsync(); }
            finally { BtnP4SyncRefresh.IsEnabled = true; }
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

        private async void BtnP4SyncRun_Click(object sender, RoutedEventArgs e)
        {
            BtnP4SyncRun.IsEnabled = false;
            BtnP4SyncRefresh.IsEnabled = false;

            try
            {
                TxtP4SyncLog.Clear();

                string ws = (TbP4SyncWorkspace.Text ?? "").Trim();
                string root = (TbP4SyncClientRoot.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(root))
                {
                    AppendP4SyncLog("[WARN] Workspace/Client Root 정보를 확인할 수 없습니다. [새로고침] 후 다시 시도하세요.");
                    MessageBox.Show("Workspace/Client Root를 확인할 수 없습니다.\n\n[새로고침] 후 다시 시도하세요.",
                        "실행 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 진행 확인(배치의 Y/N 대체) - ScriptDir 비교는 제거(요구사항)
                var confirm = MessageBox.Show(
                    $"아래 워크스페이스 기준으로 Sync를 진행합니다.\n\nWorkspace: {ws}\nClient Root: {root}\n\n진행할까요?",
                    "p4 sync 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    AppendP4SyncLog("[INFO] 사용자가 취소했습니다.");
                    return;
                }

                const string TARGET_DEPOT = "//GW/dev/...";
                const string JENKINS_USER = "gw_build";
                const string JENKINS_CLIENT = "jenkins-Agent-Win-GW_ProjectBuild";
                const string TAG = "#JenkinsBuild";

                AppendP4SyncLog("=== p4 sync 시작 ===");

                // [2/6] 로컬 최신 CL
                AppendP4SyncLog("");
                AppendP4SyncLog("[2/6] 로컬 최신 changelist 조회 중...");

                var (exitLocal, outLocal, errLocal) = await RunProcessCaptureAsync("p4", $"changes -m1 @{ws}", root);
                if (exitLocal != 0)
                {
                    AppendP4SyncLog($"[WARN] 로컬 최신 CL 조회 실패 (ExitCode={exitLocal}). LOCAL_CL=0 가정");
                    if (!string.IsNullOrWhiteSpace(errLocal)) AppendP4SyncLog("[ERR] " + errLocal.Trim());
                }

                int localCL = exitLocal == 0 ? ParseChangeNumber(outLocal) : 0;
                if (localCL < 0) localCL = 0;
                AppendP4SyncLog($"- 로컬 최신 CL : {localCL}");

                // [3/6] 서버 최신 CL
                AppendP4SyncLog("");
                AppendP4SyncLog("[3/6] 서버 최신 changelist 조회 중...");

                var (exitServer, outServer, errServer) = await RunProcessCaptureAsync("p4", $"changes -m1 {TARGET_DEPOT}", root);
                if (exitServer != 0)
                {
                    AppendP4SyncLog($"[ERROR] 서버 최신 CL 조회 실패 (ExitCode={exitServer})");
                    if (!string.IsNullOrWhiteSpace(errServer)) AppendP4SyncLog("[ERR] " + errServer.Trim());
                    return;
                }

                int serverCL = ParseChangeNumber(outServer);
                if (serverCL < 0)
                {
                    AppendP4SyncLog("[ERROR] 서버 최신 CL 파싱 실패");
                    return;
                }

                AppendP4SyncLog($"- 서버 최신 CL : {serverCL}");

                if (localCL >= serverCL)
                {
                    AppendP4SyncLog("");
                    AppendP4SyncLog($"[INFO] 이미 서버 최신 CL({serverCL})까지 동기화되어 있습니다. sync 생략.");
                    return;
                }

                AppendP4SyncLog($"[INFO] 서버에 더 최신 변경사항이 있습니다. (LOCAL: {localCL}, SERVER: {serverCL})");

                // [4/6] Jenkins build CL scan
                AppendP4SyncLog("");
                AppendP4SyncLog("[4/6] Jenkins build changelist scan (최근 5개 후보)...");

                var (exitCandidates, outCandidates, errCandidates) =
                    await RunProcessCaptureAsync("p4", $"changes -u {JENKINS_USER} -c {JENKINS_CLIENT} -m5 {TARGET_DEPOT}", root);

                if (exitCandidates != 0)
                {
                    AppendP4SyncLog($"[WARN] Jenkins 후보 CL 조회 실패 (ExitCode={exitCandidates})");
                    if (!string.IsNullOrWhiteSpace(errCandidates)) AppendP4SyncLog("[ERR] " + errCandidates.Trim());
                    return;
                }

                var candidateCLs = new List<int>();
                foreach (var line in outCandidates.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int cl = ParseChangeNumber(line);
                    if (cl > 0) candidateCLs.Add(cl);
                }

                if (candidateCLs.Count == 0)
                {
                    AppendP4SyncLog("[WARN] Jenkins 후보 CL이 없습니다. sync 없이 종료합니다.");
                    return;
                }

                int targetJenkinsCL = -1;

                foreach (var cl in candidateCLs)
                {
                    AppendP4SyncLog($"  [DEBUG] Candidate CL: {cl}");

                    var (exitDesc, outDesc, errDesc) = await RunProcessCaptureAsync("p4", $"describe -s {cl}", root);
                    if (exitDesc != 0)
                    {
                        AppendP4SyncLog($"  [WARN] describe 실패 (CL={cl}, ExitCode={exitDesc})");
                        if (!string.IsNullOrWhiteSpace(errDesc)) AppendP4SyncLog("  [ERR] " + errDesc.Trim());
                        continue;
                    }

                    if (outDesc.IndexOf(TAG, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AppendP4SyncLog($"  [DEBUG] Jenkins 태그 발견: {TAG} (CL={cl})");
                        targetJenkinsCL = cl;
                        break;
                    }
                    else
                    {
                        AppendP4SyncLog($"  [DEBUG] 태그 없음 (CL={cl})");
                    }
                }

                if (targetJenkinsCL <= 0)
                {
                    AppendP4SyncLog("");
                    AppendP4SyncLog($"[WARN] 최근 5개의 Jenkins CL 중에서 태그({TAG})를 찾지 못했습니다. sync 없이 종료합니다.");
                    return;
                }

                AppendP4SyncLog("");
                AppendP4SyncLog($"[INFO] TARGET_JENKINS_CL : {targetJenkinsCL}");

                // [5/6] 비교
                AppendP4SyncLog("");
                AppendP4SyncLog("[5/6] 로컬 CL과 Jenkins 빌드 CL 비교 중...");

                if (targetJenkinsCL <= localCL)
                {
                    AppendP4SyncLog($"[INFO] 로컬 CL({localCL})이 Jenkins CL({targetJenkinsCL})보다 새롭거나 같아 sync 생략.");
                    return;
                }

                AppendP4SyncLog($"[INFO] Jenkins CL({targetJenkinsCL})이 로컬 CL({localCL})보다 최신입니다. sync 진행.");

                // [6/6] Sync
                AppendP4SyncLog("");
                AppendP4SyncLog($"[6/6] p4 sync ...@{targetJenkinsCL} 실행");

                int code = await RunProcessAsync("p4", $"sync ...@{targetJenkinsCL}", AppendP4SyncLog, root);
                if (code != 0)
                {
                    AppendP4SyncLog($"[ERROR] p4 sync 실패 (ExitCode={code})");
                    return;
                }

                AppendP4SyncLog("");
                AppendP4SyncLog($"[OK] 워크스페이스가 changelist {targetJenkinsCL} 기준으로 동기화되었습니다.");
                AppendP4SyncLog("=== p4 sync 완료 ===");
            }
            finally
            {
                BtnP4SyncRun.IsEnabled = true;
                BtnP4SyncRefresh.IsEnabled = true;
            }
        }



    }
}
