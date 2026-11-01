using System.Net;
using System.Security.Claims;
using BISentinel.Api.GovernanceAudit;
using BISentinel.Api.Reviews;
using Xunit;

namespace BISentinel.Api.Tests;

public sealed class PowerBiOnlineReviewClientTests
{
    [Fact]
    public async Task ScanWorkspaceAsync_RejectsWorkspaceOutsideSignedInUsersPowerBiAccess()
    {
        var accessibleWorkspaceId = Guid.NewGuid();
        var requestedWorkspaceId = Guid.NewGuid();
        var requests = new List<string>();
        using var httpClient = CreateClient((request, _) =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return JsonResponse(
                $$"""{"value":[{"id":"{{accessibleWorkspaceId}}","name":"Accessible","isReadOnly":false,"isOnDedicatedCapacity":false}]}""");
        });
        var client = CreateReviewClient(httpClient);

        var exception = await Assert.ThrowsAsync<PowerBiOnlineReviewException>(
            () => client.ScanWorkspaceAsync(
                requestedWorkspaceId,
                CreateUser(),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Single(requests);
        Assert.DoesNotContain(requests, request => request.Contains(requestedWorkspaceId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanWorkspaceAsync_ReturnsOnlyAccessibleWorkspaceAndAvailableSchemaWithLimitations()
    {
        var workspaceId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        using var httpClient = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath.Replace(
                "/v1.0/myorg/",
                string.Empty,
                StringComparison.Ordinal);
            if (path == "groups")
            {
                return JsonResponse(
                    $$"""{"value":[{"id":"{{workspaceId}}","name":"Sales workspace","isReadOnly":false,"isOnDedicatedCapacity":true}]}""");
            }

            if (path == $"groups/{workspaceId}/datasets")
            {
                return JsonResponse(
                    $$"""{"value":[{"id":"{{datasetId}}","name":"Sales model"}]}""");
            }

            if (path == $"groups/{workspaceId}/reports")
            {
                return JsonResponse(
                    $$"""{"value":[{"id":"{{Guid.NewGuid()}}","name":"Sales report","datasetId":"{{datasetId}}"}]}""");
            }

            if (path == $"groups/{workspaceId}/datasets/{datasetId}/tables")
            {
                return JsonResponse(
                    """{"tables":[{"name":"FactSales","columns":[{"name":"SaleId","dataType":"Int64"}]}]}""");
            }

            if (path == $"groups/{workspaceId}/datasets/{datasetId}/relationships")
            {
                return JsonResponse(
                    """{"value":[{"fromTable":"FactSales","fromColumn":"SaleId","toTable":"DimSale","toColumn":"SaleId","fromCardinality":"Many","toCardinality":"One","crossFilteringBehavior":"OneDirection","isActive":true}]}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateReviewClient(httpClient);

        var result = await client.ScanWorkspaceAsync(
            workspaceId,
            CreateUser(),
            CancellationToken.None);

        Assert.Equal("Sales workspace", result.Project.ProjectName);
        Assert.Single(result.Project.Tables);
        Assert.Equal("SaleId", result.Project.Tables[0].Columns[0].Name);
        Assert.Single(result.Project.Relationships);
        Assert.Equal(1, result.ReportCount);
        Assert.Equal(1, result.SemanticModelCount);
        Assert.Equal(1, result.SchemaAvailableModelCount);
        Assert.Contains(result.EvidenceLimitations, item => item.Contains("DAX measures", StringComparison.Ordinal));
    }

    private static PowerBiOnlineReviewClient CreateReviewClient(HttpClient httpClient) =>
        new(httpClient, new StubTokenProvider());

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        new(new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/")
        };

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
    };

    private static ClaimsPrincipal CreateUser() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "analyst")], "test"));

    private sealed class StubTokenProvider : IPowerBiDelegatedAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(ClaimsPrincipal user, string scope) =>
            Task.FromResult("test-token");
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken));
    }
}
