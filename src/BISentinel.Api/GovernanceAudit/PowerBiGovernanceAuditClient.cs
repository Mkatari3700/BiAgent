using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Identity.Web;

namespace BISentinel.Api.GovernanceAudit;

public sealed class PowerBiGovernanceAuditClient(
    HttpClient httpClient,
    IPowerBiDelegatedAccessTokenProvider tokenProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<GovernanceWorkspace>> DiscoverWorkspacesAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        const int maximumDiscoveredWorkspaces = 50_000;
        var workspaces = new List<WorkspaceResponse>();
        for (var skip = 0; ; skip += pageSize)
        {
            var page = await GetCollectionAsync<WorkspaceResponse>(
                $"groups?$top={pageSize}&$skip={skip}",
                user,
                cancellationToken);
            workspaces.AddRange(page);
            if (page.Length < pageSize)
            {
                break;
            }

            if (workspaces.Count >= maximumDiscoveredWorkspaces)
            {
                throw new PowerBiAuditException(
                    $"Workspace discovery reached the {maximumDiscoveredWorkspaces} workspace safety limit. Narrow the Power BI identity's access before discovering workspaces.",
                    HttpStatusCode.BadGateway);
            }
        }

        return workspaces.Select(workspace => new GovernanceWorkspace(
            workspace.Id,
            workspace.Name,
            workspace.IsReadOnly,
            workspace.IsOnDedicatedCapacity)).ToArray();
    }

    public async Task<GovernanceAuditResult> ScanAsync(
        IReadOnlyCollection<Guid> workspaceIds,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (workspaceIds.Count == 0)
        {
            throw new InvalidOperationException(
                "No approved workspaces are configured. Select workspace IDs in Governance Audit Administration first.");
        }

        var audits = new List<GovernanceWorkspaceAudit>();
        var findings = new List<GovernanceFinding>();

        foreach (var workspaceId in workspaceIds)
        {
            var workspace = await GetAsync<WorkspaceResponse>(
                $"groups/{workspaceId:D}",
                user,
                cancellationToken);
            var reports = await GetCollectionAsync<ReportResponse>(
                $"groups/{workspaceId:D}/reports",
                user,
                cancellationToken);
            var datasets = await GetCollectionAsync<DatasetResponse>(
                $"groups/{workspaceId:D}/datasets",
                user,
                cancellationToken);
            var users = await GetCollectionAsync<UserResponse>(
                $"groups/{workspaceId:D}/users",
                user,
                cancellationToken);

            var workspaceModel = new GovernanceWorkspace(
                workspace.Id,
                workspace.Name,
                workspace.IsReadOnly,
                workspace.IsOnDedicatedCapacity);
            var reportModels = reports.Select(report => new GovernanceArtifact(
                report.Id,
                report.Name,
                "Report",
                report.DatasetId,
                null,
                report.WebUrl)).ToArray();
            var datasetModels = datasets.Select(dataset => new GovernanceArtifact(
                dataset.Id,
                dataset.Name,
                "Semantic model",
                null,
                dataset.ConfiguredBy,
                null)).ToArray();
            var userModels = users.Select(user => new GovernanceWorkspaceUser(
                user.Identifier,
                user.DisplayName,
                user.EmailAddress,
                user.PrincipalType,
                user.GroupUserAccessRight)).ToArray();
            audits.Add(new GovernanceWorkspaceAudit(
                workspaceModel,
                userModels,
                reportModels,
                datasetModels));

            if (!userModels.Any(user => string.Equals(user.AccessRight, "Admin", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new GovernanceFinding(
                    "GOV-001",
                    "Warning",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "No workspace administrator is visible",
                    "The workspace access response contains no Admin principal. Confirm ownership and the scan identity's visibility."));
            }

            foreach (var principal in userModels.Where(user =>
                         user.Identifier.Contains("#EXT#", StringComparison.OrdinalIgnoreCase) ||
                         user.EmailAddress?.Contains("#EXT#", StringComparison.OrdinalIgnoreCase) == true))
            {
                findings.Add(new GovernanceFinding(
                    "GOV-007",
                    "Information",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "External guest principal has workspace access",
                    $"Review whether the guest principal \"{principal.Identifier}\" still requires access and whether its permission level is appropriate."));
            }

            foreach (var principal in userModels.Where(user =>
                         string.Equals(user.AccessRight, "Member", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(user.AccessRight, "Contributor", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new GovernanceFinding(
                    "GOV-008",
                    "Information",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "Workspace principal has edit-level access",
                    $"Review whether the {principal.AccessRight} permission assigned to \"{principal.Identifier}\" is required; use the least-privileged workspace role that supports the user's duties."));
            }

            if (reportModels.Length == 0 && datasetModels.Length == 0)
            {
                findings.Add(new GovernanceFinding(
                    "GOV-002",
                    "Information",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "Workspace contains no reports or semantic models",
                    "Confirm whether this workspace is intentionally empty or can be removed."));
            }

            foreach (var report in reportModels.Where(report =>
                         Guid.TryParse(report.DatasetId, out var datasetId) &&
                         !datasets.Any(dataset => dataset.Id == datasetId)))
            {
                findings.Add(new GovernanceFinding(
                    "GOV-003",
                    "Information",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    report.Id,
                    "Report semantic model is not in the scanned workspace",
                    "The report references a semantic model not returned for this workspace. It may be shared from another workspace; verify the dependency before treating it as orphaned."));
            }

            foreach (var report in reportModels.Where(report => string.IsNullOrWhiteSpace(report.DatasetId)))
            {
                findings.Add(new GovernanceFinding(
                    "GOV-006",
                    "Information",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    report.Id,
                    "Report has no semantic model reference",
                    "Power BI did not return a semantic model ID for this report. Confirm whether it is an operationally expected report type or an incomplete dependency."));
            }

            var referencedDatasetIds = reportModels
                .Select(report => Guid.TryParse(report.DatasetId, out var datasetId) ? datasetId : (Guid?)null)
                .Where(datasetId => datasetId.HasValue)
                .Select(datasetId => datasetId!.Value)
                .ToHashSet();
            foreach (var dataset in datasetModels.Where(dataset => !referencedDatasetIds.Contains(dataset.Id)))
            {
                findings.Add(new GovernanceFinding(
                    "GOV-004",
                    "Information",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    dataset.Id,
                    "Semantic model is not referenced by a report in this workspace",
                    "No scanned report in this workspace references this semantic model. It may still be used by Excel, dashboards, other workspaces, or external clients; verify dependencies before treating it as orphaned."));
            }

            foreach (var dataset in datasetModels.Where(dataset => string.IsNullOrWhiteSpace(dataset.ConfiguredBy)))
            {
                findings.Add(new GovernanceFinding(
                    "GOV-005",
                    "Information",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    dataset.Id,
                    "Semantic model owner metadata is unavailable",
                    "Power BI did not return the configured-by identity for this semantic model. This does not prove that the model has no owner."));
            }

        }

        return new GovernanceAuditResult(Guid.Empty, DateTimeOffset.UtcNow, audits, findings);
    }

    private async Task<T> GetAsync<T>(
        string relativeUri,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        string accessToken;
        try
        {
            accessToken = await tokenProvider.GetAccessTokenAsync(user);
        }
        catch (PowerBiDelegatedConsentRequiredException)
        {
            throw new PowerBiAuditException(
                "Power BI access has not been authorized for this app. Select the \"Authorize Power BI read access\" button once and approve the requested delegated read scopes, or ask your Entra administrator to grant consent.",
                HttpStatusCode.Unauthorized,
                requiresConsent: true);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Power BI rejected the delegated access token. Sign in again and verify Power BI API consent.",
                HttpStatusCode.Forbidden => "Power BI denied access. Verify that your account has access to the selected workspace and that the delegated read scopes were consented.",
                (HttpStatusCode)429 => "Power BI throttled the audit request. Wait and retry after the service's Retry-After interval.",
                _ => $"Power BI REST API request failed with HTTP {(int)response.StatusCode}."
            };
            throw new PowerBiAuditException(detail, response.StatusCode);
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException("Power BI returned an empty response for a governance request.");
    }

    private async Task<T[]> GetCollectionAsync<T>(
        string relativeUri,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync<ODataCollection<T>>(relativeUri, user, cancellationToken);
        return response.Value
            ?? throw new PowerBiAuditException(
                "Power BI returned a collection without its expected value array.",
                HttpStatusCode.BadGateway);
    }

    private sealed record ODataCollection<T>(T[]? Value);

    private sealed record WorkspaceResponse(
        Guid Id,
        string Name,
        bool IsReadOnly,
        bool IsOnDedicatedCapacity);

    private sealed record ReportResponse(
        Guid Id,
        string Name,
        string? ReportType,
        string? DatasetId,
        string? WebUrl);

    private sealed record DatasetResponse(
        Guid Id,
        string Name,
        string? ConfiguredBy);

    private sealed record UserResponse(
        string Identifier,
        string? DisplayName,
        string? EmailAddress,
        string PrincipalType,
        string GroupUserAccessRight);
}

public sealed class PowerBiAuditException(
    string message,
    HttpStatusCode statusCode,
    bool requiresConsent = false)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public bool RequiresConsent { get; } = requiresConsent;
}
