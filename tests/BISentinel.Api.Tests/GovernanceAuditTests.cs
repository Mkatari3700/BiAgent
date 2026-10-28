using System.Net;
using System.Security.Claims;
using System.Text;
using BISentinel.Api.GovernanceAudit;

namespace BISentinel.Api.Tests;

public sealed class GovernanceAuditTests
{
    [Fact]
    public async Task ScanAsync_UsesOnlySelectedWorkspacesAndCreatesReadOnlyFindings()
    {
        var workspaceId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var unreferencedDatasetId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var sharedDatasetReportId = Guid.NewGuid();
        var unlinkedReportId = Guid.NewGuid();
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            var body = request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var path when path.EndsWith($"/groups/{workspaceId}/reports", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{reportId}}","name":"Revenue","reportType":"PowerBIReport","datasetId":"{{datasetId}}","webUrl":"https://app.powerbi.com/report"},{"id":"{{sharedDatasetReportId}}","name":"Shared","reportType":"PowerBIReport","datasetId":"{{Guid.NewGuid()}}"},{"id":"{{unlinkedReportId}}","name":"Unlinked","reportType":"PaginatedReport"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/datasets", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{datasetId}}","name":"Finance Model","configuredBy":"owner@example.com","isRefreshable":true},{"id":"{{unreferencedDatasetId}}","name":"Unused Candidate"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/users", StringComparison.Ordinal) =>
                    """{"value":[{"identifier":"owner@example.com","displayName":"Workspace Owner","emailAddress":"owner@example.com","principalType":"User","groupUserAccessRight":"Admin"},{"identifier":"viewer@example.com","displayName":"Viewer","principalType":"User","groupUserAccessRight":"Viewer"},{"identifier":"guest_example.com#EXT#@tenant.onmicrosoft.com","displayName":"Guest","emailAddress":"guest_example.com#EXT#@tenant.onmicrosoft.com","principalType":"User","groupUserAccessRight":"Contributor"}]}""",
                _ => throw new InvalidOperationException($"Unexpected Power BI request: {request.RequestUri}")
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/") },
            new StubPowerBiAccessTokenProvider());

        var result = await client.ScanAsync([workspaceId], CreateUser(), CancellationToken.None);

        Assert.Single(result.Workspaces);
        Assert.Equal("Finance", result.Workspaces[0].Workspace.Name);
        Assert.Equal(3, result.Workspaces[0].Reports.Count);
        Assert.All(result.Workspaces[0].Reports, report => Assert.Equal("Report", report.Type));
        Assert.Equal(2, result.Workspaces[0].SemanticModels.Count);
        Assert.Equal("Semantic model", result.Workspaces[0].SemanticModels[0].Type);
        Assert.Equal(3, result.Workspaces[0].Users.Count);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-003" && finding.ArtifactId == sharedDatasetReportId);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-004" && finding.ArtifactId == unreferencedDatasetId);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-005" && finding.ArtifactId == unreferencedDatasetId);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-006" && finding.ArtifactId == unlinkedReportId);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-007" && finding.Detail.Contains("#EXT#", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-008" && finding.Detail.Contains("Contributor", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, request => request.Contains("/refreshes", StringComparison.Ordinal));
        Assert.Equal(4, requests.Count);
        Assert.All(requests, request => Assert.Contains(workspaceId.ToString(), request, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverWorkspacesAsync_ReturnsOnlyWorkspacesVisibleToPowerBiIdentity()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/v1.0/myorg/groups", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":[{"id":"00000000-0000-0000-0000-000000000001","name":"Approved workspace","isReadOnly":true,"isOnDedicatedCapacity":false}]}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/") },
            new StubPowerBiAccessTokenProvider());

        var workspaces = await client.DiscoverWorkspacesAsync(CreateUser(), CancellationToken.None);

        var workspace = Assert.Single(workspaces);
        Assert.Equal("Approved workspace", workspace.Name);
        Assert.True(workspace.IsReadOnly);
    }

    [Fact]
    public async Task DiscoverWorkspacesAsync_DoesNotReturnPowerBiErrorBody()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("sensitive upstream detail", Encoding.UTF8, "text/plain")
        });
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/") },
            new StubPowerBiAccessTokenProvider());

        var error = await Assert.ThrowsAsync<PowerBiAuditException>(
            () => client.DiscoverWorkspacesAsync(CreateUser(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.DoesNotContain("sensitive upstream detail", error.Message);
        Assert.Contains("account has access", error.Message);
    }

    [Fact]
    public async Task DiscoverWorkspacesAsync_ReportsDelegatedConsentRequirement()
    {
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(new StubHttpMessageHandler(_ =>
                throw new InvalidOperationException("Power BI should not be called without a token")))
            {
                BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/")
            },
            new StubPowerBiAccessTokenProvider(
                new PowerBiDelegatedConsentRequiredException(new InvalidOperationException())));

        var error = await Assert.ThrowsAsync<PowerBiAuditException>(
            () => client.DiscoverWorkspacesAsync(CreateUser(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Contains("Authorize Power BI read access", error.Message);
        Assert.True(error.RequiresConsent);
    }

    private static ClaimsPrincipal CreateUser() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Governance Admin")], "test"));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubPowerBiAccessTokenProvider(
        Exception? error = null) : IPowerBiDelegatedAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(ClaimsPrincipal user) =>
            error is null
                ? Task.FromResult("test-token")
                : Task.FromException<string>(error);
    }
}
