using System.Text.Json.Serialization;

namespace KickDownloaderCore.Models;

public sealed record KickUser
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("username")] public string Username { get; init; } = "";
}

public sealed record KickChannel
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("user_id")] public long UserId { get; init; }
    [JsonPropertyName("vod_enabled")] public bool VodEnabled { get; init; }
    [JsonPropertyName("user")] public KickUser? User { get; init; }
}

public sealed record KickVodFile
{
    [JsonPropertyName("uuid")] public string Uuid { get; init; } = "";
}

/// <summary>Livestream session behind a VOD. Duration is in milliseconds.</summary>
public sealed record KickLivestream
{
    [JsonPropertyName("channel")] public KickClipChannel? Channel { get; init; }
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("channel_id")] public long ChannelId { get; init; }
    [JsonPropertyName("session_title")] public string SessionTitle { get; init; } = "";
    [JsonPropertyName("start_time")] public DateTimeOffset StartTime { get; init; }
    [JsonPropertyName("duration")] public long DurationMs { get; init; }
}

/// <summary>Entry of /channels/{slug}/videos: livestream fields plus the nested VOD file.</summary>
public sealed record KickVodSummary
{
    [JsonPropertyName("id")] public long LivestreamId { get; init; }
    [JsonPropertyName("channel_id")] public long ChannelId { get; init; }
    [JsonPropertyName("session_title")] public string SessionTitle { get; init; } = "";
    [JsonPropertyName("start_time")] public DateTimeOffset StartTime { get; init; }
    [JsonPropertyName("duration")] public long DurationMs { get; init; }
    [JsonPropertyName("video")] public KickVodFile? Video { get; init; }
}

/// <summary>Response of /api/v1/video/{uuid}. Source is the master.m3u8 URL.</summary>
public sealed record KickVideo
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("uuid")] public string Uuid { get; init; } = "";
    [JsonPropertyName("source")] public string Source { get; init; } = "";
    [JsonPropertyName("views")] public long Views { get; init; }
    [JsonPropertyName("livestream")] public KickLivestream? Livestream { get; init; }
}

public sealed record KickClipVod
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
}

public sealed record KickClipChannel
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
}

public sealed record KickClip
{
    /// <summary>The VOD this clip was cut from. Null when the VOD is gone.</summary>
    [JsonPropertyName("vod")] public KickClipVod? Vod { get; init; }
    [JsonPropertyName("channel")] public KickClipChannel? Channel { get; init; }
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("channel_id")] public long ChannelId { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("clip_url")] public string ClipUrl { get; init; } = "";
    [JsonPropertyName("video_url")] public string VideoUrl { get; init; } = "";
    [JsonPropertyName("duration")] public int DurationSeconds { get; init; }
    [JsonPropertyName("started_at")] public DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("vod_starts_at")] public int? VodStartsAtSeconds { get; init; }
}

public sealed record KickClipEnvelope
{
    [JsonPropertyName("clip")] public KickClip? Clip { get; init; }
}

public sealed record KickClipList
{
    [JsonPropertyName("clips")] public IReadOnlyList<KickClip> Clips { get; init; } = [];
    [JsonPropertyName("nextCursor")] public string? NextCursor { get; init; }
}

public sealed record KickBadgeMetadata
{
    [JsonPropertyName("level")] public int? Level { get; init; }
}

public sealed record KickBadge
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("image_url")] public string? ImageUrl { get; init; }
    [JsonPropertyName("selected")] public bool Selected { get; init; }
    [JsonPropertyName("metadata")] public KickBadgeMetadata? Metadata { get; init; }
}

/// <summary>Role badge from the legacy "badges" array (subscriber, moderator...). Carries no image.</summary>
public sealed record KickLegacyBadge
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("text")] public string Text { get; init; } = "";
}

public sealed record KickIdentity
{
    [JsonPropertyName("color")] public string? Color { get; init; }
    [JsonPropertyName("badges_v2")] public IReadOnlyList<KickBadge> Badges { get; init; } = [];
    [JsonPropertyName("badges")] public IReadOnlyList<KickLegacyBadge> LegacyBadges { get; init; } = [];
}

public sealed record KickSender
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("identity")] public KickIdentity? Identity { get; init; }
}

public sealed record KickChatMessage
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("chat_id")] public long ChatId { get; init; }
    [JsonPropertyName("content")] public string Content { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("sender")] public KickSender? Sender { get; init; }
}

public sealed record KickChatPage
{
    [JsonPropertyName("messages")] public IReadOnlyList<KickChatMessage> Messages { get; init; } = [];
    [JsonPropertyName("cursor")] public string? Cursor { get; init; }
}

public sealed record KickChatEnvelope
{
    [JsonPropertyName("data")] public KickChatPage? Data { get; init; }
}

/// <summary>Video as returned by web.kick.com (UUIDv7 ids). Duration is in seconds.</summary>
public sealed record KickWebVideo
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("start_time")] public DateTimeOffset StartTime { get; init; }
    [JsonPropertyName("duration")] public long DurationSeconds { get; init; }
    [JsonPropertyName("recording_url")] public string? RecordingUrl { get; init; }
    [JsonPropertyName("viewer_count")] public long ViewerCount { get; init; }
    [JsonPropertyName("channel")] public KickClipChannel? Channel { get; init; }
}

public sealed record KickWebEnvelope<T>
{
    [JsonPropertyName("data")] public T? Data { get; init; }
}
