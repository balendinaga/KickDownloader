using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TwitchDownloaderCore.TwitchObjects;
using TwitchVideo = TwitchDownloaderCore.TwitchObjects.Video;
using KickDownloaderCore.Models;

namespace KickDownloaderCore.Chat;

public sealed record EmoteAsset(string Name, byte[] Data, int Width, int Height);

public sealed record BadgeAsset(byte[] Data, string Url);

/// <summary>Downloaded images. Missing entries are tolerated: the converter degrades to plain text / no badge.</summary>
public sealed record ChatAssets(
    IReadOnlyDictionary<string, EmoteAsset> Emotes,
    IReadOnlyDictionary<(string Name, string Version), BadgeAsset> Badges)
{
    public static ChatAssets Empty { get; } = new(
        new Dictionary<string, EmoteAsset>(), new Dictionary<(string, string), BadgeAsset>());
}

public static partial class ChatConverter
{
    [GeneratedRegex(@"\[emote:(\d+):([^\]]+)\]")]
    private static partial Regex EmoteRegex();

    /// <summary>Twitch's 1x emote is 28px tall; the renderer scales by 2/imageScale.</summary>
    private const double TwitchBaseEmoteHeight = 28;

    public static IEnumerable<(string Id, string Name)> EmotesIn(string content) =>
        EmoteRegex().Matches(content).Select(m => (m.Groups[1].Value, m.Groups[2].Value));

    /// <summary>Badges to show: every selected v2 badge (version = level for "level") plus role badges.</summary>
    public static IEnumerable<(string Name, string Version, string? ImageUrl)> BadgesOf(KickChatMessage m)
    {
        var identity = m.Sender?.Identity;
        if (identity is null)
        {
            yield break;
        }

        foreach (var b in identity.Badges.Where(b => b.Selected))
        {
            var version = b.Name == "level" && b.Metadata?.Level is { } level
                ? level.ToString(CultureInfo.InvariantCulture)
                : "1";
            yield return (b.Name, version, b.ImageUrl);
        }

        foreach (var legacy in identity.LegacyBadges.Where(b => !string.IsNullOrEmpty(b.Type)))
        {
            yield return (legacy.Type, "1", null);
        }
    }

    public static ChatRoot Build(
        KickChannel channel, KickVideo video, IReadOnlyList<KickChatMessage> messages,
        ChatAssets assets, double startSeconds, double endSeconds, DateTimeOffset now)
    {
        var vodStart = video.Livestream?.StartTime
                       ?? throw new InvalidDataException("VOD response has no livestream.start_time.");
        var channelId = channel.Id.ToString(CultureInfo.InvariantCulture);
        var comments = new List<Comment>(messages.Count);
        foreach (var m in messages)
        {
            var offset = (m.CreatedAt - vodStart).TotalSeconds;
            if (m.Sender is null || offset < 0)
            {
                continue;
            }

            comments.Add(new Comment
            {
                _id = m.Id,
                created_at = m.CreatedAt.UtcDateTime,
                channel_id = channelId,
                content_type = "video",
                content_id = video.Uuid,
                content_offset_seconds = offset,
                commenter = new Commenter
                {
                    display_name = m.Sender.Username,
                    _id = m.Sender.Id.ToString(CultureInfo.InvariantCulture),
                    name = m.Sender.Slug,
                    created_at = DateTime.UnixEpoch,
                    updated_at = DateTime.UnixEpoch,
                },
                message = BuildMessage(m, assets),
            });
        }

        return new ChatRoot
        {
            FileInfo = new ChatRootInfo { Version = ChatRootVersion.CurrentVersion, CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime },
            streamer = new Streamer
            {
                name = channel.User?.Username ?? channel.Slug,
                login = channel.Slug,
                id = checked((int)channel.Id),
            },
            video = new TwitchVideo
            {
                title = video.Livestream?.SessionTitle ?? "",
                id = video.Uuid,
                created_at = vodStart.UtcDateTime,
                start = startSeconds,
                end = endSeconds,
                length = (video.Livestream?.DurationMs ?? 0) / 1000.0,
                viewCount = (int)Math.Min(video.Views, int.MaxValue),
            },
            comments = comments,
            embeddedData = BuildEmbeds(messages, assets),
        };
    }

    private static Message BuildMessage(KickChatMessage m, ChatAssets assets)
    {
        var body = new StringBuilder();
        var fragments = new List<Fragment>();
        var emoticons = new List<Emoticon2>();
        var text = new StringBuilder();
        var last = 0;

        void FlushText()
        {
            if (text.Length > 0)
            {
                fragments.Add(new Fragment { text = text.ToString() });
                text.Clear();
            }
        }

        foreach (Match match in EmoteRegex().Matches(m.Content))
        {
            Append(m.Content[last..match.Index]);
            last = match.Index + match.Length;
            var id = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            if (assets.Emotes.ContainsKey(id))
            {
                FlushText();
                fragments.Add(new Fragment { text = name, emoticon = new Emoticon { emoticon_id = id } });
                emoticons.Add(new Emoticon2 { _id = id, begin = body.Length, end = body.Length + name.Length - 1 });
                body.Append(name);
            }
            else
            {
                Append(name);
            }
        }

        Append(m.Content[last..]);
        FlushText();
        return new Message
        {
            body = body.ToString(),
            fragments = fragments,
            emoticons = emoticons,
            user_color = m.Sender?.Identity?.Color,
            user_badges = BadgesOf(m).Select(b => new UserBadge { _id = b.Name, version = b.Version }).ToList(),
        };

        void Append(string s)
        {
            text.Append(s);
            body.Append(s);
        }
    }

    private static EmbeddedData BuildEmbeds(IReadOnlyList<KickChatMessage> messages, ChatAssets assets)
    {
        var usedEmotes = messages.SelectMany(m => EmotesIn(m.Content).Select(e => e.Id)).ToHashSet();
        var firstParty = assets.Emotes
            .Where(e => usedEmotes.Contains(e.Key))
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => new EmbedEmoteData
            {
                id = e.Key,
                name = e.Value.Name,
                data = e.Value.Data,
                width = e.Value.Width,
                height = e.Value.Height,
                imageScale = ImageScaleFor(e.Value.Height),
            })
            .ToList();

        var badges = assets.Badges
            .GroupBy(b => b.Key.Name)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new EmbedChatBadge
            {
                name = g.Key,
                versions = g.ToDictionary(
                    b => b.Key.Version,
                    b => new ChatBadgeData { title = g.Key, description = g.Key, bytes = b.Value.Data, url = b.Value.Url }),
            })
            .ToList();

        return new EmbeddedData { firstParty = firstParty, twitchBadges = badges };
    }

    /// <summary>
    /// Twitch's convention: imageScale = pixel height / 28 (1x=28px, 2x=56px, 4x=112px). The renderer draws an emote
    /// at height * 2 / imageScale * ReferenceScale, so this keeps every Kick emote near Twitch's on-screen size.
    /// </summary>
    public static int ImageScaleFor(int height) =>
        height <= 0 ? 2 : Math.Max(1, (int)Math.Round(height / TwitchBaseEmoteHeight, MidpointRounding.AwayFromZero));
}
