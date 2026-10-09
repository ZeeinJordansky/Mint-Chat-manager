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

    public Task AnswerMessageEventAsync(
        long peerId,
        long userId,
        string eventId,
        CancellationToken cancellationToken)
    {
        return CallAsync("messages.sendMessageEventAnswer", new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["user_id"] = userId.ToString(),
            ["event_id"] = eventId,
            ["event_data"] = JsonSerializer.Serialize(new
            {
                type = "show_snackbar",
                text = "Команда выполняется"
            }),
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
    }

    public Task SendMessageAsync(
        long peerId,
        string message,
        CancellationToken cancellationToken,
        long? replyToMessageId = null)
    {
        return SendMessageAsync(peerId, message, null, cancellationToken, replyToMessageId);
    }

    public Task SendMessageWithKeyboardAsync(
        long peerId,
        string message,
        string keyboard,
        CancellationToken cancellationToken,
        long? replyToMessageId = null)
    {
        return SendMessageAsync(peerId, message, keyboard, cancellationToken, replyToMessageId);
    }

    public Task EditMessageAsync(
        long peerId,
        long messageId,
        string message,
        string? keyboard,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["message_id"] = messageId.ToString(),
            ["message"] = message,
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        };
        if (keyboard is not null)
        {
            parameters["keyboard"] = keyboard;
        }

        return CallAsync("messages.edit", parameters, cancellationToken);
    }

    public Task ChangeConversationMemberRestrictionsAsync(
        long peerId,
        long userId,
        int durationSeconds,
        string action,
        CancellationToken cancellationToken)
    {
        return CallAsync("messages.changeConversationMemberRestrictions", new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["member_ids"] = userId.ToString(),
            ["for"] = durationSeconds.ToString(),
            ["action"] = action,
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
    }

    public Task RemoveChatUserAsync(long peerId, long userId, CancellationToken cancellationToken)
    {
        if (peerId <= 2_000_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(peerId), "Removing users is only supported in conversations.");
        }

        return CallAsync("messages.removeChatUser", new Dictionary<string, string>
        {
            ["chat_id"] = (peerId - 2_000_000_000).ToString(),
            ["member_id"] = userId.ToString(),
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
    }

    public Task PinConversationMessageAsync(
        long peerId,
        long conversationMessageId,
        CancellationToken cancellationToken)
    {
        return CallAsync("messages.pin", new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["conversation_message_id"] = conversationMessageId.ToString(),
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
    }

    public Task UnpinConversationMessageAsync(long peerId, CancellationToken cancellationToken)
    {
        return CallAsync("messages.unpin", new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
    }

    public async Task<long?> GetConversationOwnerIdAsync(long peerId, CancellationToken cancellationToken)
    {
        var conversations = await CallAsync("messages.getConversationsById", new Dictionary<string, string>
        {
            ["peer_ids"] = peerId.ToString(),
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
        if (!conversations.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() == 0 ||
            !items[0].TryGetProperty("chat_settings", out var settings) ||
            !settings.TryGetProperty("owner_id", out var ownerId) ||
            ownerId.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return ownerId.GetInt64();
    }

    public async Task<VkConversationInfo> GetConversationInfoAsync(long peerId, CancellationToken cancellationToken)
    {
        var conversations = await CallAsync("messages.getConversationsById", new Dictionary<string, string>
        {
            ["peer_ids"] = peerId.ToString(),
            ["extended"] = "1",
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        }, cancellationToken);
        if (!conversations.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() == 0 ||
            !items[0].TryGetProperty("conversation", out var conversation))
        {
            throw new InvalidOperationException($"VK did not return conversation data for peer {peerId}.");
        }

        var title = "";
        var ownerId = 0L;
        var memberCount = 0;
        if (conversation.TryGetProperty("chat_settings", out var settings))
        {
            title = settings.TryGetProperty("title", out var titleValue)
                ? titleValue.GetString() ?? ""
                : "";
            ownerId = settings.TryGetProperty("owner_id", out var ownerValue) &&
                      ownerValue.TryGetInt64(out var parsedOwnerId)
                ? parsedOwnerId
                : 0;
            memberCount = settings.TryGetProperty("members_count", out var countValue) &&
                          countValue.TryGetInt32(out var parsedCount)
                ? parsedCount
                : 0;
        }

        if (memberCount == 0 &&
            conversation.TryGetProperty("members_count", out var fallbackCount) &&
            fallbackCount.TryGetInt32(out var parsedFallbackCount))
        {
            memberCount = parsedFallbackCount;
        }

        return new VkConversationInfo(peerId, title, memberCount, ownerId);
    }

    public async Task<IReadOnlyList<VkConversationMember>> GetConversationMembersAsync(
        long peerId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        const int maximumMembers = 2_000;
        var members = new List<VkConversationMember>();
        var offset = 0;
        var totalCount = int.MaxValue;

        while (offset < totalCount && offset < maximumMembers)
        {
            var response = await CallAsync("messages.getConversationMembers", new Dictionary<string, string>
            {
                ["peer_id"] = peerId.ToString(),
                ["count"] = pageSize.ToString(),
                ["offset"] = offset.ToString(),
                ["fields"] = "online,online_mobile,last_seen,deactivated",
                ["access_token"] = accessToken,
                ["v"] = ApiVersion
            }, cancellationToken);
            if (!response.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"VK did not return conversation members for peer {peerId}.");
            }

            if (response.TryGetProperty("count", out var count) && count.TryGetInt32(out var parsedCount))
            {
                totalCount = parsedCount;
            }

            var profiles = response.TryGetProperty("profiles", out var profileArray) &&
                           profileArray.ValueKind == JsonValueKind.Array
                ? profileArray.EnumerateArray().ToDictionary(
                    profile => profile.TryGetProperty("id", out var id) ? id.GetInt64() : 0)
                : new Dictionary<long, JsonElement>();

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("member_id", out var memberIdValue) ||
                    !memberIdValue.TryGetInt64(out var memberId))
                {
                    continue;
                }

                profiles.TryGetValue(memberId, out var profile);
                var firstName = GetString(profile, "first_name");
                var lastName = GetString(profile, "last_name");
                var online = GetBoolean(profile, "online");
                var onlineMobile = GetBoolean(profile, "online_mobile");
                var platform = profile.ValueKind == JsonValueKind.Object &&
                               profile.TryGetProperty("last_seen", out var lastSeen) &&
                               lastSeen.ValueKind == JsonValueKind.Object &&
                               lastSeen.TryGetProperty("platform", out var platformValue) &&
                               platformValue.TryGetInt32(out var parsedPlatform)
                    ? parsedPlatform
                    : 0;
                var hasLastSeen = profile.ValueKind == JsonValueKind.Object &&
                                  profile.TryGetProperty("last_seen", out var seenValue) &&
                                  seenValue.ValueKind == JsonValueKind.Object &&
                                  seenValue.TryGetProperty("time", out var seenTime) &&
                                  seenTime.TryGetInt64(out var parsedSeenTime) &&
                                  parsedSeenTime > 0;
                var deactivated = profile.ValueKind == JsonValueKind.Object &&
                                  profile.TryGetProperty("deactivated", out var deactivatedValue) &&
                                  deactivatedValue.ValueKind == JsonValueKind.String;
                members.Add(new VkConversationMember(
                    memberId,
                    firstName,
                    lastName,
                    online,
                    onlineMobile,
                    platform,
                    hasLastSeen,
                    deactivated));
            }

            if (items.GetArrayLength() == 0)
            {
                break;
            }

            offset += items.GetArrayLength();
        }

        return members;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True ||
               value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number != 0;
    }

    private async Task SendMessageAsync(
        long peerId,
        string message,
        string? keyboard,
        CancellationToken cancellationToken,
        long? replyToMessageId)
    {
        var parameters = new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["random_id"] = Random.Shared.Next().ToString(),
            ["message"] = message,
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        };
        if (!string.IsNullOrWhiteSpace(keyboard))
        {
            parameters["keyboard"] = keyboard;
        }

        if (replyToMessageId is > 0)
        {
            parameters["reply_to"] = replyToMessageId.Value.ToString();
        }

        await CallAsync("messages.send", parameters, cancellationToken);
    }

    public async Task SendPhotoAsync(
        long peerId,
        byte[] image,
        string caption,
        CancellationToken cancellationToken,
        long? replyToMessageId = null)
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
            var responseBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"VK photo upload returned no image data (HTTP {(int)uploadResponse.StatusCode}, " +
                $"content type {uploadResponse.Content.Headers.ContentType}): {responseBody}");
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
        var parameters = new Dictionary<string, string>
        {
            ["peer_id"] = peerId.ToString(),
            ["random_id"] = Random.Shared.Next().ToString(),
            ["message"] = caption,
            ["attachment"] = attachment,
            ["access_token"] = accessToken,
            ["v"] = ApiVersion
        };
        if (replyToMessageId is > 0)
        {
            parameters["reply_to"] = replyToMessageId.Value.ToString();
        }

        await CallAsync("messages.send", parameters, cancellationToken);
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

public sealed record VkConversationInfo(long PeerId, string Title, int MemberCount, long OwnerId);

public sealed record VkConversationMember(
    long UserId,
    string FirstName,
    string LastName,
    bool IsOnline,
    bool IsOnlineMobile,
    int Platform,
    bool HasLastSeen,
    bool IsDeactivated);