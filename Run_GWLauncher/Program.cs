using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Run_GWLauncher
{
    internal class Program
    {
        private const string ServerBaseUrl =
            "http://bravo-build.omnicraftlabs.co.kr/launcher/launcher.json";

        private static readonly string LocalRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GWLauncher");

        private static readonly string StateFilePath =
            Path.Combine(LocalRoot, "launcher_state.json");

        private static async Task Main()
        {
            Directory.CreateDirectory(LocalRoot);

            LauncherState localState = LoadLauncherState();
            Console.WriteLine($"[LOCAL] InstalledVersion = {localState.InstalledVersion}");
            Console.WriteLine("");

            LauncherRemoteInfo? remote = await FetchRemoteInfo();
            if (remote == null)
            {
                Console.WriteLine("[ERROR] 서버에서 launcher.json 을 읽지 못했습니다.");
                return;
            }

            Console.WriteLine($"[REMOTE] LatestVersion       = {remote.LatestVersion}");
            Console.WriteLine($"[REMOTE] MinSupportedVersion = {remote.MinSupportedVersion}");
            Console.WriteLine($"[REMOTE] Package URL         = {remote.Package.DownloadUrl}");
            Console.WriteLine("");

            // ================================
            // 업데이트 필요 여부 판단
            // ================================
            if (localState.InstalledVersion < remote.MinSupportedVersion)
            {
                Console.WriteLine("[INFO] 강제 업데이트 필요");
                await InstallLauncher(remote);
            }
            else if (localState.InstalledVersion < remote.LatestVersion)
            {
                Console.WriteLine("[INFO] 새로운 버전이 있습니다.");
                Console.Write("업데이트 하시겠습니까? (Y/N): ");
                var key = Console.ReadKey();
                Console.WriteLine();

                if (key.Key == ConsoleKey.Y)
                    await InstallLauncher(remote);
            }
            else
            {
                Console.WriteLine("[INFO] 최신 버전이 이미 설치되어 있습니다.");
            }

            // ================================
            // 런처 실행
            // ================================
            LauncherState finalState = LoadLauncherState();

            if (!File.Exists(finalState.InstalledPath))
            {
                Console.WriteLine("[ERROR] 설치된 런처 실행 파일을 찾지 못했습니다.");
                return;
            }

            Console.WriteLine($"[INFO] 런처 실행 → {finalState.InstalledPath}");
            Process.Start(new ProcessStartInfo
            {
                FileName = finalState.InstalledPath,
                UseShellExecute = true
            });
        }

        private static LauncherState LoadLauncherState()
        {
            try
            {
                if (!File.Exists(StateFilePath))
                    return new LauncherState();

                string json = File.ReadAllText(StateFilePath);
                return JsonSerializer.Deserialize<LauncherState>(json) ?? new LauncherState();
            }
            catch
            {
                return new LauncherState();
            }
        }

        private static void SaveLauncherState(LauncherState state)
        {
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(StateFilePath, json);
        }

        private static async Task<LauncherRemoteInfo?> FetchRemoteInfo()
        {
            try
            {
                using var client = new HttpClient();
                string json = await client.GetStringAsync(ServerBaseUrl);

                return JsonSerializer.Deserialize<LauncherRemoteInfo>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] launcher.json GET 실패");
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        private static async Task InstallLauncher(LauncherRemoteInfo remote)
        {
            Console.WriteLine("[INFO] 업데이트 패키지 다운로드 중...");

            string versionFolder =
                Path.Combine(LocalRoot, "Launcher", $"v{remote.LatestVersion}");
            Directory.CreateDirectory(versionFolder);

            string zipPath = Path.Combine(versionFolder, remote.Package.FileName);

            try
            {
                using var client = new HttpClient();
                using var resp = await client.GetAsync(remote.Package.DownloadUrl);
                resp.EnsureSuccessStatusCode();

                await using var fs = File.Create(zipPath);
                await resp.Content.CopyToAsync(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] 런처 ZIP 다운로드 실패");
                Console.WriteLine(ex.Message);
                return;
            }

            Console.WriteLine("[INFO] 압축 해제 중...");

            try
            {
                // 압축해제 준비
                string extractPath = Path.Combine(versionFolder, "Extracted");
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // 실제 런처 exe 찾기
                string launcherExe = Path.Combine(extractPath, "GWLauncher.exe");
                if (!File.Exists(launcherExe))
                {
                    Console.WriteLine("[ERROR] 압축 해제 후 GWLauncher.exe 파일이 없습니다.");
                    return;
                }

                // State 저장
                var newState = new LauncherState
                {
                    InstalledVersion = remote.LatestVersion,
                    InstalledPath = launcherExe,
                    LastCheckedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                SaveLauncherState(newState);

                Console.WriteLine("[INFO] 업데이트 완료!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] 압축 해제 중 오류");
                Console.WriteLine(ex.Message);
            }
        }
    }
}
