using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BISentinel.Api.GovernanceAudit;
using BISentinel.Api.Reviews;

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
        var gatewayId = Guid.NewGuid();
        var requests = new List<string>();
        var requestedScopes = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            if (request.RequestUri.AbsolutePath.EndsWith(
                    "/admin/workspaces/getInfo",
                    StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            if (request.RequestUri.AbsolutePath.EndsWith(
                    $"/groups/{workspaceId}/reports/{reportId}/Export",
                    StringComparison.Ordinal))
            {
                return ReportExportResponse();
            }

            if (request.RequestUri.AbsolutePath.Contains("/refreshes", StringComparison.Ordinal) &&
                request.RequestUri.Query.Contains("$top=1", StringComparison.Ordinal))
            {
                var refreshStatus = request.RequestUri.AbsolutePath.Contains(
                    $"/datasets/{datasetId}/",
                    StringComparison.Ordinal)
                    ? """{"value":[{"status":"Failed","startTime":"2026-10-29T10:00:00Z","endTime":"2026-10-29T10:10:00Z"}]}"""
                    : """{"value":[]}""";
                return JsonResponse(refreshStatus);
            }

            var body = request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var path when path.EndsWith($"/groups/{workspaceId}/reports", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{reportId}}","name":"Revenue","reportType":"PowerBIReport","datasetId":"{{datasetId}}","webUrl":"https://app.powerbi.com/report"},{"id":"{{sharedDatasetReportId}}","name":"Shared","reportType":"PowerBIReport","datasetId":"{{Guid.NewGuid()}}"},{"id":"{{unlinkedReportId}}","name":"Unlinked","reportType":"PaginatedReport"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/datasets", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{datasetId}}","name":"Finance Model","configuredBy":"owner@example.com","isRefreshable":true},{"id":"{{unreferencedDatasetId}}","name":"Unused Candidate"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/datasets/{datasetId}/tables", StringComparison.Ordinal) =>
                    """{"value":[{"name":"Sales","columns":[{"name":"Amount","isKey":false},{"name":"UnusedColumn","isKey":false}],"measures":[{"name":"Revenue","expression":"SUM('Sales'[Amount])"},{"name":"RevenueWithTax","expression":"[Revenue] * 1.1"}]},{"name":"UnusedTable","columns":[],"measures":[]}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/datasets/{unreferencedDatasetId}/tables", StringComparison.Ordinal) =>
                    """{"value":[{"name":"DimCustomer","columns":[{"name":"CustomerKey","isKey":true}],"measures":[]}]}""",
                var path when path.EndsWith("/datasources", StringComparison.Ordinal) =>
                    $$$"""{"value":[{"gatewayId":"{{{gatewayId}}}","datasourceType":"Sql","connectionDetails":{"server":"sql01.example.test","database":"Finance","url":"https://host.example/path?access_token=must-not-be-retained","password":"must-not-be-retained"}}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/datasets/{datasetId}/refreshes", StringComparison.Ordinal) =>
                    """{"value":[{"status":"Failed","startTime":"2026-10-29T10:00:00Z","endTime":"2026-10-29T10:10:00Z"},{"status":"Failed","startTime":"2026-10-28T10:00:00Z","endTime":"2026-10-28T10:10:00Z"},{"status":"Completed","startTime":"2026-10-27T10:00:00Z","endTime":"2026-10-27T10:10:00Z"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/datasets/{unreferencedDatasetId}/refreshes", StringComparison.Ordinal) =>
                    """{"value":[{"status":"Completed","startTime":"2026-10-29T09:00:00Z","endTime":"2026-10-29T09:10:00Z"}]}""",
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
            new StubPowerBiAccessTokenProvider(scopes: requestedScopes));

        var result = await client.ScanAsync([workspaceId], CreateUser(), CancellationToken.None);

        Assert.Single(result.Workspaces);
        Assert.Equal(new[] { workspaceId }, result.SelectedWorkspaceIds);
        Assert.Equal(new[] { "Reports", "SemanticModels" }, result.SelectedArtifactTypes);
        Assert.Equal("Finance", result.Workspaces[0].Workspace.Name);
        Assert.Equal(3, result.Workspaces[0].Reports.Count);
        Assert.All(result.Workspaces[0].Reports, report => Assert.Equal("Report", report.Type));
        Assert.Equal(2, result.Workspaces[0].SemanticModels.Count);
        Assert.Equal("Semantic model", result.Workspaces[0].SemanticModels[0].Type);
        Assert.Equal(3, result.Workspaces[0].Users.Count);
        Assert.Equal(3, result.Workspaces[0].ReportConnections.Count);
        var revenueConnection = result.Workspaces[0].ReportConnections.Single(connection =>
            connection.ReportId == reportId);
        Assert.Equal("Finance Model", revenueConnection.SemanticModelName);
        Assert.Equal("Sql", revenueConnection.SourceType);
        Assert.Equal(
            "server: sql01.example.test; database: Finance; url: https://host.example/path",
            revenueConnection.ConnectionDetails);
        Assert.Equal("Offline", revenueConnection.Status);
        Assert.Contains("latest refresh failed", revenueConnection.StatusDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Not checked", revenueConnection.GatewayStatus);
        Assert.Contains("Select gateway inventory", revenueConnection.GatewayStatusDetail);
        Assert.DoesNotContain("must-not-be-retained", revenueConnection.ConnectionDetails, StringComparison.Ordinal);
        Assert.Contains(
            result.Workspaces[0].ReportConnections,
            connection => connection.ReportId == unlinkedReportId && connection.Status == "Unavailable");
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-003" && finding.ArtifactId == sharedDatasetReportId);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-004" && finding.ArtifactId == unreferencedDatasetId);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-005" && finding.ArtifactId == unreferencedDatasetId);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-006" && finding.ArtifactId == unlinkedReportId);
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-007" && finding.Detail.Contains("#EXT#", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-008" && finding.Detail.Contains("Contributor", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding =>
            finding.Code == "OPT-001" && finding.ArtifactName == "UnusedTable" && finding.Status == "FAIL");
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Code == "OPT-001" && finding.ArtifactName == "Sales");
        Assert.Contains(result.Findings, finding =>
            finding.Code == "OPT-002" && finding.ArtifactName == "Sales[UnusedColumn]");
        Assert.Contains(result.Findings, finding =>
            finding.Code == "OPT-003" && finding.ArtifactName == "Sales[RevenueWithTax]");
        Assert.Contains(requests, request => request.EndsWith($"/datasets/{datasetId}/tables", StringComparison.Ordinal));
        Assert.Contains(
            "https://analysis.windows.net/powerbi/api/Dataset.Read.All",
            requestedScopes,
            StringComparer.Ordinal);
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(finding.Code.StartsWith("OPT-", StringComparison.Ordinal) ? "1.1" : "1.0", finding.CheckVersion);
            Assert.Equal("BI Governance Team", finding.RuleOwner);
            Assert.True(finding.EvidenceTimestamp >= result.ScannedUtc);
            Assert.NotEmpty(finding.EvidenceSource);
            Assert.NotEmpty(finding.EvaluationLogic);
            Assert.NotEmpty(finding.ExceptionRules);
            Assert.Equal(JsonValueKind.Object, finding.Evidence.ValueKind);
            Assert.Contains(finding.Status, new[] { "PASS", "FAIL", "INSUFFICIENT_EVIDENCE" });
        });
        Assert.Equal("INSUFFICIENT_EVIDENCE", result.Findings.Single(finding =>
            finding.Code == "GOV-005" && finding.Status == "INSUFFICIENT_EVIDENCE").Status);
        Assert.Equal("INSUFFICIENT_EVIDENCE", result.Findings.Single(finding =>
            finding.Code == "GOV-006" && finding.Status == "INSUFFICIENT_EVIDENCE").Status);
        Assert.Contains(requests, request => request.Contains("/refreshes", StringComparison.Ordinal));
        Assert.Equal(14, requests.Count);
        Assert.All(
            requests.Where(request => !request.Contains("/admin/workspaces/getInfo", StringComparison.Ordinal)),
            request => Assert.Contains(workspaceId.ToString(), request, StringComparison.OrdinalIgnoreCase));

        var selectedCheckResult = await client.ScanAsync(
            [workspaceId],
            CreateUser(),
            CancellationToken.None,
            ["GOV-004"]);
        Assert.Equal(2, selectedCheckResult.Findings.Count);
        Assert.All(selectedCheckResult.Findings, finding => Assert.Equal("GOV-004", finding.Code));
        Assert.Contains(selectedCheckResult.Findings, finding => finding.Status == "PASS");
        Assert.Contains(selectedCheckResult.Findings, finding => finding.Status == "FAIL");
        var refreshFindings = result.Findings.Where(finding => finding.Code == "GOV-009").ToArray();
        Assert.Equal(2, refreshFindings.Length);
        var failedRefresh = Assert.Single(refreshFindings, finding => finding.ArtifactId == datasetId);
        Assert.Equal("FAIL", failedRefresh.Status);
        Assert.Contains("2 consecutive failed refresh", failedRefresh.Detail, StringComparison.Ordinal);
        Assert.Contains(
            "/groups/{workspaceId}/datasets/{datasetId}/refreshes?$top=60",
            GovernanceCheckCatalog.All.Single(check => check.CheckId == "GOV-009").EvidenceSource,
            StringComparison.Ordinal);
        Assert.Contains("apiEndpoint", failedRefresh.Evidence.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(refreshFindings, finding => finding.ArtifactId == unreferencedDatasetId && finding.Status == "PASS");
    }

    [Fact]
    public async Task ScanAsync_ReportsRefreshHistoryPermissionAsInsufficientEvidence()
    {
        var workspaceId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith($"/groups/{workspaceId}/datasets/{datasetId}/refreshes", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            var body = path switch
            {
                var value when value.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var value when value.EndsWith($"/groups/{workspaceId}/datasets", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{datasetId}}","name":"Finance Model"}]}""",
                var value when value.EndsWith($"/groups/{workspaceId}/users", StringComparison.Ordinal) =>
                    """{"value":[]}""",
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

        var result = await client.ScanAsync(
            [workspaceId],
            CreateUser(),
            CancellationToken.None,
            ["GOV-009"],
            ["SemanticModels"]);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("INSUFFICIENT_EVIDENCE", finding.Status);
        Assert.Contains("write permission", finding.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("403", finding.Evidence.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanAsync_ReportsRefreshHistoryMissingWhenSemanticModelsNotSelected()
    {
        var workspaceId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path switch
            {
                var value when value.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var value when value.EndsWith($"/groups/{workspaceId}/reports", StringComparison.Ordinal) =>
                    """{"value":[]}""",
                var value when value.EndsWith($"/groups/{workspaceId}/users", StringComparison.Ordinal) =>
                    """{"value":[]}""",
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

        var result = await client.ScanAsync(
            [workspaceId],
            CreateUser(),
            CancellationToken.None,
            ["GOV-009"],
            ["Reports"]);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("GOV-009", finding.Code);
        Assert.Equal("INSUFFICIENT_EVIDENCE", finding.Status);
    }

    [Fact]
    public async Task ScanAsync_RejectsUnknownCheckIdsBeforeCallingPowerBi()
    {
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(new StubHttpMessageHandler(_ =>
                throw new InvalidOperationException("Power BI should not be called for an invalid check selection")))
            {
                BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/")
            },
            new StubPowerBiAccessTokenProvider());

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => client.ScanAsync(
                [Guid.NewGuid()],
                CreateUser(),
                CancellationToken.None,
                ["GOV-UNKNOWN"]));

        Assert.Contains("GOV-UNKNOWN", error.Message);
    }

    [Fact]
    public void GovernanceAuditSelection_UsesApprovedScopeAndRejectsExpansion()
    {
        var approvedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        Assert.Equal(
            approvedIds,
            GovernanceAuditSelection.ValidateWorkspaceIds(approvedIds, null, maximumWorkspaces: 10));
        Assert.Equal(
            new[] { approvedIds[1] },
            GovernanceAuditSelection.ValidateWorkspaceIds(approvedIds, [approvedIds[1]], maximumWorkspaces: 10));
        Assert.Throws<ArgumentException>(() =>
            GovernanceAuditSelection.ValidateWorkspaceIds(approvedIds, [Guid.NewGuid()], maximumWorkspaces: 10));
        Assert.Throws<ArgumentException>(() =>
            GovernanceAuditSelection.ValidateWorkspaceIds(approvedIds, [approvedIds[0], approvedIds[0]], maximumWorkspaces: 10));
        Assert.Throws<ArgumentException>(() =>
            GovernanceAuditSelection.ValidateWorkspaceIds(approvedIds, approvedIds, maximumWorkspaces: 1));
        Assert.Throws<ArgumentException>(() =>
            GovernanceAuditSelection.ValidateWorkspaceIds(approvedIds, [], maximumWorkspaces: 10));
    }

    [Fact]
    public async Task ScanAsync_RequestsOnlySelectedArtifactTypesAndMarksDependentChecksInsufficient()
    {
        var workspaceId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            var body = request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var path when path.EndsWith($"/groups/{workspaceId}/reports", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{reportId}}","name":"Revenue","reportType":"PowerBIReport","datasetId":"{{datasetId}}"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/users", StringComparison.Ordinal) =>
                    """{"value":[]}""",
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

        var result = await client.ScanAsync(
            [workspaceId],
            CreateUser(),
            CancellationToken.None,
            ["GOV-002", "GOV-003", "GOV-004", "GOV-005", "GOV-006"],
            ["reports"]);

        Assert.Equal(new[] { "Reports" }, result.SelectedArtifactTypes);
        Assert.Contains(requests, request => request.EndsWith("/reports", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, request => request.EndsWith("/datasets", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-002" && finding.Status == "INSUFFICIENT_EVIDENCE");
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-003" && finding.Status == "INSUFFICIENT_EVIDENCE");
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-004" && finding.Status == "INSUFFICIENT_EVIDENCE");
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-005" && finding.Status == "INSUFFICIENT_EVIDENCE");
        Assert.Contains(result.Findings, finding => finding.Code == "GOV-006" && finding.Status == "PASS");
    }

    [Fact]
    public async Task ScanAsync_FiltersSelectedReportsAndDashboardsAndMarksPartialInventory()
    {
        var workspaceId = Guid.NewGuid();
        var selectedReportId = Guid.NewGuid();
        var otherReportId = Guid.NewGuid();
        var selectedDashboardId = Guid.NewGuid();
        var otherDashboardId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var path when path.EndsWith($"/groups/{workspaceId}/reports", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{selectedReportId}}","name":"Revenue"},{"id":"{{otherReportId}}","name":"Forecast","datasetId":"{{datasetId}}"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/dashboards", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{selectedDashboardId}}","displayName":"Executive"},{"id":"{{otherDashboardId}}","displayName":"Operations"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/datasets", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{datasetId}}","name":"Forecast Model","configuredBy":"owner@example.com"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/users", StringComparison.Ordinal) =>
                    """{"value":[]}""",
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

        var result = await client.ScanAsync(
            [workspaceId],
            CreateUser(),
            CancellationToken.None,
            ["GOV-002", "GOV-004", "GOV-006"],
            ["Reports", "Dashboards", "SemanticModels"],
            [selectedReportId],
            [selectedDashboardId]);

        Assert.Equal(selectedReportId, Assert.Single(result.Workspaces[0].Reports).Id);
        Assert.Equal(selectedDashboardId, Assert.Single(result.Workspaces[0].Dashboards).Id);
        Assert.Contains(result.Findings, finding =>
            finding.Code == "GOV-002" && finding.Status == "INSUFFICIENT_EVIDENCE");
        Assert.Contains(result.Findings, finding =>
            finding.Code == "GOV-006" && finding.Status == "INSUFFICIENT_EVIDENCE");
        Assert.Contains(result.Findings, finding =>
            finding.Code == "GOV-006" && finding.ArtifactId == selectedReportId && finding.Status == "INSUFFICIENT_EVIDENCE");
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Code == "GOV-004" && finding.ArtifactId == datasetId);
    }

    [Fact]
    public async Task GetWorkspaceArtifactCatalogAsync_ReturnsNamedReportsAndDashboards()
    {
        var workspaceId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var path when path.EndsWith($"/groups/{workspaceId}/reports", StringComparison.Ordinal) =>
                    """{"value":[{"id":"00000000-0000-0000-0000-000000000001","name":"Revenue"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/dashboards", StringComparison.Ordinal) =>
                    """{"value":[{"id":"00000000-0000-0000-0000-000000000002","displayName":"Executive"}]}""",
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

        var catalog = await client.GetWorkspaceArtifactCatalogAsync(
            workspaceId,
            CreateUser(),
            CancellationToken.None);

        Assert.Equal("Finance", catalog.WorkspaceName);
        Assert.Equal("Revenue", Assert.Single(catalog.Reports).Name);
        Assert.Equal("Executive", Assert.Single(catalog.Dashboards).Name);
    }

    [Fact]
    public async Task ScanAsync_IncludesOnlySelectedAccountLevelAppsAndGateways()
    {
        var workspaceId = Guid.NewGuid();
        var selectedAppId = Guid.NewGuid();
        var otherAppId = Guid.NewGuid();
        var selectedGatewayId = Guid.NewGuid();
        var otherGatewayId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var path when path.EndsWith("/apps", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{selectedAppId}}","name":"Finance App","publishedBy":"Owner"},{"id":"{{otherAppId}}","name":"Operations App"}]}""",
                var path when path.EndsWith("/gateways", StringComparison.Ordinal) =>
                    $$$"""{"value":[{"id":"{{{selectedGatewayId}}}","name":"Finance Gateway","type":"Resource","gatewayStatus":"Online","publicKey":{"modulus":"must not be retained"}},{"id":"{{{otherGatewayId}}}","name":"Operations Gateway","type":"Resource","gatewayStatus":"Offline"}]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/reports", StringComparison.Ordinal) =>
                    """{"value":[]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/datasets", StringComparison.Ordinal) =>
                    """{"value":[]}""",
                var path when path.EndsWith($"/groups/{workspaceId}/users", StringComparison.Ordinal) =>
                    """{"value":[]}""",
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

        var result = await client.ScanAsync(
            [workspaceId],
            CreateUser(),
            CancellationToken.None,
            ["GOV-001"],
            ["Reports", "SemanticModels", "Apps", "Gateways"],
            selectedAppIds: [selectedAppId],
            selectedGatewayIds: [selectedGatewayId]);

        Assert.Equal("Finance App", Assert.Single(result.Apps).Name);
        Assert.Equal("Owner", result.Apps[0].ConfiguredBy);
        Assert.Equal("Finance Gateway", Assert.Single(result.Gateways).Name);
        Assert.Equal("Online", result.Gateways[0].Status);
        Assert.Equal("On-premises", result.Gateways[0].GatewayType);
    }

    [Fact]
    public async Task GetGatewaysAsync_ExplainsGatewayAdministratorRequirement()
    {
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)))
            {
                BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/")
            },
            new StubPowerBiAccessTokenProvider());

        var error = await Assert.ThrowsAsync<PowerBiAuditException>(
            () => client.GetGatewaysAsync(CreateUser(), CancellationToken.None));

        Assert.Contains("administrator of at least one gateway", error.Message);
    }

    [Fact]
    public async Task ScanAsync_RejectsUnsupportedArtifactTypesBeforeCallingPowerBi()
    {
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(new StubHttpMessageHandler(_ =>
                throw new InvalidOperationException("Power BI should not be called for an invalid artifact selection")))
            {
                BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/")
            },
            new StubPowerBiAccessTokenProvider());

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => client.ScanAsync(
                [Guid.NewGuid()],
                CreateUser(),
                CancellationToken.None,
                selectedArtifactTypes: ["Dataflows"]));

        Assert.Contains("Dataflows", error.Message);
    }

    [Fact]
    public async Task ScanAsync_RejectsEmptyAppIdsWithSpecificErrorBeforeCallingPowerBi()
    {
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(new StubHttpMessageHandler(_ =>
                throw new InvalidOperationException("Power BI should not be called for an empty app ID")))
            {
                BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/")
            },
            new StubPowerBiAccessTokenProvider());

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => client.ScanAsync(
                [Guid.NewGuid()],
                CreateUser(),
                CancellationToken.None,
                selectedArtifactTypes: ["Apps"],
                selectedAppIds: [Guid.Empty]));

        Assert.Contains("empty GUID", error.Message);
        Assert.Equal("appIds", error.ParamName);
    }

    [Fact]
    public void GovernanceOptimizationFindings_UsesPowerBiMetadataForAllObjectChecks()
    {
        var workspaceId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var evidenceTimestamp = DateTimeOffset.UtcNow;
        var tables = new[]
        {
            new GovernanceModelTable(
                "Sales",
                [
                    new GovernanceModelColumn("Amount", false),
                    new GovernanceModelColumn("UnusedColumn", false)
                ],
                [
                    new GovernanceModelMeasure("Revenue", "SUM('Sales'[Amount])"),
                    new GovernanceModelMeasure("RevenueWithTax", "[Revenue] * 1.1")
                ]),
            new GovernanceModelTable("UnusedTable", [], [])
        };

        var findings = GovernanceOptimizationFindings.CreateFromPowerBiMetadata(
            workspaceId,
            "Finance",
            datasetId,
            "Finance Model",
            tables,
            null,
            ["OPT-001", "OPT-002", "OPT-003"],
            evidenceTimestamp,
            [
                new PowerBiReportFieldReference("Sales", "Amount", "Column", "Sales.Amount"),
                new PowerBiReportFieldReference("Sales", "Revenue", "Measure", "Sales.Revenue")
            ],
            reportUsageComplete: true,
            reportsExpected: 1,
            reportsScanned: 1);

        Assert.Equal(3, findings.Count);
        Assert.Equal("UnusedTable", Assert.Single(findings, finding => finding.Code == "OPT-001").ArtifactName);
        Assert.Equal("Sales[UnusedColumn]", Assert.Single(findings, finding => finding.Code == "OPT-002").ArtifactName);
        Assert.Equal("Sales[RevenueWithTax]", Assert.Single(findings, finding => finding.Code == "OPT-003").ArtifactName);
        Assert.Equal(
            1,
            Assert.Single(findings, finding => finding.Code == "OPT-001")
                .Evidence.GetProperty("objectCounts")
                .GetProperty("potentiallyUnusedTableCount")
                .GetInt32());
        Assert.Equal(
            1,
            Assert.Single(findings, finding => finding.Code == "OPT-002")
                .Evidence.GetProperty("objectCounts")
                .GetProperty("potentiallyUnusedColumnCount")
                .GetInt32());
        Assert.Equal(
            1,
            Assert.Single(findings, finding => finding.Code == "OPT-003")
                .Evidence.GetProperty("objectCounts")
                .GetProperty("potentiallyUnusedMeasureCount")
                .GetInt32());
        Assert.All(findings, finding =>
        {
            Assert.Equal(workspaceId, finding.WorkspaceId);
            Assert.Equal("Finance", finding.WorkspaceName);
            Assert.Equal(evidenceTimestamp, finding.EvidenceTimestamp);
            Assert.Equal(JsonValueKind.Object, finding.Evidence.ValueKind);
            Assert.Contains("Power BI REST API", finding.EvidenceSource, StringComparison.Ordinal);
            Assert.Contains("verify them before making changes", finding.Detail, StringComparison.Ordinal);
            Assert.Contains("potentiallyUnused", finding.Evidence.GetRawText(), StringComparison.Ordinal);
        });
        Assert.DoesNotContain(findings, finding =>
            finding.Code == "OPT-001" && finding.ArtifactName == "Sales");
    }

    [Fact]
    public void PowerBiReportLayoutReader_ExtractsVisualColumnAndMeasureReferences()
    {
        using var export = new MemoryStream(CreateReportExport());

        var references = PowerBiReportLayoutReader.ReadReferences(export);

        Assert.Contains(references, reference =>
            reference.TableName == "Sales" &&
            reference.FieldName == "Amount" &&
            reference.Kind == "Column");
        Assert.Contains(references, reference =>
            reference.TableName == "Sales" &&
            reference.FieldName == "Revenue" &&
            reference.Kind == "Measure");
    }

    [Fact]
    public void PowerBiReportLayoutReader_ExtractsReferencesFromPbirDefinitionFiles()
    {
        using var export = new MemoryStream(CreatePbirReportExport());

        var references = PowerBiReportLayoutReader.ReadReferences(export);

        Assert.Contains(references, reference =>
            reference.TableName == "Sales" &&
            reference.FieldName == "Amount" &&
            reference.Kind == "Column");
    }

    [Fact]
    public void GovernanceOptimizationFindings_ReportsUnavailableMetadataAsInsufficientEvidence()
    {
        var finding = Assert.Single(GovernanceOptimizationFindings.CreateFromPowerBiMetadata(
            Guid.NewGuid(),
            "Finance",
            Guid.NewGuid(),
            "Finance Model",
            null,
            "Power BI returned HTTP 403 for model metadata.",
            ["OPT-001"],
            DateTimeOffset.UtcNow));

        Assert.Equal("INSUFFICIENT_EVIDENCE", finding.Status);
        Assert.Contains("HTTP 403", finding.Detail);
    }

    [Fact]
    public void GovernanceOptimizationFindings_DoesNotPassWhenReportReferencesAreUnavailable()
    {
        var finding = Assert.Single(GovernanceOptimizationFindings.CreateFromPowerBiMetadata(
            Guid.NewGuid(),
            "Finance",
            Guid.NewGuid(),
            "Finance Model",
            [
                new GovernanceModelTable(
                    "Sales",
                    [new GovernanceModelColumn("Amount", false)],
                    [new GovernanceModelMeasure("Revenue", "SUM('Sales'[Amount])")])
            ],
            null,
            ["OPT-001"],
            DateTimeOffset.UtcNow));

        Assert.Equal("INSUFFICIENT_EVIDENCE", finding.Status);
        Assert.Contains("Report visual usage", finding.Detail);
    }

    [Fact]
    public async Task ScanAsync_UsesAdminScannerForSelectedWorkspaceModelMetadata()
    {
        var workspaceId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var requestedScopes = new List<string>();
        var scannerRequestWorkspaceIds = Array.Empty<Guid>();
        var scannerPostCount = 0;
        var fallbackTablesRequested = false;
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/admin/workspaces/getInfo", StringComparison.Ordinal))
            {
                scannerPostCount++;
                Assert.Equal(HttpMethod.Post, request.Method);
                using var requestBody = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                scannerRequestWorkspaceIds = requestBody.RootElement.GetProperty("workspaces")
                    .EnumerateArray()
                    .Select(value => value.GetGuid())
                    .ToArray();
                return JsonResponse($$"""{"id":"{{scanId}}"}""");
            }

            if (path.EndsWith($"/admin/workspaces/scanStatus/{scanId}", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                return JsonResponse("""{"status":"Succeeded"}""");
            }

            if (path.EndsWith($"/admin/workspaces/scanResult/{scanId}", StringComparison.Ordinal))
            {
                return JsonResponse(
                    $$"""{"workspaces":[{"id":"{{workspaceId}}","name":"Finance","datasets":[{"id":"{{datasetId}}","name":"Model","tables":[{"name":"Sales","columns":[{"name":"Amount","isKey":false}],"measures":[{"name":"Revenue","expression":"SUM('Sales'[Amount])"}]},{"name":"Unused DimCustomer","columns":[{"name":"CustomerKey","isKey":true}],"measures":[]}]}]}]}""");
            }

            if (path.EndsWith($"/groups/{workspaceId}/reports/{reportId}/Export", StringComparison.Ordinal))
            {
                return UnsupportedReportExportResponse();
            }

            if (path.EndsWith($"/groups/{workspaceId}/datasets/{datasetId}/tables", StringComparison.Ordinal))
            {
                fallbackTablesRequested = true;
                throw new InvalidOperationException("The legacy tables endpoint should not be needed when scanner metadata is available.");
            }

            var body = path switch
            {
                var value when value.EndsWith($"/groups/{workspaceId}", StringComparison.Ordinal) =>
                    $$"""{"id":"{{workspaceId}}","name":"Finance","isReadOnly":false,"isOnDedicatedCapacity":true}""",
                var value when value.EndsWith($"/groups/{workspaceId}/reports", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{reportId}}","name":"Finance report","reportType":"PowerBIReport","datasetId":"{{datasetId}}"}]}""",
                var value when value.EndsWith($"/groups/{workspaceId}/datasets", StringComparison.Ordinal) =>
                    $$"""{"value":[{"id":"{{datasetId}}","name":"Model","configuredBy":"owner@example.test"}]}""",
                var value when value.EndsWith($"/groups/{workspaceId}/users", StringComparison.Ordinal) =>
                    """{"value":[]}""",
                var value when value.EndsWith("/datasources", StringComparison.Ordinal) =>
                    """{"value":[]}""",
                _ => throw new InvalidOperationException($"Unexpected Power BI request: {request.RequestUri}")
            };
            return JsonResponse(body);
        });
        var client = new PowerBiGovernanceAuditClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/") },
            new StubPowerBiAccessTokenProvider(scopes: requestedScopes));

        var result = await client.ScanAsync(
            [workspaceId],
            CreateUser(),
            CancellationToken.None,
            ["OPT-001"],
            ["Reports", "SemanticModels"]);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("Model", finding.ArtifactName);
        Assert.Equal("INSUFFICIENT_EVIDENCE", finding.Status);
        Assert.Contains("neither the classic Report/Layout file nor supported PBIR definition JSON", finding.Detail);
        Assert.Equal(1, scannerPostCount);
        Assert.Equal([workspaceId], scannerRequestWorkspaceIds);
        Assert.Contains(
            "https://analysis.windows.net/powerbi/api/Tenant.Read.All",
            requestedScopes);
        Assert.Contains(
            "https://analysis.windows.net/powerbi/api/Report.Read.All",
            requestedScopes);
        Assert.False(fallbackTablesRequested);
    }

    [Fact]
    public void GovernanceCheckCatalog_HasUniqueVersionedChecksWithEvidenceAndExceptionGuidance()
    {
        Assert.NotEmpty(GovernanceCheckCatalog.All);
        Assert.Equal(
            GovernanceCheckCatalog.All.Count,
            GovernanceCheckCatalog.All.Select(check => check.CheckId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(GovernanceCheckCatalog.All, check =>
        {
            Assert.False(string.IsNullOrWhiteSpace(check.Version));
            Assert.False(string.IsNullOrWhiteSpace(check.Owner));
            Assert.False(string.IsNullOrWhiteSpace(check.EvidenceSource));
            Assert.False(string.IsNullOrWhiteSpace(check.EvaluationLogic));
            Assert.False(string.IsNullOrWhiteSpace(check.ExceptionRules));
        });
    }

    [Fact]
    public void GovernanceCheckCatalog_UsesSeveritiesAcceptedByAuditHistorySchema()
    {
        var supportedSeverities = new[]
        {
            "Critical",
            "High",
            "Medium",
            "Low",
            "Error",
            "Warning",
            "Information"
        };

        Assert.All(
            GovernanceCheckCatalog.All,
            check => Assert.Contains(check.Severity, supportedSeverities));
    }

    [Fact]
    public void GovernanceExceptionApplicator_OnlyOverlaysMatchingUnexpiredApprovedFailures()
    {
        var now = DateTimeOffset.UtcNow;
        var workspaceId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var finding = new GovernanceFinding(
            "GOV-009",
            "High",
            workspaceId,
            "Finance",
            Guid.NewGuid(),
            "Semantic model",
            "Finance Model",
            "Refresh failed",
            "Latest refresh failed.",
            "1.0",
            "Refresh",
            "Governance",
            "Power BI REST",
            now,
            "FAIL",
            "Latest refresh failed",
            "Review before remediation.",
            "Completed",
            "Failed",
            JsonDocument.Parse("""{"status":"Failed"}""").RootElement.Clone());
        var exception = new GovernanceExceptionRecord(
            Guid.NewGuid(),
            workspaceId,
            finding.Code,
            finding.ArtifactId,
            finding.ArtifactType,
            finding.ArtifactName!,
            "Finance owner",
            "Temporary maintenance window",
            "admin@example.test",
            now.AddDays(-1),
            now.AddDays(10),
            "Approved",
            runId,
            [new GovernanceExceptionAuditEvent("Approved", now.AddDays(-1), "admin@example.test", "Approved")]);

        var result = new GovernanceAuditResult(runId, now, [], [finding]);
        var applied = GovernanceExceptionApplicator.Apply(result, [exception], now);
        var approvedFinding = Assert.Single(applied.Findings);

        Assert.Equal("EXCEPTION_APPROVED", approvedFinding.Status);
        Assert.Equal("FAIL", approvedFinding.OriginalStatus);
        Assert.Equal(exception.ExceptionId, approvedFinding.ExceptionRecord?.ExceptionId);
        Assert.Single(approvedFinding.ExceptionRecord!.AuditTrail);
        Assert.Equal("Failed", approvedFinding.Evidence.GetProperty("status").GetString());

        var expired = GovernanceExceptionApplicator.Apply(
            result,
            [exception with { ExpiresUtc = now.AddSeconds(-1) }],
            now);
        Assert.Equal("FAIL", Assert.Single(expired.Findings).Status);
        Assert.Equal("Expired", expired.Findings[0].ExceptionRecord?.Status);

        var revoked = GovernanceExceptionApplicator.Apply(
            result,
            [exception with { Status = "Revoked" }],
            now);
        Assert.Equal("FAIL", Assert.Single(revoked.Findings).Status);
        Assert.Equal("Revoked", revoked.Findings[0].ExceptionRecord?.Status);
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

    [Fact]
    public void GovernanceExcelExporter_CreatesMultiSheetWorkbookWithFindingsAndAccess()
    {
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var user = new GovernanceWorkspaceUser(
            "owner@example.test",
            "Workspace Owner",
            "owner@example.test",
            "User",
            "Admin");
        var workspace = new GovernanceWorkspace(workspaceId, "Finance", false, true);
        var finding = new GovernanceFinding(
            "GOV-001",
            "Warning",
            workspaceId,
            "Finance",
            null,
            "Workspace",
            "Finance",
            "Workspace administrator",
            "No visible administrator was returned.",
            "1.0",
            "Visible workspace administrator",
            "BI Governance Team",
            "Power BI REST API",
            DateTimeOffset.UtcNow,
            "FAIL",
            "Check workspace user roles.",
            "Review the identity permissions.",
            "At least one Admin",
            "No Admin returned",
            JsonSerializer.SerializeToElement(new { accessRight = "Viewer" }));
        var result = new GovernanceAuditResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            [
                new GovernanceWorkspaceAudit(
                    workspace,
                    [user],
                    [new GovernanceArtifact(reportId, "Revenue", "Report", null, null, null)],
                    [new GovernanceArtifact(Guid.NewGuid(), "Finance Model", "Semantic model", null, null, null)],
                    [])
                {
                    ReportConnections =
                    [
                        new GovernanceReportConnection(
                            reportId,
                            "Revenue",
                            "Finance Model",
                            "Sql",
                            "server: sql01",
                            null,
                            "Online",
                            "Latest refresh completed.",
                            "Offline",
                            "Gateway is not running.")
                    ]
                }
            ],
            [finding])
        {
            ScannedBy = "audit@example.test"
        };

        var bytes = GovernanceExcelExporter.CreateWorkbook(result);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var workbook = ReadEntry(archive, "xl/workbook.xml");
        var workbookDocument = XDocument.Parse(workbook);
        Assert.Contains("Findings", workbook, StringComparison.Ordinal);
        Assert.Contains("Run Summary", workbook, StringComparison.Ordinal);
        Assert.Contains("Workspace Access", workbook, StringComparison.Ordinal);
        Assert.Contains("Connections", workbook, StringComparison.Ordinal);
        Assert.Equal(7, workbookDocument.Root!.Element(
            XName.Get("sheets", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))!.Elements().Count());
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            _ = XDocument.Parse(ReadEntry(archive, entry.FullName));
        }
        var findings = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        Assert.Contains("GOV-001", findings, StringComparison.Ordinal);
        Assert.Contains("No visible administrator was returned.", findings, StringComparison.Ordinal);
        Assert.Contains("accessRight", findings, StringComparison.Ordinal);
        var summary = ReadEntry(archive, "xl/worksheets/sheet2.xml");
        Assert.Contains("Finance", summary, StringComparison.Ordinal);
        Assert.Contains("Run ID", summary, StringComparison.Ordinal);
        Assert.Contains("audit@example.test", summary, StringComparison.Ordinal);
        var access = ReadEntry(archive, "xl/worksheets/sheet4.xml");
        Assert.Contains("owner@example.test", access, StringComparison.Ordinal);
        Assert.Contains("Admin", access, StringComparison.Ordinal);
        var connections = ReadEntry(archive, "xl/worksheets/sheet6.xml");
        Assert.Contains("Connection status", connections, StringComparison.Ordinal);
        Assert.Contains("Gateway status", connections, StringComparison.Ordinal);
        Assert.Contains("Online", connections, StringComparison.Ordinal);
        Assert.Contains("Offline", connections, StringComparison.Ordinal);
        var checkReference = ReadEntry(archive, "xl/worksheets/sheet7.xml");
        Assert.Contains("OPT-001", checkReference, StringComparison.Ordinal);
        Assert.Contains("Expected result", checkReference, StringComparison.Ordinal);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Workbook entry {name} is missing.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static HttpResponseMessage ReportExportResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateReportExport())
        };

    private static HttpResponseMessage UnsupportedReportExportResponse()
    {
        using var export = new MemoryStream();
        using (var archive = new ZipArchive(export, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("Metadata");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(export.ToArray())
        };
    }

    private static byte[] CreateReportExport()
    {
        using var export = new MemoryStream();
        using (var archive = new ZipArchive(export, ZipArchiveMode.Create, leaveOpen: true))
        {
            var layout = archive.CreateEntry("Report/Layout");
            using var stream = layout.Open();
            var json = """{"sections":[{"visualContainers":[{"config":"{\"singleVisual\":{\"Column\":{\"queryRef\":\"Sales.Amount\"},\"Measure\":{\"queryRef\":\"Sales.Revenue\"}}}"}]}]}""";
            var bytes = Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes(json))
                .ToArray();
            stream.Write(bytes);
        }

        return export.ToArray();
    }

    private static byte[] CreatePbirReportExport()
    {
        using var export = new MemoryStream();
        using (var archive = new ZipArchive(export, ZipArchiveMode.Create, leaveOpen: true))
        {
            var visual = archive.CreateEntry("Report/definition/pages/page1/visuals/visual1/visual.json");
            using var stream = visual.Open();
            var json = """{"visual":{"Column":{"Expression":{"SourceRef":{"Entity":"Sales"}},"Property":"Amount"}}}""";
            var bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes);
        }

        return export.ToArray();
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

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
        Exception? error = null,
        List<string>? scopes = null) : IPowerBiDelegatedAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(ClaimsPrincipal user, string scope)
        {
            scopes?.Add(scope);
            return error is null
                ? Task.FromResult("test-token")
                : Task.FromException<string>(error);
        }
    }
}
