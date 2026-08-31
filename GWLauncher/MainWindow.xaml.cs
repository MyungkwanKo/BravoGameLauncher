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
using System.Windows.Threading;

namespace BravoGameLauncherGui
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly GameBuildLauncher _launcher;

        // 서버에서 받은 전체 빌드 목록 (WIN·DS 합집합, 동일 DS는 WIN 행에 페어링된 경우 DS 전용 행에서 제외)
        private List<ServerBuildItem> _allBuilds = new();

        // 크래시 로그 압축·업로드 취소용. null이 아니면 전송이 진행 중이라는 뜻이다.
        private System.Threading.CancellationTokenSource? _crashSendCts;

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
            public string Client    { get; set; } = "X";         // 클라이언트(WIN) O / X
            public string DS        { get; set; } = "x";           // DS O / X
            public DateTime SortKey { get; set; }                 // 내림차순 정렬용
        }

        public MainWindow()
        {
            InitializeComponent();

            // GW Sync 탭: 세션 영역 높이를 로그창 최소 높이(80)를 침범하지 않는 선에서
            // 세션 내용/창 크기에 맞춰 코드로 재계산한다(자세한 이유는 XAML 주석 참고).
            GWSyncSectionsPanel.SizeChanged += (_, __) => UpdateGWSyncSectionsRowHeight();
            GWSyncOuterGrid.SizeChanged += (_, __) => UpdateGWSyncSectionsRowHeight();

            _settings = AppSettings.Load();
            _launcher = new GameBuildLauncher(AppendLog, _settings.RootDownloadDir);

            // Engine BasePath 기본값 보정
            if (string.IsNullOrWhiteSpace(_settings.InstalledBuildBasePath))
                _settings.InstalledBuildBasePath = AppSettings.DefaultInstalledBuildBasePath;

            // UI 반영
            ApplyEngineBasePathToUi(_settings.InstalledBuildBasePath);

            // Setup p4 - P4USER 기본 선택: gw_developer
            CbP4UserDeveloper.IsChecked = true;
            CbP4UserEngine.IsChecked = false;
            CbP4UserGuest.IsChecked = false;

            // Workspace 입력란은 비워두고, 창 로드 후 현재 설정된 P4CLIENT로 자동 채움(RefreshP4SectionStatusAsync)
            TbP4Workspace.Text = "";

            TxtCachePath.Text = _launcher.RootDownloadDir;
            TbGameStarterArgs.Text = GameBuildLauncher.DefaultClientLaunchArgs;
            AppendLog("=== GW Launcher (GUI) ===");
            AppendLog($"캐시 루트 경로: {_launcher.RootDownloadDir}");
            AppendLog(string.Empty);

            CmbBuildType.SelectionChanged += (_, __) => RefreshBuildListUI();
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

        private async void BtnDownloadBuild_Click(object sender, RoutedEventArgs e)
        {
            await DownloadSelectedBuildOnlyAsync();
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
        // GW Sync 탭(Perforce 설정 / Engine / GWEditor)은 통합 로그창(TxtSharedLog) 하나만 사용한다.
        private void AppendSharedLog(string message) => AppendToLog(TxtSharedLog, message);

        /// <summary>GameStarter 장시간 작업 중: 탭 내 조작·다른 탭 이동을 막습니다.</summary>
        private void SetGameStarterInteractionLocked(bool locked)
        {
            bool en = !locked;
            if (CmbBuildType != null) CmbBuildType.IsEnabled = en;
            if (BtnRefreshFromServer != null) BtnRefreshFromServer.IsEnabled = en;
            if (BtnDownloadBuild != null) BtnDownloadBuild.IsEnabled = en;
            if (BtnRun != null) BtnRun.IsEnabled = en;
            if (CbWindowed != null) CbWindowed.IsEnabled = en;
            if (CbRunClient != null) CbRunClient.IsEnabled = en;
            if (CbRunDS != null) CbRunDS.IsEnabled = en;
            if (BtnGameStarterArgsReset != null) BtnGameStarterArgsReset.IsEnabled = en;
            if (BtnGameStarterDsArgsReset != null) BtnGameStarterDsArgsReset.IsEnabled = en;
            if (BtnChangeCachePath != null) BtnChangeCachePath.IsEnabled = en;
            if (BtnClearCache != null) BtnClearCache.IsEnabled = en;
            if (BtnOpenCachePath != null) BtnOpenCachePath.IsEnabled = en;
            if (BtnSendCrashLog != null) BtnSendCrashLog.IsEnabled = en;
            if (TbGameStarterArgs != null) TbGameStarterArgs.IsEnabled = en;
            if (TbGameStarterDsArgs != null) TbGameStarterDsArgs.IsEnabled = en;
            if (LvBuilds != null) LvBuilds.IsEnabled = en;

            if (TabItemGWEditor != null) TabItemGWEditor.IsEnabled = en;
            if (TabItemGameStarter != null) TabItemGameStarter.IsEnabled = true;
        }

        /// <summary>GWEditor 장시간 작업 중: 해당 탭 버튼·인자 입력·다른 탭 이동을 막습니다.</summary>
        private void SetGWEditorInteractionLocked(bool locked)
        {
            bool en = !locked;
            if (BtnGWEditorRefresh != null) BtnGWEditorRefresh.IsEnabled = en;
            if (BtnRunGWEditor != null) BtnRunGWEditor.IsEnabled = en;
            if (BtnGWEditorSync != null) BtnGWEditorSync.IsEnabled = en;
            if (BtnGWEditorDataSync != null) BtnGWEditorDataSync.IsEnabled = en;
            if (BtnGWEditorLocalRollback != null) BtnGWEditorLocalRollback.IsEnabled = en;
            if (BtnGWEditorArgsReset != null) BtnGWEditorArgsReset.IsEnabled = en;
            if (TbGWEditorArgs != null) TbGWEditorArgs.IsEnabled = en;

            if (TabItemGameStarter != null) TabItemGameStarter.IsEnabled = en;
            if (TabItemGWEditor != null) TabItemGWEditor.IsEnabled = true;
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

        private async void MenuClearCache_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"캐시 폴더 전체를 삭제합니다.\n\n경로: {_launcher.RootDownloadDir}\n\n계속하시겠습니까?",
                "캐시 삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            SetGameStarterInteractionLocked(true);
            // 비활성화가 그려지기 전에 동기 삭제가 끝나면 체감이 없음 → 한 틱 양보 후 같은 스레드에서 삭제
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string dir = _launcher.RootDownloadDir;
            try
            {
                if (!Directory.Exists(dir))
                {
                    AppendLog("[INFO] 삭제할 캐시 폴더가 없습니다.");
                    return;
                }

                Directory.Delete(dir, recursive: true);
                AppendLog("[INFO] 캐시 폴더를 삭제했습니다.");
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 캐시 삭제 중 오류가 발생했습니다.");
                AppendLog(ex.Message);
            }
            finally
            {
                SetGameStarterInteractionLocked(false);
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

        /// <summary>
        /// GameStarter 탭 - 선택한 빌드의 크래시 로그를 zip으로 묶어 Slack 채널로 전송한다 (#PJTGW-3099).
        /// 기본 경로는 사내 릴레이 서버(GWCrashRelay)로 자동 업로드하는 것이고,
        /// 릴레이가 응답하지 않을 때만 클립보드 + 딥링크 수동 전송으로 폴백한다.
        /// </summary>
        private async void BtnSendCrashLog_Click(object sender, RoutedEventArgs e)
        {
            // 전송 중이면 같은 버튼이 "전송 취소"로 동작한다.
            if (_crashSendCts != null)
            {
                AppendLog("[INFO] 크래시 로그 전송 취소를 요청했습니다...");
                try
                {
                    _crashSendCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 이미 끝난 직후라면 무시
                }
                return;
            }

            // 1) 빌드 선택 확인
            if (LvBuilds.SelectedItem is not ServerBuildItem selected)
            {
                AppendLog("[WARN] 크래시 로그를 전송할 빌드를 선택하세요.");
                MessageBox.Show(
                    "크래시 로그를 전송할 빌드를 먼저 선택해주세요.",
                    "빌드 선택",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 2) 클라이언트(WIN) 패키지가 있는 빌드만 크래시 로그 경로를 갖는다
            if (string.IsNullOrWhiteSpace(selected.FileName))
            {
                AppendLog("[WARN] 선택한 빌드에는 클라이언트(WIN) 패키지가 없어 크래시 로그 경로를 찾을 수 없습니다.");
                MessageBox.Show(
                    "선택한 빌드에는 클라이언트(WIN) 패키지가 없습니다.\n크래시 로그는 클라이언트 빌드에서만 수집됩니다.",
                    "크래시 로그 전송",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string? unpackDir = _launcher.GetClientUnpackDir(selected.FileName);
            if (string.IsNullOrWhiteSpace(unpackDir))
            {
                AppendLog($"[WARN] 빌드 파일명에서 로컬 경로를 계산하지 못했습니다: {selected.FileName}");
                MessageBox.Show(
                    $"빌드 파일명에서 로컬 경로를 계산하지 못했습니다.\n\n{selected.FileName}",
                    "크래시 로그 전송",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 3) 크래시 로그 폴더 확인 (없거나 비어 있으면 경고 후 종료)
            string crashesDir = CrashLogReporter.GetCrashesDir(unpackDir);
            if (!CrashLogReporter.HasCrashLogs(crashesDir))
            {
                AppendLog($"[WARN] 크래시 로그 폴더가 없거나 비어 있습니다: {crashesDir}");
                MessageBox.Show(
                    "크래시 로그 폴더가 없거나 비어 있습니다.\n\n" +
                    $"경로: {crashesDir}\n\n" +
                    "해당 빌드를 실행한 뒤 크래시가 발생한 경우에만 생성됩니다.",
                    "크래시 로그 없음",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 4) 용량이 크면 압축 전에 한 번 확인
            var (totalBytes, fileCount) = CrashLogReporter.Measure(crashesDir);
            if (totalBytes > CrashLogReporter.SizeWarningThresholdBytes)
            {
                var answer = MessageBox.Show(
                    $"크래시 로그 용량이 큽니다. ({CrashLogReporter.FormatSize(totalBytes)}, 파일 {fileCount}개)\n" +
                    "압축과 업로드에 시간이 걸릴 수 있습니다.\n\n계속하시겠습니까?",
                    "용량 확인",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

                if (answer != MessageBoxResult.OK)
                {
                    AppendLog("[INFO] 크래시 로그 전송을 취소했습니다. (용량 확인 단계)");
                    return;
                }
            }

            // 5) 크래시 상황 입력 (취소하면 zip 압축도 하지 않고 종료)
            string buildName = Path.GetFileNameWithoutExtension(selected.FileName);
            var inputWindow = new CrashReportInputWindow(buildName) { Owner = this };
            if (inputWindow.ShowDialog() != true)
            {
                AppendLog("[INFO] 크래시 로그 전송을 취소했습니다. (상황 입력 취소 - zip을 생성하지 않음)");
                return;
            }

            string reportText = inputWindow.ReportText;

            _crashSendCts = new System.Threading.CancellationTokenSource();
            var cancellationToken = _crashSendCts.Token;

            SetGameStarterInteractionLocked(true);
            SetCrashSendButtonCancelMode(true);

            try
            {
                AppendLog("=== 크래시 로그 전송 ===");
                AppendLog($"[INFO] 대상 폴더: {crashesDir}");
                AppendLog($"[INFO] 압축 시작... (파일 {fileCount}개, {CrashLogReporter.FormatSize(totalBytes)})");
                SetGameStarterProgress(true, 0, "크래시 로그 압축 중...");

                string zipPath = await Task.Run(
                    () => CrashLogReporter.CreateZip(crashesDir, buildName, reportText, cancellationToken),
                    cancellationToken);

                long zipSize = new FileInfo(zipPath).Length;
                AppendLog($"[INFO] 압축 완료: {zipPath} ({CrashLogReporter.FormatSize(zipSize)})");

                // 6) 사내 릴레이 서버로 자동 전송 (서버가 Slack 채널에 파일 + 메시지를 올린다)
                // 보안상 로그에는 서버 URL·Slack 링크를 남기지 않는다.
                AppendLog("[INFO] 릴레이 서버로 업로드 중...");
                SetGameStarterProgress(true, 0, "크래시 로그 업로드 준비 중...");

                var uploadResult = await CrashLogReporter.UploadAsync(
                    zipPath, buildName, reportText, ReportGameStarterProgress, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    SetGameStarterProgress(false, 0, "크래시 로그 전송 취소됨");
                    AppendLog($"[INFO] 크래시 로그 전송을 취소했습니다. zip 경로: {zipPath}");
                    return;
                }

                if (uploadResult.Ok)
                {
                    SetGameStarterProgress(false, 100, "크래시 로그 전송 완료");
                    AppendLog("[INFO] 크래시 로그를 Slack 채널로 전송했습니다.");

                    // 전송에 성공한 zip은 로컬에 남길 이유가 없다(용량 절약).
                    // 실패·취소로 남은 zip은 수동 폴백에 필요하므로 지우지 않는다.
                    CrashLogReporter.DeleteSentZip(zipPath);
                    AppendLog("[INFO] 전송이 완료되어 임시 zip을 삭제했습니다.");

                    MessageBox.Show(
                        "크래시 로그를 Slack 채널로 전송했습니다.",
                        "전송 완료",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // 7) 자동 전송 실패 → 수동(클립보드 + 딥링크) 경로로 폴백할지 묻는다
                AppendLog($"[ERROR] 자동 전송 실패: {uploadResult.Error}");
                SetGameStarterProgress(false, 0, "크래시 로그 전송 실패");

                var fallbackAnswer = MessageBox.Show(
                    $"크래시 로그 자동 전송에 실패했습니다.\n\n{uploadResult.Error}\n\n" +
                    "Slack에 직접 붙여넣어 전송하시겠습니까?\n(zip은 이미 만들어져 있습니다)",
                    "전송 실패",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (fallbackAnswer != MessageBoxResult.Yes)
                {
                    AppendLog($"[INFO] 수동 전송을 선택하지 않았습니다. zip 경로: {zipPath}");
                    return;
                }

                string message = CrashLogReporter.BuildSlackMessage(buildName, reportText);

                // 8) 폴백: 클립보드에 zip(파일) + 메시지(텍스트)를 함께 올린다
                if (!CrashLogReporter.TryCopyToClipboard(zipPath, message))
                {
                    AppendLog("[ERROR] 클립보드 복사에 실패했습니다. zip 폴더를 대신 열어드립니다.");
                    CrashLogReporter.TryOpenFolder(Path.GetDirectoryName(zipPath) ?? CrashLogReporter.TempZipDir);
                    MessageBox.Show(
                        "클립보드 복사에 실패했습니다.\n탐색기에서 열린 zip 파일을 Slack 채널로 직접 끌어다 놓아주세요.\n\n" +
                        zipPath,
                        "크래시 로그 전송",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // 9) Slack 채널 열기 (실패해도 클립보드에는 이미 올라가 있으므로 안내만 다르게 한다)
                bool slackOpened = CrashLogReporter.TryOpenSlackChannel();
                if (slackOpened)
                    AppendLog("[INFO] Slack 채널을 열었습니다.");
                else
                    AppendLog("[WARN] Slack 채널 열기에 실패했습니다. Slack을 직접 열어 붙여넣어 주세요.");

                AppendLog("[INFO] zip을 클립보드에 복사했습니다. Slack 입력창에서 Ctrl+V 후 Enter로 전송하세요.");

                var guideWindow = new CrashReportGuideWindow(zipPath, message, slackOpened) { Owner = this };
                guideWindow.ShowDialog();
            }
            catch (OperationCanceledException)
            {
                // 압축 단계에서 취소한 경우 (만들던 zip은 CreateZip이 지운다)
                SetGameStarterProgress(false, 0, "크래시 로그 전송 취소됨");
                AppendLog("[INFO] 크래시 로그 전송을 취소했습니다.");
            }
            catch (Exception ex)
            {
                // 진행률 바를 켜 둔 채 예외가 나면 계속 돌아가므로 여기서 반드시 되돌린다.
                SetGameStarterProgress(false, 0, "크래시 로그 전송 실패");

                AppendLog($"[ERROR] 크래시 로그 전송 실패: {ex.Message}");
                MessageBox.Show(
                    $"크래시 로그 전송 준비 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _crashSendCts?.Dispose();
                _crashSendCts = null;

                SetCrashSendButtonCancelMode(false);
                SetGameStarterInteractionLocked(false);
            }
        }

        /// <summary>
        /// 크래시 로그 전송 중에는 전송 버튼을 "전송 취소"로 바꿔 활성 상태로 둔다.
        /// (다른 GameStarter 조작은 SetGameStarterInteractionLocked가 잠근 상태를 유지)
        /// </summary>
        private void SetCrashSendButtonCancelMode(bool cancelMode)
        {
            if (BtnSendCrashLog == null)
                return;

            BtnSendCrashLog.Content = cancelMode ? "전송 취소" : "크래시 로그 전송";
            BtnSendCrashLog.IsEnabled = true;
        }

        private async void BtnRefreshFromServer_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFromServerAsync(lockGameStarterInteraction: true);
        }

        // ====== (1번 기능 핵심) 서버 목록 로드 + DS 존재 여부 계산 ======
        private async Task RefreshFromServerAsync(bool lockGameStarterInteraction = false)
        {
            if (lockGameStarterInteraction)
                SetGameStarterInteractionLocked(true);
            else if (BtnRefreshFromServer != null)
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
                var winBuilds = result.Platforms.TryGetValue("WIN", out var win) && win?.Builds != null
                    ? win.Builds
                    : new List<BuildItem>();
                var dsBuilds = result.Platforms.TryGetValue("DS", out var ds) && ds?.Builds != null
                    ? ds.Builds
                    : new List<BuildItem>();

                if (winBuilds.Count == 0 && dsBuilds.Count == 0)
                {
                    AppendLog("[WARN] 서버 WIN·DS 빌드 리스트가 모두 비어 있습니다.");
                    _allBuilds.Clear();
                    RefreshBuildListUI();
                    return;
                }

                // DS 조회: Jenkins 번호당 DS가 여러 개(Development/Shipping/…)일 수 있으므로 Lookup 사용.
                // 짝은 (1) 같은 Jenkins 번호의 DS 중 Config가 클라이언트와 같은 것 (2) 없으면 baseName+"_DS" fallback.
                var dsByBuildNo = dsBuilds
                    .Where(b => b.JenkinsBuildNumber > 0 && !string.IsNullOrWhiteSpace(b.FileName))
                    .ToLookup(b => b.JenkinsBuildNumber);

                var dsByBaseName = dsBuilds
                    .Where(b => !string.IsNullOrWhiteSpace(b.FileName))
                    .GroupBy(b => Path.GetFileNameWithoutExtension(b.FileName), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                // WIN: buildTime 기준 내림차순
                winBuilds.Sort((a, b) =>
                {
                    var ta = a.BuildTime ?? DateTime.MinValue;
                    var tb = b.BuildTime ?? DateTime.MinValue;
                    return tb.CompareTo(ta);
                });

                var dsSorted = dsBuilds
                    .OrderByDescending(b => b.BuildTime ?? DateTime.MinValue)
                    .ToList();

                _allBuilds = new List<ServerBuildItem>();
                var dsPairedWithWin = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in winBuilds)
                {
                    if (string.IsNullOrWhiteSpace(item.FileName))
                        continue;

                    var parse = ParseBuildInfoFromFileName(item.FileName);
                    var dt = item.BuildTime ?? parse.Timestamp ?? DateTime.MinValue;

                    string clientConfig = !string.IsNullOrWhiteSpace(item.Config)
                        ? item.Config
                        : GetBuildConfigFromFileName(item.FileName);

                    var baseName = Path.GetFileNameWithoutExtension(item.FileName);
                    var expectedDsBaseName = baseName + "_DS";

                    BuildItem? pairedDs = null;
                    if (item.JenkinsBuildNumber > 0)
                    {
                        pairedDs = dsByBuildNo[item.JenkinsBuildNumber].FirstOrDefault(d =>
                        {
                            string dcfg = !string.IsNullOrWhiteSpace(d.Config)
                                ? d.Config
                                : GetBuildConfigFromFileName(d.FileName);
                            return string.Equals(clientConfig, dcfg, StringComparison.OrdinalIgnoreCase);
                        });
                    }

                    if (pairedDs == null && dsByBaseName.TryGetValue(expectedDsBaseName, out var byName))
                    {
                        string dcfg = !string.IsNullOrWhiteSpace(byName.Config)
                            ? byName.Config
                            : GetBuildConfigFromFileName(byName.FileName);
                        if (string.Equals(clientConfig, dcfg, StringComparison.OrdinalIgnoreCase))
                            pairedDs = byName;
                    }

                    bool dsOk = pairedDs != null;
                    string matchedDsFileName = pairedDs != null ? pairedDs.FileName : string.Empty;
                    if (pairedDs != null && !string.IsNullOrWhiteSpace(pairedDs.FileName))
                        dsPairedWithWin.Add(pairedDs.FileName);

                    _allBuilds.Add(new ServerBuildItem
                    {
                        BuildNo   = item.JenkinsBuildNumber,
                        FileName  = item.FileName,
                        DsFileName = matchedDsFileName,
                        Config    = clientConfig,
                        Version   = !string.IsNullOrWhiteSpace(item.Version) ? item.Version : (parse.Version ?? string.Empty),
                        CL        = item.Cl != 0 ? item.Cl : parse.CL,
                        BuildDate = dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd"),
                        BuildTime = dt == DateTime.MinValue ? "" : dt.ToString("HH:mm:ss"),
                        Client    = "O",
                        DS        = dsOk ? "O" : "X",
                        SortKey   = dt
                    });
                }

                // WIN에 매칭되지 않은 DS만 별도 행으로 표시 (클라이언트 없음)
                var dsOnlyRowsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dsItem in dsSorted)
                {
                    if (string.IsNullOrWhiteSpace(dsItem.FileName))
                        continue;
                    if (dsPairedWithWin.Contains(dsItem.FileName))
                        continue;
                    if (!dsOnlyRowsSeen.Add(dsItem.FileName))
                        continue;

                    var dsParse = ParseBuildInfoFromFileName(dsItem.FileName);
                    var dsDt = dsItem.BuildTime ?? dsParse.Timestamp ?? DateTime.MinValue;
                    string dsConfig = !string.IsNullOrWhiteSpace(dsItem.Config)
                        ? dsItem.Config
                        : GetBuildConfigFromFileName(dsItem.FileName);

                    _allBuilds.Add(new ServerBuildItem
                    {
                        BuildNo = dsItem.JenkinsBuildNumber,
                        FileName = string.Empty,
                        DsFileName = dsItem.FileName,
                        Config = dsConfig,
                        Version = !string.IsNullOrWhiteSpace(dsItem.Version) ? dsItem.Version : (dsParse.Version ?? string.Empty),
                        CL = dsItem.Cl != 0 ? dsItem.Cl : dsParse.CL,
                        BuildDate = dsDt == DateTime.MinValue ? "" : dsDt.ToString("yyyy-MM-dd"),
                        BuildTime = dsDt == DateTime.MinValue ? "" : dsDt.ToString("HH:mm:ss"),
                        Client = "X",
                        DS = "O",
                        SortKey = dsDt
                    });
                }

                _allBuilds = _allBuilds.OrderByDescending(b => b.SortKey).ToList();

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
                if (lockGameStarterInteraction)
                    SetGameStarterInteractionLocked(false);
                else if (BtnRefreshFromServer != null)
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
                string ws = (TbP4Workspace.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(ws))
                {
                    // 입력 누락은 사용자 실수이므로: 로그 + 팝업(친절)
                    AppendSharedLog("[WARN] Workspace 이름을 입력하세요.");
                    MessageBox.Show("Workspace(P4CLIENT) 이름을 입력하세요.", "입력 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                AppendSharedLog("=== setup_p4 시작 ===");
                AppendSharedLog($"Workspace(P4CLIENT): {ws}");
                AppendSharedLog("");

                // 배치와 동일한 설정(값 그대로)
                // p4 set P4IGNORE=.p4ignore
                // p4 set P4CHARSET=utf8
                // p4 set P4PORT=bravo-repo.omnicraftlabs.co.kr:1666
                // p4 set P4CLIENT=<workspace>
                // p4 set (확인 출력)

                int code;

                code = await RunProcessAsync("p4", "set P4IGNORE=.p4ignore", AppendSharedLog);
                if (code != 0) AppendSharedLog($"[WARN] ExitCode={code}");

                code = await RunProcessAsync("p4", "set P4CHARSET=utf8", AppendSharedLog);
                if (code != 0) AppendSharedLog($"[WARN] ExitCode={code}");

                var p4user = GetSelectedP4User();
                AppendSharedLog($"P4USER: {p4user}");
                code = await RunProcessAsync("p4", $"set P4USER={p4user}", AppendSharedLog);
                if (code != 0) AppendSharedLog($"[WARN] ExitCode={code}");

                code = await RunProcessAsync("p4", "set P4PORT=bravo-repo.omnicraftlabs.co.kr:1666", AppendSharedLog);
                if (code != 0) AppendSharedLog($"[WARN] ExitCode={code}");

                code = await RunProcessAsync("p4", $"set P4CLIENT={ws}", AppendSharedLog);
                if (code != 0) AppendSharedLog($"[WARN] ExitCode={code}");

                AppendSharedLog("");
                AppendSharedLog("===== Perforce 환경 변수 확인 =====");

                code = await RunProcessAsync("p4", "set", AppendSharedLog);
                if (code != 0) AppendSharedLog($"[WARN] ExitCode={code}");

                AppendSharedLog("===== P4 Info 확인 =====");

                code = await RunProcessAsync("p4", "info", AppendSharedLog);
                if (code != 0) AppendSharedLog($"[WARN] ExitCode={code}");

                AppendSharedLog("==================================");
                AppendSharedLog("=== setup_p4 완료 ===");

                await RefreshP4SectionStatusAsync();
            }
            finally
            {
                BtnSetupP4Apply.IsEnabled = true;
            }
        }

        /// <summary>Perforce 설정 섹션 - "조회" 버튼: 선택된 P4User + 로컬 host 기준으로 워크스페이스를 조회해 팝업으로 보여준다.</summary>
        private async void BtnP4Lookup_Click(object sender, RoutedEventArgs e)
        {
            BtnP4Lookup.IsEnabled = false;
            try
            {
                string p4user = GetSelectedP4User();
                string host = Environment.MachineName;

                AppendSharedLog($"[INFO] 워크스페이스 조회 중... (P4USER={p4user}, Host={host})");

                var (exit, stdout, stderr) = await RunProcessCaptureAsync("p4", $"-ztag clients -u {p4user}");
                if (exit != 0)
                {
                    AppendSharedLog($"[ERROR] 워크스페이스 조회 실패 (ExitCode={exit})");
                    if (!string.IsNullOrWhiteSpace(stderr)) AppendSharedLog(stderr.Trim());
                    MessageBox.Show($"워크스페이스 조회에 실패했습니다.\n\n{stderr}", "조회 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var allClients = ParseP4ClientsZtag(stdout);

                // 로컬 host와 일치하거나, Host 제한이 없는(어느 PC에서나 사용 가능한) 워크스페이스만 표시
                var matched = allClients
                    .Where(c => string.IsNullOrWhiteSpace(c.Host) || string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                AppendSharedLog($"[INFO] 워크스페이스 {matched.Count}건 조회됨");

                var popup = new P4WorkspaceLookupWindow(matched, p4user, host) { Owner = this };
                if (popup.ShowDialog() == true && !string.IsNullOrWhiteSpace(popup.SelectedWorkspaceName))
                {
                    TbP4Workspace.Text = popup.SelectedWorkspaceName;
                    AppendSharedLog($"[INFO] 워크스페이스 선택됨: {popup.SelectedWorkspaceName}");
                }
            }
            catch (Exception ex)
            {
                AppendSharedLog($"[ERROR] 워크스페이스 조회 중 오류: {ex.Message}");
                MessageBox.Show($"워크스페이스 조회 중 오류가 발생했습니다.\n\n{ex.Message}", "조회 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnP4Lookup.IsEnabled = true;
            }
        }

        /// <summary>`p4 -ztag clients -u {user}` 태그 출력 파싱: 레코드 구분자는 "... client " 라인.</summary>
        private static List<(string Client, string Root, string Host)> ParseP4ClientsZtag(string stdout)
        {
            var result = new List<(string Client, string Root, string Host)>();
            if (string.IsNullOrWhiteSpace(stdout)) return result;

            string? curClient = null, curRoot = null, curHost = null;

            void Flush()
            {
                if (!string.IsNullOrWhiteSpace(curClient))
                    result.Add((curClient!, curRoot ?? "", curHost ?? ""));
            }

            foreach (var rawLine in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = rawLine.TrimEnd();
                if (!line.StartsWith("... "))
                    continue;

                var rest = line.Substring(4);
                int sp = rest.IndexOf(' ');
                string key = sp >= 0 ? rest.Substring(0, sp) : rest;
                string value = sp >= 0 ? rest.Substring(sp + 1).Trim() : "";

                if (key.Equals("client", StringComparison.OrdinalIgnoreCase))
                {
                    Flush();
                    curClient = value;
                    curRoot = null;
                    curHost = null;
                }
                else if (key.Equals("Root", StringComparison.OrdinalIgnoreCase))
                {
                    curRoot = value;
                }
                else if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    curHost = value;
                }
            }
            Flush();

            return result;
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
            AppendSharedLog("=== GWEditor: Workspace / Editor 경로 확인 ===");

            var (exit, stdout, stderr) = await RunProcessCaptureAsync("p4", "-ztag info", onFatalLog: AppendSharedLog);
            if (exit != 0)
            {
                AppendSharedLog($"[WARN] p4 -ztag info 실패 (ExitCode={exit})");
                if (!string.IsNullOrWhiteSpace(stderr))
                    AppendSharedLog(stderr.Trim());

                TbGWEditorClientStream.Text = "";
                _gwEditorWorkspaceName = null;
                _gwEditorClientRoot = null;
                _gwEditorClientStream = null;
                _gwEditorEditorExePath = null;
                SetGWEditorSyncStatus("", 0);
                SetGWEditorStreamLatestClText(-1);
                SetGWEditorDataTableGenerateClText("");
                if (BtnGWEditorDataSync != null) BtnGWEditorDataSync.IsEnabled = false;
                return;
            }

            var (clientName, clientRoot, clientStream) = ParseP4ZtagInfo(stdout);
            _gwEditorWorkspaceName = clientName;
            _gwEditorClientRoot = clientRoot;
            _gwEditorClientStream = clientStream;
            SetGWEditorClientStreamText(clientStream);

            // Editor exe 경로는 Engine 탭 InstallRoot 기준 (Installed Build). UI 행은 없지만(Workspace와 함께 중복 정보라 제거)
            // Editor 실행 시 사용하기 위해 내부 필드에 보관.
            string engineInstallRoot = TbEngineInstallRoot.Text?.Trim() ?? "";
            string editorExe = Path.Combine(engineInstallRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
            _gwEditorEditorExePath = editorExe;
            if (BtnGWEditorDataSync != null) BtnGWEditorDataSync.IsEnabled = true;

            AppendSharedLog($"Workspace: {FormatGWEditorWorkspaceDisplay(clientName, clientRoot)}");
            string streamDisplay = FormatP4ClientStreamDisplay(clientStream);
            AppendSharedLog(string.IsNullOrWhiteSpace(clientStream)
                ? "Client stream: -"
                : $"Client stream: {streamDisplay}");
            string? uproject = GetGWEditorUprojectPath();
            if (!string.IsNullOrWhiteSpace(uproject))
                AppendSharedLog($"Project: {uproject}");
            AppendSharedLog($"Editor : {editorExe}");

            // Local / Stream Latest / Build CL — Local CL은 한 번만 조회해 공유
            int localCL = await QueryLocalChangeAsync(clientName, clientRoot, log: null);

            int streamLatestCL = await QueryStreamLatestChangeAsync(clientStream, clientRoot, log: null);
            SetGWEditorStreamLatestClText(streamLatestCL);
            AppendSharedLog(streamLatestCL > 0
                ? $"Stream Latest CL: {streamLatestCL}"
                : "[WARN] Stream Latest CL 조회 실패");

            if (IsArtDevP4Stream(clientStream))
            {
                SetGWEditorSyncStatus("아트 스트림 입니다.", 2);
                SetGWEditorClAndButtons(localCL, -1, needSync: false, isArtDevStream: true);
            }
            else
            {
                var (buildCL, needSync, status, statusKind) =
                    await QueryProjectBuildClStateAsync(clientRoot, localCL, log: null);
                SetGWEditorSyncStatus(status, statusKind);
                SetGWEditorClAndButtons(localCL, buildCL, needSync, isArtDevStream: false);
            }

            var (_, latestServerCL, targetCLs) =
                await QueryDataTableSyncTargetsAsync(clientName, clientRoot, localCL, log: null);
            if (latestServerCL < 0)
                SetGWEditorDataTableGenerateClText("조회 실패");
            else
                SetGWEditorDataTableGenerateClText(targetCLs.Count > 0 ? string.Join(", ", targetCLs) : "-");
        }

        /// <summary>
        /// 섹션 헤더 상태 점 색상. statusKind: 0=없음(회색), 1=경고/조치 필요(빨강), 2=정상(초록), 3=주의(주황).
        /// Perforce 설정 / Engine / GWEditor 3개 섹션 헤더가 공통으로 사용.
        /// </summary>
        private static System.Windows.Media.Brush GetSectionStatusDotBrush(int statusKind) => statusKind switch
        {
            1 => System.Windows.Media.Brushes.Crimson,
            2 => System.Windows.Media.Brushes.MediumSeaGreen,
            3 => System.Windows.Media.Brushes.Orange,
            _ => System.Windows.Media.Brushes.Gray
        };

        /// <summary>
        /// 녹색(정상)이 아닌 경고성 상태(statusKind 1=빨강, 3=주황)일 때 섹션 헤더 바 배경에도 옅은 색을 입혀
        /// 접힌 상태에서도 눈에 잘 띄게 한다. 정상(2)·정보없음(0)은 배경을 강조하지 않는다.
        /// </summary>
        private static System.Windows.Media.Brush GetSectionHeaderBackgroundBrush(int statusKind) => statusKind switch
        {
            1 => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFD, 0xEC, 0xEA)),
            3 => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF4, 0xE1)),
            _ => System.Windows.Media.Brushes.Transparent
        };

        /// <summary>
        /// Sync 필요여부는 GWEditor 섹션 본문에 별도 행을 두지 않고, 섹션 헤더의 상태 점·요약·배경색으로만 표시한다(중복 제거).
        /// </summary>
        /// <param name="statusKind">0=없음(회색), 1=동기화필요(빨강), 2=동일(초록), 3=주의(주황)</param>
        private void SetGWEditorSyncStatus(string statusText, int statusKind)
        {
            Dispatcher.Invoke(() =>
            {
                var brush = GetSectionStatusDotBrush(statusKind);

                // 섹션 헤더(접었을 때도 보이는 상태 점·요약·배경색)에 반영
                if (GWEditorSectionStatusDot != null)
                    GWEditorSectionStatusDot.Fill = brush;
                if (TxtGWEditorSectionSummary != null)
                    TxtGWEditorSectionSummary.Text = statusText ?? "";
                if (GWEditorSectionHeaderBorder != null)
                    GWEditorSectionHeaderBorder.Background = GetSectionHeaderBackgroundBrush(statusKind);
            });
        }

        private void SetGWEditorDataTableGenerateClText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (TbGWEditorDataTableGenerateCLs != null)
                    TbGWEditorDataTableGenerateCLs.Text = text ?? "";
            });
        }

        private void SetGWEditorStreamLatestClText(int streamLatestCL)
        {
            Dispatcher.Invoke(() =>
            {
                if (TbGWEditorStreamLatestCL != null)
                    TbGWEditorStreamLatestCL.Text = streamLatestCL > 0 ? streamLatestCL.ToString() : "-";
            });
        }

        private void SetGWEditorClientStreamText(string? clientStream)
        {
            Dispatcher.Invoke(() =>
            {
                if (TbGWEditorClientStream != null)
                    TbGWEditorClientStream.Text = FormatP4ClientStreamDisplay(clientStream);
            });
        }

        /// <summary>GWEditor 통합 탭: Local CL / Build CL 표시, Sync / Local Rollback 버튼 활성화.</summary>
        private void SetGWEditorClAndButtons(int localCL, int buildCL, bool needSync, bool isArtDevStream = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (TbGWEditorLocalCL != null)
                    TbGWEditorLocalCL.Text = localCL > 0 ? localCL.ToString() : "0";
                if (TbGWEditorBuildCL != null)
                    TbGWEditorBuildCL.Text = buildCL > 0 ? buildCL.ToString() : "-";
                if (BtnGWEditorSync != null)
                    BtnGWEditorSync.IsEnabled = isArtDevStream || buildCL > 0;
                if (BtnGWEditorLocalRollback != null)
                    BtnGWEditorLocalRollback.IsEnabled = !isArtDevStream && buildCL > 0 && localCL > buildCL;
            });
        }

        private string? _gwEditorWorkspaceName;
        private string? _gwEditorClientRoot;
        private string? _gwEditorClientStream;
        // Editor(UnrealEditor.exe) 경로 - UI 행은 제거되었지만(중복 정보) 내부적으로는 계속 계산·보관해 Editor 실행에 사용
        private string? _gwEditorEditorExePath;

        private static string FormatGWEditorWorkspaceDisplay(string? clientName, string? clientRoot)
        {
            string name = clientName?.Trim() ?? "";
            string root = clientRoot?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(root))
                return "";
            if (string.IsNullOrWhiteSpace(root))
                return name;
            if (string.IsNullOrWhiteSpace(name))
                return root;
            return $"{name} ({root})";
        }

        private string? GetGWEditorUprojectPath()
        {
            string root = _gwEditorClientRoot?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(root))
                return null;
            return Path.Combine(root, "GW", "GW.uproject");
        }


        private async void BtnGWEditorRefresh_Click(object sender, RoutedEventArgs e)
        {
            BtnGWEditorRefresh.IsEnabled = false;
            try { await RefreshGWEditorP4InfoAsync(); }
            finally { BtnGWEditorRefresh.IsEnabled = true; }
        }
       
        private void BtnRunGWEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_engineLocalMeta == null)
            {
                AppendSharedLog("[WARN] 설치된 Engine이 없어 UnrealEditor를 실행할 수 없습니다.");
                MessageBox.Show(
                    "설치된 Engine이 없습니다.\n\nEngine 세션에서 먼저 엔진을 다운로드 + 설치해주세요.",
                    "실행 불가",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string? uproject = GetGWEditorUprojectPath();
            if (string.IsNullOrWhiteSpace(uproject) || !File.Exists(uproject))
            {
                AppendSharedLog("[WARN] 프로젝트 파일(GW.uproject) 경로가 유효하지 않습니다: " + uproject);
                MessageBox.Show(
                    "프로젝트 파일(GW.uproject)을 찾을 수 없습니다.\n\n" + uproject +
                    "\n\n[새로고침] 후 다시 시도하세요.",
                    "실행 불가",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 내부에 보관된 "에디터 실행 파일 경로"(Engine InstallRoot 기준)를 사용해 실행
            string editorExe = _gwEditorEditorExePath?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(editorExe) || !File.Exists(editorExe))
            {
                AppendSharedLog("[WARN] UnrealEditor.exe 경로가 유효하지 않습니다: " + editorExe);
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

            AppendSharedLog("=== UnrealEditor 실행 요청 ===");
            AppendSharedLog("Editor : " + editorExe);
            AppendSharedLog("Project: " + uproject);
            AppendSharedLog("Args   : " + args);

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

                AppendSharedLog("실행 요청 완료.");
            }
            catch (Exception ex)
            {
                AppendSharedLog("[FATAL] 실행 실패: " + ex.Message);
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
            TbGameStarterArgs.Text = GameBuildLauncher.DefaultClientLaunchArgs;
        }

        private void BtnGameStarterDsArgsReset_Click(object sender, RoutedEventArgs e)
        {
            TbGameStarterDsArgs.Text = GameBuildLauncher.DefaultDedicatedServerArgs;
        }

        /// <summary>
        /// GW Sync 탭의 세션(Expander) 영역 행 높이를 재계산한다.
        /// 세션 내용이 필요로 하는 실제 높이(GWSyncSectionsPanel.ActualHeight)만큼 주되,
        /// 로그창이 최소 80px는 항상 보이도록 (전체 탭 높이 - 80)을 넘지 않게 제한한다.
        /// 그 이상 넘치는 내용은 세션 영역의 ScrollViewer가 스크롤바로 보여준다.
        /// GWSyncSectionsPanel/GWSyncOuterGrid의 SizeChanged 이벤트(세션 펼침/접힘, 창 크기 변경)에서 호출된다.
        /// </summary>
        private void UpdateGWSyncSectionsRowHeight()
        {
            if (GWSyncOuterGrid == null || GWSyncOuterGrid.RowDefinitions.Count < 2)
                return;

            double totalHeight = GWSyncOuterGrid.ActualHeight;
            if (totalHeight <= 0)
                return; // 아직 레이아웃이 이루어지기 전

            const double logMinHeight = 80;
            const double sectionsMinHeight = 40;

            double available = totalHeight - logMinHeight;
            if (available < sectionsMinHeight) available = sectionsMinHeight;

            double desired = GWSyncSectionsPanel?.ActualHeight ?? available;
            if (desired <= 0) desired = available;

            double newHeight = Math.Min(desired, available);
            if (newHeight < sectionsMinHeight) newHeight = sectionsMinHeight;

            GWSyncOuterGrid.RowDefinitions[0].Height = new GridLength(newHeight);
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

            // GW Sync 탭 = Perforce 설정 + Engine + GWEditor 3개 섹션 통합 탭.
            // 탭이 선택될 때(최초 진입 포함) 세 섹션 상태를 모두 갱신한다.
            if (header == "GW Sync")
            {
                await AutoRefreshP4SetupAsync();
                await AutoRefreshEngineAsync();
                await AutoRefreshGWEditorAsync();
                return;
            }
        }

        private bool _p4SectionRefreshing = false;

        /// <summary>Perforce 설정 섹션: GW Sync 탭에 들어올 때마다 현재 P4CLIENT 상태를 다시 조회해 표시.</summary>
        private async Task AutoRefreshP4SetupAsync()
        {
            if (_p4SectionRefreshing) return;
            _p4SectionRefreshing = true;
            try
            {
                await RefreshP4SectionStatusAsync();
            }
            finally
            {
                _p4SectionRefreshing = false;
            }
        }

        private bool _p4SectionAutoStateApplied = false;

        /// <summary>Perforce 설정 섹션 헤더의 상태 점·요약, Workspace 입력란을 실제 설정된 P4CLIENT로 갱신한다(미적용 상태의 입력값은 덮어씀).</summary>
        private async Task RefreshP4SectionStatusAsync()
        {
            try
            {
                var (exit, stdout, _) = await RunProcessCaptureAsync("p4", "-ztag info");
                string clientName = "";
                if (exit == 0)
                {
                    var parsed = ParseP4ZtagInfo(stdout);
                    clientName = parsed.clientName;
                }

                if (!string.IsNullOrWhiteSpace(clientName))
                {
                    // 조회 팝업 등에서 선택만 하고 "적용"하지 않은 값이 남아있어도,
                    // 새로고침 시점엔 항상 실제 설정된 P4CLIENT로 덮어써서 보여준다.
                    TbP4Workspace.Text = clientName;

                    if (TxtP4SectionSummary != null)
                        TxtP4SectionSummary.Text = $"{clientName} 적용됨";
                    if (P4SectionStatusDot != null)
                        P4SectionStatusDot.Fill = GetSectionStatusDotBrush(2);
                    if (P4SectionHeaderBorder != null)
                        P4SectionHeaderBorder.Background = GetSectionHeaderBackgroundBrush(2);

                    if (!_p4SectionAutoStateApplied)
                    {
                        ExpanderP4Setup.IsExpanded = false;
                        _p4SectionAutoStateApplied = true;
                    }
                }
                else
                {
                    if (TxtP4SectionSummary != null)
                        TxtP4SectionSummary.Text = "워크스페이스 미설정";
                    if (P4SectionStatusDot != null)
                        P4SectionStatusDot.Fill = GetSectionStatusDotBrush(1);
                    if (P4SectionHeaderBorder != null)
                        P4SectionHeaderBorder.Background = GetSectionHeaderBackgroundBrush(1);

                    if (!_p4SectionAutoStateApplied)
                    {
                        ExpanderP4Setup.IsExpanded = true;
                        _p4SectionAutoStateApplied = true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSharedLog($"[WARN] 워크스페이스 상태 확인 실패: {ex.Message}");
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
        private const string P4_DEV_STREAM         = "//GW/dev";
        private const string P4_ART_DEV_STREAM     = "//GWArt/ArtDev";
        private const string P4SYNC_TARGET_DEPOT   = "//GW/dev/...";
        /// <summary>DataTableGenerate 제출 CL 조회 시 추가로 스캔하는 depot (GW/dev 외 경로).</summary>
        private const string P4SYNC_DATATABLE_DEPOT_EXTRA = "//streamDepot/dev/DataTable/...";
        private const string P4SYNC_JENKINS_USER   = "gw_build";
        private const string P4SYNC_JENKINS_CLIENT = "jenkins-Agent-Win-GW_ProjectBuild";
        private const string P4SYNC_TAG            = "#JenkinsBuild";
        private const string P4SYNC_DATATABLE_TAG  = "#DataTableGenerate";
        private const int    P4SYNC_PROJECTBUILD_SCAN_COUNT = 5;

        private static readonly string[] P4SYNC_DATATABLE_DEPOT_PATHS =
        {
            P4SYNC_TARGET_DEPOT,
            P4SYNC_DATATABLE_DEPOT_EXTRA
        };

        private static string GetDepotPrefix(string depotSpec)
        {
            int idx = depotSpec.IndexOf("...", StringComparison.Ordinal);
            return idx >= 0 ? depotSpec[..idx] : depotSpec;
        }

        private static bool IsArtDevP4Stream(string? clientStream) =>
            string.Equals(clientStream?.Trim(), P4_ART_DEV_STREAM, StringComparison.OrdinalIgnoreCase);

        private static bool IsDevP4Stream(string? clientStream) =>
            string.Equals(clientStream?.Trim(), P4_DEV_STREAM, StringComparison.OrdinalIgnoreCase);

        private static string GetP4StreamCategoryLabel(string? clientStream)
        {
            if (IsDevP4Stream(clientStream)) return "개발 스트림";
            if (IsArtDevP4Stream(clientStream)) return "아트 스트림";
            return "기타";
        }

        private static string FormatP4ClientStreamDisplay(string? clientStream)
        {
            string stream = clientStream?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(stream))
                return "-";
            return $"{stream} ({GetP4StreamCategoryLabel(stream)})";
        }

        /// <summary>p4 changes -m1 공용 실행. 실패 시 -1 (zeroOnFailure면 0).</summary>
        private async Task<int> QueryLatestChangeByArgsAsync(
            string changesArg,
            string root,
            Action<string>? log,
            string queryLabel,
            bool zeroOnFailure = false)
        {
            void LogMsg(string msg) => log?.Invoke(msg);
            var (exit, stdout, stderr) = await RunProcessCaptureAsync("p4", $"changes -m1 {changesArg}", root);
            if (exit != 0)
            {
                LogMsg($"[WARN] {queryLabel} 조회 실패 (ExitCode={exit})" +
                       (zeroOnFailure ? ". LOCAL_CL=0 가정" : ""));
                if (!string.IsNullOrWhiteSpace(stderr)) LogMsg("[ERR] " + stderr.Trim());
                return zeroOnFailure ? 0 : -1;
            }

            int cl = ParseChangeNumber(stdout);
            if (cl < 0)
            {
                LogMsg($"[WARN] {queryLabel} 파싱 실패");
                return zeroOnFailure ? 0 : -1;
            }

            return cl;
        }

        /// <summary>로컬 워크스페이스 최신 CL (p4 changes -m1 @workspace).</summary>
        private async Task<int> QueryLocalChangeAsync(string ws, string root, Action<string>? log = null)
        {
            void LogMsg(string msg) => log?.Invoke(msg);
            if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(root))
            {
                LogMsg("[WARN] Workspace/Client Root 확인 불가. LOCAL_CL=0 가정");
                return 0;
            }

            LogMsg("[INFO] Local CL 조회 중...");
            int localCL = await QueryLatestChangeByArgsAsync(
                $"@{ws}", root, log, "Local CL", zeroOnFailure: true);
            LogMsg($"- Local CL: {localCL}");
            return localCL;
        }

        /// <summary>워크스페이스 스트림 서버의 최신 submitted CL (p4 changes -m1 -S stream).</summary>
        private async Task<int> QueryStreamLatestChangeAsync(string? clientStream, string root, Action<string>? log = null)
        {
            void LogMsg(string msg) => log?.Invoke(msg);
            string stream = clientStream?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(stream))
            {
                LogMsg("[WARN] clientStream이 없어 Stream Latest CL을 조회할 수 없습니다.");
                return -1;
            }

            LogMsg($"[INFO] Stream Latest CL 조회 중... ({stream})");
            int cl = await QueryLatestChangeByArgsAsync($"-S {stream}", root, log, "Stream Latest CL");
            if (cl < 0)
            {
                string depotSpec = stream.EndsWith("/...", StringComparison.Ordinal) ? stream : $"{stream}/...";
                LogMsg($"[DEBUG] -S 조회 실패, depot 경로로 재시도: {depotSpec}");
                cl = await QueryLatestChangeByArgsAsync(depotSpec, root, log, "Stream Latest CL");
            }

            if (cl > 0)
                LogMsg($"- Stream Latest CL: {cl}");
            return cl;
        }

        private static bool IsDataSyncTargetDepotFile(string depotFileWithRevision)
        {
            int revIdx = depotFileWithRevision.LastIndexOf('#');
            string depotFile = revIdx > 0 ? depotFileWithRevision[..revIdx] : depotFileWithRevision;
            foreach (var depotSpec in P4SYNC_DATATABLE_DEPOT_PATHS)
            {
                string prefix = GetDepotPrefix(depotSpec);
                if (depotFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private async Task<List<string>> QueryDataTableSyncFileRevisionsByChangeAsync(
            int cl, string root, Action<string>? log = null)
        {
            void LogMsg(string msg) => log?.Invoke(msg);
            var (exitDesc, outDesc, errDesc) = await RunProcessCaptureAsync("p4", $"describe -s {cl}", root);
            if (exitDesc != 0)
            {
                LogMsg($"[WARN] Data Sync describe 실패 (CL={cl}, ExitCode={exitDesc})");
                if (!string.IsNullOrWhiteSpace(errDesc)) LogMsg("[ERR] " + errDesc.Trim());
                return new List<string>();
            }

            var result = new List<string>();
            foreach (var line in outDesc.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                // 예: ... //GW/dev/Some/File.uasset#17 edit
                var m = Regex.Match(line, @"^\.\.\.\s+(//\S+#\d+)\s+\w+", RegexOptions.CultureInvariant);
                if (!m.Success) continue;

                string fileWithRev = m.Groups[1].Value;
                if (!IsDataSyncTargetDepotFile(fileWithRev))
                    continue;

                result.Add(fileWithRev);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>GW_ProjectBuild CL 조회·태그 스캔 후 Local CL과 비교 (Sync 필요여부 판단).</summary>
        private async Task<(int buildCL, bool needSync, string status, int statusKind)> QueryProjectBuildClStateAsync(
            string root, int localCL, Action<string>? log = null)
        {
            void LogMsg(string msg) => log?.Invoke(msg);

            if (string.IsNullOrWhiteSpace(root))
                return (-1, false, "Workspace/Client Root 확인 불가", 0);

            LogMsg("");
            LogMsg($"[INFO] GW_ProjectBuild CL scan (최근 {P4SYNC_PROJECTBUILD_SCAN_COUNT}개)...");
            var (exitCandidates, outCandidates, errCandidates) =
                await RunProcessCaptureAsync("p4", $"changes -u {P4SYNC_JENKINS_USER} -c {P4SYNC_JENKINS_CLIENT} -m{P4SYNC_PROJECTBUILD_SCAN_COUNT} {P4SYNC_TARGET_DEPOT}", root);

            int buildCL = -1;
            if (exitCandidates != 0)
            {
                LogMsg($"[WARN] GW_ProjectBuild CL 조회 실패 (ExitCode={exitCandidates})");
                if (!string.IsNullOrWhiteSpace(errCandidates)) LogMsg("[ERR] " + errCandidates.Trim());
                return (-1, false, "GW_ProjectBuild CL 조회 실패", 0);
            }
            else
            {
                var candidateCLs = new List<int>();
                foreach (var line in outCandidates.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int cl = ParseChangeNumber(line);
                    if (cl > 0) candidateCLs.Add(cl);
                }

                if (candidateCLs.Count == 0)
                {
                    LogMsg("[WARN] GW_ProjectBuild CL이 없습니다.");
                    return (-1, false, "GW_ProjectBuild CL 없음", 0);
                }

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
            }

            if (buildCL <= 0)
            {
                LogMsg($"[WARN] 최근 {P4SYNC_PROJECTBUILD_SCAN_COUNT}개 CL에서 태그({P4SYNC_TAG})를 찾지 못했습니다.");
                return (-1, false, "태그 탐지 실패", 0);
            }

            LogMsg($"[INFO] GW_ProjectBuild CL: {buildCL}");

            // Local CL vs Build CL 비교 (statusKind: 1=동기화필요, 2=동일, 3=주의)
            bool needSync = buildCL > localCL;
            string status;
            int statusKind;
            if (needSync)
            {
                status = "배포된 프로젝트 바이너리가 있습니다. 동기화 필요 합니다.";
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
            return (buildCL, needSync, status, statusKind);
        }

        /// <summary>Local CL + GW_ProjectBuild CL + Sync 필요여부 (Sync 버튼 등 단독 호출용).</summary>
        private async Task<(int localCL, int buildCL, bool needSync, string status, int statusKind)> QueryP4SyncClStateAsync(
            string ws, string root, Action<string>? log = null)
        {
            int localCL = await QueryLocalChangeAsync(ws, root, log);
            var (buildCL, needSync, status, statusKind) = await QueryProjectBuildClStateAsync(root, localCL, log);
            return (localCL, buildCL, needSync, status, statusKind);
        }

        private async void BtnGWEditorSync_Click(object sender, RoutedEventArgs e)
        {
            if (BtnGWEditorSync != null)
                BtnGWEditorSync.IsEnabled = false;

            bool gwEditorFullLock = false;
            try
            {
                string ws = _gwEditorWorkspaceName ?? "";
                string root = _gwEditorClientRoot ?? "";
                string workspaceDisplay = FormatGWEditorWorkspaceDisplay(ws, root);

                if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(root))
                {
                    AppendSharedLog("[WARN] Workspace/Client Root를 확인할 수 없습니다. [새로고침] 후 다시 시도하세요.");
                    MessageBox.Show("Workspace/Client Root를 확인할 수 없습니다.\n\n[새로고침] 후 다시 시도하세요.",
                        "실행 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var confirm = MessageBox.Show(
                    $"아래 워크스페이스 기준으로 Sync를 진행합니다.\n\n{workspaceDisplay}\n\n진행할까요?",
                    "p4 sync 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    AppendSharedLog("[INFO] 사용자가 취소했습니다.");
                    return;
                }

                SetGWEditorInteractionLocked(true);
                gwEditorFullLock = true;

                AppendSharedLog("=== p4 sync 시작 ===");
                if (IsArtDevP4Stream(_gwEditorClientStream))
                {
                    int localCL = await QueryLocalChangeAsync(ws, root, AppendSharedLog);
                    AppendSharedLog("");
                    AppendSharedLog($"[INFO] ArtDev 스트림: p4 sync 실행 (Local CL={localCL})");
                    int code = await RunProcessAsync("p4", "sync", AppendSharedLog, root);
                    if (code != 0)
                    {
                        AppendSharedLog($"[ERROR] p4 sync 실패 (ExitCode={code})");
                        return;
                    }

                    AppendSharedLog("");
                    AppendSharedLog("[OK] ArtDev 스트림 워크스페이스 동기화가 완료되었습니다.");
                }
                else
                {
                    var (localCL, buildCL, _, _, _) = await QueryP4SyncClStateAsync(ws, root, AppendSharedLog);
                    if (buildCL <= 0)
                    {
                        AppendSharedLog("[WARN] 유효한 GW_ProjectBuild CL을 찾지 못해 sync를 중단합니다.");
                        return;
                    }

                    AppendSharedLog("");
                    AppendSharedLog($"[4/4] GW_ProjectBuild 기준 p4 sync ...@{buildCL} 실행 (Local CL={localCL})");
                    int code = await RunProcessAsync("p4", $"sync ...@{buildCL}", AppendSharedLog, root);
                    if (code != 0)
                    {
                        AppendSharedLog($"[ERROR] p4 sync 실패 (ExitCode={code})");
                        return;
                    }

                    AppendSharedLog("");
                    AppendSharedLog($"[OK] 로컬 워크스페이스가 최신 GW_ProjectBuild CL {buildCL} 까지 동기화되었습니다.");
                }
                AppendSharedLog("=== p4 sync 완료 ===");
            }
            finally
            {
                if (gwEditorFullLock)
                    SetGWEditorInteractionLocked(false);
                try { await RefreshGWEditorP4InfoAsync(); }
                catch { if (BtnGWEditorSync != null) BtnGWEditorSync.IsEnabled = false; }
            }
        }

        private async Task<(int localCL, int latestServerCL, List<int> targetCLs)> QueryDataTableSyncTargetsAsync(
            string ws, string root, int? knownLocalCL = null, Action<string>? log = null)
        {
            void LogMsg(string msg) => log?.Invoke(msg);

            if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(root))
                return (0, -1, new List<int>());

            int localCL = knownLocalCL ?? await QueryLocalChangeAsync(ws, root, log);

            int fromCL = Math.Max(localCL + 1, 1);
            LogMsg($"[INFO] gw_build submitted CL 조회 중... (Range: {fromCL},now)");
            LogMsg($"[INFO] 대상 depot: {P4SYNC_TARGET_DEPOT}, {P4SYNC_DATATABLE_DEPOT_EXTRA}");

            var submitCLs = new List<int>();
            bool anySubmitQueryOk = false;

            foreach (var depot in new[] { P4SYNC_TARGET_DEPOT, P4SYNC_DATATABLE_DEPOT_EXTRA })
            {
                var spec = $"{depot}@{fromCL},now";
                var (exitChanges, outChanges, errChanges) = await RunProcessCaptureAsync(
                    "p4",
                    $"changes -s submitted -u {P4SYNC_JENKINS_USER} {spec}",
                    root);

                if (exitChanges != 0)
                {
                    LogMsg($"[WARN] 서버 제출 CL 조회 실패 ({depot}, ExitCode={exitChanges})");
                    if (!string.IsNullOrWhiteSpace(errChanges)) LogMsg("[ERR] " + errChanges.Trim());
                    continue;
                }

                anySubmitQueryOk = true;
                foreach (var line in outChanges.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int cl = ParseChangeNumber(line);
                    if (cl > localCL)
                        submitCLs.Add(cl);
                }
            }

            if (!anySubmitQueryOk)
            {
                LogMsg("[WARN] 모든 depot에 대해 서버 제출 CL 조회에 실패했습니다.");
                return (localCL, -1, new List<int>());
            }

            var orderedSubmitCLs = submitCLs
                .Distinct()
                .OrderBy(cl => cl)
                .ToList();

            if (orderedSubmitCLs.Count == 0)
            {
                LogMsg($"[INFO] Local CL({localCL}) 이후 gw_build 제출 CL이 없습니다.");
                return (localCL, localCL, new List<int>());
            }

            int latestServerCL = orderedSubmitCLs[^1];
            LogMsg($"[INFO] 검사 구간: {localCL} ~ {latestServerCL} (총 {orderedSubmitCLs.Count}개 CL)");

            var targetCLs = new List<int>();
            foreach (var cl in orderedSubmitCLs)
            {
                var (exitDesc, outDesc, errDesc) = await RunProcessCaptureAsync("p4", $"describe -s {cl}", root);
                if (exitDesc != 0)
                {
                    LogMsg($"[WARN] describe 실패 (CL={cl}, ExitCode={exitDesc})");
                    if (!string.IsNullOrWhiteSpace(errDesc)) LogMsg("[ERR] " + errDesc.Trim());
                    continue;
                }

                if (outDesc.IndexOf(P4SYNC_DATATABLE_TAG, StringComparison.OrdinalIgnoreCase) >= 0)
                    targetCLs.Add(cl);
            }

            return (localCL, latestServerCL, targetCLs);
        }

        private async void BtnGWEditorDataSync_Click(object sender, RoutedEventArgs e)
        {
            if (BtnGWEditorDataSync != null)
                BtnGWEditorDataSync.IsEnabled = false;

            bool gwEditorFullLock = false;
            try
            {
                string ws = _gwEditorWorkspaceName ?? "";
                string root = _gwEditorClientRoot ?? "";
                string workspaceDisplay = FormatGWEditorWorkspaceDisplay(ws, root);

                if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(root))
                {
                    AppendSharedLog("[WARN] Workspace/Client Root를 확인할 수 없습니다. [새로고침] 후 다시 시도하세요.");
                    MessageBox.Show("Workspace/Client Root를 확인할 수 없습니다.\n\n[새로고침] 후 다시 시도하세요.",
                        "실행 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var confirm = MessageBox.Show(
                    $"아래 워크스페이스 기준으로 Data Sync를 진행합니다.\n\n{workspaceDisplay}\n\n진행할까요?",
                    "Data Sync 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    AppendSharedLog("[INFO] 사용자가 취소했습니다.");
                    return;
                }

                SetGWEditorInteractionLocked(true);
                gwEditorFullLock = true;

                AppendSharedLog("=== Data Sync 시작 ===");
                var (localCL, latestServerCL, targetCLs) =
                    await QueryDataTableSyncTargetsAsync(ws, root, log: AppendSharedLog);

                if (latestServerCL < 0)
                {
                    AppendSharedLog("[WARN] 서버 제출 CL 조회에 실패하여 Data Sync를 중단합니다.");
                    return;
                }

                if (targetCLs.Count == 0)
                {
                    AppendSharedLog($"[INFO] 검사 구간(Local {localCL} ~ Server {latestServerCL})에서 {P4SYNC_DATATABLE_TAG} 태그 CL이 없어 sync 생략.");
                    return;
                }

                AppendSharedLog($"[INFO] Data Sync 대상 CL(#DataTableGenerate): {string.Join(", ", targetCLs)}");
                foreach (var cl in targetCLs)
                {
                    var filesAtChange = await QueryDataTableSyncFileRevisionsByChangeAsync(cl, root, AppendSharedLog);
                    if (filesAtChange.Count == 0)
                    {
                        AppendSharedLog($"[WARN] CL {cl}에서 Data Sync 대상 파일을 찾지 못해 건너뜁니다.");
                        continue;
                    }

                    const int syncChunkSize = 50;
                    AppendSharedLog("");
                    AppendSharedLog($"[SYNC] CL {cl}: 대상 파일 {filesAtChange.Count}개");

                    for (int i = 0; i < filesAtChange.Count; i += syncChunkSize)
                    {
                        var chunk = filesAtChange.Skip(i).Take(syncChunkSize);
                        string syncArgs = "sync " + string.Join(" ", chunk.Select(f => $"\"{f}\""));
                        int code = await RunProcessAsync("p4", syncArgs, AppendSharedLog, root);
                        if (code != 0)
                        {
                            AppendSharedLog($"[ERROR] Data Sync 실패 (CL={cl}, ExitCode={code})");
                            return;
                        }
                    }
                }

                AppendSharedLog("");
                AppendSharedLog($"[OK] Data Sync 완료. 총 {targetCLs.Count}개 CL 반영");
                AppendSharedLog("=== Data Sync 완료 ===");
            }
            finally
            {
                if (gwEditorFullLock)
                    SetGWEditorInteractionLocked(false);
                try { await RefreshGWEditorP4InfoAsync(); }
                catch { if (BtnGWEditorDataSync != null) BtnGWEditorDataSync.IsEnabled = false; }
            }
        }

        private async void BtnGWEditorLocalRollback_Click(object sender, RoutedEventArgs e)
        {
            string buildCLText = (TbGWEditorBuildCL?.Text ?? "").Trim();
            if (!int.TryParse(buildCLText, out int buildCL) || buildCL <= 0)
            {
                AppendSharedLog("[WARN] GW_ProjectBuild CL이 유효하지 않습니다. [새로고침] 후 다시 시도하세요.");
                return;
            }

            string root = _gwEditorClientRoot ?? "";
            if (string.IsNullOrWhiteSpace(root))
            {
                AppendSharedLog("[WARN] Client Root를 확인할 수 없습니다. [새로고침] 후 다시 시도하세요.");
                MessageBox.Show("Client Root를 확인할 수 없습니다.\n\n[새로고침] 후 다시 시도하세요.", "실행 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string localCLText = (TbGWEditorLocalCL?.Text ?? "0").Trim();
            int localCL = int.TryParse(localCLText, out int lcl) ? lcl : 0;
            if (localCL <= buildCL)
            {
                AppendSharedLog("[WARN] Local Rollback은 Local CL이 GW_ProjectBuild CL보다 클 때만 사용할 수 있습니다.");
                return;
            }

            var confirm = MessageBox.Show(
                $"로컬 워크스페이스를 GW_ProjectBuild CL {buildCL} 상태로 되돌립니다.\n\n열려 있는 파일이 있으면 진행이 되지 않습니다.\n\n진행할까요?",
                "Local Rollback 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                AppendSharedLog("[INFO] 사용자가 취소했습니다.");
                return;
            }

            SetGWEditorInteractionLocked(true);
            try
            {
                AppendSharedLog($"=== Local Rollback: p4 sync //...@{buildCL} ===");
                int code = await RunProcessAsync("p4", $"sync //...@{buildCL}", AppendSharedLog, root);
                if (code != 0)
                    AppendSharedLog($"[ERROR] p4 sync 실패 (ExitCode={code})");
                else
                    AppendSharedLog("=== Local Rollback 완료 ===");
            }
            finally
            {
                SetGWEditorInteractionLocked(false);
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

            if (runClient && string.IsNullOrWhiteSpace(selected.FileName))
            {
                AppendLog("[WARN] 선택한 빌드에는 클라이언트(WIN) 패키지가 없습니다. 클라이언트 실행을 끄거나 다른 빌드를 선택하세요.");
                MessageBox.Show(
                    "선택한 빌드에는 클라이언트(WIN) 패키지가 없습니다.\nDS만 있는 빌드에서는 클라이언트 실행을 해제하세요.",
                    "실행 대상",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SetGameStarterInteractionLocked(true);

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
                SetGameStarterInteractionLocked(false);
                SetGameStarterProgress(false, 0, null);
            }
        }

        private async Task DownloadSelectedBuildOnlyAsync()
        {
            if (LvBuilds.SelectedItem is not ServerBuildItem selected)
            {
                AppendLog("[WARN] 다운로드할 빌드를 선택하세요.");
                return;
            }

            bool wantClient = CbRunClient.IsChecked == true;
            bool wantDS = CbRunDS.IsChecked == true;

            if (!wantClient && !wantDS)
            {
                AppendLog("[WARN] 클라이언트 또는 DS 중 하나 이상을 선택한 뒤 다운로드를 눌러주세요.");
                MessageBox.Show("클라이언트 또는 DS 중 하나 이상을 선택한 뒤 다운로드해주세요.", "다운로드 대상 선택", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (wantClient && string.IsNullOrWhiteSpace(selected.FileName))
            {
                AppendLog("[WARN] 선택한 빌드에는 클라이언트(WIN) 패키지가 없습니다. 클라이언트 다운로드를 끄거나 다른 빌드를 선택하세요.");
                MessageBox.Show(
                    "선택한 빌드에는 클라이언트(WIN) 패키지가 없습니다.\nDS만 있는 빌드에서는 클라이언트 다운로드를 해제하세요.",
                    "다운로드 대상",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SetGameStarterInteractionLocked(true);

            try
            {
                string winZip = selected.FileName;
                string dsZip = !string.IsNullOrWhiteSpace(selected.DsFileName)
                    ? selected.DsFileName
                    : Path.GetFileNameWithoutExtension(winZip) + "_DS.zip";

                await _launcher.PrepareBuildsOnlyAsync(winZip, dsZip, wantClient, wantDS, ReportGameStarterProgress);
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 다운로드 중 오류가 발생했습니다.");
                AppendLog(ex.Message);
                MessageBox.Show(
                    $"다운로드 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "다운로드 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetGameStarterInteractionLocked(false);
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

        // Engine(Installed Build) 버전 — 변경이 거의 없어 드롭다운 없이 UE5.6 고정 사용
        private readonly string _engineVersion = "UE5.6";

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
        private async void BtnEngineDownloadInstall_Click(object sender, RoutedEventArgs e)
        {
            await DownloadInstalledBuildAsync(installAfterDownload: true);
        }

        private bool _engineSectionAutoStateApplied = false;

        private async Task RefreshEngineStatusAsync()
        {
            try
            {
                PbEngine.IsIndeterminate = true;
                TxtEngineProgress.Text = "상태 확인 중...";
                BtnEngineDownloadInstall.IsEnabled = false;

                string basePath = TbEngineBasePath.Text;
                string installRoot = GetInstallRoot(basePath, _engineVersion);

                Directory.CreateDirectory(installRoot);

                // 서버 latest.json
                _engineLatest = await InstalledBuildServices.GetLatestAsync(_engineVersion, AppendSharedLog);

                // 로컬 meta
                _engineLocalMeta = InstalledBuildServices.TryLoadLocalMeta(installRoot);

                bool needUpdate = _engineLatest != null &&
                                (_engineLocalMeta == null || !string.Equals(_engineLocalMeta.label, _engineLatest.label, StringComparison.OrdinalIgnoreCase));

                // 섹션 헤더(상태 점 + 요약 + 배경색)에 서버/로컬 상태를 반영 — 본문의 서버최신/로컬상태 박스는 제거됨(v25 개편)
                int engineStatusKind = _engineLocalMeta == null ? 1 : (needUpdate ? 3 : 2);
                if (TxtEngineSectionSummary != null)
                {
                    TxtEngineSectionSummary.Text = _engineLocalMeta == null
                        ? "미설치"
                        : $"{_engineLocalMeta.label} · {(needUpdate ? "업데이트 필요" : "최신")}";
                }
                if (EngineSectionStatusDot != null)
                    EngineSectionStatusDot.Fill = GetSectionStatusDotBrush(engineStatusKind);
                if (EngineSectionHeaderBorder != null)
                    EngineSectionHeaderBorder.Background = GetSectionHeaderBackgroundBrush(engineStatusKind);

                // 최초 상태 확인 시에만 자동 펼침/접힘 적용(이후엔 사용자가 직접 펼치고 접은 상태를 존중)
                if (!_engineSectionAutoStateApplied)
                {
                    ExpanderEngine.IsExpanded = needUpdate || _engineLocalMeta == null;
                    _engineSectionAutoStateApplied = true;
                }

                BtnEngineDownloadInstall.IsEnabled = _engineLatest != null && needUpdate && !_engineWorking;

                TxtEngineProgress.Text = needUpdate ? "업데이트 필요" : "최신 상태";
            }
            catch (Exception ex)
            {
                AppendSharedLog($"[ERROR] 상태 확인 실패: {ex.Message}");
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
                    _engineLatest = await InstalledBuildServices.GetLatestAsync(_engineVersion, AppendSharedLog);

                if (_engineLatest == null)
                {
                    AppendSharedLog("[ERROR] 서버 latest.json 로드 실패");
                    return;
                }

                string basePath = TbEngineBasePath.Text;
                string installRoot = GetInstallRoot(basePath, _engineVersion);
                Directory.CreateDirectory(installRoot);

                // zip 저장 위치: InstallRoot\{label}.zip
                string zipPath = Path.Combine(installRoot, $"{_engineLatest.label}.zip");

                BtnEngineDownloadInstall.IsEnabled = false;

                // 다운로드 (이미 존재하면 size/sha256로 재사용 가능)
                await InstalledBuildServices.DownloadZipAsync(
                    url: _engineLatest.zip.url,
                    destZipPath: zipPath,
                    expectedSize: _engineLatest.zip.size,
                    log: AppendSharedLog,
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
                    AppendSharedLog);

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
                    log: AppendSharedLog);

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

                AppendSharedLog("[SUCCESS] 설치 완료 및 meta 갱신");

                // 이전 버전 zip 정리: 방금 설치에 사용한 zip만 남기고 InstallRoot에 쌓인 나머지 zip은 삭제(용량 누적 방지)
                CleanupOldEngineZips(installRoot, zipPath);

                TxtEngineProgress.Text = "설치 완료";

                await RefreshEngineStatusAsync();
            }
            catch (Exception ex)
            {
                AppendSharedLog($"[ERROR] 다운로드/설치 실패: {ex.Message}");
                TxtEngineProgress.Text = "오류";
            }
            finally
            {
                PbEngine.IsIndeterminate = false;
                PbEngine.Value = 0;
                _engineWorking = false;
                BtnEngineDownloadInstall.IsEnabled = true;
            }
        }

        /// <summary>
        /// InstallRoot에 남아있는 이전 버전 엔진 zip 파일들을 정리한다.
        /// 버전업이 반복되면 zip이 계속 쌓이는 것을 막기 위해, 방금 설치에 사용한 zip(keepZipPath) 하나만 남기고 나머지 *.zip은 삭제한다.
        /// </summary>
        private void CleanupOldEngineZips(string installRoot, string keepZipPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                    return;

                string keepFileName = Path.GetFileName(keepZipPath);
                foreach (var zip in Directory.EnumerateFiles(installRoot, "*.zip"))
                {
                    if (string.Equals(Path.GetFileName(zip), keepFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        File.Delete(zip);
                        AppendSharedLog($"[INFO] 이전 버전 zip 삭제: {Path.GetFileName(zip)}");
                    }
                    catch (Exception ex)
                    {
                        AppendSharedLog($"[WARN] 이전 zip 삭제 실패({Path.GetFileName(zip)}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSharedLog($"[WARN] 이전 zip 정리 중 오류: {ex.Message}");
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

    }
}
