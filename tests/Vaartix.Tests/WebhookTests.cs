using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Vaartix.Tests;

public class WebhookTests
{
    // Test-only secret: the worked example from the Standard Webhooks specification.
    private const string Prefix = "whsec_";
    private static readonly string SpecSecret = Prefix + "MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string SpecBody = "{\"test\": 2432232314}";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1614265330);

    private static Dictionary<string, string> Headers() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["webhook-id"] = "msg_p5jXN8AQM9LWM0D4loKWxJek",
        ["webhook-timestamp"] = "1614265330",
        ["webhook-signature"] = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=",
    };

    private static Func<string, string?> Get(Dictionary<string, string> h) => n => h.TryGetValue(n, out var v) ? v : null;

    [Fact]
    public void SignatureOfTheReferenceExamplePasses()
    {
        // One of several signatures matching is enough.
        var h = Headers();
        h["webhook-signature"] = "v1,bm90LXRoaXM= " + h["webhook-signature"];
        Assert.NotNull(WebhookVerifier.Verify(SpecBody, Get(h), SpecSecret, TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void Rejects()
    {
        var tol = TimeSpan.FromMinutes(5);
        Assert.Contains("signature", Assert.Throws<WebhookVerificationException>(() => WebhookVerifier.Verify("{\"test\": 1}", Get(Headers()), SpecSecret, tol, Now)).Message);
        Assert.Contains("signature", Assert.Throws<WebhookVerificationException>(() => WebhookVerifier.Verify(SpecBody, Get(Headers()), Prefix + "c2VjcmV0", tol, Now)).Message);
        Assert.Contains("timestamp", Assert.Throws<WebhookVerificationException>(() => WebhookVerifier.Verify(SpecBody, Get(Headers()), SpecSecret, tol, Now.AddMinutes(10))).Message);
        Assert.Contains("Missing", Assert.Throws<WebhookVerificationException>(() => WebhookVerifier.Verify(SpecBody, _ => null, SpecSecret, tol, Now)).Message);
        Assert.Contains("whsec_", Assert.Throws<WebhookVerificationException>(() => WebhookVerifier.Verify(SpecBody, Get(Headers()), "nope", tol, Now)).Message);
    }

    [Fact]
    public void ParsesASignedDelay()
    {
        var body = "{\"type\":\"email.delivery_delayed\",\"createdAt\":\"x\",\"data\":{\"emailId\":\"em_1\",\"eventId\":\"evt_1\",\"recipient\":\"a@yahoo.com\",\"from\":\"a@acme.in\",\"to\":[\"a@yahoo.com\"],\"subject\":\"s\",\"tags\":[],\"details\":{\"delayType\":\"TransientCommunicationFailure\",\"diagnosticCode\":\"421 4.7.0 [TSS04]\",\"statusCode\":\"4.7.0\"}}}";
        var secret = Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));
        var sig = Convert.ToBase64String(HMACSHA256.HashData(Convert.FromBase64String(secret[Prefix.Length..]), Encoding.UTF8.GetBytes($"whd_1.1614265330.{body}")));
        var h = new Dictionary<string, string> { ["webhook-id"] = "whd_1", ["webhook-timestamp"] = "1614265330", ["webhook-signature"] = "v1," + sig };
        var p = WebhookVerifier.Verify(body, Get(h), secret, TimeSpan.FromMinutes(5), Now);
        Assert.Equal(Events.DeliveryDelayed, p.Type);
        Assert.Equal("421 4.7.0 [TSS04]", p.Data.Details.GetProperty("diagnosticCode").GetString());
    }
}
