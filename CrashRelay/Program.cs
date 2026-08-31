using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using GWCrashRelay;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

// GW Crash Relay (#PJTGW-3099)
//
// GW Launcher가 보낸 크래시 로그 zip을 받아 Slack 채널에 업로드하는 사내 릴레이 서비스.
// Slack 봇 토큰은 이 서비스가 도는 빌드 머신에만 두고, 런처(클라이언트)에는 어떤 시크릿도 두지 않는다.
//
// 요청:  POST /upload  (multipart/form-data)
//        file(zip, 필수) / message / build / user / machine
// 응답:  { "ok": true,  "permalink": "https://..." }
//        { "ok": false, "error": "사유" }

var builder = WebApplication.CreateBuilder(args);

var relayConfig = builder.Configuration.GetSection("Relay");

int  port           = relayConfig.GetValue<int?>("Port") ?? 5080;
long maxUploadBytes = relayConfig.GetValue<long?>("MaxUploadBytes") ?? 512L * 1024 * 1024;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;

    // Nginx가 앞단에서 프록시하므로 로컬에서만 듣는다. 외부 노출은 Nginx location으로만 이뤄진다.
    options.ListenLocalhost(port);
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});

// Slack 업로드는 대용량이라 기본 100초 타임아웃으로는 부족하다.
builder.Services.AddHttpClient("slack", client => client.Timeout = TimeSpan.FromMinutes(10));

// 남용 방지: IP당 10분에 10회
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 거부 응답도 성공/실패 응답과 같은 형태로 내려줘야 런처가 사유를 표시할 수 있다.
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new UploadResponse(false, null, "요청이 너무 잦습니다. 잠시 후 다시 시도해주세요."),
            cancellationToken);
    };

    options.AddPolicy<string>("upload", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Nginx 뒤에 있으므로 실제 클라이언트 IP를 X-Forwarded-For에서 복원한다(레이트리밋·로그용).
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownProxies.Add(System.Net.IPAddress.Loopback);
app.UseForwardedHeaders(forwardedOptions);

app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { ok = true, service = "GWCrashRelay" }));

app.MapPost("/upload", async (
        HttpRequest request,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var log = loggerFactory.CreateLogger("Upload");
        var relay = configuration.GetSection("Relay");

        string botToken  = relay.GetValue<string>("SlackBotToken") ?? string.Empty;
        string channelId = relay.GetValue<string>("SlackChannelId") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(channelId))
        {
            log.LogError("SlackBotToken 또는 SlackChannelId가 설정되지 않았습니다.");
            return Results.Json(
                new UploadResponse(false, null, "서버에 Slack 설정이 없습니다. 관리자에게 문의하세요."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!request.HasFormContentType)
            return Results.BadRequest(new UploadResponse(false, null, "multipart/form-data 요청이 아닙니다."));

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"];

        if (file == null || file.Length <= 0)
            return Results.BadRequest(new UploadResponse(false, null, "업로드할 파일이 없습니다."));

        long maxBytes = relay.GetValue<long?>("MaxUploadBytes") ?? 512L * 1024 * 1024;
        if (file.Length > maxBytes)
        {
            return Results.BadRequest(new UploadResponse(
                false, null, $"파일이 너무 큽니다. (최대 {maxBytes / (1024 * 1024)}MB)"));
        }

        // 확장자 + 매직넘버로 zip만 허용
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new UploadResponse(false, null, "zip 파일만 업로드할 수 있습니다."));

        if (!await IsZipAsync(file, cancellationToken))
            return Results.BadRequest(new UploadResponse(false, null, "zip 형식이 아닙니다."));

        // 클라이언트가 준 문자열은 그대로 믿지 않고 길이 제한 + 제어문자 제거
        string build   = Clean(form["build"],   120);
        string user    = Clean(form["user"],    64);
        string machine = Clean(form["machine"], 64);
        string message = Clean(form["message"], 500);

        if (string.IsNullOrWhiteSpace(message))
            return Results.BadRequest(new UploadResponse(false, null, "크래시 상황 설명이 비어 있습니다."));

        // 파일명은 클라이언트 값을 쓰지 않고 서버가 새로 만든다(경로 조작 차단)
        string safeName = $"Crash_{Sanitize(build)}_{Sanitize(user)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

        string comment = BuildComment(build, user, machine, message);

        // 선택: 원본 보관
        string? archiveDir = relay.GetValue<string>("ArchiveDir");
        if (!string.IsNullOrWhiteSpace(archiveDir))
        {
            try
            {
                await ArchiveAsync(file, archiveDir!, safeName,
                    relay.GetValue<int?>("ArchiveKeepDays") ?? 30, cancellationToken);
            }
            catch (Exception ex)
            {
                // 보관 실패가 전송을 막지 않도록 로그만 남긴다
                log.LogWarning(ex, "크래시 로그 보관에 실패했습니다.");
            }
        }

        try
        {
            var uploader = new SlackUploader(httpClientFactory.CreateClient("slack"), botToken);

            await using var stream = file.OpenReadStream();
            string? permalink = await uploader.UploadAsync(
                stream, file.Length, safeName, safeName, channelId, comment, cancellationToken);

            log.LogInformation(
                "업로드 성공: {File} ({Bytes} bytes) build={Build} user={User} machine={Machine}",
                safeName, file.Length, build, user, machine);

            return Results.Ok(new UploadResponse(true, permalink, null));
        }
        catch (SlackUploadException ex)
        {
            log.LogError(ex, "Slack 업로드 실패: {File}", safeName);
            return Results.Json(
                new UploadResponse(false, null, ex.Message),
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "업로드 처리 중 오류: {File}", safeName);
            return Results.Json(
                new UploadResponse(false, null, "서버 내부 오류로 전송하지 못했습니다."),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .RequireRateLimiting("upload");

app.Run();


// ── 헬퍼 ────────────────────────────────────────────────────────────────

/// <summary>ZIP 매직넘버(PK\x03\x04) 확인. 빈 zip(PK\x05\x06)도 허용한다.</summary>
static async Task<bool> IsZipAsync(IFormFile file, CancellationToken ct)
{
    byte[] header = new byte[4];

    await using var stream = file.OpenReadStream();
    int read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
    if (read < 4)
        return false;

    return header[0] == 0x50 && header[1] == 0x4B
           && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07);
}

/// <summary>제어문자를 지우고 길이를 제한한다.</summary>
static string Clean(string? value, int maxLength)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    var sb = new StringBuilder(value.Length);
    foreach (char c in value)
    {
        // 줄바꿈은 허용, 그 외 제어문자는 제거
        if (c == '\n' || c == '\r' || !char.IsControl(c))
            sb.Append(c);
    }

    string cleaned = sb.ToString().Trim();
    return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
}

/// <summary>파일명에 쓸 수 없는 문자를 '_'로 치환한다.</summary>
static string Sanitize(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return "unknown";

    return Regex.Replace(value, @"[^A-Za-z0-9._-]", "_");
}

/// <summary>Slack 메시지 본문.</summary>
static string BuildComment(string build, string user, string machine, string message)
{
    var sb = new StringBuilder();
    sb.AppendLine($"*[GW 크래시 리포트]* {(string.IsNullOrWhiteSpace(build) ? "(빌드 미상)" : build)}");
    sb.AppendLine($"보고자: {user} / PC: {machine} / {DateTime.Now:yyyy-MM-dd HH:mm}");
    sb.Append($"상황: {message}");
    return sb.ToString();
}

/// <summary>업로드된 zip 사본을 보관하고, 보관 기간이 지난 파일은 정리한다.</summary>
static async Task ArchiveAsync(IFormFile file, string archiveDir, string fileName, int keepDays, CancellationToken ct)
{
    Directory.CreateDirectory(archiveDir);

    string path = Path.Combine(archiveDir, fileName);
    await using (var source = file.OpenReadStream())
    await using (var target = File.Create(path))
    {
        await source.CopyToAsync(target, ct);
    }

    if (keepDays <= 0)
        return;

    var cutoff = DateTime.Now.AddDays(-keepDays);
    foreach (string old in Directory.EnumerateFiles(archiveDir, "Crash_*.zip", SearchOption.TopDirectoryOnly))
    {
        try
        {
            if (File.GetLastWriteTime(old) < cutoff)
                File.Delete(old);
        }
        catch
        {
            // 삭제 실패는 무시
        }
    }
}

/// <summary>런처가 파싱하는 응답 형태.</summary>
internal record UploadResponse(bool Ok, string? Permalink, string? Error);
