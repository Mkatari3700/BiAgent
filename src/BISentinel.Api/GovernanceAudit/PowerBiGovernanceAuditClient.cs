using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Identity.Web;

namespace BISentinel.Api.GovernanceAudit;

public sealed class PowerBiGovernanceAuditClient(
    HttpClient httpClient,
    IPowerBiDelegatedAccessTokenProvider tokenProvider,
    ILogger<PowerBiGovernanceAuditClient>? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const long MaximumReportExportBytes = 150L * 1024 * 1024;

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

    public async Task<IReadOnlyList<GovernanceWorkspace>> GetApprovedWorkspaceDetailsAsync(
        IReadOnlyCollection<Guid> approvedWorkspaceIds,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var workspaces = new List<GovernanceWorkspace>(approvedWorkspaceIds.Count);
        foreach (var workspaceId in approvedWorkspaceIds)
        {
            var workspace = await GetAsync<WorkspaceResponse>(
                $"groups/{workspaceId:D}",
                user,
                cancellationToken);
            workspaces.Add(new GovernanceWorkspace(
                workspace.Id,
                workspace.Name,
                workspace.IsReadOnly,
                workspace.IsOnDedicatedCapacity));
        }

        return workspaces;
    }

    public async Task<GovernanceWorkspaceArtifactCatalog> GetWorkspaceArtifactCatalogAsync(
        Guid workspaceId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var workspace = await GetAsync<WorkspaceResponse>(
            $"groups/{workspaceId:D}",
            user,
            cancellationToken);
        var reports = await GetCollectionAsync<ReportResponse>(
            $"groups/{workspaceId:D}/reports",
            user,
            cancellationToken);
        var dashboards = await GetCollectionAsync<DashboardResponse>(
            $"groups/{workspaceId:D}/dashboards",
            user,
            cancellationToken);

        return new GovernanceWorkspaceArtifactCatalog(
            workspace.Id,
            workspace.Name,
            reports.Select(report => new GovernanceArtifact(
                report.Id,
                report.Name,
                "Report",
                report.DatasetId,
                null,
                report.WebUrl)).ToArray(),
            dashboards.Select(dashboard => new GovernanceArtifact(
                dashboard.Id,
                dashboard.DisplayName,
                "Dashboard",
                null,
                null,
                dashboard.WebUrl)).ToArray());
    }

    public async Task<IReadOnlyList<GovernanceArtifact>> GetAppsAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var apps = await GetCollectionAsync<AppResponse>("apps", user, cancellationToken);
        return apps.Select(app => new GovernanceArtifact(
            app.Id,
            app.Name,
            "App",
            null,
            app.PublishedBy,
            null)).ToArray();
    }

    public async Task<IReadOnlyList<GovernanceArtifact>> GetGatewaysAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        GatewayResponse[] gateways;
        try
        {
            gateways = await GetCollectionAsync<GatewayResponse>("gateways", user, cancellationToken);
        }
        catch (PowerBiAuditException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new PowerBiAuditException(
                "Power BI returned no gateway inventory because the signed-in user must be an administrator of at least one gateway.",
                exception.StatusCode,
                innerException: exception);
        }

        return gateways.Select(gateway => new GovernanceArtifact(
            gateway.Id,
            gateway.Name,
            "Gateway",
            null,
            null,
            null,
            NormalizeGatewayStatus(gateway.GatewayStatus),
            NormalizeGatewayType(gateway.Type))).ToArray();
    }

    public async Task<GovernanceAuditResult> ScanAsync(
        IReadOnlyCollection<Guid> workspaceIds,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? selectedCheckIds = null,
        IReadOnlyCollection<string>? selectedArtifactTypes = null,
        IReadOnlyCollection<Guid>? selectedReportIds = null,
        IReadOnlyCollection<Guid>? selectedDashboardIds = null,
        IReadOnlyCollection<Guid>? selectedAppIds = null,
        IReadOnlyCollection<Guid>? selectedGatewayIds = null)
    {
        if (workspaceIds.Count == 0)
        {
            throw new InvalidOperationException(
                "No approved workspaces are configured. Select workspace IDs in Governance Audit Administration first.");
        }

        var selectedChecks = selectedCheckIds is null
            ? GovernanceCheckCatalog.All.Select(check => check.CheckId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : selectedCheckIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownChecks = selectedChecks
            .Where(checkId => !GovernanceCheckCatalog.TryGet(checkId, out _))
            .ToArray();
        if (unknownChecks.Length > 0)
        {
            throw new ArgumentException(
                $"Unsupported governance check IDs: {string.Join(", ", unknownChecks)}.",
                nameof(selectedCheckIds));
        }

        if (selectedChecks.Count == 0)
        {
            throw new ArgumentException("Select at least one governance check.", nameof(selectedCheckIds));
        }

        var requestedArtifactTypes = selectedArtifactTypes ?? ["Reports", "SemanticModels"];
        var unknownArtifactTypes = requestedArtifactTypes
            .Where(type =>
                !string.Equals(type, "Reports", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "SemanticModels", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "Dashboards", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "Apps", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "Gateways", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownArtifactTypes.Length > 0)
        {
            throw new ArgumentException(
                $"Unsupported governance artifact types: {string.Join(", ", unknownArtifactTypes)}.",
                nameof(selectedArtifactTypes));
        }

        var selectedArtifacts = requestedArtifactTypes
            .Select(type => string.Equals(type, "Reports", StringComparison.OrdinalIgnoreCase)
                ? "Reports"
                : string.Equals(type, "SemanticModels", StringComparison.OrdinalIgnoreCase)
                    ? "SemanticModels"
                    : string.Equals(type, "Dashboards", StringComparison.OrdinalIgnoreCase)
                        ? "Dashboards"
                        : string.Equals(type, "Apps", StringComparison.OrdinalIgnoreCase)
                            ? "Apps"
                            : "Gateways")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedArtifacts.Count == 0)
        {
            throw new ArgumentException("Select at least one supported artifact type.", nameof(selectedArtifactTypes));
        }

        var reportsSelected = selectedArtifacts.Contains("Reports");
        var modelsSelected = selectedArtifacts.Contains("SemanticModels");
        var dashboardsSelected = selectedArtifacts.Contains("Dashboards");
        var appsSelected = selectedArtifacts.Contains("Apps");
        var gatewaysSelected = selectedArtifacts.Contains("Gateways");
        var reportIdFilter = selectedReportIds?.ToHashSet();
        var dashboardIdFilter = selectedDashboardIds?.ToHashSet();
        var appIdFilter = selectedAppIds?.ToHashSet();
        var gatewayIdFilter = selectedGatewayIds?.ToHashSet();
        ValidateSelectedArtifactIds(selectedReportIds, reportsSelected, "reportIds");
        ValidateSelectedArtifactIds(selectedDashboardIds, dashboardsSelected, "dashboardIds");
        ValidateSelectedArtifactIds(selectedAppIds, appsSelected, "appIds");
        ValidateSelectedArtifactIds(selectedGatewayIds, gatewaysSelected, "gatewayIds");
        var apps = appsSelected
            ? await GetAppsAsync(user, cancellationToken)
            : [];
        var gateways = gatewaysSelected
            ? await GetGatewaysAsync(user, cancellationToken)
            : [];
        ValidateSelectedGlobalArtifactIds(apps, appIdFilter, "apps");
        ValidateSelectedGlobalArtifactIds(gateways, gatewayIdFilter, "gateways");
        if (appIdFilter is not null)
        {
            apps = apps.Where(app => appIdFilter.Contains(app.Id)).ToArray();
        }
        if (gatewayIdFilter is not null)
        {
            gateways = gateways.Where(gateway => gatewayIdFilter.Contains(gateway.Id)).ToArray();
        }
        var scanStartedUtc = DateTimeOffset.UtcNow;
        var audits = new List<GovernanceWorkspaceAudit>();
        var findings = new List<GovernanceFinding>();
        var returnedReportIds = new HashSet<Guid>();
        var returnedDashboardIds = new HashSet<Guid>();
        var selectedOptimizationChecks = selectedChecks
            .Where(checkId => checkId.StartsWith("OPT-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        IReadOnlyList<WorkspaceMetadataResponse>? scannedWorkspaces = null;
        string? workspaceMetadataError = null;
        if (selectedOptimizationChecks.Length > 0 && modelsSelected)
        {
            try
            {
                scannedWorkspaces = await ScanWorkspaceMetadataAsync(
                    workspaceIds,
                    user,
                    cancellationToken);
            }
            catch (PowerBiAuditException exception)
            {
                workspaceMetadataError = exception.Message;
                logger?.LogWarning(
                    exception,
                    "Power BI metadata scanner was unavailable for {WorkspaceCount} explicitly selected workspace(s).",
                    workspaceIds.Count);
            }
        }

        foreach (var workspaceId in workspaceIds)
        {
            var workspace = await GetAsync<WorkspaceResponse>(
                $"groups/{workspaceId:D}",
                user,
                cancellationToken);
            var allWorkspaceReports = reportsSelected
                ? await GetCollectionAsync<ReportResponse>(
                    $"groups/{workspaceId:D}/reports",
                    user,
                    cancellationToken)
                : [];
            var workspaceReportIds = allWorkspaceReports.Select(report => report.Id).ToHashSet();
            returnedReportIds.UnionWith(workspaceReportIds);
            var selectedWorkspaceReportIds = reportIdFilter is null
                ? workspaceReportIds
                : reportIdFilter.Intersect(workspaceReportIds).ToHashSet();
            var reportInventoryPartial = reportIdFilter is not null &&
                !selectedWorkspaceReportIds.SetEquals(workspaceReportIds);
            var reports = reportIdFilter is null
                ? allWorkspaceReports
                : allWorkspaceReports.Where(report => reportIdFilter.Contains(report.Id)).ToArray();
            var datasets = modelsSelected
                ? await GetCollectionAsync<DatasetResponse>(
                    $"groups/{workspaceId:D}/datasets",
                    user,
                    cancellationToken)
                : [];
            var dashboards = dashboardsSelected
                ? await GetCollectionAsync<DashboardResponse>(
                    $"groups/{workspaceId:D}/dashboards",
                    user,
                    cancellationToken)
                : [];
            returnedDashboardIds.UnionWith(dashboards.Select(dashboard => dashboard.Id));
            if (dashboardIdFilter is not null)
            {
                dashboards = dashboards.Where(dashboard => dashboardIdFilter.Contains(dashboard.Id)).ToArray();
            }
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
            var dashboardModels = dashboards.Select(dashboard => new GovernanceArtifact(
                dashboard.Id,
                dashboard.DisplayName,
                "Dashboard",
                null,
                null,
                dashboard.WebUrl)).ToArray();
            var userModels = users.Select(user => new GovernanceWorkspaceUser(
                user.Identifier,
                user.DisplayName,
                user.EmailAddress,
                user.PrincipalType,
                user.GroupUserAccessRight)).ToArray();
            var reportConnections = await GetReportConnectionsAsync(
                workspace.Id,
                reportModels,
                datasetModels,
                modelsSelected,
                selectedChecks.Contains("GOV-009"),
                gatewaysSelected,
                gateways,
                user,
                cancellationToken);
            var evidenceTimestamp = DateTimeOffset.UtcNow;
            audits.Add(new GovernanceWorkspaceAudit(
                workspaceModel,
                userModels,
                reportModels,
                datasetModels,
                dashboardModels)
            {
                ReportConnections = reportConnections
            });

            if (selectedChecks.Contains("GOV-001"))
            {
                var hasVisibleAdmin = userModels.Any(user =>
                    string.Equals(user.AccessRight, "Admin", StringComparison.OrdinalIgnoreCase));
                findings.Add(CreateFinding(
                    "GOV-001",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    hasVisibleAdmin ? "Visible workspace administrator found" : "No workspace administrator is visible",
                    hasVisibleAdmin
                        ? "At least one principal with Admin access was returned for the workspace."
                        : "The workspace access response contains no Admin principal. Confirm ownership and the scan identity's visibility.",
                    evidenceTimestamp,
                    "At least one visible Admin principal",
                    hasVisibleAdmin ? "Visible Admin principal returned" : "No visible Admin principal",
                    new { principalCount = userModels.Length, accessRights = userModels.Select(user => user.AccessRight).Distinct().ToArray() },
                    status: hasVisibleAdmin ? "PASS" : "FAIL",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }
            else if (selectedChecks.Contains("GOV-003") && reportInventoryPartial)
            {
                findings.Add(CreateFinding(
                    "GOV-003",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "Report dependency inventory is filtered",
                    "Dependencies are evaluated only for the selected reports. Select all reports to assess the full workspace report inventory.",
                    evidenceTimestamp,
                    "All reports and semantic models are selected",
                    "Only a subset of reports was selected",
                    new { selectedReportCount = reportModels.Length, workspaceReportCount = workspaceReportIds.Count },
                    status: "INSUFFICIENT_EVIDENCE",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            var guestPrincipals = selectedChecks.Contains("GOV-007")
                ? userModels.Where(user =>
                         user.Identifier.Contains("#EXT#", StringComparison.OrdinalIgnoreCase) ||
                         user.EmailAddress?.Contains("#EXT#", StringComparison.OrdinalIgnoreCase) == true).ToArray()
                : [];
            foreach (var principal in guestPrincipals)
            {
                findings.Add(CreateFinding(
                    "GOV-007",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "External guest principal has workspace access",
                    $"Review whether the guest principal \"{principal.Identifier}\" still requires access and whether its permission level is appropriate.",
                    evidenceTimestamp,
                    "Guest access is approved and periodically reviewed",
                    principal.AccessRight,
                    new { principal.Identifier, principal.DisplayName, principal.PrincipalType, principal.AccessRight },
                    artifactType: "Workspace principal",
                    artifactName: principal.DisplayName ?? principal.Identifier));
            }

            if (selectedChecks.Contains("GOV-007") && guestPrincipals.Length == 0)
            {
                findings.Add(CreateFinding(
                    "GOV-007",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "No external guest marker found",
                    "No workspace principal identifier or email included Entra's #EXT# guest marker.",
                    evidenceTimestamp,
                    "Guest access is approved and periodically reviewed",
                    "No #EXT# guest marker returned",
                    new { principalCount = userModels.Length },
                    status: "PASS",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            var editLevelPrincipals = selectedChecks.Contains("GOV-008")
                ? userModels.Where(user =>
                         string.Equals(user.AccessRight, "Member", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(user.AccessRight, "Contributor", StringComparison.OrdinalIgnoreCase)).ToArray()
                : [];
            foreach (var principal in editLevelPrincipals)
            {
                findings.Add(CreateFinding(
                    "GOV-008",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "Workspace principal has edit-level access",
                    $"Review whether the {principal.AccessRight} permission assigned to \"{principal.Identifier}\" is required; use the least-privileged workspace role that supports the user's duties.",
                    evidenceTimestamp,
                    "Workspace role is least-privileged for the user's responsibilities",
                    principal.AccessRight,
                    new { principal.Identifier, principal.DisplayName, principal.PrincipalType, principal.AccessRight },
                    artifactType: "Workspace principal",
                    artifactName: principal.DisplayName ?? principal.Identifier));
            }

            if (selectedChecks.Contains("GOV-008") && editLevelPrincipals.Length == 0)
            {
                findings.Add(CreateFinding(
                    "GOV-008",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "No Member or Contributor role returned",
                    "No workspace principal with Member or Contributor access was returned.",
                    evidenceTimestamp,
                    "Workspace role is least-privileged for the user's responsibilities",
                    "No Member or Contributor role returned",
                    new { principalCount = userModels.Length },
                    status: "PASS",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            if (selectedChecks.Contains("GOV-002"))
            {
                var hasArtifacts = reportModels.Length > 0 || datasetModels.Length > 0;
                var completeInventorySelected = reportsSelected && !reportInventoryPartial && modelsSelected;
                findings.Add(CreateFinding(
                    "GOV-002",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    !completeInventorySelected
                        ? "Workspace inventory is partial or filtered"
                        : hasArtifacts ? "Workspace artifacts found" : "Workspace contains no reports or semantic models",
                    !completeInventorySelected
                        ? "Select all Reports and Semantic models to determine whether this workspace contains either supported artifact type."
                        : hasArtifacts
                        ? "At least one report or semantic model was returned."
                        : "Confirm whether this workspace is intentionally empty or can be removed.",
                    evidenceTimestamp,
                    "At least one report or semantic model",
                    !completeInventorySelected
                        ? $"Partial inventory: {string.Join(", ", selectedArtifacts)}; report selection filtered={reportInventoryPartial}"
                        : hasArtifacts ? "Artifacts returned" : "No reports or semantic models returned",
                    new { reportCount = reportModels.Length, semanticModelCount = datasetModels.Length, selectedArtifactTypes = selectedArtifacts },
                    status: !completeInventorySelected ? "INSUFFICIENT_EVIDENCE" : hasArtifacts ? "PASS" : "FAIL",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            if (selectedChecks.Contains("GOV-003") && (!reportsSelected || !modelsSelected))
            {
                findings.Add(CreateFinding(
                    "GOV-003",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "Report-to-model dependency check has partial evidence",
                    "Select both Reports and Semantic models to compare each report's model reference with the workspace inventory.",
                    evidenceTimestamp,
                    "Both reports and semantic models are selected",
                    $"Selected: {string.Join(", ", selectedArtifacts)}",
                    new { selectedArtifactTypes = selectedArtifacts },
                    status: "INSUFFICIENT_EVIDENCE",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            foreach (var report in selectedChecks.Contains("GOV-003")
                         && reportsSelected && modelsSelected
                         ? reportModels.Where(report =>
                         Guid.TryParse(report.DatasetId, out var datasetId) &&
                         !datasets.Any(dataset => dataset.Id == datasetId))
                         : [])
            {
                findings.Add(CreateFinding(
                    "GOV-003",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    report.Id,
                    "Report semantic model is not in the scanned workspace",
                    "The report references a semantic model not returned for this workspace. It may be shared from another workspace; verify the dependency before treating it as orphaned.",
                    evidenceTimestamp,
                    "Report model is present in the workspace or validated as shared",
                    report.DatasetId ?? "Not returned",
                    new { report.Id, report.Name, report.DatasetId, workspaceModels = datasets.Select(dataset => dataset.Id).ToArray() },
                    artifactType: "Report",
                    artifactName: report.Name));
            }

            foreach (var report in selectedChecks.Contains("GOV-003")
                         && reportsSelected && modelsSelected
                         ? reportModels.Where(report =>
                         Guid.TryParse(report.DatasetId, out var datasetId) &&
                         datasets.Any(dataset => dataset.Id == datasetId))
                         : [])
            {
                findings.Add(CreateFinding(
                    "GOV-003",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    report.Id,
                    "Report semantic model found in workspace",
                    "The report's referenced semantic model was returned in this workspace's scan.",
                    evidenceTimestamp,
                    "Report model is present in the workspace or validated as shared",
                    report.DatasetId ?? "Not returned",
                    new { report.Id, report.Name, report.DatasetId },
                    status: "PASS",
                    artifactType: "Report",
                    artifactName: report.Name));
            }

            foreach (var report in selectedChecks.Contains("GOV-006")
                         && reportsSelected
                         ? reportModels.Where(report => string.IsNullOrWhiteSpace(report.DatasetId))
                         : [])
            {
                findings.Add(CreateFinding(
                    "GOV-006",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    report.Id,
                    "Report has no semantic model reference",
                    "Power BI did not return a semantic model ID for this report. Confirm whether it is an operationally expected report type or an incomplete dependency.",
                    evidenceTimestamp,
                    "Report dependency is known or the report type is approved",
                    "Semantic model ID was not returned",
                    new { report.Id, report.Name, report.DatasetId },
                    status: "INSUFFICIENT_EVIDENCE",
                    artifactType: "Report",
                    artifactName: report.Name));
            }

            foreach (var report in selectedChecks.Contains("GOV-006")
                         && reportsSelected
                         ? reportModels.Where(report => !string.IsNullOrWhiteSpace(report.DatasetId))
                         : [])
            {
                findings.Add(CreateFinding(
                    "GOV-006",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    report.Id,
                    "Report model reference returned",
                    "Power BI returned a semantic model ID for this report.",
                    evidenceTimestamp,
                    "Report dependency is known or the report type is approved",
                    report.DatasetId ?? "Not returned",
                    new { report.Id, report.Name, report.DatasetId },
                    status: "PASS",
                    artifactType: "Report",
                    artifactName: report.Name));
            }

            if (selectedChecks.Contains("GOV-006") && (!reportsSelected || reportInventoryPartial))
            {
                findings.Add(CreateFinding(
                    "GOV-006",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    reportInventoryPartial
                        ? "Report dependency inventory is filtered"
                        : "Report dependency check was not run",
                    reportInventoryPartial
                        ? "Model references are evaluated only for the selected reports. Select all reports to assess the full workspace."
                        : "Reports were not selected for this scan, so their model references could not be evaluated.",
                    evidenceTimestamp,
                    "All reports are selected for dependency review",
                    reportInventoryPartial ? "Only a subset of reports was selected" : "Reports not selected",
                    new { selectedArtifactTypes = selectedArtifacts, reportInventoryPartial },
                    status: "INSUFFICIENT_EVIDENCE",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            if (selectedChecks.Contains("GOV-004") &&
                (!reportsSelected || !modelsSelected || reportInventoryPartial))
            {
                findings.Add(CreateFinding(
                    "GOV-004",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    reportInventoryPartial
                        ? "Model usage check has filtered report evidence"
                        : "Model usage check has partial evidence",
                    reportInventoryPartial
                        ? "Only selected reports were compared with the semantic-model inventory. Select all reports to identify models with no report reference."
                        : "Select both Reports and Semantic models to compare report references with the workspace's model inventory.",
                    evidenceTimestamp,
                    "All Reports and Semantic models are selected",
                    reportInventoryPartial
                        ? $"Filtered report selection; scanned {reportModels.Length} of {workspaceReportIds.Count} reports"
                        : $"Selected: {string.Join(", ", selectedArtifacts)}",
                    new { selectedArtifactTypes = selectedArtifacts, reportInventoryPartial },
                    status: "INSUFFICIENT_EVIDENCE",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            var referencedDatasetIds = reportModels
                .Select(report => Guid.TryParse(report.DatasetId, out var datasetId) ? datasetId : (Guid?)null)
                .Where(datasetId => datasetId.HasValue)
                .Select(datasetId => datasetId!.Value)
                .ToHashSet();
            if (selectedChecks.Contains("GOV-005") && !modelsSelected)
            {
                findings.Add(CreateFinding(
                    "GOV-005",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "Semantic model owner check was not run",
                    "Semantic models were not selected for this scan, so configured-by metadata could not be evaluated.",
                    evidenceTimestamp,
                    "Semantic models are selected for owner metadata review",
                    "Semantic models not selected",
                    new { selectedArtifactTypes = selectedArtifacts },
                    status: "INSUFFICIENT_EVIDENCE",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            if (selectedChecks.Contains("GOV-009") && !modelsSelected)
            {
                findings.Add(CreateFinding(
                    "GOV-009",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    null,
                    "Refresh history was not requested",
                    "Semantic models were not selected, so refresh history was not retrieved. Select semantic models to evaluate refresh status.",
                    evidenceTimestamp,
                    "Semantic models and their refresh history are selected",
                    "Semantic models not selected",
                    new
                    {
                        apiEndpoint = $"groups/{workspaceModel.Id:D}/datasets/{{datasetId}}/refreshes?$top=60",
                        selectedArtifactTypes = selectedArtifacts
                    },
                    status: "INSUFFICIENT_EVIDENCE",
                    artifactType: "Workspace",
                    artifactName: workspaceModel.Name));
            }

            foreach (var dataset in selectedChecks.Contains("GOV-004")
                         && reportsSelected && modelsSelected && !reportInventoryPartial
                         ? datasetModels.Where(dataset => !referencedDatasetIds.Contains(dataset.Id))
                         : [])
            {
                findings.Add(CreateFinding(
                    "GOV-004",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    dataset.Id,
                    "Semantic model is not referenced by a report in this workspace",
                    "No scanned report in this workspace references this semantic model. It may still be used by Excel, dashboards, other workspaces, or external clients; verify dependencies before treating it as orphaned.",
                    evidenceTimestamp,
                    "Model has a known consumer or an approved exception",
                    "No report reference in this workspace",
                    new { dataset.Id, dataset.Name, scannedReportCount = reportModels.Length },
                    artifactType: "Semantic model",
                    artifactName: dataset.Name));
            }

            foreach (var dataset in selectedChecks.Contains("GOV-004")
                         && reportsSelected && modelsSelected && !reportInventoryPartial
                         ? datasetModels.Where(dataset => referencedDatasetIds.Contains(dataset.Id))
                         : [])
            {
                findings.Add(CreateFinding(
                    "GOV-004",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    dataset.Id,
                    "Semantic model referenced by a report",
                    "At least one scanned report in this workspace references this semantic model.",
                    evidenceTimestamp,
                    "Model has a known consumer or an approved exception",
                    "Referenced by a report in this workspace",
                    new { dataset.Id, dataset.Name, scannedReportCount = reportModels.Length },
                    status: "PASS",
                    artifactType: "Semantic model",
                    artifactName: dataset.Name));
            }

            foreach (var dataset in selectedChecks.Contains("GOV-005")
                         && modelsSelected
                         ? datasetModels.Where(dataset => string.IsNullOrWhiteSpace(dataset.ConfiguredBy))
                         : [])
            {
                findings.Add(CreateFinding(
                    "GOV-005",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    dataset.Id,
                    "Semantic model owner metadata is unavailable",
                    "Power BI did not return the configured-by identity for this semantic model. This does not prove that the model has no owner.",
                    evidenceTimestamp,
                    "An owner is recorded or missing metadata is reviewed",
                    "configuredBy was not returned",
                    new { dataset.Id, dataset.Name, dataset.ConfiguredBy },
                    status: "INSUFFICIENT_EVIDENCE",
                    artifactType: "Semantic model",
                    artifactName: dataset.Name));
            }

            foreach (var dataset in selectedChecks.Contains("GOV-005")
                         && modelsSelected
                         ? datasetModels.Where(dataset => !string.IsNullOrWhiteSpace(dataset.ConfiguredBy))
                         : [])
            {
                findings.Add(CreateFinding(
                    "GOV-005",
                    workspaceModel.Id,
                    workspaceModel.Name,
                    dataset.Id,
                    "Semantic model owner metadata returned",
                    "Power BI returned configuredBy metadata for this semantic model.",
                    evidenceTimestamp,
                    "An owner is recorded or missing metadata is reviewed",
                    dataset.ConfiguredBy ?? "Not returned",
                    new { dataset.Id, dataset.Name, dataset.ConfiguredBy },
                    status: "PASS",
                    artifactType: "Semantic model",
                    artifactName: dataset.Name));
            }

            if (selectedChecks.Contains("GOV-009") && modelsSelected)
            {
                foreach (var dataset in datasetModels)
                {
                    var refreshEndpoint =
                        $"groups/{workspaceModel.Id:D}/datasets/{dataset.Id:D}/refreshes?$top=60";
                    IReadOnlyList<RefreshResponse> refreshHistory;
                    try
                    {
                        refreshHistory = await GetCollectionAsync<RefreshResponse>(
                            refreshEndpoint,
                            user,
                            cancellationToken);
                    }
                    catch (PowerBiAuditException exception) when (
                        exception.StatusCode is HttpStatusCode.Forbidden or
                            HttpStatusCode.NotFound or
                            HttpStatusCode.BadRequest or
                            HttpStatusCode.NotImplemented)
                    {
                        findings.Add(CreateFinding(
                            "GOV-009",
                            workspaceModel.Id,
                            workspaceModel.Name,
                            dataset.Id,
                            "Refresh history is unavailable",
                            "Power BI did not provide refresh history for this semantic model. The signed-in identity may lack write permission on the model, or this model type may not expose refresh history. No refresh failure is inferred.",
                            DateTimeOffset.UtcNow,
                            "Latest refresh history is available",
                            $"Power BI returned HTTP {(int)exception.StatusCode}",
                            new
                            {
                                apiEndpoint = refreshEndpoint,
                                httpStatus = (int)exception.StatusCode,
                                reason = exception.Message
                            },
                            status: "INSUFFICIENT_EVIDENCE",
                            artifactType: "Semantic model",
                            artifactName: dataset.Name));
                        continue;
                    }

                    var orderedHistory = refreshHistory
                        .OrderByDescending(refresh => refresh.EndTime ?? refresh.StartTime)
                        .ToArray();
                    var capturedUtc = DateTimeOffset.UtcNow;
                    if (orderedHistory.Length == 0)
                    {
                        findings.Add(CreateFinding(
                            "GOV-009",
                            workspaceModel.Id,
                            workspaceModel.Name,
                            dataset.Id,
                            "Refresh history contains no entries",
                            "Power BI returned an empty refresh-history collection. No refresh status can be determined from the available evidence.",
                            capturedUtc,
                            "At least one refresh-history entry",
                            "No history entries returned",
                            new
                            {
                                apiEndpoint = refreshEndpoint,
                                dataset.Id,
                                dataset.Name,
                                evidenceTimestamp = capturedUtc
                            },
                            status: "INSUFFICIENT_EVIDENCE",
                            artifactType: "Semantic model",
                            artifactName: dataset.Name));
                        continue;
                    }

                    var latest = orderedHistory[0];
                    var latestStatus = latest.Status?.Trim();
                    var lastSuccessfulRefresh = orderedHistory.FirstOrDefault(refresh =>
                        string.Equals(refresh.Status, "Completed", StringComparison.OrdinalIgnoreCase));
                    var lastFailedRefresh = orderedHistory.FirstOrDefault(refresh =>
                        string.Equals(refresh.Status, "Failed", StringComparison.OrdinalIgnoreCase));
                    var consecutiveFailures = orderedHistory
                        .TakeWhile(refresh =>
                            string.Equals(refresh.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                        .Count();
                    var hasKnownLatestStatus =
                        string.Equals(latestStatus, "Completed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(latestStatus, "Failed", StringComparison.OrdinalIgnoreCase);
                    var latestFailed = string.Equals(
                        latestStatus,
                        "Failed",
                        StringComparison.OrdinalIgnoreCase);
                    var status = !hasKnownLatestStatus
                        ? "INSUFFICIENT_EVIDENCE"
                        : latestFailed ? "FAIL" : "PASS";
                    findings.Add(CreateFinding(
                        "GOV-009",
                        workspaceModel.Id,
                        workspaceModel.Name,
                        dataset.Id,
                        latestFailed
                            ? "Latest semantic model refresh failed"
                            : hasKnownLatestStatus
                                ? "Latest semantic model refresh completed"
                                : "Latest semantic model refresh status is unknown",
                        latestFailed
                            ? $"The latest refresh returned status Failed. {consecutiveFailures} consecutive failed refresh(es) appear at the start of the available history."
                            : hasKnownLatestStatus
                                ? "The latest refresh returned status Completed."
                                : $"The latest refresh returned status '{latestStatus ?? "not provided"}'; a pass or failure cannot be established.",
                        capturedUtc,
                        "Latest refresh status is Completed",
                        latestStatus ?? "Status not returned",
                        new
                        {
                            apiEndpoint = refreshEndpoint,
                            dataset.Id,
                            dataset.Name,
                            latestRefreshStatus = latestStatus,
                            latestRefreshStartTime = latest.StartTime,
                            latestRefreshEndTime = latest.EndTime,
                            lastSuccessfulRefresh = lastSuccessfulRefresh?.EndTime ?? lastSuccessfulRefresh?.StartTime,
                            lastFailedRefresh = lastFailedRefresh?.EndTime ?? lastFailedRefresh?.StartTime,
                            consecutiveFailureCount = consecutiveFailures,
                            historyEntriesEvaluated = orderedHistory.Length,
                            evidenceTimestamp = capturedUtc
                        },
                        status: status,
                        artifactType: "Semantic model",
                        artifactName: dataset.Name));
                }
            }

            if (selectedOptimizationChecks.Length > 0)
            {
                if (!modelsSelected)
                {
                    findings.AddRange(GovernanceOptimizationFindings.CreateFromPowerBiMetadata(
                        workspaceModel.Id,
                        workspaceModel.Name,
                        null,
                        null,
                        null,
                        "Semantic models were not included in this scan. Select semantic models to run the automatic model-object checks.",
                        selectedOptimizationChecks,
                        DateTimeOffset.UtcNow));
                }
                else
                {
                    foreach (var dataset in datasetModels)
                    {
                        var allLinkedReports = allWorkspaceReports
                            .Where(report => Guid.TryParse(report.DatasetId, out var reportDatasetId) &&
                                reportDatasetId == dataset.Id)
                            .ToArray();
                        var selectedLinkedReports = reports
                            .Where(report => Guid.TryParse(report.DatasetId, out var reportDatasetId) &&
                                reportDatasetId == dataset.Id)
                            .ToArray();
                        var reportCountExpected = reportsSelected ? allLinkedReports.Length : -1;
                        var reportReferences = new List<PowerBiReportFieldReference>();
                        var reportUsageErrors = new List<string>();
                        var reportsScanned = 0;
                        var reportUsageComplete = reportsSelected &&
                            !allLinkedReports.Except(selectedLinkedReports).Any();
                        if (reportsSelected)
                        {
                            foreach (var report in selectedLinkedReports)
                            {
                                try
                                {
                                    var references = await ExportReportReferencesAsync(
                                        workspaceModel.Id,
                                        report.Id,
                                        user,
                                        cancellationToken);
                                    reportReferences.AddRange(references);
                                    reportsScanned++;
                                }
                                catch (Exception exception) when (
                                    exception is PowerBiAuditException or
                                        HttpRequestException or
                                        IOException or
                                        InvalidDataException or
                                        JsonException)
                                {
                                    reportUsageComplete = false;
                                    reportUsageErrors.Add($"{report.Name}: {exception.Message}");
                                    logger?.LogWarning(
                                        exception,
                                        "Report definition could not be analyzed for report {ReportId} in workspace {WorkspaceId}.",
                                        report.Id,
                                        workspaceModel.Id);
                                }
                            }
                        }

                        var scannedDataset = scannedWorkspaces?
                            .FirstOrDefault(scannedWorkspace => scannedWorkspace.Id == workspaceModel.Id)?
                            .Datasets?
                            .FirstOrDefault(scannedModel => scannedModel.Id == dataset.Id);
                        IReadOnlyList<GovernanceModelTable>? tables = scannedDataset?.Tables?
                            .Select(table => new GovernanceModelTable(
                                table.Name,
                                table.Columns?.Select(column => new GovernanceModelColumn(
                                    column.Name,
                                    column.IsKey)).ToArray(),
                                table.Measures?.Select(measure => new GovernanceModelMeasure(
                                    measure.Name,
                                    measure.Expression)).ToArray()))
                            .ToArray();
                        var metadataError = workspaceMetadataError;
                        if (tables is null || tables.Count == 0)
                        {
                            try
                            {
                                tables = (await GetCollectionAsync<DatasetTableResponse>(
                                        $"groups/{workspaceModel.Id:D}/datasets/{dataset.Id:D}/tables",
                                        user,
                                        cancellationToken))
                                    .Select(table => new GovernanceModelTable(
                                        table.Name,
                                        table.Columns?.Select(column => new GovernanceModelColumn(
                                            column.Name,
                                            column.IsKey)).ToArray(),
                                        table.Measures?.Select(measure => new GovernanceModelMeasure(
                                            measure.Name,
                                            measure.Expression)).ToArray()))
                                    .ToArray();
                                metadataError = null;
                            }
                            catch (PowerBiAuditException exception) when (
                                exception.StatusCode is HttpStatusCode.Forbidden or
                                    HttpStatusCode.NotFound or
                                    HttpStatusCode.BadRequest or
                                    HttpStatusCode.NotImplemented or
                                    HttpStatusCode.Unauthorized)
                            {
                                metadataError =
                                    workspaceMetadataError ??
                                    "Power BI did not return semantic-model schema. The Fabric admin metadata-scanning permission or tenant metadata-scanning settings may be unavailable, and the fallback tables endpoint supports push datasets only.";
                                logger?.LogWarning(
                                    exception,
                                    "Power BI returned no schema metadata for semantic model {DatasetId} in workspace {WorkspaceId}.",
                                    dataset.Id,
                                    workspaceModel.Id);
                            }
                        }

                        findings.AddRange(GovernanceOptimizationFindings.CreateFromPowerBiMetadata(
                            workspaceModel.Id,
                            workspaceModel.Name,
                            dataset.Id,
                            dataset.Name,
                            tables,
                            metadataError,
                            selectedOptimizationChecks,
                            DateTimeOffset.UtcNow,
                            reportReferences,
                            reportUsageComplete,
                            reportCountExpected,
                            reportsScanned,
                            reportUsageErrors));
                    }

                    if (datasetModels.Length == 0)
                    {
                        findings.AddRange(GovernanceOptimizationFindings.CreateFromPowerBiMetadata(
                            workspaceModel.Id,
                            workspaceModel.Name,
                            null,
                            null,
                            null,
                            "Power BI returned no semantic models for this workspace. The optimization checks could not inspect any model.",
                            selectedOptimizationChecks,
                            DateTimeOffset.UtcNow));
                    }
                }
            }
        }

        if (reportIdFilter is not null && !reportIdFilter.IsSubsetOf(returnedReportIds))
        {
            throw new ArgumentException(
                "One or more selected reports were not returned in the selected workspace scope.",
                nameof(selectedReportIds));
        }

        if (dashboardIdFilter is not null && !dashboardIdFilter.IsSubsetOf(returnedDashboardIds))
        {
            throw new ArgumentException(
                "One or more selected dashboards were not returned in the selected workspace scope.",
                nameof(selectedDashboardIds));
        }

        return new GovernanceAuditResult(Guid.Empty, scanStartedUtc, audits, findings)
        {
            SelectedWorkspaceIds = workspaceIds.ToArray(),
            SelectedArtifactTypes = selectedArtifacts.OrderBy(type => type, StringComparer.Ordinal).ToArray(),
            SelectedReportIds = selectedReportIds?.ToArray() ?? [],
            SelectedDashboardIds = selectedDashboardIds?.ToArray() ?? [],
            Apps = apps,
            Gateways = gateways
        };
    }

    private static void ValidateSelectedGlobalArtifactIds(
        IReadOnlyCollection<GovernanceArtifact> availableArtifacts,
        HashSet<Guid>? selectedIds,
        string parameterName)
    {
        if (selectedIds is not null &&
            !selectedIds.IsSubsetOf(availableArtifacts.Select(artifact => artifact.Id)))
        {
            throw new ArgumentException(
                $"One or more selected {parameterName} were not returned by Power BI for the signed-in user.",
                parameterName);
        }
    }

    private static void ValidateSelectedArtifactIds(
        IReadOnlyCollection<Guid>? artifactIds,
        bool artifactTypeSelected,
        string parameterName)
    {
        if (artifactIds is null)
        {
            return;
        }

        if (!artifactTypeSelected)
        {
            throw new ArgumentException(
                "Artifact IDs can only be supplied when their matching artifact type is selected.",
                parameterName);
        }

        if (artifactIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "Selected artifact IDs cannot contain an empty GUID. Reload the artifact list and select valid items.",
                parameterName);
        }

        if (artifactIds.Distinct().Count() != artifactIds.Count)
        {
            throw new ArgumentException(
                "Selected artifact IDs must be unique.",
                parameterName);
        }
    }

    private async Task<IReadOnlyList<GovernanceReportConnection>> GetReportConnectionsAsync(
        Guid workspaceId,
        IReadOnlyCollection<GovernanceArtifact> reports,
        IReadOnlyCollection<GovernanceArtifact> semanticModels,
        bool modelsSelected,
        bool refreshHistorySelected,
        bool gatewayInventorySelected,
        IReadOnlyCollection<GovernanceArtifact> gateways,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var connections = new List<GovernanceReportConnection>();
        var datasourceResults = new Dictionary<Guid, (DatasourceResponse[]? Sources, string? Error)>();
        var refreshResults = new Dictionary<Guid, (string Status, string Detail)>();
        foreach (var datasetGroup in reports
                     .Where(report => Guid.TryParse(report.DatasetId, out _))
                     .GroupBy(report => Guid.Parse(report.DatasetId!), report => report))
        {
            if (!modelsSelected)
            {
                datasourceResults[datasetGroup.Key] = (null, "Semantic models were not selected for this scan.");
                continue;
            }

            try
            {
                var sources = await GetCollectionAsync<DatasourceResponse>(
                    $"groups/{workspaceId:D}/datasets/{datasetGroup.Key:D}/datasources",
                    user,
                    cancellationToken);
                datasourceResults[datasetGroup.Key] = (sources, null);
            }
            catch (PowerBiAuditException exception) when (
                exception.StatusCode is HttpStatusCode.Forbidden or
                    HttpStatusCode.NotFound or
                    HttpStatusCode.BadRequest or
                    HttpStatusCode.NotImplemented)
            {
                datasourceResults[datasetGroup.Key] =
                    (null, $"Power BI returned HTTP {(int)exception.StatusCode}: {exception.Message}");
            }

            if (refreshHistorySelected)
            {
                try
                {
                    var refreshHistory = await GetCollectionAsync<RefreshResponse>(
                        $"groups/{workspaceId:D}/datasets/{datasetGroup.Key:D}/refreshes?$top=1",
                        user,
                        cancellationToken);
                    refreshResults[datasetGroup.Key] = GetConnectionHealth(refreshHistory);
                }
                catch (PowerBiAuditException exception) when (
                    exception.StatusCode is HttpStatusCode.Forbidden or
                        HttpStatusCode.NotFound or
                        HttpStatusCode.BadRequest or
                        HttpStatusCode.NotImplemented)
                {
                    refreshResults[datasetGroup.Key] = (
                        "Unknown",
                        $"Refresh history could not be read (HTTP {(int)exception.StatusCode}).");
                }
            }
            else
            {
                refreshResults[datasetGroup.Key] = (
                    "Unknown",
                    "Select GOV-009 to check the latest semantic model refresh.");
            }
        }

        foreach (var report in reports)
        {
            if (!Guid.TryParse(report.DatasetId, out var datasetId))
            {
                connections.Add(new GovernanceReportConnection(
                    report.Id,
                    report.Name,
                    null,
                    null,
                    null,
                    null,
                    "Unavailable",
                    "Power BI did not return a semantic model ID for this report.",
                    "Unknown",
                    "The semantic model could not be identified."));
                continue;
            }

            var datasetResult = datasourceResults[datasetId];
            var connectionHealth = refreshResults.TryGetValue(datasetId, out var health)
                ? health
                : (Status: "Unknown", Detail: "Refresh history was not available.");
            if (datasetResult.Error is not null)
            {
                connections.Add(new GovernanceReportConnection(
                    report.Id,
                    report.Name,
                    null,
                    null,
                    null,
                    null,
                    modelsSelected ? "Unavailable" : "Not checked",
                    datasetResult.Error,
                    "Unknown",
                    "Refresh status could not be determined."));
                continue;
            }

            var sources = datasetResult.Sources ?? [];
            if (sources.Length == 0)
            {
                connections.Add(new GovernanceReportConnection(
                    report.Id,
                    report.Name,
                    null,
                    null,
                    null,
                    null,
                    "Unknown",
                    "Power BI returned no datasource entries for the referenced semantic model.",
                    connectionHealth.Status,
                    connectionHealth.Detail));
                continue;
            }

            foreach (var source in sources)
            {
                var gateway = source.GatewayId is Guid gatewayId
                    ? gateways.FirstOrDefault(candidate => candidate.Id == gatewayId)
                    : null;
                var gatewayStatus = source.GatewayId is null
                    ? "Not applicable"
                    : gateway?.Status ?? (gatewayInventorySelected
                        ? "Unknown"
                        : "Not checked");
                var gatewayStatusDetail = source.GatewayId is null
                    ? "Power BI did not associate this source with a gateway."
                    : gateway is null
                        ? gatewayInventorySelected
                            ? "The associated gateway was not returned in the signed-in user's gateway inventory."
                            : "Select gateway inventory to check the associated gateway status."
                        : $"Power BI gateway connectivity status: {gateway.Status ?? "Unknown"}.";
                connections.Add(new GovernanceReportConnection(
                    report.Id,
                    report.Name,
                    semanticModels.FirstOrDefault(model => model.Id == datasetId)?.Name,
                    source.DatasourceType,
                    GetSafeConnectionDetails(source.ConnectionDetails),
                    source.GatewayId,
                    connectionHealth.Status,
                    connectionHealth.Detail,
                    gatewayStatus,
                    gatewayStatusDetail));
            }
        }

        return connections;
    }

    private static (string Status, string Detail) GetConnectionHealth(
        IReadOnlyList<RefreshResponse> refreshHistory)
    {
        var latestRefresh = refreshHistory
            .OrderByDescending(refresh => refresh.EndTime ?? refresh.StartTime)
            .FirstOrDefault();
        if (latestRefresh is null)
        {
            return ("Unknown", "Power BI returned no refresh history for the semantic model.");
        }

        var timestamp = latestRefresh.EndTime ?? latestRefresh.StartTime;
        var recorded = timestamp.HasValue
            ? $" at {timestamp.Value.ToUniversalTime():O}"
            : string.Empty;
        return latestRefresh.Status?.Trim().ToUpperInvariant() switch
        {
            "COMPLETED" => (
                "Online",
                $"The latest refresh completed{recorded}. This indicates the source worked at that time; it is not a live connectivity probe."),
            "FAILED" => (
                "Offline",
                $"The latest refresh failed{recorded}. This is historical evidence and does not prove the source is still offline."),
            "UNKNOWN" => (
                "Unknown",
                $"Power BI reported an unknown or in-progress refresh{recorded}."),
            null or "" => (
                "Unknown",
                $"Power BI did not return a refresh status{recorded}."),
            _ => (
                "Unknown",
                $"The latest refresh status is '{latestRefresh.Status}'{recorded}; connectivity cannot be determined.")
        };
    }

    private static string NormalizeGatewayStatus(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? "Unknown"
            : string.Equals(status.Trim(), "Online", StringComparison.OrdinalIgnoreCase)
                ? "Online"
                : string.Equals(status.Trim(), "Offline", StringComparison.OrdinalIgnoreCase)
                    ? "Offline"
                    : "Unknown";

    private static string NormalizeGatewayType(string? type) =>
        string.IsNullOrWhiteSpace(type)
            ? "Unknown"
            : string.Equals(type.Trim(), "Resource", StringComparison.OrdinalIgnoreCase)
                ? "On-premises"
                : type.Trim();

    private static string? GetSafeConnectionDetails(JsonElement? connectionDetails)
    {
        if (connectionDetails is not { ValueKind: JsonValueKind.Object } details)
        {
            return null;
        }

        var safeProperties = new[] { "server", "database", "url", "path", "endpoint" };
        var values = safeProperties
            .Select(name => details.TryGetProperty(name, out var value)
                ? (Name: name, Value: SanitizeConnectionValue(
                    name,
                    value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()))
                : (Name: name, Value: null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{item.Name}: {item.Value}");
        var result = string.Join("; ", values);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static string? SanitizeConnectionValue(string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = value.Trim();
        if (propertyName is "url" or "endpoint" &&
            Uri.TryCreate(sanitized, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.ToString();
        }

        var sensitiveSuffix = sanitized.IndexOfAny(['?', '#', ';']);
        if (sensitiveSuffix >= 0)
        {
            sanitized = sanitized[..sensitiveSuffix];
        }

        var userInfoSeparator = sanitized.LastIndexOf('@');
        if (userInfoSeparator >= 0)
        {
            sanitized = sanitized[(userInfoSeparator + 1)..];
        }

        return sanitized;
    }

    private static GovernanceFinding CreateFinding(
        string checkId,
        Guid workspaceId,
        string workspaceName,
        Guid? artifactId,
        string title,
        string detail,
        DateTimeOffset evidenceTimestamp,
        string expectedValue,
        string actualValue,
        object evidence,
        string? status = null,
        string? artifactType = null,
        string? artifactName = null)
    {
        if (!GovernanceCheckCatalog.TryGet(checkId, out var check))
        {
            throw new InvalidOperationException($"Governance check '{checkId}' is not registered.");
        }

        return new GovernanceFinding(
            check.CheckId,
            check.Severity,
            workspaceId,
            workspaceName,
            artifactId,
            artifactType,
            artifactName,
            title,
            detail,
            check.Version,
            check.Name,
            check.Owner,
            check.EvidenceSource,
            evidenceTimestamp,
            status ?? "FAIL",
            check.EvaluationLogic,
            check.ExceptionRules,
            expectedValue,
            actualValue,
            JsonSerializer.SerializeToElement(evidence));
    }

    private async Task<IReadOnlyList<WorkspaceMetadataResponse>> ScanWorkspaceMetadataAsync(
        IReadOnlyCollection<Guid> workspaceIds,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var scan = await PostAsync<ScanWorkspacesRequest, ScanStartedResponse>(
            "admin/workspaces/getInfo?datasetSchema=true&datasetExpressions=true",
            new ScanWorkspacesRequest(workspaceIds.ToArray()),
            user,
            cancellationToken);
        if (scan.Id == Guid.Empty)
        {
            throw new PowerBiAuditException(
                "Power BI accepted the metadata scan request without returning a scan ID.",
                HttpStatusCode.BadGateway);
        }

        ScanStatusResponse? status = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            status = await GetAsync<ScanStatusResponse>(
                $"admin/workspaces/scanStatus/{scan.Id:D}",
                user,
                cancellationToken);
            if (string.Equals(status.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var result = await GetAsync<WorkspaceMetadataScanResponse>(
                    $"admin/workspaces/scanResult/{scan.Id:D}",
                    user,
                    cancellationToken);
                return result.Workspaces ?? [];
            }

            if (string.Equals(status.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new PowerBiAuditException(
                    status.Message ??
                        "Power BI could not return metadata for the selected workspaces. Confirm that tenant metadata scanning is enabled.",
                    HttpStatusCode.BadGateway);
            }
        }

        throw new PowerBiAuditException(
            $"Power BI metadata scan did not complete within 60 seconds (last status: {status?.Status ?? "unknown"}).",
            HttpStatusCode.GatewayTimeout);
    }

    private async Task<IReadOnlyList<PowerBiReportFieldReference>> ExportReportReferencesAsync(
        Guid workspaceId,
        Guid reportId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var relativeUri =
            $"groups/{workspaceId:D}/reports/{reportId:D}/Export?downloadType=LiveConnect";
        string accessToken;
        try
        {
            accessToken = await tokenProvider.GetAccessTokenAsync(
                user,
                "https://analysis.windows.net/powerbi/api/Report.Read.All");
        }
        catch (PowerBiDelegatedConsentRequiredException exception)
        {
            throw new PowerBiAuditException(
                "Report.Read.All consent is required to inspect report visual usage. Reauthorize Power BI and approve the requested read scopes.",
                HttpStatusCode.Unauthorized,
                requiresConsent: true,
                innerException: exception);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = response.StatusCode switch
            {
                HttpStatusCode.Forbidden =>
                    "The signed-in Power BI user cannot export this report. Confirm report access and that PBIX download is supported for this report.",
                HttpStatusCode.NotFound =>
                    "The report definition is unavailable or the report no longer exists.",
                _ => $"Power BI report export returned HTTP {(int)response.StatusCode}."
            };
            throw new PowerBiAuditException(detail, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength is > MaximumReportExportBytes)
        {
            throw new InvalidDataException(
                "The report export exceeds the 150 MB safety limit and could not be analyzed.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var export = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await responseStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (export.Length > MaximumReportExportBytes - read)
            {
                throw new InvalidDataException(
                    "The report export exceeds the 150 MB safety limit and could not be analyzed.");
            }

            await export.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        export.Position = 0;
        return PowerBiReportLayoutReader.ReadReferences(export);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativeUri,
        TRequest content,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri)
        {
            Content = JsonContent.Create(content, options: JsonOptions)
        };
        string accessToken;
        try
        {
            accessToken = await tokenProvider.GetAccessTokenAsync(user, GetRequiredScope(relativeUri));
        }
        catch (PowerBiDelegatedConsentRequiredException)
        {
            throw new PowerBiAuditException(
                "Power BI tenant metadata-scanning access has not been authorized. Select the \"Authorize Power BI read access\" button and approve the requested delegated scopes, or ask your Entra administrator to grant consent.",
                HttpStatusCode.Unauthorized,
                requiresConsent: true);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var stopwatch = Stopwatch.StartNew();
        logger?.LogInformation("Calling Power BI REST API {Method} {Endpoint}.", request.Method, relativeUri);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError(
                exception,
                "Power BI REST API {Method} {Endpoint} timed out after {DurationMs} ms.",
                request.Method,
                relativeUri,
                stopwatch.ElapsedMilliseconds);
            throw new PowerBiAuditException(
                "Power BI metadata-scanner request timed out.",
                HttpStatusCode.GatewayTimeout,
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            logger?.LogError(
                exception,
                "Power BI REST API {Method} {Endpoint} failed after {DurationMs} ms.",
                request.Method,
                relativeUri,
                stopwatch.ElapsedMilliseconds);
            throw new PowerBiAuditException(
                "Power BI metadata scanner could not be reached. Check connectivity and retry.",
                HttpStatusCode.BadGateway,
                innerException: exception);
        }

        using (response)
        {
            logger?.LogInformation(
                "Power BI REST API {Method} {Endpoint} returned HTTP {StatusCode} in {DurationMs} ms.",
                request.Method,
                relativeUri,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized =>
                        "Power BI rejected the delegated Tenant.Read.All access token. Reauthorize the app and confirm tenant admin consent.",
                    HttpStatusCode.Forbidden =>
                        "Power BI denied the workspace metadata scan. It requires delegated Tenant.Read.All, a Fabric administrator account, and enabled tenant metadata-scanning settings.",
                    (HttpStatusCode)429 =>
                        "Power BI throttled the metadata scan. Wait for the Retry-After interval and retry.",
                    _ =>
                        $"Power BI metadata scan request failed with HTTP {(int)response.StatusCode}. Confirm tenant metadata-scanning settings and the selected workspace scope."
                };
                throw new PowerBiAuditException(detail, response.StatusCode);
            }

            var value = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
            return value ?? throw new PowerBiAuditException(
                "Power BI returned an empty response for the metadata-scanner request.",
                HttpStatusCode.BadGateway);
        }
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
            accessToken = await tokenProvider.GetAccessTokenAsync(user, GetRequiredScope(relativeUri));
        }
        catch (PowerBiDelegatedConsentRequiredException)
        {
            throw new PowerBiAuditException(
                "Power BI access has not been authorized for this app. Select the \"Authorize Power BI read access\" button once and approve the requested delegated read scopes, or ask your Entra administrator to grant consent.",
                HttpStatusCode.Unauthorized,
                requiresConsent: true);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var stopwatch = Stopwatch.StartNew();
        logger?.LogInformation("Calling Power BI REST API {Method} {Endpoint}.", request.Method, relativeUri);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError(
                exception,
                "Power BI REST API {Method} {Endpoint} timed out after {DurationMs} ms.",
                request.Method,
                relativeUri,
                stopwatch.ElapsedMilliseconds);
            throw new PowerBiAuditException(
                "Power BI REST API request timed out. Retry after checking service health and scan scope.",
                HttpStatusCode.GatewayTimeout,
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            logger?.LogError(
                exception,
                "Power BI REST API {Method} {Endpoint} failed after {DurationMs} ms.",
                request.Method,
                relativeUri,
                stopwatch.ElapsedMilliseconds);
            throw new PowerBiAuditException(
                "Power BI REST API could not be reached. Check network connectivity and retry.",
                HttpStatusCode.BadGateway,
                innerException: exception);
        }

        using (response)
        {
            logger?.LogInformation(
                "Power BI REST API {Method} {Endpoint} returned HTTP {StatusCode} in {DurationMs} ms.",
                request.Method,
                relativeUri,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Power BI rejected the delegated access token. Sign in again and verify Power BI API consent.",
                    HttpStatusCode.Forbidden when string.Equals(relativeUri, "apps", StringComparison.OrdinalIgnoreCase) =>
                        "Power BI denied installed-app access. Verify that delegated App.Read.All is configured and consented for this app.",
                    HttpStatusCode.Forbidden when string.Equals(relativeUri, "gateways", StringComparison.OrdinalIgnoreCase) =>
                        "Power BI returned no gateway inventory because the signed-in user must be an administrator of at least one gateway.",
                    HttpStatusCode.Forbidden when relativeUri.StartsWith("admin/workspaces/", StringComparison.OrdinalIgnoreCase) =>
                        "Power BI denied the workspace metadata scan. It requires delegated Tenant.Read.All, a Fabric administrator account, and enabled tenant metadata-scanning settings.",
                    HttpStatusCode.Forbidden when relativeUri.EndsWith("/dashboards", StringComparison.OrdinalIgnoreCase) =>
                        "Power BI denied dashboard access. Verify that delegated Dashboard.Read.All is configured and consented for this app.",
                    HttpStatusCode.Forbidden => "Power BI denied access. Verify that your account has access to the selected workspace and that the delegated read scopes were consented.",
                    (HttpStatusCode)429 => "Power BI throttled the audit request. Wait and retry after the service's Retry-After interval.",
                    _ => $"Power BI REST API request failed with HTTP {(int)response.StatusCode}."
                };
                throw new PowerBiAuditException(detail, response.StatusCode);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new InvalidDataException("Power BI returned an empty response for a governance request.");
        }
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

    private static string GetRequiredScope(string relativeUri)
    {
        var permission = relativeUri switch
        {
            "apps" => "App.Read.All",
            "gateways" => "Dataset.Read.All",
            _ when relativeUri.StartsWith("admin/workspaces/", StringComparison.OrdinalIgnoreCase) => "Tenant.Read.All",
            _ when relativeUri.Contains("/reports/", StringComparison.OrdinalIgnoreCase) => "Report.Read.All",
            _ when relativeUri.Contains("/datasets/", StringComparison.OrdinalIgnoreCase) => "Dataset.Read.All",
            _ when relativeUri.EndsWith("/dashboards", StringComparison.OrdinalIgnoreCase) => "Dashboard.Read.All",
            _ when relativeUri.EndsWith("/reports", StringComparison.OrdinalIgnoreCase) => "Report.Read.All",
            _ when relativeUri.EndsWith("/datasets", StringComparison.OrdinalIgnoreCase) => "Dataset.Read.All",
            _ => "Workspace.Read.All"
        };
        return $"https://analysis.windows.net/powerbi/api/{permission}";
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

    private sealed record DatasetTableResponse(
        string Name,
        DatasetColumnResponse[]? Columns,
        DatasetMeasureResponse[]? Measures);

    private sealed record DatasetColumnResponse(string Name, bool? IsKey);

    private sealed record DatasetMeasureResponse(string Name, string? Expression);

    private sealed record ScanWorkspacesRequest(Guid[] Workspaces);

    private sealed record ScanStartedResponse(Guid Id);

    private sealed record ScanStatusResponse(string Status, string? Message);

    private sealed record WorkspaceMetadataScanResponse(WorkspaceMetadataResponse[]? Workspaces);

    private sealed record WorkspaceMetadataResponse(
        Guid Id,
        string Name,
        DatasetMetadataResponse[]? Datasets);

    private sealed record DatasetMetadataResponse(
        Guid Id,
        string Name,
        DatasetTableResponse[]? Tables);

    private sealed record RefreshResponse(
        string? Status,
        DateTimeOffset? StartTime,
        DateTimeOffset? EndTime);

    private sealed record DashboardResponse(
        Guid Id,
        string DisplayName,
        string? WebUrl);

    private sealed record AppResponse(
        Guid Id,
        string Name,
        string? PublishedBy);

    private sealed record GatewayResponse(
        Guid Id,
        string Name,
        string? Type,
        string? GatewayStatus);

    private sealed record DatasourceResponse(
        Guid? GatewayId,
        string? DatasourceType,
        JsonElement? ConnectionDetails);

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
    bool requiresConsent = false,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public bool RequiresConsent { get; } = requiresConsent;
}
