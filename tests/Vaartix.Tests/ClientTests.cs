using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Vaartix.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;
    public HttpRequestMessage? Last;
    public JsonElement? LastBody;

    public FakeHandler(int status, string body) { _status = (HttpStatusCode)status; _body = body; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Last = request;
        LastBody = request.Content is null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
        return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
    }
}

public class ClientTests
{
    private static (VaartixClient, FakeHandler) Client(int status, string body)
    {
        var h = new FakeHandler(status, body);
        return (new VaartixClient("vx_test", "https://api.example/", new HttpClient(h)), h);
    }

    [Fact]
    public async Task SendsAnEmail()
    {
        var (c, h) = Client(200, "{\"id\":\"em_123\"}");
        var res = await c.Emails.SendAsync(new SendEmailRequest
        {
            From = "Acme <orders@acme.in>", To = new() { "priya@example.com" }, Subject = "Hi", Html = "<p>Hi</p>",
            ReplyTo = new() { "help@acme.in" }, Tracking = new Tracking(Opens: true),
        }, "order-1");
        Assert.Equal("em_123", res.Id);
        Assert.Equal(HttpMethod.Post, h.Last!.Method);
        Assert.Equal("https://api.example/emails", h.Last.RequestUri!.ToString());
        Assert.Equal("Bearer vx_test", h.Last.Headers.Authorization!.ToString());
        Assert.Equal("order-1", h.Last.Headers.GetValues("Idempotency-Key").Single());
        Assert.Contains($"vaartix-dotnet/{VaartixClient.Version}", h.Last.Headers.UserAgent.ToString());
        var b = h.LastBody!.Value;
        Assert.Equal("help@acme.in", b.GetProperty("replyTo")[0].GetString());
        Assert.False(b.TryGetProperty("text", out _));
        Assert.True(b.GetProperty("tracking").GetProperty("opens").GetBoolean());
        Assert.False(b.GetProperty("tracking").TryGetProperty("clicks", out _));
    }

    [Fact]
    public async Task Errors()
    {
        var (c, _) = Client(422, "{\"error\":{\"type\":\"validation_error\",\"message\":\"to: not a valid email address.\"}}");
        var e = await Assert.ThrowsAsync<VaartixException>(() => c.Emails.SendAsync(new SendEmailRequest { From = "a@acme.in", To = new() { "x" }, Subject = "s", Text = "t" }));
        Assert.Equal("validation_error", e.Type);
        Assert.Equal(422, e.StatusCode);
        var (c2, _) = Client(500, "oops");
        var e2 = await Assert.ThrowsAsync<VaartixException>(() => c2.Emails.GetAsync("em_1"));
        Assert.Equal("api_error", e2.Type);
        var dead = new VaartixClient("vx_test", "http://127.0.0.1:1");
        var e3 = await Assert.ThrowsAsync<VaartixException>(() => dead.Emails.GetAsync("em_1"));
        Assert.Equal("network_error", e3.Type);
        Assert.Null(e3.StatusCode);
        Assert.Throws<ArgumentException>(() => new VaartixClient("", "https://x"));
    }

    [Fact]
    public async Task ReadsListsAndCancels()
    {
        var (c, h) = Client(200, "{\"id\":\"em_1\",\"status\":\"delivered\",\"tracking\":{\"opens\":true},\"events\":[{\"type\":\"delivered\",\"recipient\":\"a@b.in\",\"data\":{},\"occurredAt\":\"x\"}],\"data\":[],\"hasMore\":true}");
        var email = await c.Emails.GetAsync("em_1/x");
        Assert.Equal("delivered", email.Status);
        Assert.True(email.Tracking!.Opens);
        Assert.Single(email.Events);
        Assert.EndsWith("/emails/em_1%2Fx", h.Last!.RequestUri!.AbsoluteUri);
        var page = await c.Emails.ListAsync(5, "em_9");
        Assert.True(page.HasMore);
        Assert.Equal("?limit=5&before=em_9", h.Last.RequestUri!.Query);
        await c.Emails.CancelAsync("em_3");
        Assert.EndsWith("/emails/em_3/cancel", h.Last.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task BatchAndDomains()
    {
        var (c, h) = Client(200, "{\"data\":[{\"id\":\"em_a\"},{\"id\":\"em_b\"}]}");
        var out1 = await c.Batch.SendAsync(new[] { new SendEmailRequest { From = "a@acme.in", To = new() { "x@y.in" }, Subject = "1", Text = "1" } });
        Assert.Equal(2, out1.Count);
        Assert.Equal(JsonValueKind.Array, h.LastBody!.Value.ValueKind);
        var (c2, h2) = Client(200, "{\"id\":\"dom_1\",\"name\":\"acme.in\",\"openTracking\":true,\"clickTracking\":false}");
        var d = await c2.Domains.UpdateAsync("dom_1", new UpdateDomainRequest(OpenTracking: true));
        Assert.True(d.OpenTracking);
        Assert.Equal("acme.in", d.Raw.GetProperty("name").GetString());
        Assert.Equal(HttpMethod.Patch, h2.Last!.Method);
        Assert.False(h2.LastBody!.Value.TryGetProperty("clickTracking", out _));
    }

    [Fact]
    public async Task Webhooks()
    {
        var (c, h) = Client(200, "{\"id\":\"wh_1\",\"url\":\"https://acme.in/h\",\"events\":[\"email.delivered\"],\"status\":\"enabled\",\"secret\":\"whsec_x\",\"data\":[]}");
        var w = await c.Webhooks.CreateAsync(new CreateWebhookRequest("https://acme.in/h", new() { Events.Delivered }));
        Assert.Equal("whsec_x", w.Secret);
        await c.Webhooks.UpdateAsync("wh_1", new UpdateWebhookRequest(Status: "disabled"));
        Assert.False(h.LastBody!.Value.TryGetProperty("url", out _));
        await c.Webhooks.RotateSecretAsync("wh_1");
        Assert.EndsWith("/webhooks/wh_1/rotate-secret", h.Last!.RequestUri!.AbsolutePath);
        await c.Webhooks.DeliveriesAsync("wh_1", 10);
        Assert.Equal("?limit=10", h.Last.RequestUri!.Query);
        var (c2, h2) = Client(204, "");
        await c2.Webhooks.RemoveAsync("wh_1");
        Assert.Equal(HttpMethod.Delete, h2.Last!.Method);
    }
}
