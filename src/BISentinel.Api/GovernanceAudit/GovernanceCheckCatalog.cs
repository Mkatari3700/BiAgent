namespace BISentinel.Api.GovernanceAudit;

public sealed record GovernanceCheckDefinition(
    string CheckId,
    string Version,
    string Name,
    string Owner,
    string Severity,
    string Description,
    string EvaluationLogic,
    string EvidenceSource,
    string ExceptionRules,
    string ExpectedResult);

public static class GovernanceCheckCatalog
{
    private const string Owner = "BI Governance Team";

    public static IReadOnlyList<GovernanceCheckDefinition> All { get; } =
    [
        new("GOV-001", "1.0", "Visible workspace administrator", Owner, "Warning",
            "Check that the scanned workspace user listing contains an Admin principal.",
            "Fail when the workspace user response contains no principal with Admin access.",
            "Power BI REST API: GET /groups/{workspaceId}/users",
            "Review the principal list and confirm the audit identity can see all workspace admins.",
            "At least one visible Admin principal"),
        new("GOV-002", "1.0", "Workspace contains governed artifacts", Owner, "Information",
            "Identify workspaces with no reports or semantic models returned by the API.",
            "Fail when both the report and semantic-model collections are empty.",
            "Power BI REST API: GET /groups/{workspaceId}/reports and /datasets",
            "An intentionally empty workspace is acceptable.",
            "At least one report or semantic model"),
        new("GOV-003", "1.0", "Report model dependency is outside the workspace", Owner, "Information",
            "Identify reports whose referenced model is not in the scanned workspace inventory.",
            "Fail when a report has a model ID that is not present in the workspace model response.",
            "Power BI REST API: GET /groups/{workspaceId}/reports and /datasets",
            "Shared semantic models are valid; verify cross-workspace dependencies before action.",
            "Report model is present in the workspace or validated as shared"),
        new("GOV-004", "1.0", "Semantic model referenced by a report", Owner, "Information",
            "Identify semantic models not referenced by any report in the scanned workspace.",
            "Fail when no returned report in the workspace contains the semantic model ID.",
            "Power BI REST API: GET /groups/{workspaceId}/reports and /datasets",
            "Models can be consumed by Excel, dashboards, other workspaces, or external clients.",
            "Model has a known consumer or an approved exception"),
        new("GOV-005", "1.0", "Semantic model owner metadata available", Owner, "Information",
            "Check whether the semantic model response contains configuredBy metadata.",
            "Insufficient evidence when configuredBy is empty; this does not prove that no owner exists.",
            "Power BI REST API: GET /groups/{workspaceId}/datasets",
            "Record an approved owner source or exception when Power BI does not return configuredBy.",
            "An owner is recorded or missing metadata is reviewed"),
        new("GOV-006", "1.0", "Report model reference available", Owner, "Information",
            "Check whether the report response contains a semantic-model ID.",
            "Insufficient evidence when the report has no returned datasetId.",
            "Power BI REST API: GET /groups/{workspaceId}/reports",
            "Some report types can legitimately have no model reference.",
            "Report dependency is known or the report type is approved"),
        new("GOV-007", "1.0", "External guest access review", Owner, "Information",
            "Identify workspace principals whose identifier contains Entra's #EXT# guest marker.",
            "Flag a review candidate when identifier or email contains #EXT#.",
            "Power BI REST API: GET /groups/{workspaceId}/users",
            "Guest collaboration may be approved; review business need and expiry before changing access.",
            "Guest access is approved and periodically reviewed"),
        new("GOV-008", "1.0", "Edit-level workspace access review", Owner, "Information",
            "Identify Member and Contributor workspace roles for least-privilege review.",
            "Flag a review candidate when access right is Member or Contributor.",
            "Power BI REST API: GET /groups/{workspaceId}/users",
            "Edit-level access may be required for the person's responsibilities.",
            "Workspace role is least-privileged for the user's responsibilities"),
        new("GOV-009", "1.0", "Latest semantic model refresh status", Owner, "High",
            "Evaluate the latest returned refresh-history entry for each selected semantic model.",
            "FAIL when the latest refresh status is Failed; PASS when it is Completed; INSUFFICIENT_EVIDENCE when history is empty, the latest status is unknown, or Power BI does not permit history access.",
            "Power BI REST API: GET /groups/{workspaceId}/datasets/{datasetId}/refreshes?$top=60",
            "Verify refresh ownership, permissions, and source availability before remediation. Missing history is not proof of a failed refresh.",
            "The latest refresh is Completed, or a failed refresh has an approved remediation plan"),
        new("OPT-001", "1.1", "Potentially unused model tables", Owner, "Warning",
            "Identify candidate tables that are not referenced by DAX dependencies or visuals in the scanned workspace reports.",
            "Compare semantic-model table metadata and DAX measure expressions with field references parsed from each linked report's exported layout. If any linked report cannot be exported or parsed, report insufficient evidence instead of an unused candidate. Other workspaces and external consumers are outside the scan scope.",
            "Power BI REST APIs: GET /groups/{workspaceId}/datasets/{datasetId}/tables and GET /groups/{workspaceId}/reports/{reportId}/Export?downloadType=LiveConnect",
            "Verify external reports, refresh/query dependencies, and downstream consumers before removal.",
            "Every table has a known consumer or an approved removal decision"),
        new("OPT-002", "1.1", "Potentially unused model columns", Owner, "Warning",
            "Identify candidate columns not referenced by DAX dependencies or visuals in the scanned workspace reports.",
            "Compare semantic-model column metadata and DAX measure expressions with field references parsed from each linked report's exported layout. If any linked report cannot be exported or parsed, report insufficient evidence instead of an unused candidate. Other workspaces and external consumers are outside the scan scope.",
            "Power BI REST APIs: GET /groups/{workspaceId}/datasets/{datasetId}/tables and GET /groups/{workspaceId}/reports/{reportId}/Export?downloadType=LiveConnect",
            "Verify report visuals, sort/group settings, refresh/query dependencies, and downstream consumers before removal.",
            "Every column has a known consumer or an approved removal decision"),
        new("OPT-003", "1.1", "Potentially unused model measures", Owner, "Warning",
            "Identify candidate measures not referenced by other DAX measures or visuals in the scanned workspace reports.",
            "Compare semantic-model measure expressions with field references parsed from each linked report's exported layout. If any linked report cannot be exported or parsed, report insufficient evidence instead of an unused candidate. Other workspaces and external consumers are outside the scan scope.",
            "Power BI REST APIs: GET /groups/{workspaceId}/datasets/{datasetId}/tables and GET /groups/{workspaceId}/reports/{reportId}/Export?downloadType=LiveConnect",
            "Verify external reports and downstream consumers before removal.",
            "Every measure has a known consumer or an approved removal decision")
    ];

    public static bool TryGet(string checkId, out GovernanceCheckDefinition definition)
    {
        definition = All.FirstOrDefault(check =>
            string.Equals(check.CheckId, checkId, StringComparison.OrdinalIgnoreCase))!;
        return definition is not null;
    }
}
