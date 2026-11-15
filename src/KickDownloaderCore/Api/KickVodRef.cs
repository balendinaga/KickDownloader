namespace KickDownloaderCore.Api;

/// <summary>
/// A VOD as the user names it. Kick has two id families that do not know each other:
/// legacy UUIDv4 ids (kick.com/api/v1/video/{id}) and the UUIDv7 ids used by today's web links, which are
/// only reachable together with the channel (web.kick.com/api/v1/channels/{channelId}/videos/{id}).
/// </summary>
/// <param name="Uuid">The id from the link.</param>
/// <param name="ChannelSlug">Channel from the link (kick.com/{channel}/videos/{id}); required for UUIDv7 ids.</param>
public sealed record KickVodRef(string Uuid, string? ChannelSlug = null)
{
    /// <summary>UUIDv7 carries the version nibble 7 at position 14.</summary>
    public bool IsV7 => Uuid.Length == 36 && Uuid[14] == '7';

    public static implicit operator KickVodRef(string uuid) => new(uuid);
}

/// <summary>A UUIDv7 VOD id was given without its channel, so it cannot be looked up.</summary>
public sealed class KickVodNeedsChannelException(string uuid)
    : ArgumentException($"VOD {uuid} uses the new id format, which can only be looked up together with its channel. Paste the full kick.com/<channel>/videos/<id> link.");
