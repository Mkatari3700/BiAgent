using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Security;

public interface IAccessRequestEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(AccessRequestEmail email, CancellationToken cancellationToken);
}

public sealed class SmtpAccessRequestEmailSender(IConfiguration configuration) : IAccessRequestEmailSender
{
    private readonly string? host = configuration["AccessRequests:Email:SmtpHost"];
    private readonly string? fromAddress = configuration["AccessRequests:Email:FromAddress"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(host) &&
        !string.IsNullOrWhiteSpace(fromAddress);

    public async Task SendAsync(AccessRequestEmail email, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Access request email delivery is not configured.");
        }

        var template = AccessRequestEmailTemplates.Render(email, configuration["AccessRequests:PublicBaseUrl"]);
        using var message = new MailMessage(fromAddress!, email.RecipientEmail, template.Subject, template.HtmlBody)
        {
            IsBodyHtml = true
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            template.TextBody,
            null,
            "text/plain"));

        var port = configuration.GetValue<int?>("AccessRequests:Email:SmtpPort") ?? 587;
        using var client = new SmtpClient(host!, port)
        {
            EnableSsl = configuration.GetValue("AccessRequests:Email:EnableSsl", true)
        };
        var username = configuration["AccessRequests:Email:Username"];
        var password = configuration["AccessRequests:Email:Password"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(username, password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}

public sealed class AccessRequestNotificationService(
    IAccessRequestStore store,
    IAccessRequestEmailSender emailSender,
    ILogger<AccessRequestNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                var settings = await store.GetSettingsAsync(stoppingToken);
                var queued = await store.QueueDailyNotificationsAsync(settings.EscalationDays, stoppingToken);
                if (queued > 0)
                {
                    logger.LogInformation("Queued {AccessRequestNotificationCount} access-request notifications.", queued);
                }

                if (emailSender.IsConfigured)
                {
                    var emails = await store.GetPendingEmailsAsync(50, stoppingToken);
                    foreach (var email in emails)
                    {
                        try
                        {
                            await emailSender.SendAsync(email, stoppingToken);
                            await store.RecordEmailAttemptAsync(email.NotificationId, true, null, stoppingToken);
                        }
                        catch (Exception exception) when (
                            exception is SmtpException or InvalidOperationException or FormatException)
                        {
                            logger.LogError(
                                exception,
                                "Email delivery failed for access request notification {NotificationId}.",
                                email.NotificationId);
                            await store.RecordEmailAttemptAsync(
                                email.NotificationId,
                                false,
                                exception.Message,
                                stoppingToken);
                        }
                    }
                }
                else if (queued > 0)
                {
                    logger.LogWarning(
                        "Access request notifications are queued in the database, but SMTP is not configured. Set AccessRequests:Email:SmtpHost and FromAddress to enable email delivery.");
                }
            }
            catch (SqlException exception)
            {
                logger.LogError(exception, "Scheduled access-request notification processing failed.");
            }
            catch (InvalidOperationException exception)
            {
                logger.LogError(exception, "Scheduled access-request email processing failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed record AccessRequestEmailContent(string Subject, string HtmlBody, string TextBody);

public static class AccessRequestEmailTemplates
{
    public static AccessRequestEmailContent Render(AccessRequestEmail email, string? publicBaseUrl)
    {
        var scope = $"{email.ScopeType}: {email.ScopeName}";
        var role = FormatRole(email.RequestedRole);
        var url = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? "/access-requests"
            : $"{publicBaseUrl.TrimEnd('/')}/access-requests";
        var link = HtmlEncoder.Default.Encode(url);
        var requester = HtmlEncoder.Default.Encode(email.RequesterName);
        var safeScope = HtmlEncoder.Default.Encode(scope);
        var safeRole = HtmlEncoder.Default.Encode(role);
        var optionalMessage = string.IsNullOrWhiteSpace(email.ResolutionMessage)
            ? ""
            : $"<p><strong>Message:</strong> {HtmlEncoder.Default.Encode(email.ResolutionMessage)}</p>";
        var requestMessage = string.IsNullOrWhiteSpace(email.RequestMessage)
            ? ""
            : $"<p><strong>Request note:</strong> {HtmlEncoder.Default.Encode(email.RequestMessage)}</p>";

        var (subject, heading, content) = email.TemplateKey switch
        {
            "OnboardingApproved" => (
                "Your BI Sentinel access is approved",
                "Welcome to BI Sentinel",
                $"Your access request for <strong>{safeScope}</strong> as <strong>{safeRole}</strong> was approved. You can now sign in and use your assigned workspace.{optionalMessage}"),
            "AccessAdjustmentApproved" => (
                "Your BI Sentinel access was updated",
                "Access approved",
                $"Your request for <strong>{safeRole}</strong> access to <strong>{safeScope}</strong> was approved.{optionalMessage}"),
            "AccessRequestDeclined" => (
                "Update on your BI Sentinel access request",
                "Access request declined",
                $"Your request for <strong>{safeRole}</strong> access to <strong>{safeScope}</strong> was declined.{optionalMessage}"),
            _ => (
                "Action required: BI Sentinel access request",
                "Access request needs review",
                $"<strong>{requester}</strong> requested <strong>{safeRole}</strong> access to <strong>{safeScope}</strong>.{requestMessage}<p>Please review the request in BI Sentinel.</p>")
        };

        var html = $"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;background:#f3f6fb;color:#25334b;font-family:Segoe UI,Arial,sans-serif">
                <main style="max-width:620px;margin:32px auto;padding:28px;background:#fff;border:1px solid #e1e7f0;border-radius:12px">
                  <p style="color:#4262bc;font-size:12px;font-weight:700;letter-spacing:.1em">BI SENTINEL</p>
                  <h1 style="font-size:22px">{heading}</h1>
                  <div style="font-size:14px;line-height:1.6">{content}</div>
                  <p style="margin:24px 0"><a href="{link}" style="display:inline-block;padding:11px 16px;border-radius:7px;background:#315bd6;color:#fff;text-decoration:none;font-weight:700">Open access requests</a></p>
                  <p style="color:#75839a;font-size:11px">This is an automated access workflow notification.</p>
                </main>
              </body>
            </html>
            """;
        var plainMessage = email.TemplateKey == "AccessRequestReminder"
            ? $"{email.RequesterName} requested {role} access to {scope}. {email.RequestMessage} Review at {url}"
            : $"{content.Replace("<strong>", "", StringComparison.Ordinal).Replace("</strong>", "", StringComparison.Ordinal).Replace("<p>", "", StringComparison.Ordinal).Replace("</p>", Environment.NewLine, StringComparison.Ordinal)}{Environment.NewLine}{url}";
        return new AccessRequestEmailContent(subject, html, plainMessage);
    }

    private static string FormatRole(string roleName) => roleName switch
    {
        "OrgLead" => "Organization Lead",
        "ProjectLead" => "Project Lead",
        "TeamLead" => "Team Lead",
        "ReadOnly" => "Read-only",
        _ => roleName
    };
}
