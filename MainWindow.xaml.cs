﻿﻿using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms; // FolderBrowserDialog
using MessageBox = System.Windows.MessageBox;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Linq;
using System.Text.RegularExpressions;

namespace BravoGameLauncherGui
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly GameBuildLauncher _launcher;

        // 서버에서 받은 전체 빌드 목록
        private List<ServerBuildItem> _allBuilds = new();

        private class ServerBuildItem
        {
            public string FileName  { get; set; } = string.Empty;
            public string Config    { get; set; } = string.Empty;
            public string Version   { get; set; } = string.Empty;
            public int    CL        { get; set; }
            public string BuildDate { get; set; } = string.Empty; // yyyy-MM-dd
            public string BuildTime { get; set; } = string.Empty; // HH:mm:ss
            public DateTime SortKey { get; set; }                 // 내림차순 정렬용
        }

        public MainWindow()
        {
            InitializeComponent();

            // 설정 로드
            _settings = AppSettings.Load();

            // 런처 생성 (캐시 루트 경로를 설정에서 가져옴)
            _launcher = new GameBuildLauncher(AppendLog, _settings.RootDownloadDir);

            // UI 초기화
            TxtCachePath.Text = _launcher.RootDownloadDir;
            AppendLog("=== GW Launcher (GUI) ===");
            AppendLog($"캐시 루트 경로: {_launcher.RootDownloadDir}");
            AppendLog(string.Empty);

            // 빌드 타입 변경 시 목록 갱신
            CmbBuildType.SelectionChanged += (_, __) => RefreshBuildListUI();

            // Local / Server 기본값: Local만 선택
            CbLocal.IsChecked  = true;
            CbServer.IsChecked = false;

            // 창이 로드되면 자동으로 서버 목록 새로고침
            Loaded += async (_, __) => await RefreshFromServerAsync();
        }

        // ================================
        // 빌드 리스트 UI 갱신
        // ================================
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

            // 기본 선택: 첫 번째 항목
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

        // ================================
        // Local / Server 선택 (단일선택 유지)
        // ================================
        private void CbLocal_Click(object sender, RoutedEventArgs e)
        {
            if (CbLocal.IsChecked == true)
            {
                CbServer.IsChecked = false;
            }
            else
            {
                // 둘 다 해제되는 경우 방지 → 항상 하나는 선택
                if (CbServer.IsChecked != true)
                    CbLocal.IsChecked = true;
            }
        }

        private void CbServer_Click(object sender, RoutedEventArgs e)
        {
            if (CbServer.IsChecked == true)
            {
                CbLocal.IsChecked = false;
            }
            else
            {
                // 둘 다 해제되는 경우 방지 → 항상 하나는 선택
                if (CbLocal.IsChecked != true)
                    CbServer.IsChecked = true;
            }
        }

        // ================================
        // 실행 버튼
        // ================================
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

                // Local / Server 선택에 따라 IP 결정
                string ipAddress = "localhost";
                if (CbServer.IsChecked == true)
                    ipAddress = "100.66.7.43";

                bool useWindowed = CbWindowed.IsChecked == true;

                string fileName = selected.FileName;

                // 최근 목록 기록 유지
                _settings.AddRecentFileName(fileName);
                _settings.Save();

                await _launcher.RunAsync(fileName, ipAddress, useWindowed);
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

        /// <summary>
        /// 로그 출력 (UI 스레드에서 안전하게 호출)
        /// </summary>
        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }

        // ================================
        // 메뉴: 캐시 경로 변경 / 캐시 삭제 / 종료
        // ================================
        private void MenuChangeCachePath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "게임 빌드 캐시를 저장할 폴더를 선택하세요.",
                SelectedPath = _launcher.RootDownloadDir,
                ShowNewFolderButton = true
            };

            var result = dialog.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK)
                return;

            string newPath = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(newPath))
                return;

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

            if (result != MessageBoxResult.Yes)
                return;

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

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ================================
        // 빌드목록 새로고침
        // ================================
        private async void BtnRefreshFromServer_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFromServerAsync();
        }

        private async Task RefreshFromServerAsync()
        {
            if (BtnRefreshFromServer != null)
                BtnRefreshFromServer.IsEnabled = false;

            AppendLog("[INFO] 서버에서 빌드 리스트를 가져오는 중...");

            try
            {
                var result = await BuildListService.FetchBuildListAsync();
                if (result == null || result.Builds == null || result.Builds.Count == 0)
                {
                    AppendLog("[WARN] 서버에서 가져온 빌드 정보가 없습니다.");
                    _allBuilds.Clear();
                    RefreshBuildListUI();
                    return;
                }

                // buildTime 기준으로 내림차순 정렬 (서버 기준 정렬)
                result.Builds.Sort((a, b) =>
                {
                    var ta = a.BuildTime ?? DateTime.MinValue;
                    var tb = b.BuildTime ?? DateTime.MinValue;
                    return tb.CompareTo(ta);
                });

                _allBuilds = new List<ServerBuildItem>();

                foreach (var item in result.Builds)
                {
                    if (string.IsNullOrWhiteSpace(item.FileName))
                        continue;

                    var parse = ParseBuildInfoFromFileName(item.FileName);

                    var dt = item.BuildTime ?? parse.Timestamp ?? DateTime.MinValue;

                    _allBuilds.Add(new ServerBuildItem
                    {
                        FileName  = item.FileName,
                        Config    = GetBuildConfigFromFileName(item.FileName),
                        Version   = parse.Version ?? string.Empty,
                        CL        = parse.CL,
                        BuildDate = dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd"),
                        BuildTime = dt == DateTime.MinValue ? "" : dt.ToString("HH:mm:ss"),
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

        // ================================
        // 파일명 파싱 유틸
        // ================================
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
    }
}
