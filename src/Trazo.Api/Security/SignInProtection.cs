using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Trazo.Api.Security;

/// <summary>
/// Failed password attempts per account AND network address. Five wrong passwords from one address block that address
/// for that account only, so someone guessing from elsewhere cannot keep the owner locked out. The account-wide lockout
/// (<see cref="AccountLockoutThreshold"/>) still caps guessing spread over many addresses.
/// </summary>
public sealed class LoginAttemptThrottle(TimeProvider clock)
{
    public const int MaximumFailures = 5;
    /// <summary>Wrong passwords, from any address, that lock the whole account.</summary>
    public const int AccountLockoutThreshold = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> _failures = new();

    public bool IsBlocked(Guid userId, string address) =>
        _failures.TryGetValue(Key(userId, address), out var entry) &&
        clock.GetUtcNow() - entry.Start <= Window && entry.Count >= MaximumFailures;

    public void RegisterFailure(Guid userId, string address)
    {
        var now = clock.GetUtcNow();
        if (_failures.Count > 50_000)
            foreach (var stale in _failures.Where(c => now - c.Value.Start > Window).Select(c => c.Key).ToList()) _failures.TryRemove(stale, out _);
        _failures.AddOrUpdate(Key(userId, address), _ => (now, 1),
            (_, current) => now - current.Start > Window ? (now, 1) : (current.Start, current.Count + 1));
    }

    public void Reset(Guid userId, string address) => _failures.TryRemove(Key(userId, address), out _);

    private static string Key(Guid userId, string address) => $"{userId:N}|{address}";
}

/// <summary>
/// Single-use nonces for Google sign-in. The app asks for one, Google embeds it in the ID token and the API accepts
/// that token once: an ID token captured elsewhere cannot be replayed. Kept in memory: a restart only makes the app ask again.
/// </summary>
public sealed class GoogleNonceStore(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const int MaximumOutstanding = 20_000;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new();

    public string Issue()
    {
        var now = clock.GetUtcNow();
        if (_nonces.Count > MaximumOutstanding / 2)
            foreach (var stale in _nonces.Where(n => n.Value <= now).Select(n => n.Key).ToList()) _nonces.TryRemove(stale, out _);
        // Still full of live nonces: drop them all rather than grow without bound (callers simply ask again).
        if (_nonces.Count > MaximumOutstanding) _nonces.Clear();
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _nonces[Hash(raw)] = now.Add(Lifetime);
        return raw;
    }

    /// <summary>
    /// True when the token may be used: its nonce was issued here, has not expired and had not been used. A token
    /// without a nonce is accepted only while <paramref name="required"/> is off (app versions that predate nonces).
    /// </summary>
    public bool Accept(string? nonce, bool required)
    {
        if (string.IsNullOrEmpty(nonce)) return !required;
        return nonce.Length <= 200 && _nonces.TryRemove(Hash(nonce), out var expires) && expires > clock.GetUtcNow();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
