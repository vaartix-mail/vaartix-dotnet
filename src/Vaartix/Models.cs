using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vaartix;

/// <summary>A name/value label on an email, returned in events and webhooks.</summary>
public sealed record Tag(string Name, string Value);

/// <summary>A file sent with an email. <c>Content</c> is base64.</summary>
public sealed record Attachment(string Filename, string Content, string? ContentType = null, string? ContentId = null);

/// <summary>Open and click tracking for one email; null uses the domain's default.</summary>
public sealed record Tracking(bool? Opens = null, bool? Clicks = null);

/// <summary>One email. Set Html or Text (or both).</summary>
public sealed class SendEmailRequest
{
    /// <summary>Sender, on a domain verified in Vaartix.</summary>
    public required string From { get; set; }
    /// <summary>Recipients.</summary>
    public required List<string> To { get; set; }
    /// <summary>Subject line.</summary>
    public required string Subject { get; set; }
    /// <summary>HTML body.</summary>
    public string? Html { get; set; }
    /// <summary>Plain-text body.</summary>
    public string? Text { get; set; }
    /// <summary>Cc recipients.</summary>
    public List<string>? Cc { get; set; }
    /// <summary>Bcc recipients.</summary>
    public List<string>? Bcc { get; set; }
    /// <summary>Reply-To addresses.</summary>
    public List<string>? ReplyTo { get; set; }
    /// <summary>Extra headers.</summary>
    public Dictionary<string, string>? Headers { get; set; }
    /// <summary>Tags.</summary>
    public List<Tag>? Tags { get; set; }
    /// <summary>Attachments.</summary>
    public List<Attachment>? Attachments { get; set; }
    /// <summary>ISO 8601 date-time to send later, e.g. 2026-11-01T09:00:00+05:30.</summary>
    public string? ScheduledAt { get; set; }
    /// <summary>Open and click tracking for this email, overriding the domain's defaults.</summary>
    public Tracking? Tracking { get; set; }
}

/// <summary>The new email's ID.</summary>
public sealed record SendEmailResponse(string Id);

/// <summary>One step in an email's timeline.</summary>
public sealed record EmailEvent(string Type, string? Recipient, JsonElement Data, string OccurredAt);

/// <summary>An attachment on a sent email.</summary>
public sealed record AttachmentInfo(string Filename, string ContentType, long Size);

/// <summary>An email as listed.</summary>
public record EmailSummary
{
    /// <summary>em_...</summary>
    public string Id { get; init; } = "";
    /// <summary>Sender.</summary>
    public string From { get; init; } = "";
    /// <summary>Recipients.</summary>
    public List<string> To { get; init; } = new();
    /// <summary>Subject.</summary>
    public string Subject { get; init; } = "";
    /// <summary>Status, e.g. delivered.</summary>
    public string Status { get; init; } = "";
    /// <summary>Tags.</summary>
    public List<Tag> Tags { get; init; } = new();
    /// <summary>When scheduled to send.</summary>
    public string? ScheduledAt { get; init; }
    /// <summary>When sent.</summary>
    public string? SentAt { get; init; }
    /// <summary>Error, if it failed.</summary>
    public string? Error { get; init; }
    /// <summary>When created.</summary>
    public string CreatedAt { get; init; } = "";
}

/// <summary>One email with its content and events.</summary>
public sealed record Email : EmailSummary
{
    /// <summary>HTML body.</summary>
    public string? Html { get; init; }
    /// <summary>Plain-text body.</summary>
    public string? Text { get; init; }
    /// <summary>The email's own tracking setting, or null when it uses the domain's defaults.</summary>
    public Tracking? Tracking { get; init; }
    /// <summary>Timeline.</summary>
    public List<EmailEvent> Events { get; init; } = new();
}

/// <summary>One page of emails.</summary>
public sealed record ListEmailsResponse(List<EmailSummary> Data, bool HasMore);

/// <summary>A sending domain. Only the tracking fields are typed; Raw holds the full JSON.</summary>
public sealed record Domain(string Id, bool OpenTracking, bool ClickTracking, JsonElement Raw);

/// <summary>Change a domain's tracking defaults. Null keeps the current value.</summary>
public sealed record UpdateDomainRequest(bool? OpenTracking = null, bool? ClickTracking = null);

/// <summary>A webhook endpoint. Secret (whsec_...) is only returned when created or rotated.</summary>
public sealed record Webhook(string Id, string Url, List<string> Events, string Status, string? CreatedAt, string? Secret);

/// <summary>Create an endpoint for a public https:// URL.</summary>
public sealed record CreateWebhookRequest(string Url, List<string> Events);

/// <summary>Change an endpoint. Null keeps the value; Status is "enabled" or "disabled".</summary>
public sealed record UpdateWebhookRequest(string? Url = null, List<string>? Events = null, string? Status = null);

/// <summary>One request Vaartix made to your endpoint.</summary>
public sealed record WebhookDelivery(string Id, string Type, string? EventId, string Status, int Attempts,
    int? ResponseStatus, string? ResponseBody, string? NextAttemptAt, string? DeliveredAt, string CreatedAt, JsonElement Payload);

internal sealed record DataList<T>(List<T> Data);

/// <summary>Webhook event types.</summary>
public static class Events
{
    /// <summary>Accepted by the mail servers.</summary>
    public const string Sent = "email.sent";
    /// <summary>Accepted by the recipient's server.</summary>
    public const string Delivered = "email.delivered";
    /// <summary>Deferred by the receiving server; details carry its exact reply (diagnosticCode).</summary>
    public const string DeliveryDelayed = "email.delivery_delayed";
    /// <summary>Bounced; details carry the receiving server's reply (diagnosticCode).</summary>
    public const string Bounced = "email.bounced";
    /// <summary>Marked as spam.</summary>
    public const string Complained = "email.complained";
    /// <summary>Rejected before sending.</summary>
    public const string Rejected = "email.rejected";
    /// <summary>Could not be rendered.</summary>
    public const string RenderingFailure = "email.rendering_failure";
    /// <summary>Not sent: the address is suppressed.</summary>
    public const string Suppressed = "email.suppressed";
    /// <summary>Opened (machine opens flagged).</summary>
    public const string Opened = "email.opened";
    /// <summary>A link was clicked (scanner clicks flagged).</summary>
    public const string Clicked = "email.clicked";
}

/// <summary>The JSON body Vaartix posts. CreatedAt is when the event happened; sort by it.</summary>
public sealed record WebhookPayload(string Type, string CreatedAt, WebhookData Data);

/// <summary>Recipient is null when the email had more than one recipient. Details depends on the type.</summary>
public sealed record WebhookData(string EmailId, string EventId, string? Recipient, string From, List<string> To,
    string Subject, List<Tag>? Tags, string? EmailCreatedAt, JsonElement Details);
