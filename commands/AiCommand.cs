using System.Net.Http.Json;
using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class AiCommand(HttpClient httpClient, Uri ollamaBaseUri, string model) : IBotCommand
{
    public string Name => "/ии";
    public string Description => "Задать вопрос локальной модели Ollama";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            await api.SendMessageAsync(context.PeerId, "Использование: /ии ваш вопрос", cancellationToken);
            return;
        }

        var requestUri = new Uri(ollamaBaseUri, "api/chat");
        using var response = await httpClient.PostAsJsonAsync(
            requestUri,
            new
            {
                model,
                messages = new[] { new { role = "user", content = arguments } },
                stream = false
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Ollama returned HTTP {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
        }

        using var result = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var answer = result.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("Ollama returned an empty AI response.");
        }

        await api.SendMessageAsync(context.PeerId, answer, cancellationToken);
    }
}
