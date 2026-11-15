# Vaartix .NET SDK

The official .NET client for [Vaartix](https://vaartix.com), the transactional email API built in India.
Data stored in India (AWS Mumbai), pricing in ₹.

```sh
dotnet add package Vaartix
```

.NET 8 or newer. No dependencies outside the framework.

## Send an email

```csharp
using Vaartix;

var vaartix = new VaartixClient(); // reads VAARTIX_API_KEY

var sent = await vaartix.Emails.SendAsync(new SendEmailRequest
{
    From = "Acme <orders@acme.in>",   // an address on a domain verified in Vaartix
    To = ["priya@example.com"],
    Subject = "Your order is confirmed",
    Html = "<p>Thanks for your order!</p>",
});
Console.WriteLine(sent.Id); // em_...
```

Testing? Send to `delivered@test.vaartix.com` (or `bounced@` / `complained@`). Nothing real is sent.

The client is thread-safe. In ASP.NET Core, register it once:

```csharp
builder.Services.AddHttpClient<VaartixClient>((http, _) => new VaartixClient(builder.Configuration["Vaartix:ApiKey"], httpClient: http));
```

## Errors

Failures throw `VaartixException` with the API's `Type`, `Message` and `StatusCode`:

```csharp
try { await vaartix.Emails.SendAsync(email); }
catch (VaartixException e) when (e.Type == "validation_error") { /* fix the request */ }
```

`Type` is `network_error` (and `StatusCode` is null) when no response was received.

## More sending options

```csharp
await vaartix.Emails.SendAsync(email, idempotencyKey: "receipt-10482"); // safe retries for 24 hours
email.ScheduledAt = "2026-11-15T10:00:00+05:30";                       // send later
await vaartix.Batch.SendAsync([emailA, emailB]);                        // up to 100 in one request
email.Tracking = new Tracking(Opens: true, Clicks: false);              // per-email tracking
```

Keep click tracking off for login codes, password resets and magic links.

## Read emails

Needs a full-access key.

```csharp
var email = await vaartix.Emails.GetAsync("em_...");          // content, status and events
var page = await vaartix.Emails.ListAsync(limit: 50);
await vaartix.Emails.CancelAsync("em_...");                    // a scheduled email
```

## Webhooks

Check that a webhook request really came from Vaartix before you trust it. Use the **raw** body.

```csharp
app.MapPost("/webhooks/vaartix", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    try
    {
        var evt = WebhookVerifier.Verify(body, name => request.Headers[name], Environment.GetEnvironmentVariable("VAARTIX_WEBHOOK_SECRET")!);
        if (evt.Type == Events.Bounced)
        {
            var reply = evt.Data.Details.GetProperty("diagnosticCode").GetString(); // the receiving server's reply
        }
        return Results.Ok();
    }
    catch (WebhookVerificationException)
    {
        return Results.BadRequest();
    }
});
```

Signatures follow the [Standard Webhooks](https://www.standardwebhooks.com) spec. Manage endpoints with
`vaartix.Webhooks`: `CreateAsync`, `ListAsync`, `GetAsync`, `UpdateAsync`, `RemoveAsync`, `RotateSecretAsync`,
`DeliveriesAsync`, `RetryDeliveryAsync`.

## Domains

```csharp
await vaartix.Domains.UpdateAsync(domainId, new UpdateDomainRequest(OpenTracking: true));
```

## Docs

- Docs: https://vaartix.com/docs
- API reference: https://vaartix.com/docs/api
- Status: https://status.vaartix.com

## Contributing and security

Issues and pull requests are welcome. Report security problems privately, see [SECURITY.md](SECURITY.md).

## License

MIT
