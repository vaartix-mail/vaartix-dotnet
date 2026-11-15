using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vaartix;

/// <summary>A webhook request's signature, timestamp or body is not valid.</summary>
public sealed class WebhookVerificationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public WebhookVerificationException(string message) : base(message) { }
}

/// <summary>Checks that a webhook request really came from Vaartix (Standard Webhooks spec).</summary>
public static class WebhookVerifier
{
    /// <summary>Default timestamp tolerance.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Verify a request and parse its body. <paramref name="payload"/> is the raw body; <paramref name="header"/>
    /// looks a header up by name (case-insensitive in ASP.NET Core: <c>name =&gt; Request.Headers[name]</c>).
    /// </summary>
    public static WebhookPayload Verify(string payload, Func<string, string?> header, string secret)
        => Verify(payload, header, secret, DefaultTolerance, DateTimeOffset.UtcNow);

    /// <summary>Verify with an explicit tolerance and current time.</summary>
    public static WebhookPayload Verify(string payload, Func<string, string?> header, string secret, TimeSpan tolerance, DateTimeOffset now)
    {
        var id = header("webhook-id");
        var ts = header("webhook-timestamp");
        var sigs = header("webhook-signature");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(sigs))
            throw new WebhookVerificationException("Missing webhook-id, webhook-timestamp or webhook-signature header.");
        if (!long.TryParse(ts, out var sent) || Math.Abs(now.ToUnixTimeSeconds() - sent) > tolerance.TotalSeconds)
            throw new WebhookVerificationException("The webhook timestamp is too old or in the future.");
        if (secret is null || !secret.StartsWith("whsec_", StringComparison.Ordinal))
            throw new WebhookVerificationException("The secret should start with whsec_.");
        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret["whsec_".Length..]);
        }
        catch (FormatException)
        {
            throw new WebhookVerificationException("The secret is not valid base64.");
        }
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{ts}.{payload}"));
        var ok = false;
        // The header can hold several space-separated signatures, e.g. while a secret is rotated.
        foreach (var entry in sigs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var comma = entry.IndexOf(',');
            if (comma < 0 || entry[..comma] != "v1") continue;
            try
            {
                if (CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(entry[(comma + 1)..]), expected))
                {
                    ok = true;
                    break;
                }
            }
            catch (FormatException)
            {
                // not base64: try the next one
            }
        }
        if (!ok) throw new WebhookVerificationException("The webhook signature does not match.");
        try
        {
            return JsonSerializer.Deserialize<WebhookPayload>(payload, VaartixClient.Json)
                ?? throw new WebhookVerificationException("The body is empty.");
        }
        catch (JsonException)
        {
            throw new WebhookVerificationException("The body is not valid JSON.");
        }
    }
}
