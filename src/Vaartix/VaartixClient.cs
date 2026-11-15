using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vaartix;

/// <summary>The API answered with an error, or the request could not be made.</summary>
public sealed class VaartixException : Exception
{
    /// <summary>Creates the exception.</summary>
    public VaartixException(string type, string message, int? statusCode, Exception? inner = null) : base(message, inner)
    {
        Type = type;
        StatusCode = statusCode;
    }

    /// <summary>The API's error type, e.g. validation_error, or network_error.</summary>
    public string Type { get; }

    /// <summary>The HTTP status, or null when no response was received.</summary>
    public int? StatusCode { get; }
}

/// <summary>
/// The Vaartix client. Thread-safe: register one as a singleton, or pass your own HttpClient
/// (for example from IHttpClientFactory).
/// </summary>
public sealed class VaartixClient
{
    /// <summary>This SDK's version, sent in the User-Agent header.</summary>
    public const string Version = "0.1.0";
    /// <summary>The Vaartix API.</summary>
    public const string DefaultBaseUrl = "https://api.vaartix.com";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly HttpClient _http;

    /// <summary>Creates a client. A null apiKey reads VAARTIX_API_KEY.</summary>
    public VaartixClient(string? apiKey = null, string baseUrl = DefaultBaseUrl, HttpClient? httpClient = null)
    {
        var key = string.IsNullOrEmpty(apiKey) ? Environment.GetEnvironmentVariable("VAARTIX_API_KEY") : apiKey;
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Missing API key. Pass it to new VaartixClient(\"vx_...\") or set VAARTIX_API_KEY.");
        }
        _apiKey = key;
        _baseUrl = baseUrl.TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        Emails = new EmailsResource(this);
        Batch = new BatchResource(this);
        Domains = new DomainsResource(this);
        Webhooks = new WebhooksResource(this);
    }

    /// <summary>Send and read emails.</summary>
    public EmailsResource Emails { get; }
    /// <summary>Send up to 100 emails in one request.</summary>
    public BatchResource Batch { get; }
    /// <summary>Sending domains.</summary>
    public DomainsResource Domains { get; }
    /// <summary>Webhook endpoints.</summary>
    public WebhooksResource Webhooks { get; }

    internal static string Esc(string s) => Uri.EscapeDataString(s);

    internal async Task<T?> RequestAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, _baseUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Headers.UserAgent.ParseAdd($"vaartix-dotnet/{Version}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(idempotencyKey)) req.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
        {
            req.Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), Json), Encoding.UTF8, "application/json");
        }
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new VaartixException("network_error", e.Message, null, e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new VaartixException("network_error", "The request timed out.", null, e);
        }
        using (res)
        {
            var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var status = (int)res.StatusCode;
            if (!res.IsSuccessStatusCode)
            {
                var type = "api_error";
                var message = $"Request failed with status {status}";
                try
                {
                    using var doc = JsonDocument.Parse(bytes);
                    if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                    {
                        if (err.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String) type = t.GetString()!;
                        if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString()!;
                    }
                }
                catch (JsonException)
                {
                    // not JSON: keep the generic message
                }
                throw new VaartixException(type, message, status);
            }
            if (bytes.Length == 0) return default;
            try
            {
                return JsonSerializer.Deserialize<T>(bytes, Json);
            }
            catch (JsonException e)
            {
                throw new VaartixException("api_error", "Could not read the response: " + e.Message, status, e);
            }
        }
    }
}

/// <summary>Send and read emails.</summary>
public sealed class EmailsResource
{
    private readonly VaartixClient _c;
    internal EmailsResource(VaartixClient c) => _c = c;

    /// <summary>Send one email. Retrying with the same idempotency key within 24 hours returns the first result.</summary>
    public async Task<SendEmailResponse> SendAsync(SendEmailRequest email, string? idempotencyKey = null, CancellationToken ct = default)
        => (await _c.RequestAsync<SendEmailResponse>(HttpMethod.Post, "/emails", email, idempotencyKey, ct).ConfigureAwait(false))!;

    /// <summary>One email with its events. Needs a full-access key.</summary>
    public async Task<Email> GetAsync(string id, CancellationToken ct = default)
        => (await _c.RequestAsync<Email>(HttpMethod.Get, "/emails/" + VaartixClient.Esc(id), null, null, ct).ConfigureAwait(false))!;

    /// <summary>Recent emails, newest first. Needs a full-access key.</summary>
    public async Task<ListEmailsResponse> ListAsync(int? limit = null, string? before = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (limit is not null) q.Add($"limit={limit}");
        if (!string.IsNullOrEmpty(before)) q.Add("before=" + VaartixClient.Esc(before));
        var path = "/emails" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return (await _c.RequestAsync<ListEmailsResponse>(HttpMethod.Get, path, null, null, ct).ConfigureAwait(false))!;
    }

    /// <summary>Cancel a scheduled email before it is sent. Needs a full-access key.</summary>
    public async Task<EmailSummary> CancelAsync(string id, CancellationToken ct = default)
        => (await _c.RequestAsync<EmailSummary>(HttpMethod.Post, $"/emails/{VaartixClient.Esc(id)}/cancel", null, null, ct).ConfigureAwait(false))!;
}

/// <summary>Send many emails in one request.</summary>
public sealed class BatchResource
{
    private readonly VaartixClient _c;
    internal BatchResource(VaartixClient c) => _c = c;

    /// <summary>Up to 100 emails (no attachments or scheduling). All or nothing.</summary>
    public async Task<List<SendEmailResponse>> SendAsync(IEnumerable<SendEmailRequest> emails, CancellationToken ct = default)
        => (await _c.RequestAsync<DataList<SendEmailResponse>>(HttpMethod.Post, "/emails/batch", emails.ToList(), null, ct).ConfigureAwait(false))!.Data;
}

/// <summary>Sending domains. Changes need a full-access key from an owner or admin.</summary>
public sealed class DomainsResource
{
    private readonly VaartixClient _c;
    internal DomainsResource(VaartixClient c) => _c = c;

    /// <summary>Change a domain's default open and click tracking.</summary>
    public async Task<Domain> UpdateAsync(string id, UpdateDomainRequest update, CancellationToken ct = default)
    {
        var raw = await _c.RequestAsync<JsonElement>(HttpMethod.Patch, "/domains/" + VaartixClient.Esc(id), update, null, ct).ConfigureAwait(false);
        bool Flag(string name) => raw.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        return new Domain(raw.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "", Flag("openTracking"), Flag("clickTracking"), raw.Clone());
    }
}

/// <summary>Webhook endpoints. Needs a full-access key. To check incoming requests, see <see cref="WebhookVerifier"/>.</summary>
public sealed class WebhooksResource
{
    private readonly VaartixClient _c;
    internal WebhooksResource(VaartixClient c) => _c = c;

    /// <summary>Create an endpoint. The response includes the signing secret, shown only this once.</summary>
    public async Task<Webhook> CreateAsync(CreateWebhookRequest create, CancellationToken ct = default)
        => (await _c.RequestAsync<Webhook>(HttpMethod.Post, "/webhooks", create, null, ct).ConfigureAwait(false))!;

    /// <summary>All endpoints.</summary>
    public async Task<List<Webhook>> ListAsync(CancellationToken ct = default)
        => (await _c.RequestAsync<DataList<Webhook>>(HttpMethod.Get, "/webhooks", null, null, ct).ConfigureAwait(false))!.Data;

    /// <summary>One endpoint.</summary>
    public async Task<Webhook> GetAsync(string id, CancellationToken ct = default)
        => (await _c.RequestAsync<Webhook>(HttpMethod.Get, "/webhooks/" + VaartixClient.Esc(id), null, null, ct).ConfigureAwait(false))!;

    /// <summary>Change the URL or events, or turn the endpoint off ("disabled") and on ("enabled").</summary>
    public async Task<Webhook> UpdateAsync(string id, UpdateWebhookRequest update, CancellationToken ct = default)
        => (await _c.RequestAsync<Webhook>(HttpMethod.Patch, "/webhooks/" + VaartixClient.Esc(id), update, null, ct).ConfigureAwait(false))!;

    /// <summary>Delete an endpoint.</summary>
    public Task RemoveAsync(string id, CancellationToken ct = default)
        => _c.RequestAsync<JsonElement>(HttpMethod.Delete, "/webhooks/" + VaartixClient.Esc(id), null, null, ct);

    /// <summary>Replace the signing secret. The old one stops working immediately.</summary>
    public async Task<Webhook> RotateSecretAsync(string id, CancellationToken ct = default)
        => (await _c.RequestAsync<Webhook>(HttpMethod.Post, $"/webhooks/{VaartixClient.Esc(id)}/rotate-secret", null, null, ct).ConfigureAwait(false))!;

    /// <summary>Recent deliveries, newest first.</summary>
    public async Task<List<WebhookDelivery>> DeliveriesAsync(string id, int? limit = null, CancellationToken ct = default)
        => (await _c.RequestAsync<DataList<WebhookDelivery>>(HttpMethod.Get,
            $"/webhooks/{VaartixClient.Esc(id)}/deliveries" + (limit is null ? "" : $"?limit={limit}"), null, null, ct).ConfigureAwait(false))!.Data;

    /// <summary>Send a delivery again, with the same webhook-id.</summary>
    public async Task<WebhookDelivery> RetryDeliveryAsync(string id, string deliveryId, CancellationToken ct = default)
        => (await _c.RequestAsync<WebhookDelivery>(HttpMethod.Post,
            $"/webhooks/{VaartixClient.Esc(id)}/deliveries/{VaartixClient.Esc(deliveryId)}/retry", null, null, ct).ConfigureAwait(false))!;
}
