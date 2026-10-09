using System.Text.Json;

namespace MintChatManager;

public sealed class VkApiClient(HttpClient httpClient, string accessToken)
{
    private const string ApiVersion = "5.199";

    public Task<JsonElement> GetLongPollServerAsync(long groupId, CancellationToken cancellationToken)
    {
        return CallAsync("groups.getLongPollServer", new Dictionary<string, string>
        {
            ["group_id"] = groupId.ToString(),
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
    }

    public async Task<JsonDocument> GetLongPollResponseAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    public async Task SendMessageAsync(long peerId, string message, CancellationToken cancellationToken)
    {
        await CallAsync("messages.send", new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["random_id"] = Random.Shared.Next().ToString(),
            ["message"] = message,
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
    }

    public async Task SendPhotoAsync(
        long peerId,
        byte[] image,
        string caption,
        CancellationToken cancellationToken)
    {
        var uploadServer = await CallAsync("photos.getMessagesUploadServer", new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
        var uploadUrl = uploadServer.GetProperty("upload_url").GetString()
            ?? throw new InvalidOperationException("VK did not return a photo upload URL.");
        var uploadUri = new Uri(uploadUrl);

        using var form = new MultipartFormDataContent();
        using var imageContent = new ByteArrayContent(image);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "photo", "statistics.png");
        using var uploadResponse = await httpClient.PostAsync(uploadUri, form, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"VK photo upload failed (HTTP {(int)uploadResponse.StatusCode} " +
                $"{uploadResponse.StatusCode}): {errorBody}");
        }

        using var uploadDocument = await JsonDocument.ParseAsync(
            await uploadResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var upload = uploadDocument.RootElement;
        if (upload.TryGetProperty("error", out var uploadError))
        {
            throw new InvalidOperationException($"VK photo upload error: {uploadError}");
        }

        var photoData = upload.GetProperty("photo").GetString();
        if (string.IsNullOrEmpty(photoData))
        {
            throw new InvalidOperationException(
                $"VK photo upload returned no image data (HTTP {(int)uploadResponse.StatusCode}, " +
                $"content type {uploadResponse.Content.Headers.ContentType}).");
        }

        photoData = photoData.Replace("\\", string.Empty, StringComparison.Ordinal);
        var savedPhotos = await CallAsync("photos.saveMessagesPhoto", new Dictionary<string, string>
        {
            ["server"] = upload.GetProperty("server").ToString(),
            ["photo"] = photoData,
            ["hash"] = upload.GetProperty("hash").GetString()
                ?? throw new InvalidOperationException("VK photo upload returned no hash."),
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);

        if (savedPhotos.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("VK did not save the uploaded statistics photo.");
        }

        var photo = savedPhotos[0];
        var attachment = $"photo{photo.GetProperty("owner_id").GetInt64()}_{photo.GetProperty("id").GetInt64()}";
        await CallAsync("messages.send", new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["random_id"] = Random.Shared.Next().ToString(),
            ["message"] = caption,
            ["attachment"] = attachment,
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
    }

    private async Task<JsonElement> CallAsync(
        string method,
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"https://api.vk.com/method/{method}",
            new FormUrlEncodedContent(parameters),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"VK API error: {error}");
        }

        return document.RootElement.GetProperty("response").Clone();
    }
}