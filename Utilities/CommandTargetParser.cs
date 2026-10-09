using System.Globalization;
using System.Text.RegularExpressions;

namespace MintChatManager;

internal static class CommandTargetParser
{
    private static readonly Regex MentionPattern = new(
        @"\[id(?<bracket>\d+)\|[^\]]+\]|(?:https?://)?(?:m\.)?vk\.com/id(?<url>\d+)|@?id(?<id>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static (long? TargetId, string RemainingArguments) Resolve(
        BotCommandContext context,
        string arguments)
    {
        if (context.ReplyToUserId is > 0)
        {
            return (context.ReplyToUserId, arguments.Trim());
        }

        var mention = MentionPattern.Match(arguments);
        if (mention.Success)
        {
            var value = mention.Groups["bracket"].Success
                ? mention.Groups["bracket"].Value
                : mention.Groups["url"].Success
                    ? mention.Groups["url"].Value
                    : mention.Groups["id"].Value;
            return (
                long.Parse(value, CultureInfo.InvariantCulture),
                arguments.Remove(mention.Index, mention.Length).Trim());
        }

        var firstToken = arguments.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (long.TryParse(firstToken, NumberStyles.None, CultureInfo.InvariantCulture, out var targetId) &&
            targetId > 0)
        {
            return (targetId, arguments[firstToken!.Length..].Trim());
        }

        return (null, arguments.Trim());
    }
}
