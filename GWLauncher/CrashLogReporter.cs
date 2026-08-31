using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
// UseWindowsForms=true + ImplicitUsings 로 System.Windows.Forms 가 global using 되어 있어
// Clipboard / DataObject 가 모호해진다(CS0104). WPF 쪽 타입을 별칭으로 고정한다.
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace BravoGameLauncherGui
{
    /// <summary>
    /// GameStarter 크래시 로그 전송 (v30, #PJTGW-3099).
    ///
    /// 기본 경로는 **사내 릴레이 서버로 자동 전송**이다.
    ///   1) 선택한 빌드의 Crashes 폴더를 zip으로 압축(사용자가 입력한 크래시 상황을 CrashReport.txt로 동봉)
    ///   2) 빌드 머신의 GWCrashRelay 로 multipart 업로드
    ///   3) 릴레이가 Slack 채널에 파일 + 메시지를 올린다
    ///
    /// Slack 봇 토큰은 릴레이 서버에만 있고 런처에는 어떤 시크릿도 두지 않는다.
    /// 릴레이가 죽었거나 응답하지 않을 때만 "클립보드 + 딥링크" 수동 경로로 폴백한다.
    /// </summary>
    public static class CrashLogReporter
    {
        /// <summary>사내 크래시 릴레이 업로드 엔드포인트(Nginx가 로컬 서비스로 프록시).</summary>
        public const string RelayUploadUrl = "http://bravo-build.omnicraftlabs.co.kr/crash-report/upload";

        /// <summary>
        /// 업로드 타임아웃. 업로드 중에는 GameStarter 탭이 잠기고 사용자가 중단할 수단이 없으므로
        /// 사내망 속도를 고려해 과하게 길지 않게 잡는다.
        /// </summary>
        private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(5);

        private static readonly HttpClient UploadHttpClient = new() { Timeout = UploadTimeout };

        /// <summary>전송 대상 Slack 채널 ID. 채널 변경 시 이 상수만 수정한다.</summary>
        public const string SlackChannelId = "C09ET0RBBBJ";

        /// <summary>
        /// slack:// 딥링크의 team 값. 값이 있으면 브라우저를 거치지 않고 데스크톱 앱을 바로 연다.
        ///
        /// KRAFTON은 Enterprise Grid라 웹 클라이언트 URL이
        /// https://app.slack.com/client/{E...}/{C...} 형태로 **조직 ID가 팀 자리에** 들어간다.
        /// 딥링크에도 같은 값을 쓴다.
        /// </summary>
        public const string SlackTeamId = "E01DL1Z9D6Z";

        /// <summary>
        /// 대상 채널의 웹 클라이언트 링크. slack:// 딥링크가 실패했을 때의 폴백이며,
        /// 워크스페이스가 URL에 확정되어 있어 기본 워크스페이스로 잘못 이동하지 않는다.
        /// </summary>
        public const string SlackChannelLink = "https://app.slack.com/client/E01DL1Z9D6Z/C09ET0RBBBJ";

        /// <summary>크래시 로그 폴더의 unpacked 기준 상대 경로.</summary>
        private const string CrashesRelativePath = @"GW\Saved\Crashes";

        /// <summary>이 크기를 넘으면 압축 전에 사용자에게 계속 여부를 확인한다.</summary>
        public const long SizeWarningThresholdBytes = 100L * 1024 * 1024;

        /// <summary>크래시 상황 입력 최대 글자 수.</summary>
        public const int MaxReportLength = 100;

        /// <summary>임시 zip 보관 일수. 이보다 오래된 zip은 새 zip 생성 시 자동 정리.</summary>
        private const int TempZipKeepDays = 7;

        /// <summary>크래시 리포트 zip을 만드는 임시 폴더.</summary>
        public static string TempZipDir =>
            Path.Combine(Path.GetTempPath(), "GWLauncher", "CrashReports");

        /// <summary>unpacked 폴더 경로로부터 크래시 로그 폴더 경로를 만든다.</summary>
        public static string GetCrashesDir(string clientUnpackDir)
        {
            if (string.IsNullOrWhiteSpace(clientUnpackDir))
                return string.Empty;

            return Path.Combine(clientUnpackDir, CrashesRelativePath);
        }

        /// <summary>크래시 로그 폴더가 존재하고 내용물이 하나라도 있는지 확인한다.</summary>
        public static bool HasCrashLogs(string crashesDir)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(crashesDir)
                       && Directory.Exists(crashesDir)
                       && Directory.EnumerateFileSystemEntries(crashesDir).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>크래시 로그 폴더의 총 용량과 파일 개수를 구한다(접근 불가 파일은 건너뜀).</summary>
        public static (long Bytes, int FileCount) Measure(string crashesDir)
        {
            long bytes = 0;
            int count = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(crashesDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        bytes += new FileInfo(file).Length;
                        count++;
                    }
                    catch
                    {
                        // 접근 불가/삭제된 파일은 집계에서 제외
                    }
                }
            }
            catch
            {
                // 폴더 열거 자체가 실패하면 0으로 둔다
            }

            return (bytes, count);
        }

        /// <summary>바이트 수를 사람이 읽기 쉬운 문자열로 변환한다.</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
            if (bytes >= 1024L * 1024)        return $"{bytes / (1024.0 * 1024):0.#} MB";
            if (bytes >= 1024L)               return $"{bytes / 1024.0:0.#} KB";
            return $"{bytes} B";
        }

        /// <summary>
        /// 크래시 로그 폴더를 zip으로 압축한다. zip 안에는 Crashes/ 하위로 원본이 들어가고,
        /// 최상위에 사용자가 입력한 상황과 환경 정보를 담은 CrashReport.txt가 함께 들어간다.
        /// </summary>
        /// <returns>생성된 zip 파일의 전체 경로</returns>
        /// <exception cref="OperationCanceledException">취소되면 만들던 zip을 지우고 throw 한다.</exception>
        public static string CreateZip(
            string crashesDir,
            string buildName,
            string reportText,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(crashesDir))
                throw new DirectoryNotFoundException($"크래시 로그 폴더를 찾을 수 없습니다: {crashesDir}");

            Directory.CreateDirectory(TempZipDir);
            CleanupOldZips();

            string zipPath = Path.Combine(
                TempZipDir,
                $"Crash_{Sanitize(buildName)}_{Sanitize(Environment.UserName)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            string root = Path.GetFullPath(crashesDir);

            try
            {
                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    // 1) 사용자 입력 + 환경 정보 (전송 경로와 무관하게 내용이 유실되지 않도록 zip 안에 항상 동봉)
                    var reportEntry = archive.CreateEntry("CrashReport.txt", CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(reportEntry.Open(), new UTF8Encoding(true)))
                    {
                        writer.Write(BuildReportFileText(buildName, reportText));
                    }

                    // 2) Crashes 폴더 원본
                    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string relative = Path.GetRelativePath(root, file);
                        string entryName = "Crashes/" + relative.Replace('\\', '/');

                        try
                        {
                            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                        }
                        catch (IOException)
                        {
                            // 게임이 아직 잡고 있는 파일 등은 건너뛴다
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // 권한 없는 파일도 건너뛴다
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 만들다 만 zip은 남기지 않는다
                TryDelete(zipPath);
                throw;
            }

            return zipPath;
        }

        /// <summary>zip 안에 동봉할 CrashReport.txt 내용.</summary>
        private static string BuildReportFileText(string buildName, string reportText)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== GW Launcher 크래시 리포트 ===");
            sb.AppendLine($"빌드      : {buildName}");
            sb.AppendLine($"보고자    : {Environment.UserName}");
            sb.AppendLine($"PC        : {Environment.MachineName}");
            sb.AppendLine($"OS        : {RuntimeInformation.OSDescription}");
            sb.AppendLine($"런처 버전 : {LauncherVersionInfo.VersionCode}");
            sb.AppendLine($"작성 시각 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("--- 크래시 상황 ---");
            sb.AppendLine(reportText ?? string.Empty);
            return sb.ToString();
        }

        /// <summary>Slack 입력창에 붙여넣을 메시지 본문.</summary>
        public static string BuildSlackMessage(string buildName, string reportText)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[GW 크래시 리포트] {buildName}");
            sb.AppendLine($"보고자: {Environment.UserName} / PC: {Environment.MachineName} / {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.Append($"상황: {reportText}");
            return sb.ToString();
        }

        /// <summary>크래시 릴레이 업로드 결과.</summary>
        public sealed record CrashUploadResult(bool Ok, string? Permalink, string? Error);

        /// <summary>
        /// 사내 크래시 릴레이 서버로 zip과 메시지를 업로드한다. 서버가 Slack 채널에 올려준다.
        /// 예외를 던지지 않고 항상 결과 객체를 돌려주므로, 실패 시 호출부가 폴백 경로를 태우면 된다.
        /// </summary>
        public static async Task<CrashUploadResult> UploadAsync(
            string zipPath,
            string buildName,
            string reportText,
            Action<double, string?>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                long length = new FileInfo(zipPath).Length;

                using var form = new MultipartFormDataContent();
                using var fileStream = File.OpenRead(zipPath);

                var fileContent = new ProgressStreamContent(fileStream, length, progress);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                form.Add(fileContent, "file", Path.GetFileName(zipPath));

                form.Add(new StringContent(reportText ?? string.Empty, Encoding.UTF8), "message");
                form.Add(new StringContent(buildName ?? string.Empty, Encoding.UTF8), "build");
                form.Add(new StringContent(Environment.UserName, Encoding.UTF8), "user");
                form.Add(new StringContent(Environment.MachineName, Encoding.UTF8), "machine");

                using var response = await UploadHttpClient.PostAsync(RelayUploadUrl, form, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                return ParseUploadResponse(response.StatusCode, body);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new CrashUploadResult(false, null, "업로드를 취소했습니다.");
            }
            catch (TaskCanceledException)
            {
                return new CrashUploadResult(false, null, "업로드 시간이 초과되었습니다.");
            }
            catch (HttpRequestException ex)
            {
                return new CrashUploadResult(false, null, $"릴레이 서버에 연결하지 못했습니다: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new CrashUploadResult(false, null, $"업로드 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>릴레이 응답(JSON)을 결과 객체로 바꾼다. JSON이 아니면 상태 코드로 판단한다.</summary>
        private static CrashUploadResult ParseUploadResponse(HttpStatusCode statusCode, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                bool ok = root.TryGetProperty("ok", out var okProp)
                          && okProp.ValueKind == JsonValueKind.True;

                string? permalink = root.TryGetProperty("permalink", out var p) ? p.GetString() : null;
                string? error     = root.TryGetProperty("error", out var e) ? e.GetString() : null;

                if (ok)
                    return new CrashUploadResult(true, permalink, null);

                return new CrashUploadResult(false, null, error ?? $"서버 오류 (HTTP {(int)statusCode})");
            }
            catch (JsonException)
            {
                if (statusCode == HttpStatusCode.OK)
                    return new CrashUploadResult(false, null, "서버 응답을 해석하지 못했습니다.");

                return new CrashUploadResult(false, null, $"서버 오류 (HTTP {(int)statusCode})");
            }
        }

        /// <summary>업로드 진행률을 보고하는 HttpContent. 1% 단위로만 보고해 UI 부하를 줄인다.</summary>
        private sealed class ProgressStreamContent : HttpContent
        {
            private const int BufferSize = 81920;

            private readonly Stream _stream;
            private readonly long _length;
            private readonly Action<double, string?>? _progress;

            public ProgressStreamContent(Stream stream, long length, Action<double, string?>? progress)
            {
                _stream = stream;
                _length = length;
                _progress = progress;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
                => CopyAsync(stream, CancellationToken.None);

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
                => CopyAsync(stream, cancellationToken);

            protected override bool TryComputeLength(out long length)
            {
                length = _length;
                return true;
            }

            private async Task CopyAsync(Stream target, CancellationToken cancellationToken)
            {
                // 리다이렉트 등으로 요청이 재시도되면 이 메서드가 다시 불린다.
                // 되감지 않으면 스트림이 EOF라 0바이트가 조용히 올라간다.
                if (_stream.CanSeek)
                    _stream.Position = 0;

                var buffer = new byte[BufferSize];
                long sent = 0;
                int lastReported = -1;
                int read;

                while ((read = await _stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    sent += read;

                    if (_progress == null || _length <= 0)
                        continue;

                    int percent = (int)(sent * 100 / _length);
                    if (percent == lastReported)
                        continue;

                    lastReported = percent;
                    _progress(percent, $"크래시 로그 업로드 {percent}% ({FormatSize(sent)} / {FormatSize(_length)})");
                }
            }
        }

        /// <summary>
        /// zip 파일을 클립보드에 올린다. 파일(CF_HDROP)과 텍스트를 함께 올려,
        /// Slack이 텍스트까지 받아주면 한 번의 붙여넣기로 파일+메시지가 들어가도록 한다.
        /// (Slack이 파일만 인식하는 경우를 대비해 안내 창에서 메시지만 다시 복사할 수 있다.)
        /// </summary>
        public static bool TryCopyToClipboard(string zipPath, string? message)
        {
            // 다른 프로세스가 클립보드를 점유 중이면 실패할 수 있어 짧게 재시도한다.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var files = new StringCollection { zipPath };

                    var data = new DataObject();
                    data.SetFileDropList(files);
                    if (!string.IsNullOrEmpty(message))
                        data.SetText(message);

                    Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch
                {
                    Thread.Sleep(150);
                }
            }

            return false;
        }

        /// <summary>메시지 텍스트만 클립보드에 올린다(안내 창의 "메시지 복사" 버튼용).</summary>
        public static bool TryCopyTextToClipboard(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetText(text ?? string.Empty);
                    return true;
                }
                catch
                {
                    Thread.Sleep(150);
                }
            }

            return false;
        }

        /// <summary>
        /// Slack 데스크톱 앱에서 대상 채널을 연다.
        /// 팀 ID가 지정되어 있으면 브라우저를 거치지 않는 slack:// 딥링크를 우선 사용하고,
        /// 실패하거나 팀 ID가 없으면 웹 app_redirect로 넘어간다.
        /// (팀 ID 없이 app_redirect만 쓰면 채널이 없는 기본 워크스페이스로 이동해 오류 페이지가 뜬다.)
        /// </summary>
        public static bool TryOpenSlackChannel()
        {
            foreach (string url in BuildChannelUrls())
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    return true;
                }
                catch
                {
                    // 다음 후보 URL로 넘어간다 (예: slack:// 핸들러 미등록)
                }
            }

            return false;
        }

        /// <summary>
        /// 채널을 여는 데 시도할 URL 목록을 우선순위대로 만든다.
        ///   1) slack:// 딥링크(팀 ID가 있을 때만 — 브라우저를 거치지 않고 데스크톱 앱 직행)
        ///   2) 웹 클라이언트 채널 링크(워크스페이스가 URL에 확정되어 있어 Grid 환경에서도 안전)
        ///   3) web app_redirect(최후 수단 — 팀 ID가 없으면 기본 워크스페이스로 이동한다)
        /// </summary>
        private static string[] BuildChannelUrls()
        {
            var urls = new List<string>();
            string channel = Uri.EscapeDataString(SlackChannelId);

            if (!string.IsNullOrWhiteSpace(SlackTeamId))
                urls.Add($"slack://channel?team={Uri.EscapeDataString(SlackTeamId)}&id={channel}");

            if (!string.IsNullOrWhiteSpace(SlackChannelLink))
                urls.Add(SlackChannelLink);

            string webUrl = $"https://slack.com/app_redirect?channel={channel}";
            if (!string.IsNullOrWhiteSpace(SlackTeamId))
                webUrl += $"&team={Uri.EscapeDataString(SlackTeamId)}";
            urls.Add(webUrl);

            return urls.ToArray();
        }

        /// <summary>
        /// 웹 클라이언트 링크로 채널을 연다.
        /// slack:// 실행은 핸들러만 등록돼 있으면 "성공"으로 보이기 때문에 앱이 실제로 채널을 열었는지
        /// 코드로 알 수 없다. 그래서 사용자가 직접 누를 수 있는 폴백 경로를 따로 둔다.
        /// </summary>
        public static bool TryOpenChannelLink()
        {
            if (string.IsNullOrWhiteSpace(SlackChannelLink))
                return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SlackChannelLink,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>지정한 폴더를 탐색기로 연다(딥링크/클립보드 실패 시 수동 첨부용 fallback).</summary>
        public static bool TryOpenFolder(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>보관 기간이 지난 임시 zip을 정리한다.</summary>
        private static void CleanupOldZips()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-TempZipKeepDays);

                foreach (var file in Directory.EnumerateFiles(TempZipDir, "Crash_*.zip", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch
                    {
                        // 삭제 실패는 무시(다음 실행 때 다시 시도)
                    }
                }
            }
            catch
            {
                // 정리는 부가 기능이므로 실패해도 전송 흐름을 막지 않는다
            }
        }

        /// <summary>
        /// 전송이 끝난 임시 zip을 삭제한다.
        /// 전송에 성공한 zip은 로컬에 남길 이유가 없어 바로 지운다(용량 절약).
        /// 실패/취소로 남은 zip은 수동 폴백에 필요하므로 지우지 않고, CleanupOldZips가 나중에 정리한다.
        /// </summary>
        public static void DeleteSentZip(string zipPath) => TryDelete(zipPath);

        /// <summary>실패해도 무시하는 파일 삭제.</summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 삭제 실패는 무시 (임시 폴더 정리에서 다시 지워진다)
            }
        }

        /// <summary>파일명에 쓸 수 없는 문자를 '_'로 치환한다.</summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);

            foreach (char c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            return sb.ToString();
        }
    }
}
