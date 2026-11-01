using BISentinel.Api.Security;

namespace BISentinel.Api.Tests;

public sealed class AccessRequestTests
{
    [Theory]
    [InlineData("OnboardingApproved", "Welcome to BI Sentinel")]
    [InlineData("AccessAdjustmentApproved", "Access approved")]
    [InlineData("AccessRequestDeclined", "Access request declined")]
    [InlineData("AccessRequestReminder", "Access request needs review")]
    public void EmailTemplates_RenderDecisionAndReminderMessages(string templateKey, string expectedHeading)
    {
        var email = new AccessRequestEmail(
            Guid.NewGuid(),
            templateKey,
            "Jordan",
            "jordan@example.test",
            "<script>alert(1)</script>",
            "jordan@example.test",
            "Team",
            "Northwind / Analytics / Finance",
            "TeamLead",
            "Please review <b>my request</b>.",
            "Approved with <i>notes</i>.",
            DateTimeOffset.UtcNow);

        var content = AccessRequestEmailTemplates.Render(email, "https://sentinel.example.test");

        Assert.Contains(expectedHeading, content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("https://sentinel.example.test/access-requests", content.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", content.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>my request</b>", content.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>notes</i>", content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Team Lead", content.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailTemplates_UseRelativeLinkWhenPublicUrlIsNotConfigured()
    {
        var email = new AccessRequestEmail(
            Guid.NewGuid(),
            "AccessRequestReminder",
            "Lead",
            "lead@example.test",
            "Requester",
            null,
            "Organization",
            "Northwind",
            "OrgLead",
            null,
            null,
            DateTimeOffset.UtcNow);

        var content = AccessRequestEmailTemplates.Render(email, null);

        Assert.Contains("href=\"/access-requests\"", content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Organization Lead", content.HtmlBody, StringComparison.Ordinal);
    }
}
