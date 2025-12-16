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

namespace BravoGameLauncherGui
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly GameBuildLauncher _launcher;

        private class ServerBuildItem
        {
            public string FileName  { get; set; } = string.Empty;
            public string Config    { get; set; } = string.Empty;
            public string Version   { get; set; } = string.Empty;
            public int    CL        { get; set; }
            public string BuildDate { get; set; } = string.Empty; // yyyy-MM-dd
            public string BuildTime { get; set; } = string.Empty; // HH:mm:ss
            public DateTime SortKey { get; set; }                 // 내림차순 정렬용
            public string Platform  { get; set; } = string.Empty; // WIN / DS / ...
            public int JenkinsNumber { get; set; }

        }

        private List<ServerBuildItem> _allBuilds = new();

        public MainWindow()
        {
            InitializeComponent();

            // 설정 로드
            _settings = AppSettings.Load();

            // 런처 생성 (캐시 루트 경로를 설정에서 가져옴)
            _launcher = new GameBuildLauncher(AppendLog, _settings.RootDownloadDir);

            // UI 초기화
            TxtCachePath.Text = _launcher.RootDownloadDir;
            AppendLog($"=== {LauncherVersionInfo.WindowTitle} ===");
            AppendLog($"현재 런처 버전: {LauncherVersionInfo.VersionForServer}");
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
        // 로그 출력
        // ================================
        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }

        // ================================
        // 서버에서 빌드 목록 가져오기 (JSON v1/v2 공통)
        // ================================
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

                _allBuilds.Clear();

                foreach (var item in result.Builds)
                {
                    if (item == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(item.FileName))
                        continue;

                    // 현재 UI는 WIN 빌드만 표시 (기존 동작 유지)
                    if (!string.Equals(item.Platform, "WIN", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parse = ParseBuildInfoFromFileName(item.FileName);

                    // JSON의 buildTime이 있으면 우선 사용, 없으면 파일명 기반 타임스탬프 사용
                    var dt = item.BuildTime ?? parse.Timestamp ?? DateTime.MinValue;

                    _allBuilds.Add(new ServerBuildItem
                    {
                        FileName  = item.FileName,
                        Config    = string.IsNullOrWhiteSpace(item.Config)
                            ? GetBuildConfigFromFileName(item.FileName)
                            : item.Config,
                        Version   = parse.Version ?? string.Empty,
                        CL        = parse.CL,
                        JenkinsNumber = item.JenkinsBuildNumber,
                        BuildDate = dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd"),
                        BuildTime = dt == DateTime.MinValue ? "" : dt.ToString("HH:mm:ss"),
                        SortKey   = dt,
                        Platform  = item.Platform
                    });
                }

                AppendLog($"[INFO] 서버 빌드 리스트 {_allBuilds.Count}개 로드 완료.");
                RefreshBuildListUI();
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 서버에서 빌드 리스트를 가져오는 중 예외가 발생했습니다.");
                AppendLog(ex.Message);
            }
            finally
            {
                if (BtnRefreshFromServer != null)
                    BtnRefreshFromServer.IsEnabled = true;
            }
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

            LvBuilds.ItemsSource = filtered;
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
                    AppendLog("[WARN] 실행할 빌드를 선택하세요.");
                    return;
                }

                string ipAddress;

                if (CbLocal.IsChecked == true)
                {
                    ipAddress = "127.0.0.1";
                }
                else if (CbServer.IsChecked == true)
                {
                    // 프로젝트 지침에 따른 서버 IP
                    ipAddress = "100.66.7.43";
                }
                else
                {
                    // 둘 다 해제된 경우 방지: 기본 Local
                    ipAddress = "127.0.0.1";
                }

                bool useWindowed = CbWindowed.IsChecked == true;

                string fileName = selected.FileName;

                // 최근 목록 기록 유지
                _settings.AddRecentFileName(fileName);
                _settings.Save();

                // 현재는 WIN만 실행 (기존 동작 유지) → 플랫폼이 필요하면 여기서 전달 가능
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

        // ================================
        // 메뉴: 캐시 경로 변경 / 캐시 삭제 / 종료
        // ================================
        private void MenuChangeCachePath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "캐시 루트 경로를 선택하세요."
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string path = dialog.SelectedPath;
                _settings.RootDownloadDir = path;
                _settings.Save();

                TxtCachePath.Text = path;
                AppendLog($"[INFO] 캐시 루트 경로 변경: {path}");
            }
        }

        private void MenuClearCache_Click(object sender, RoutedEventArgs e)
        {
            var cacheRoot = _launcher.RootDownloadDir;
            if (!Directory.Exists(cacheRoot))
            {
                AppendLog("[INFO] 캐시 폴더가 존재하지 않습니다.");
                return;
            }

            var result = MessageBox.Show(
                $"캐시 폴더를 모두 삭제하시겠습니까?\n\n{cacheRoot}",
                "캐시 삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                Directory.Delete(cacheRoot, recursive: true);
                Directory.CreateDirectory(cacheRoot);
                AppendLog("[INFO] 캐시 폴더 삭제 후 재생성 완료.");
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 캐시 폴더 삭제 중 예외가 발생했습니다.");
                AppendLog(ex.Message);
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ================================
        // Dedicated Server 테스트 다운로드
        // ================================
        private async void BtnDownloadDsSample_Click(object sender, RoutedEventArgs e)
        {
            const string testUrl =
                "http://bravo-build.omnicraftlabs.co.kr:8000/GameBuilds/0.0.1/DS/GW_v0.0.1_CL2351_Development_20251212220043_DS.zip";

            if (BtnDownloadDsSample != null)
                BtnDownloadDsSample.IsEnabled = false;

            try
            {
                AppendLog("[INFO] Dedicated Server 테스트 빌드 다운로드를 시작합니다.");

                string? extractedPath = await _launcher.DownloadAndExtractOnlyAsync(testUrl);
                if (string.IsNullOrEmpty(extractedPath))
                {
                    AppendLog("[ERROR] DS 테스트 빌드 다운로드 또는 압축 해제에 실패했습니다.");
                    return;
                }

                AppendLog($"[INFO] DS 테스트 빌드 압축 해제 경로: {extractedPath}");

                try
                {
                    Process.Start("explorer.exe", extractedPath);
                }
                catch (Exception ex)
                {
                    AppendLog("[ERROR] 탐색기 실행 중 오류가 발생했습니다.");
                    AppendLog(ex.Message);
                }
            }
            finally
            {
                if (BtnDownloadDsSample != null)
                    BtnDownloadDsSample.IsEnabled = true;
            }
        }


        // ================================
        // 서버 새로고침 버튼
        // ================================
        private async void BtnRefreshFromServer_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFromServerAsync();
        }

        // ================================
        // Local / Server 라디오 동작
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
        // 파일명 파싱 유틸 (B안: _DS 허용)
        // ================================
        private static (string Version, int CL, DateTime? Timestamp) ParseBuildInfoFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return (string.Empty, 0, null);

            // 예: GW_v0.0.1_CL2301_Shipping_20251205123010.zip
            //     GW_v0.0.1_CL2351_Development_20251212220043_DS.zip
            // var pattern = @"^GW_v(?<ver>\d+\.\d+\.\d+)_CL(?<cl>\d+)_.*_(?<ts>\d{14})(?:_DS)?\.zip$";
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
                    null,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out var dt))
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
