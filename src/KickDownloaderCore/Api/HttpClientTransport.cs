using System.Net;
using System.Net.Http.Headers;

namespace KickDownloaderCore.Api;

/// <param name="MinInterval">Minimum gap between requests (Kick answers 429 after a few hundred rapid calls).</param>
/// <param name="MaxRetries">Retries for 429/503 before giving up.</param>
/// <param name="Delay">Injectable delay, for tests.</param>
/// <param name="Backoff">Wait before retry n (the last entry repeats). The server's Retry-After wins when longer.</param>
public sealed record TransportOptions(
    TimeSpan MinInterval,
    int MaxRetries = 8,
    Func<TimeSpan, CancellationToken, Task>? Delay = null,
    IReadOnlyList<TimeSpan>? Backoff = null)
{
    public static TransportOptions Default { get; } = new(TimeSpan.FromMilliseconds(400));

    public static IReadOnlyList<TimeSpan> DefaultBackoff { get; } =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];
}

/// <summary>Raised while a request waits out a rate limit. SecondsLeft is 0 once the wait is over.</summary>
public sealed record RateLimitNotice(int SecondsLeft, int Attempt, int MaxAttempts);

public sealed class HttpClientTransport : IKickTransport
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    /// <summary>Fired from the requesting thread about once per second during a 429/503 wait.</summary>
    public event Action<RateLimitNotice>? RateLimited;

    private readonly HttpClient _http;
    private readonly TransportOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextAllowedUtc = DateTime.MinValue;

    public HttpClientTransport(HttpClient http, TransportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _options = options ?? TransportOptions.Default;
        _delay = _options.Delay ?? ((span, ct) => Task.Delay(span, ct));
    }

    public static HttpClientTransport CreateDefault()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Referrer = new Uri("https://kick.com/");
        return new HttpClientTransport(http);
    }

    public async Task<string> GetStringAsync(Uri uri, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            await ThrottleAsync(ct).ConfigureAwait(false);
            using var response = await _http.GetAsync(uri, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KickBlockedException(uri);
            }

            var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
            if (retryable && attempt < _options.MaxRetries)
            {
                var schedule = _options.Backoff ?? TransportOptions.DefaultBackoff;
                var wait = schedule[Math.Min(attempt, schedule.Count - 1)];
                if (response.Headers.RetryAfter?.Delta is { } hinted && hinted > wait)
                {
                    wait = hinted;
                }

                await WaitOutRateLimitAsync(wait > MaxBackoff ? MaxBackoff : wait, attempt + 1, ct).ConfigureAwait(false);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task WaitOutRateLimitAsync(TimeSpan wait, int attempt, CancellationToken ct)
    {
        for (var left = wait; left > TimeSpan.Zero;)
        {
            RateLimited?.Invoke(new RateLimitNotice((int)Math.Ceiling(left.TotalSeconds), attempt, _options.MaxRetries));
            var step = left < Tick ? left : Tick;
            await _delay(step, ct).ConfigureAwait(false);
            left -= step;
        }

        RateLimited?.Invoke(new RateLimitNotice(0, attempt, _options.MaxRetries));
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = _nextAllowedUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await _delay(wait, ct).ConfigureAwait(false);
            }

            _nextAllowedUtc = DateTime.UtcNow + _options.MinInterval;
        }
        finally
        {
            _gate.Release();
        }
    }
}
