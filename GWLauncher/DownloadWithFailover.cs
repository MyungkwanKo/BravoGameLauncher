using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BravoGameLauncherGui
{
    public static class DownloadWithFailover
    {
        private static bool IsFailoverStatusCode(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 404 || code == 408 || code == 401 || code == 403 || code >= 500;
        }

        private static void SafeDeletePartial(string destPath)
        {
            try
            {
                if (File.Exists(destPath))
                    File.Delete(destPath);
            }
            catch
            {
                // 삭제 실패는 상위에서 재시도/에러 처리
            }
        }

        /// <param name="progress">(퍼센트 0~100, 받은 바이트, 총 바이트). 총 크기를 모르면 totalBytes는 0.</param>
        public static async Task DownloadToFileWithFailoverAsync(
            HttpClient http,
            string primaryUrl,
            string fallbackUrl,
            string destPath,
            Action<string> log,
            Action<double, long, long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string primaryHost = DownloadHostRouter.ExtractHost(primaryUrl);
            Exception? lastException = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                string url = attempt == 0 ? primaryUrl : fallbackUrl;
                bool isFallback = attempt > 0;

                if (isFallback)
                    log($"[HOST] failover → {DownloadHostRouter.ExtractHost(url)} 로 재시도");

                SafeDeletePartial(destPath);

                try
                {
                    await DownloadSingleUrlToFileAsync(http, url, destPath, log, progress, cancellationToken);

                    log($"[HOST] primary={primaryHost}, failover={(isFallback ? "yes" : "no")}, result=success");
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    SafeDeletePartial(destPath);
                    log($"[HOST] primary={primaryHost}, cancelled=yes");
                    throw;
                }
                catch (Exception ex) when (attempt == 0)
                {
                    lastException = ex;
                    log($"[HOST] primary={primaryHost} 실패: {ex.Message}");
                    SafeDeletePartial(destPath);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    SafeDeletePartial(destPath);
                }
            }

            SafeDeletePartial(destPath);
            log($"[HOST] primary={primaryHost}, failover=yes, result=failure");
            throw lastException ?? new IOException($"ZIP 다운로드 실패: {destPath}");
        }

        private static async Task DownloadSingleUrlToFileAsync(
            HttpClient http,
            string url,
            string destPath,
            Action<string> log,
            Action<double, long, long>? progress,
            CancellationToken cancellationToken)
        {
            log($"[INFO] ZIP 다운로드 요청: {url}");

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (IsFailoverStatusCode(response.StatusCode))
            {
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} ({url})");
            }

            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? 0;
            if (total < 0)
                total = 0;

            progress?.Invoke(0, 0, total);

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fs = new FileStream(
                destPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                useAsync: true);

            byte[] buffer = new byte[1024 * 1024];
            long readTotal = 0;
            int read;
            while ((read = await httpStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                readTotal += read;

                if (total > 0)
                {
                    double pct = (double)readTotal / total * 100.0;
                    progress?.Invoke(Math.Min(100, pct), readTotal, total);
                }
                else
                {
                    progress?.Invoke(0, readTotal, 0);
                }
            }

            long doneTotal = total > 0 ? total : readTotal;
            progress?.Invoke(100, readTotal, doneTotal);
        }
    }
}
