using System.Net.Http.Headers;
using System.Text.Json;

namespace GWCrashRelay;

/// <summary>Slack 업로드 실패를 나타내는 예외(응답의 error 코드를 담는다).</summary>
public sealed class SlackUploadException : Exception
{
    public SlackUploadException(string message) : base(message) { }
}

/// <summary>
/// Slack 파일 업로드 (files.getUploadURLExternal → 바이트 업로드 → files.completeUploadExternal).
/// 구 files.upload는 2025-11-12에 sunset 되었으므로 사용하지 않는다.
/// </summary>
public sealed class SlackUploader
{
    private const string GetUploadUrlEndpoint  = "https://slack.com/api/files.getUploadURLExternal";
    private const string CompleteUploadEndpoint = "https://slack.com/api/files.completeUploadExternal";

    private readonly HttpClient _http;
    private readonly string _botToken;

    public SlackUploader(HttpClient http, string botToken)
    {
        _http = http;
        _botToken = botToken;
    }

    /// <summary>파일을 업로드하고 채널에 공유한다.</summary>
    /// <returns>업로드된 파일의 permalink (없으면 null)</returns>
    public async Task<string?> UploadAsync(
        Stream content,
        long length,
        string fileName,
        string title,
        string channelId,
        string initialComment,
        CancellationToken ct)
    {
        // 1) 업로드 URL 발급
        var (uploadUrl, fileId) = await GetUploadUrlAsync(fileName, length, ct);

        // 2) 발급받은 URL로 파일 바이트 전송 (이 요청에는 토큰을 붙이지 않는다)
        using (var putRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl))
        {
            var streamContent = new StreamContent(content);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            streamContent.Headers.ContentLength = length;
            putRequest.Content = streamContent;

            using var putResponse = await _http.SendAsync(putRequest, ct);
            if (!putResponse.IsSuccessStatusCode)
            {
                string body = await putResponse.Content.ReadAsStringAsync(ct);
                throw new SlackUploadException(
                    $"파일 바이트 업로드 실패 (HTTP {(int)putResponse.StatusCode}): {Truncate(body, 300)}");
            }
        }

        // 3) 업로드 완료 처리 + 채널 공유
        return await CompleteUploadAsync(fileId, title, channelId, initialComment, ct);
    }

    private async Task<(string UploadUrl, string FileId)> GetUploadUrlAsync(string fileName, long length, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GetUploadUrlEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["filename"] = fileName,
            ["length"]   = length.ToString()
        });

        using var response = await _http.SendAsync(request, ct);
        string json = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
            throw new SlackUploadException($"files.getUploadURLExternal 실패: {ReadError(root)}");

        string? uploadUrl = root.TryGetProperty("upload_url", out var u) ? u.GetString() : null;
        string? fileId    = root.TryGetProperty("file_id", out var f) ? f.GetString() : null;

        if (string.IsNullOrWhiteSpace(uploadUrl) || string.IsNullOrWhiteSpace(fileId))
            throw new SlackUploadException("files.getUploadURLExternal 응답에 upload_url/file_id가 없습니다.");

        return (uploadUrl!, fileId!);
    }

    private async Task<string?> CompleteUploadAsync(
        string fileId,
        string title,
        string channelId,
        string initialComment,
        CancellationToken ct)
    {
        // files 파라미터는 JSON 문자열로 넘긴다.
        string filesJson = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, string> { ["id"] = fileId, ["title"] = title }
        });

        var fields = new Dictionary<string, string>
        {
            ["files"]      = filesJson,
            ["channel_id"] = channelId
        };

        if (!string.IsNullOrWhiteSpace(initialComment))
            fields["initial_comment"] = initialComment;

        using var request = new HttpRequestMessage(HttpMethod.Post, CompleteUploadEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
        request.Content = new FormUrlEncodedContent(fields);

        using var response = await _http.SendAsync(request, ct);
        string json = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
            throw new SlackUploadException($"files.completeUploadExternal 실패: {ReadError(root)}");

        if (root.TryGetProperty("files", out var files)
            && files.ValueKind == JsonValueKind.Array
            && files.GetArrayLength() > 0
            && files[0].TryGetProperty("permalink", out var permalink))
        {
            return permalink.GetString();
        }

        return null;
    }

    /// <summary>Slack 응답에서 error 코드와 힌트를 뽑아낸다.</summary>
    private static string ReadError(JsonElement root)
    {
        string error = root.TryGetProperty("error", out var e) ? (e.GetString() ?? "unknown") : "unknown";

        // 자주 나오는 오류에 대한 조치 안내를 붙여준다.
        string hint = error switch
        {
            "not_in_channel"     => " (봇을 대상 채널에 초대해야 합니다)",
            "channel_not_found"  => " (채널 ID가 잘못되었거나 봇이 접근할 수 없는 채널입니다)",
            "invalid_auth"       => " (봇 토큰이 잘못되었거나 만료되었습니다)",
            "not_authed"         => " (봇 토큰이 설정되지 않았습니다)",
            "missing_scope"      => " (봇에 files:write / chat:write 스코프가 필요합니다)",
            _ => string.Empty
        };

        return error + hint;
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";
}
