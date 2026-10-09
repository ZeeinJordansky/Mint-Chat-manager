using System.Globalization;
using System.Text.RegularExpressions;

namespace MintChatManager;

internal static class CommandTargetParser
{
    private static readonly Regex MentionPattern = new(
        @"\[(?:id(?<bracket>\d+)|(?:club|public)(?<bracketCommunity>\d+))\|[^\]]+\]|(?:https?://)?(?:m\.)?vk\.com/(?:id(?<url>\d+)|(?:club|public)(?<urlCommunity>\d+))|(?:@?id(?<id>\d+)|(?:club|public)(?<community>\d+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static (long? TargetId, string RemainingArguments) Resolve(
        BotCommandContext context,
        string arguments,
        bool allowNumericTarget = true)
    {
        var mention = MentionPattern.Match(arguments);
        if (mention.Success)
        {
            var community = mention.Groups["bracketCommunity"].Success ||
                            mention.Groups["urlCommunity"].Success ||
                            mention.Groups["community"].Success;
            var value = mention.Groups["bracket"].Success
                ? mention.Groups["bracket"].Value
                : mention.Groups["bracketCommunity"].Success
                    ? mention.Groups["bracketCommunity"].Value
                    : mention.Groups["url"].Success
                        ? mention.Groups["url"].Value
                        : mention.Groups["urlCommunity"].Success
                            ? mention.Groups["urlCommunity"].Value
                            : mention.Groups["id"].Success
                                ? mention.Groups["id"].Value
                                : mention.Groups["community"].Value;
            return (
                community
                    ? -long.Parse(value, CultureInfo.InvariantCulture)
                    : long.Parse(value, CultureInfo.InvariantCulture),
                arguments.Remove(mention.Index, mention.Length).Trim());
        }

        if (context.ReplyToUserId is not null and not 0)
        {
            return (context.ReplyToUserId, arguments.Trim());
        }

        if (!allowNumericTarget)
        {
            return (null, arguments.Trim());
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
