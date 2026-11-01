# BI Sentinel

BI Sentinel is an advisory Power BI review assistant built as an ASP.NET Core API. Application startup never creates schema; review workflows write feedback and review history to explicitly migrated SQL tables.

## Current foundation

- ASP.NET Core Web API targeting .NET 10
- `Microsoft.Data.SqlClient`
- SQL Server `RAMADAS`, database `BiAgent`
- Windows Integrated Authentication for local development
- Microsoft Entra ID sign-in for all application pages and APIs
- BI Sentinel application roles stored in SQL, independently of Entra app roles
- `/health/live` checks that the process is running
- `/health/ready` runs a read-only database connectivity check
- `POST /api/reviews/upload` reviews a browser-uploaded PBIP/PBIR project ZIP (preferred) or PBIT template, up to 500 MiB
- Review screen at `/` for findings, saved review history, submissions, and reviewer responses
- Development-only Swagger UI at `/swagger` for interactive API calls
- Local connection settings are stored with .NET user-secrets, not in the repository

## Run locally

Install the .NET 10 SDK, then configure a connection string in user-secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:BiAgent" "Server=RAMADAS;Database=BiAgent;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5" --project "src\BISentinel.Api\BISentinel.Api.csproj"
dotnet user-secrets set "Authentication:Entra:TenantId" "<your-tenant-guid>" --project "src\BISentinel.Api\BISentinel.Api.csproj"
dotnet user-secrets set "Authentication:Entra:ClientId" "<your-web-app-client-guid>" --project "src\BISentinel.Api\BISentinel.Api.csproj"
dotnet user-secrets set "Authentication:Entra:ClientSecret" "<confidential-client-secret>" --project "src\BISentinel.Api\BISentinel.Api.csproj"
dotnet user-secrets set "Authentication:BootstrapAdminObjectId" "<initial-admin-object-guid>" --project "src\BISentinel.Api\BISentinel.Api.csproj"
dotnet run --project "src\BISentinel.Api\BISentinel.Api.csproj" --launch-profile https
```

Before starting the app, apply migrations 001–006, 008, and 012. For Governance Audit, also apply 007, 009, 010, and 011 in that order. Migration 012 requires review-history migrations 002 and 003 plus application-access migration 008. Register a single-tenant Microsoft Entra web application with the local redirect URI `https://localhost:7053/signin-oidc` and the deployed HTTPS callback URL. For local sign-in, use the HTTPS launch profile and open `https://localhost:7053`; the HTTP listener redirects to HTTPS in Development so OIDC correlation cookies are retained. Use a confidential-client credential from user-secrets locally and an approved Key Vault reference or certificate configuration in App Service. The bootstrap setting is the Entra **user object ID**, not an email address. Never commit credentials or replace the placeholders above with real values in documentation.

Database-backed API errors include migration/script guidance and the SQL error detail in Development to help diagnose local setup. Production responses intentionally omit migration numbers and internal SQL details; they provide generic guidance to verify database connectivity, required scripts, and permissions. The API does not apply schema changes automatically.

The review UI is upload-only: the browser sends the selected file to the API, so it does not depend on local paths being visible to the server. The preferred input is a ZIP containing one complete PBIP/PBIR project with referenced folders and relative paths preserved. A lone `.pbip` descriptor is not a complete project. PBIT is an alternative and uses the pbi-tools executable bundled with the application. PBIX is not accepted. The upload limit is 500 MiB. ZIP expansion and PBIT extraction have a separate 512 MiB expanded-output safety limit, plus file-count, traversal, and symbolic-link protections. Temporary inputs and extracted files are removed after review. Set `Review:PbiToolsPath` only if you need to override the bundled executable.

### Deploy the UI to Windows App Service with ZIP Deploy

Publish the Release build and zip the contents of the publish directory (so `BISentinel.Api.dll` and `web.config` are at the ZIP root):

```powershell
dotnet publish ".\src\BISentinel.Api\BISentinel.Api.csproj" -c Release -f net10.0 -o ".\publish"
Compress-Archive -Path ".\publish\*" -DestinationPath ".\BISentinel.Api.zip" -Force
```

Configuration uses the standard ASP.NET Core environment overlay, not a publish-time JSON transform: the app reads `appsettings.json` and then overlays `appsettings.{ASPNETCORE_ENVIRONMENT}.json`. The publish output includes `appsettings.Production.json` and excludes `appsettings.Development.json`; the App Service setting `ASPNETCORE_ENVIRONMENT=Production` selects the Production settings at runtime. There is no need to rewrite `appsettings.json`. The base file contains shared defaults, Development and Production files contain only non-secret environment settings, and SQL connection strings/secrets must be supplied through App Service configuration or Key Vault references.

Upload `BISentinel.Api.zip` with App Service **Advanced Tools (Kudu) → Zip Push Deploy**. In App Service configuration, set:

- Connection string `BiAgent` to the Azure SQL connection string (type `SQLAzure`).
- App setting `ASPNETCORE_ENVIRONMENT` to `Production`.
- App setting `Feedback__SqlEnabled` to `true` after applying database migrations 001–008, 012, and 018. `appsettings.Production.json` also enables it; leave this setting false until the feedback, review-configuration, governance-history, module-availability, application-access, review-submission, and support-request migrations are applied.
- No `Review__ProjectPath` or server-side project directory is required; review input comes from uploads.
- Apply database migrations `001`–`008`, `012`, and `018` before using the authenticated review workflow and portal dashboard. To enable Organization → Project → Team administration and scoped access requests, also apply `019_add_organization_project_team_hierarchy.sql` and then `020_create_access_requests.sql`; `020` requires both `008` and `019`. Use a controlled deployment identity for migrations and grant the runtime app identity only the reviewed permissions needed by the enabled features. Migration `008` is independent of Governance migration `007`; migrations `012` and `018` require `008`.
- The published `web.config`, ASP.NET endpoint, and multipart form limits allow a 500 MiB file plus multipart overhead. If a reverse proxy, WAF, or other ingress sits in front of App Service, configure its request-body limit to at least 501 MiB as well.
- Allow sufficient writable temporary storage for the buffered upload and expanded project. ZIP/PBIT expanded output is capped separately at 512 MiB; confirm the App Service plan has enough temporary disk capacity and test with representative files.
- The deployment package includes the supplied pbi-tools 1.2.0 Windows executable and its `.exe.config` under `tools\`; no separate App Service installation or `Review__PbiToolsPath` setting is needed. PBIT extraction uses this bundled executable by default. A non-empty `Review__PbiToolsPath` overrides it and may be an absolute path or a path relative to the application directory. Validate PBIT extraction on the target Windows App Service with representative files before production use.

Keep `ASPNETCORE_ENVIRONMENT` as `Production`. The UI is served at `/`; Swagger remains disabled in Production. App Service must have `Authentication__Entra__TenantId`, `Authentication__Entra__ClientId`, `Authentication__Entra__ClientSecret` (or approved certificate configuration), and `Authentication__BootstrapAdminObjectId` configured before startup. Entra authentication is enabled by default and the app fails startup if the tenant, client, or bootstrap administrator ID is missing. The supported setup is single-tenant: all application pages and APIs require an account from the configured tenant (including an external guest invited into that tenant). Do not change the app registration to multitenant without implementing and security-testing tenant allow-listing, tenant-to-organization mapping, and cross-module data isolation; see [Supporting users from Infosys, Wipro, or another Entra tenant](#supporting-users-from-infosys-wipro-or-another-entra-tenant). Only users granted the SQL-backed **ApplicationAdmin** role can change review/module configuration, assign Admin/Manager/Lead roles, or view the application access list. Every newly signed-in tenant user can upload and review their own projects and submit per-finding justifications. Submitters can see only their own uploaded review history and submissions; submissions are locked after submission. **ReviewManager**, **ReviewLead**, and **ApplicationAdmin** users can see the shared reviewer queue and submit per-finding decisions and responses, which submitters can read but cannot edit. The review APIs accept untrusted uploads and write review/submission data to SQL.

For local development, `appsettings.Development.json` contains non-secret Development defaults and is loaded when `ASPNETCORE_ENVIRONMENT=Development` (as selected by the local launch profiles). For production, `appsettings.Production.json` contains the Production defaults and is loaded when the App Service environment is `Production`. Higher-priority App Service environment variables and connection-string settings override either JSON file.

pbi-tools 1.2.0 is bundled from the supplied Windows ZIP. It targets .NET Framework 4.7.2, so the Windows App Service runtime must support it. pbi-tools is a separate open-source component distributed under AGPL-3.0; review the [license](https://github.com/pbi-tools/pbi-tools/blob/main/LICENSE) and required notices/source-distribution obligations with your company's open-source/legal team before distributing this deployment package. See the [pbi-tools CLI documentation](https://pbi.tools/cli/) for supported versions. Verify runtime availability and PBIT extraction using a representative file on the target App Service before production use.

The `http` launch profile opens the interactive OpenAPI page in your browser at `http://localhost:5066/swagger`. Configure the local Entra app registration and redirect URI first; Swagger, the review UI, and their APIs require sign-in. Swagger UI and the OpenAPI JSON document are enabled only in the Development environment.

The review screen is at `http://localhost:5066/`. Select a ZIP containing a complete PBIP/PBIR project (preferred) or a PBIT template and choose **Upload & review**. Standalone PBIR reports receive report-structure checks only; model-specific rules require a semantic model. After reviewing findings, a submitter can add optional per-finding justifications and submit once. Their dashboard lists only their own submissions. Admins, Managers, and Leads see the reviewer queue, add a decision and optional response to every finding, and submit once. The submitter can then reopen the review to read the response; submitted justifications and reviewer responses cannot be edited from the submitter view.

The current developer connection uses `TrustServerCertificate=True` to support the verified demo connection. For company deployment, use a certificate trusted by the application host and set certificate validation according to company policy; do not copy the demo setting without review.

Check the endpoints:

```powershell
Invoke-RestMethod http://localhost:5066/health/live
Invoke-RestMethod http://localhost:5066/health/ready
```

The readiness endpoint performs a read-only query and does not expose the server or database name. On database failure, it returns HTTP 503 and logs the SQL exception without returning connection details to the caller.

## Supported Power BI input formats

The upload endpoint accepts a ZIP containing exactly one complete PBIP project or one standalone PBIR report, or a PBIT template. PBIX is explicitly rejected. Preserve the ZIP folder layout and all referenced model/report folders. A `.pbip` descriptor uploaded by itself is insufficient. PBIP expects a report `definition.pbir` with `definition/report.json` and `definition/pages/pages.json`, and a path-based semantic model with TMDL table and relationship files. Standalone PBIR validates report structure and skips semantic-model rules because it has no semantic model. PBIT extraction uses pbi-tools in TMDL mode, then applies the semantic-model rules. Uploads are limited to 500 MiB; ZIP/PBIT extracted output is limited to 512 MiB. Encrypted, unsupported, oversized, or unextractable files return an error. Automated tests cover ZIP intake and extracted-TMDL parsing, but actual PBIT extraction must be validated in the target environment before enabling that path in App Service.

## Database-backed review configuration

Review and module settings are stored in relational SQL tables, not JSON columns. Scalar standards and preferences are columns in `dbo.BiAgentReviewConfigurationSettings`; repeated values such as table candidates and required measures are rows in `dbo.BiAgentReviewConfigurationValues`; rule policies and module availability each have dedicated tables. Versioned audit snapshots use corresponding relational tables. `dbo.BiAgentReviewConfiguration` holds only the singleton configuration version and update metadata. Updates are transactional and use optimistic concurrency. The Workspace tab saves module availability independently from the Power BI Project Review tab, which saves review standards and rule policies; both writes preserve the other tab's saved values. Application startup does not create schema. Host-specific settings such as `Review:PbiToolsPath` stay in App Service configuration and are not editable in the UI.

Apply `database\migrations\005_create_review_configuration.sql` after migrations 001–004, then apply `database\migrations\006_normalize_review_configuration.sql`. Migration 005 creates and seeds the normalized schema on a fresh database. Migration 006 converts databases that already applied the earlier JSON-based migration 005, migrates their audit snapshots, and removes the JSON columns; it is a no-op on a fresh normalized schema. The conversion uses `OPENJSON`, so the database must support SQL Server compatibility level 130 or later. Apply both scripts in order before enabling Administration. The application identity needs `SELECT`/`UPDATE` on `dbo.BiAgentReviewConfiguration` and `dbo.BiAgentReviewConfigurationSettings`; `SELECT`/`INSERT`/`DELETE` on `dbo.BiAgentReviewConfigurationValues`, `dbo.BiAgentReviewRulePolicies`, and `dbo.BiAgentModuleAvailability`; and `INSERT` on `dbo.BiAgentReviewConfigurationAudit` and its four audit snapshot tables. New reviews read the active database configuration; saved review history remains unchanged.

Select **Administration** in the left navigation to enable or disable workspace modules, and use **Power BI Project Review** to edit naming regexes, measure-table candidates, prefixes, required measures, DAX toggles, and built-in rule enablement/severity. The model review includes potential-unused-object checks for tables, columns, and measures (`OPT-001`–`OPT-003`). OPT-001 now treats a relationship by itself as insufficient proof that a table is used; a table is a candidate when there are no detected DAX or included-report references. These remain advisory static checks against included PBIR report JSON and DAX references; for model-only PBIT input, report visual usage cannot be checked. Findings display the exact model object name and bullet-form evidence. Review every candidate for external reports, query/refresh dependencies, and downstream consumers before deleting model objects. Each editable tab has its own save action: saving Workspace only updates availability; saving Power BI Project Review only updates review standards and rule policies. Both actions use the configuration version to reject stale edits and preserve the other tab's saved values. The **Users & access** tab lists users after their first sign-in and lets an administrator grant or remove the in-app Admin, Manager, and Lead roles. The bootstrap administrator cannot be demoted, and the final administrator cannot be removed. Module visibility is not user authorization. Settings can change registered rules, but adding new executable rule logic still requires a reviewed code change.

## Saved review history

In Development, **Upload & review** saves the summary and complete findings to SQL Server. The recent-review selector loads the signed-in user's runs, while Admins, Managers, and Leads can search all saved runs to service the review queue. Repeating a review with the same project name overwrites the saved review for that uploader rather than creating another row; legacy timestamp-suffixed duplicates are consolidated per uploader. Older runs without uploader identity remain reviewer-only. The source file is not modified. After a successful upload review, a compact confirmation popup shows the file name, local review date/time, and check/finding counts; selecting **OK — view findings** scrolls to the review results. Review history is retained for 30 days by default, configurable through `ReviewHistory:RetentionDays` (allowed range 1–3650). Expired runs are excluded from reads and deleted at startup and every 24 hours while the Development app is running; a new review also removes expired rows. Application startup does not create schema. User submission and reviewer response records are stored separately from the legacy pilot-feedback table.

Reviewers can choose one disposition in **Apply this decision to all findings** to prefill every finding in an open submission, then adjust individual decisions and optional responses before submitting. This is a UI convenience only; the server still validates the complete per-finding decision set before saving it.

The **Working in** header selector sets the active Organization → Project → Team context. Root administrators can choose **All organizations**; other users see only scopes directly assigned to them, so two team assignments show only those two teams. Parent assignments still grant data access to descendants, but a team assignment does not allow selecting its broader project or organization. Selected scopes filter the portal dashboard, support requests, PBIP review history, reviewer submissions, and review feedback to their descendants. The Profile page lists people whose roles apply to the selected scope, including inherited parent roles and descendant scopes. Review/support APIs reject unscoped requests from non-administrators rather than exposing organization-wide data. Access-request workflows remain available outside the active data scope so users can request access to another scope.

Governance Audit, online Power BI review, and the Power BI Governance MCP tools use Power BI-wide data that is not mapped to BI Sentinel organizations/projects/teams; they are therefore available only in the root administrator's **All organizations** context and return an explanatory conflict when a hierarchy scope is selected. Performance Optimization analyzes the file supplied for that request and does not persist or scope its result. Apply database migration `019_add_organization_project_team_hierarchy.sql` before using scope selection; migrations `018` and `019` are needed for support and hierarchy-scoped records respectively.

The `RAMADAS` / `BiAgent` database is not the active application target. The Azure SQL target is `powerbipracticeserver.database.windows.net` / `SmartAgent`; its normalized Review configuration schema and application access schema are present, and Governance migration 007 has been applied there. For a fresh database, apply migrations 001–006, then 007, 008, 009, 010, 011, and 012 (009–011 follow 007; 008 is independent; 012 follows 002, 003, and 008). Grant the application identity only the table-level permissions documented for each feature. Review history stores finding messages and locations, but never the full PBIP artifact. The pilot review retention is 30 days. Governance scan history keeps only the latest successfully saved run; saving a new run transactionally deletes the previous run and its cascading workspace, artifact, and finding rows. Failed scans do not replace the existing saved run.

## Governance (read-only pilot)

Governance Audit uses Power BI REST APIs for live data; it does not enumerate every tenant workspace by default. A BI Sentinel administrator discovers workspaces visible to their signed-in Power BI user, explicitly selects an allowlist, and saves it. During each scan, a reader chooses approved workspaces by their API-returned names, then can choose Reports, Dashboards, and/or Semantic models. Reports and dashboards are listed by name within the selected workspaces and can be individually included or excluded; the API validates artifact IDs against the selected approved workspace scope. Apps and gateways can also be explicitly selected as separate account-level inventories: Apps are the signed-in user's installed apps, and Gateways include only gateways where that user is an administrator. They are not workspace children, so the pre-scan confirmation displays them separately from the approved workspace subset. App inventory requires delegated `App.Read.All`; gateway visibility requires the Power BI gateway-admin assignment. Partial report selection is called out as `INSUFFICIENT_EVIDENCE` for checks that require a complete report inventory. Dataflows and datamarts remain unavailable because their current API/permission requirements exceed this delegated model. Scans are limited to `GovernanceAudit:MaximumWorkspacesPerScan` (default 50, maximum 100); tenant-wide, name-pattern, department, capacity, and owner-based scopes are not implemented. Scans use read-only Power BI requests only for explicitly selected workspace metadata and artifact inventories. OPT-001 through OPT-003 use the Admin workspace metadata scanner only for those selected workspace IDs; no tenant-wide scan is requested. The versioned check catalog is available at `GET /api/governance-audit/checks`. A scan may select checks through repeated `checks` query parameters, for example `POST /api/governance-audit/scan?checks=GOV-001&checks=GOV-007`; omitting the parameter runs all registered checks. Checks that require an unselected artifact inventory return `INSUFFICIENT_EVIDENCE`; the scan does not infer a missing dependency or orphan from an inventory that was not requested. Standard advisory findings cover workspaces with no visible Admin principal or no artifacts; reports whose semantic model is not returned in that workspace or has no returned semantic-model reference; semantic models not referenced by reports in that workspace or without returned `configuredBy` metadata; guest principals identified by Entra's `#EXT#` marker; and Member/Contributor edit-level workspace access. OPT findings use semantic-model tables, columns, and measures where scanner metadata is available. These are candidate results, not proof objects are unused, because report visual and external-consumer references may be unavailable. Missing metadata is marked `INSUFFICIENT_EVIDENCE`. Each finding records its check ID/version, owner, evaluation and exception guidance, API evidence source/timestamp, expected/actual values, and structured evidence. Shared models, guest collaboration, edit-level roles, and missing metadata can all be valid. Report-level owner metadata is not provided by the report-list response; workspace principals and semantic-model `configuredBy` are shown where available. Governance history stores only the latest successfully saved run; each new saved run replaces the previous scan record and its related findings/artifacts, while failed scans preserve the previous result. Configuration changes have a separate relational audit trail.

Refresh history and failed-refresh detection are deferred to preserve the read-only permission boundary. Although the refresh-history endpoint is an HTTP GET and accepts the `Dataset.Read.All` scope, Microsoft requires the caller to have Write permission on each dataset. Enabling this feature requires an explicit permission-model decision and a test identity/workspace; this audit currently does not request or use dataset Write access. Certification status, sensitivity labels, lineage, automated schedules/alerts, configurable organizational standards, and tenant-wide admin inventory are not implemented. Do not infer missing data as a clean audit: API permission failures stop the scan and are surfaced as errors.

### Governance MCP agent

The official .NET MCP SDK is integrated with the authenticated API as a stateless Streamable HTTP endpoint at `/mcp` whenever `GovernanceAudit:Enabled` is true. Current tools expose the versioned check catalog, administrator-approved workspace IDs, account-level Apps/Gateways catalogs, allowlist-bounded scans, and saved-run retrieval; read-only resources describe the current check catalog/capabilities, and a prompt template helps review persisted evidence. A scan tool requires `confirmScan: true` and is limited to supported workspace artifacts, explicitly confirmed account-level Apps/Gateways inventory, workspace-user inventory, and `GOV-001` through `GOV-008` checks. It is secured with BI Sentinel's current Entra web session and SQL-backed `ApplicationAdmin` role. It is not yet a standard OAuth-protected MCP resource server for third-party remote MCP clients; do not connect those clients or expose `/mcp` publicly until the OAuth/resource-metadata and authorization flow is implemented and tested. Tenant-wide Scanner/Admin API, Graph ownership verification, refresh monitoring, exception registry, discrepancy analysis, and report generation remain unimplemented.

See the [Power BI Governance MCP architecture](docs/POWER-BI-GOVERNANCE-MCP-ARCHITECTURE.md) for all 20 requested architecture deliverables, tool/resource contracts, deployment and security design, and the explicit production-readiness checklist. A focused workspace agent is available at [.github/agents/powerbi-governance.agent.md](.github/agents/powerbi-governance.agent.md).

### Required identity and authorization setup

The Power BI connector uses the same signed-in BI Sentinel administrator as a delegated user:

- Add the Power BI Service delegated API permissions `Workspace.Read.All`, `Report.Read.All`, `Dataset.Read.All`, `Dashboard.Read.All`, `App.Read.All`, and `Tenant.Read.All` to the app registration; obtain tenant admin consent as required by organizational policy. Governance Audit requests these scopes when an administrator selects **Authorize Power BI and model metadata access**. Users who previously consented must authorize again. OPT checks require the signed-in user to be a Fabric administrator and the tenant's metadata-scanning settings to allow schema and expression extraction. The scanner request contains only workspace IDs already selected for the audit. If the role, permission, or tenant setting is unavailable, the OPT results explicitly show `INSUFFICIENT_EVIDENCE`; no pass or unused-object conclusion is inferred.
- The refresh-history endpoint documentation lists `Dataset.Read.All` or `Dataset.ReadWrite.All` as the required scope, and also requires the caller to have Write permissions on the dataset. Possessing the OAuth scope alone is insufficient. See [Get Refresh History In Group](https://learn.microsoft.com/en-us/rest/api/power-bi/datasets/get-refresh-history-in-group).
- The connector uses read-only requests and does not modify Power BI artifacts. OPT checks invoke the Admin workspace scanner with the selected workspace IDs and require `Tenant.Read.All`, a Fabric administrator, and enabled tenant metadata-scanning settings. The app does not request tenant-wide inventory. The app scans only the separately saved workspace allowlist; failures are surfaced as evidence gaps rather than clean results.
- BI Sentinel sign-in is application-wide Microsoft Entra OpenID Connect using the authorization-code flow with PKCE. Governance Audit APIs currently require the SQL-backed `ApplicationAdmin` role; the application middleware loads that role for governance requests, and ordinary Review users are denied. Dedicated Governance Reader/Admin assignments can be introduced separately if needed.
- `GovernanceAudit:Enabled` is `true` for Development and `false` for Production. Development still requires the delegated Power BI permissions/consent before API calls succeed. Production remains disabled until a least-privilege test account/workspace is approved and retention is agreed. Migration 007 is applied to Azure `SmartAgent`.
- The token cache is in-memory for this initial pilot. Production deployments should remain single-instance until a protected distributed token cache is configured and tested; in-memory caches are lost on restart and are not shared across instances.

Migration `database\migrations\007_create_governance_audit_configuration.sql` creates the current workspace allowlist, configuration audit snapshots, and normalized audit run/workspace/user/artifact/finding tables. Migration `009_add_governance_finding_traceability.sql` adds the versioned rule and evidence columns for new findings. Migration `010_add_governance_dashboards.sql` allows persisted Dashboard artifact rows. Migration `011_add_governance_global_artifacts.sql` adds a run-linked table for account-level App and Gateway inventory. Migration `013_allow_governance_severity_levels.sql` expands the findings severity constraint to support the versioned check catalog, including `High` severity used by `GOV-009`. Migration `014_add_governance_connection_status.sql` persists per-report data-source inventory. Migration `015_create_governance_exception_management.sql` adds time-limited, admin-approved finding exceptions with an append-only approval/revocation audit trail. Migration `016_add_governance_connection_health_status.sql` persists separate connection-health and gateway-health evidence. Apply 009, 010, 011, 013, 014, 015, and 016 after 007, before deploying this version; these migrations are additive. Migration 007 has been applied to Azure `SmartAgent`; migrations 009–011 and 013–016 are required there before using the expanded Governance Audit features. The API requires `SELECT`, `INSERT`, and `UPDATE` on `dbo.BiAgentGovernanceAuditConfiguration`; `SELECT`, `INSERT`, and `DELETE` on `dbo.BiAgentGovernanceAuditWorkspaces`; `INSERT` on the configuration audit tables; `SELECT`/`INSERT` on the Governance Audit run tables (including the new global artifacts and report connection tables); `SELECT`, `INSERT`, and `UPDATE` on `dbo.BiAgentGovernanceExceptions`; and `SELECT`/`INSERT` on `dbo.BiAgentGovernanceExceptionAudit`. Review/adjust grants against the SQL script before deployment. Application startup does not create schema.
Migration `database\migrations\007_create_governance_audit_configuration.sql` creates the current workspace allowlist, configuration audit snapshots, and normalized audit run/workspace/user/artifact/finding tables. Migration `009_add_governance_finding_traceability.sql` adds the versioned rule and evidence columns for new findings. Migration `010_add_governance_dashboards.sql` allows persisted Dashboard artifact rows. Migration `011_add_governance_global_artifacts.sql` adds a run-linked table for account-level App and Gateway inventory. Migration `013_allow_governance_severity_levels.sql` expands the findings severity constraint to support the versioned check catalog, including `High` severity used by `GOV-009`. Migration `014_add_governance_connection_status.sql` persists per-report data-source inventory. Migration `015_create_governance_exception_management.sql` adds time-limited, admin-approved finding exceptions with an append-only approval/revocation audit trail. Migration `016_add_governance_connection_health_status.sql` persists separate connection-health and gateway-health evidence. Apply 009, 010, 011, 013, 014, 015, and 016 after 007, before deploying this version; 009 must be applied before 015. These migrations are additive. Migration 007 has been applied to Azure `SmartAgent`; migrations 009–011 and 013–016 are required there before using the expanded Governance Audit features. The API requires `SELECT`, `INSERT`, and `UPDATE` on `dbo.BiAgentGovernanceAuditConfiguration`; `SELECT`, `INSERT`, and `DELETE` on `dbo.BiAgentGovernanceAuditWorkspaces`; `INSERT` on the configuration audit tables; `SELECT`/`INSERT` on the Governance Audit run tables (including the new global artifacts and report connection tables); `SELECT`, `INSERT`, and `UPDATE` on `dbo.BiAgentGovernanceExceptions`; and `SELECT`/`INSERT` on `dbo.BiAgentGovernanceExceptionAudit`. Review/adjust grants against the SQL script before deployment. Application startup does not create schema.

Migration `database\migrations\017_add_governance_gateway_type.sql` persists Power BI gateway types (including On-premises) in saved audit history. Apply it after migrations 011 and 014, and before deploying this version; application startup does not create schema.

Governance exceptions are available to BI Sentinel application administrators. An administrator can approve an exception only for a saved `FAIL` finding, and must provide an accountable owner, justification, and future expiry (maximum one year). The approval actor, approval time, and every approval/revocation event are recorded. An active approved exception changes the displayed finding status to `EXCEPTION_APPROVED` while retaining the original failure status and evidence; expiry or revocation returns the finding to `FAIL`. Exception records also apply to matching findings in later scans and are shown in the UI and MCP results.

Governance Audit selects all registered checks by default, including the model optimization checks (`OPT-001` through `OPT-003`). Each scan requests semantic-model table, column, and measure metadata directly from Power BI for the selected workspaces; no optimization file upload is required. Model-object candidates are advisory static-reference results, not proof that an object has no report-visual or external consumers. Connection status is inferred from the latest semantic-model refresh (`Online` for completed, `Offline` for failed); it is historical evidence, not a live source probe. Gateway status reflects Power BI's `gatewayStatus` response. If Power BI does not return either status, the UI shows `Unknown` or `Not checked`, not a guessed offline state. If model metadata or refresh evidence is unavailable, the scan records an evidence gap rather than a pass. Saved runs can be exported as a multi-sheet Excel workbook containing findings and evidence, workspace inventory, access roles, artifacts, and report connections.

### Application sign-in and in-app roles

Microsoft Entra ID authenticates all users with accounts in the configured single tenant. The app does not use Entra app roles for BI Sentinel Administration. After the first sign-in, user identities are keyed by the Entra tenant/object IDs in `dbo.BiAgentApplicationUsers`; the in-app Admin, Manager, and Lead roles are stored in `dbo.BiAgentApplicationUserRoles`, with role changes recorded in `dbo.BiAgentApplicationRoleAudit`. Tenant users can review and submit their own projects; only designated reviewers (Managers, Leads, and Admins) can access the shared review queue. The Users & access administration tab lists signed-in users and lets an administrator grant or remove these roles. The configured bootstrap object ID is permanently protected from demotion, and the final administrator cannot be removed.

Apply `database\migrations\008_create_application_access.sql` after migrations 001–006, then apply `database\migrations\012_add_review_submission_workflow.sql` after migrations 002, 003, and 008. Migration 012 expands the allowed roles to include Manager and Lead, adds the uploader identity to review runs, and creates the signed-in submission/reviewer-response tables. It is independent of migration 007 and does not enable Governance Audit. Grant the application database principal `SELECT`, `INSERT`, and `UPDATE` on `dbo.BiAgentApplicationUsers`; `SELECT`, `INSERT`, and `DELETE` on `dbo.BiAgentApplicationUserRoles`; `INSERT` on `dbo.BiAgentApplicationRoleAudit`; and `SELECT`, `INSERT`, `UPDATE`, and `DELETE` on `dbo.BiAgentReviewSubmissions` and `dbo.BiAgentReviewSubmissionFindings`. Keep the existing review-history permissions. Application startup does not apply schema changes. The app provisions users at sign-in and resolves current roles from SQL on requests; submitters can read only their own uploaded review runs and submissions, while reviewer-queue APIs require Manager, Lead, or Admin.

The same Entra tenant is a compatible identity foundation for a future Microsoft Teams integration, but Teams SSO/token exchange is not implemented. Teams SSO uses a Teams-provided Entra token and a separate server-side token-validation/on-behalf-of flow; it should be added deliberately rather than treating a Teams token as the existing browser cookie.

Governance scans store workspace principal identifiers, names, email addresses where returned, artifact names/URLs, finding evidence, and rule metadata. Decide data classification and retention with the data owner before running on sensitive workspaces; migration 007/009 do not implement automatic audit-history purging. No finding suppression or exception-approval workflow is implemented; the recorded exception guidance is not an approved exception. Restrict results to BI Sentinel application administrators, use a private network, protect the SQL connection with Key Vault/managed identity as appropriate, and agree a retention policy before production use.

### Microsoft references

- [Power BI workspace list API and required `Workspace.Read.All` scope](https://learn.microsoft.com/en-us/rest/api/power-bi/groups/get-groups)
- [List workspaces visible to the caller](https://learn.microsoft.com/en-us/rest/api/power-bi/groups/get-groups)
- [List workspace principals](https://learn.microsoft.com/en-us/rest/api/power-bi/groups/get-group-users)
- [List reports in a workspace](https://learn.microsoft.com/en-us/rest/api/power-bi/reports/get-reports-in-group)
- [List semantic models in a workspace](https://learn.microsoft.com/en-us/rest/api/power-bi/datasets/get-datasets-in-group)
- [Refresh history permissions](https://learn.microsoft.com/en-us/rest/api/power-bi/datasets/get-refresh-history-in-group)

## Module availability

The home route (`/`) is a dashboard with module tiles. Review counts are limited to the signed-in user's saved reviews; administrators see aggregate review, governance, support-queue, and active-user counts. Module navigation is driven by rows in `dbo.BiAgentModuleAvailability`, can be collapsed to an icon rail, and provides more workspace width when collapsed. Administration controls module availability, with bookmarkable routes under `/admin/`; Project Review settings are at `/admin/reviews`. Direct navigation and refresh use the same workspace layout. Disabled or unknown module routes return to the dashboard. Module availability controls navigation and review API access, but does not replace authentication or authorization. Module configuration reads fail closed when the database-backed configuration cannot be read.

The profile menu opens the signed-in account details and Support page. Users can submit support requests and read administrator responses; ApplicationAdmins can review all requests, reply, and set Open, In progress, or Resolved status. Support requests are stored in SQL by migration `018_create_support_requests.sql` and are scoped to their owner except for administrators.

Project Review offers a read-only online workspace scan as an alternative to uploading PBIP/PBIR or PBIT. It lists only workspaces visible to the signed-in Power BI user and stores the resulting review in that user's existing review history. It requests delegated `Workspace.Read.All`, `Report.Read.All`, and `Dataset.Read.All` access. REST API model-schema availability varies: inaccessible/import model schemas are reported as evidence gaps, and online scans do not fetch DAX measures, report visual definitions, partitions, or refresh history. Upload a project artifact for those checks. Online scans never edit Power BI assets or grant workspace permissions.

## Review rules and project structure

Review is organized around small components: `IPbipProjectReader` reads supported Power BI/TMDL input, `IPbipProjectReviewer` orchestrates a review, and each `IReviewRule` implements one rule and declares whether it requires a semantic model. Rules are registered independently in dependency injection, so format readers, extraction, orchestration, and individual rules can be changed/tested separately.

The active review policies are database-backed and edited in **Administration**. The `Review` section of `appsettings.Development.json` now contains only host-specific values such as `PbiToolsPath` and the default project identifier.

The measure-table naming rule checks only the exact table names listed in `MeasureTableNameCandidates`. This avoids assuming that every table containing a measure is a dedicated measure container; dimension or fact tables may legitimately own measures. Leave the list empty to disable candidate detection, or configure known candidate table names. `MeasureTableName` is the expected canonical name for those candidates.

The review configuration applies to new reviews as soon as it is saved. Do not put secrets in review configuration.

To add a genuinely new kind of check, implement `IReviewRule`, give it a unique `Key`, register it in dependency injection, and add its default policy to the database seed/configuration model. Rule logic remains application code; the admin UI controls the policies of registered checks and does not execute code supplied in configuration.

The included deterministic checks cover required PBIP files, table/column/measure naming, placeholder columns, the measure-table name, configured required measures, relationship endpoints, Fact/Dimension naming, bidirectional filters, explicit measures, and basic static DAX practices. Findings are advisory and do not infer business meaning, calculate cardinality, or execute/fully parse DAX.

## Performance Optimization module status

The Performance Optimization module now provides a read-only static assessment when enabled in Administration. Upload a PBIP/PBIR project ZIP, PBIT template, `Model.bim`, a TMDL definition or TMDL ZIP, or DAX. PBIP/PBIR inputs produce report/page/visual inventories when those definitions are included; model inputs inventory tables, measures, calculated columns/tables, relationships, partitions, and detected hierarchies. Assessment output includes DAX observations, reusable artifacts, findings, OPT-001/002/003 candidate classes, evidence gaps, Fabric/Premium readiness, and PO-0 through PO-3 gate status. Uploads are processed temporarily and are not saved as performance history.

Findings explicitly distinguish static observations, hypotheses, and measured issues. This module does not connect to Power BI, execute DAX, collect telemetry, or change tenant content. AI is not required for this deterministic static analysis and is not used to generate findings. Any future AI assistance should remain optional and advisory, grounded in existing evidence, and must not invent timings or performance gains. The OPT-001/002/003 labels here are the Performance Optimization opportunity taxonomy; they do not redefine similarly numbered Project Review or Governance Audit checks.

Cardinality/data distribution, model memory and dataset size, refresh duration/history, query plans/duration, visual render duration, capacity usage, and benchmark traces are not available from static project definitions. Such values are reported as missing rather than zero, and no performance gains are claimed. DAX pattern detection is static and is not a complete DAX semantic parser. Collect DAX Studio traces, Performance Analyzer logs, refresh history, and capacity metrics before treating any candidate as a measured bottleneck. See the [Performance Optimization implementation plan](BI-SENTINEL-DELIVERY-AND-IMPLEMENTATION-PLAN.md).

## Sign-in access requests

Microsoft Entra sign-in verifies identity; it does not automatically grant a new user access to BI Sentinel. A newly seen account is provisioned in a pending state and can only view available scopes, submit access requests, and check its own request history. An organization, project, or team lead must approve a scoped role before application APIs become available. Existing enabled accounts and the configured bootstrap root administrator are not disabled by this workflow.

Users can request `ReadOnly` or the lead role for the selected scope. Requests route to Team Lead → Project Lead → Org Lead for teams, Project Lead → Org Lead for projects, and Org Lead for organizations. Leads at the current stage and earlier stages can act; root administrators can review and decide at any time. An optional response is stored with the decision and included in the requester email. Approval assigns the requested scope role; decline does not grant access.

Root administrators can configure the escalation interval from 1 to 30 days in **Access requests** (default: 1 day). The app sends one reminder per pending request, recipient, and UTC day. At each interval, the next ancestor lead level is added to the notification recipients; earlier leads continue to receive daily reminders. Root administrators are included at the final escalation stage and can always view the approval inbox.

Review and apply `database/migrations/020_create_access_requests.sql` after migrations 008 and 019. Application startup does not create schema. The migration preserves existing user access and hierarchy records.

Email delivery uses the access-request notification outbox and the following configuration. Supply SMTP values through the deployment’s secret/configuration provider; do not commit credentials:

| Key | Required | Description |
|---|---|---|
| `AccessRequests:Email:SmtpHost` | Yes for email | SMTP server hostname |
| `AccessRequests:Email:FromAddress` | Yes for email | Sender address |
| `AccessRequests:Email:SmtpPort` | No | SMTP port (default 587) |
| `AccessRequests:Email:EnableSsl` | No | TLS setting (default true) |
| `AccessRequests:Email:Username` | No | SMTP authentication username |
| `AccessRequests:Email:Password` | No | SMTP authentication password; configure as a secret |
| `AccessRequests:PublicBaseUrl` | Recommended | Public HTTPS app URL used to build actionable email links |

When SMTP is not configured, notification rows remain queued and the application logs that email delivery is unavailable; configure SMTP to actually deliver approval, decline, onboarding, access-adjustment, and reminder templates. The background sender checks the outbox every five minutes and retries failed sends. Ensure notification recipient addresses are present in Entra claims.

### Supporting users from Infosys, Wipro, or another Entra tenant

**Current state: single-tenant sign-in only.** `Authentication:Entra:TenantId` is the one Entra tenant authority used by the app. A user signing in directly from another company's Entra tenant is rejected unless that identity is first invited into this configured tenant as a B2B guest. Changing an email-domain setting or adding an Organization in BI Sentinel does not enable another Entra tenant.

There are two distinct onboarding choices:

1. **Invite selected people as guests (works with the current single-tenant design):** In the home tenant's Entra admin center, invite each person as an external/guest user and have them redeem the invitation. If enterprise-app assignment is required, assign them to BI Sentinel as well. They then sign in through the configured home tenant. The app's pending-access workflow still requires an appropriate BI Sentinel access request and lead approval. No multitenant app-registration change is needed for this option.
2. **Allow people to sign in directly from their company's tenant (requires engineering work):** Register the web app as multitenant for organizational accounts, obtain the appropriate administrator consent/service-principal setup in each participating tenant, and update the app's sign-in authority and issuer validation to support only onboarded tenants. Do not turn off issuer validation or accept every tenant just because the registration is multitenant. A security-reviewed tenant allow-list/onboarding process is required.

**Important: Entra tenant and BI Sentinel organization are different identifiers.** The application user table identifies an account by the Entra `TenantId` plus `EntraObjectId`; the Organization/Project/Team tables are a separate app-level hierarchy. The current schema and authorization flow do not establish or enforce a mapping from an external Entra tenant to exactly one BI Sentinel Organization, and not every module's data is yet scoped through the hierarchy. Therefore, multitenant sign-in must not be enabled for production sharing until tenant onboarding, organization mapping, and end-to-end data isolation are implemented and tested.

#### Work required before direct partner-tenant sign-in

- **Entra administrator:** Change the app registration's supported account types to organizational directories, review the verified publisher/consent model, and approve the app's requested permissions. Each partner tenant's administrator must grant consent/create the enterprise application (service principal) when required by the permission model. Add only approved redirect URIs, including the deployed HTTPS `/signin-oidc` callback.
- **Application identity/authentication code:** Replace the single-tenant authority assumption with a supported multitenant authority and secure issuer validation. Resolve and validate the token's tenant ID (`tid`) against an explicit tenant onboarding/allow-list record; reject unapproved, consumer, or otherwise unsupported tenants. Do not use email suffix/domain as the security boundary.
- **Organization onboarding and authorization:** Add an administrative, auditable relationship between the Entra tenant ID and the BI Sentinel Organization (including enabled/pending/disabled onboarding state and the responsible organization owner). Define whether one Entra tenant may map to one or multiple BI Sentinel Organizations before designing the constraint. Ensure access-request scopes, users, review history, support, Governance, Performance Optimization, and every other tenant-owned record/API are filtered and authorized through this mapping. Organization/project/team membership approvals remain a separate in-app authorization step; successful Entra sign-in alone must never grant BI Sentinel access.
- **Provisioning and lifecycle:** Define who approves a new company tenant, how the first Org Lead/root-level administration is established safely, how existing users are migrated or reconciled, and how tenant offboarding disables access and handles retained data. Audit tenant onboarding, consent, role changes, and offboarding.
- **Verification before rollout:** Test a user from each approved tenant, an unapproved tenant, guest accounts if supported, issuer/audience/signature failures, cross-organization reads and writes for every module, approval/escalation emails, logout, and tenant offboarding. Review SQL schema and API authorization with security owners; do not treat UI hiding as data isolation.

Partner setup information to collect securely: partner display name; Entra tenant GUID (not just an email domain); named tenant administrator/consent owner; approved BI Sentinel organization and organization owner; app registration/service-principal consent approval; permitted delegated/application permissions and business purpose; first Org Lead; support contact; data-retention/offboarding decision. Never collect or document partner passwords, access tokens, client secrets, or other credentials in this checklist.

Until the multitenant work is delivered, keep `Authentication:Entra:TenantId` set to the current tenant and use guest invitations for selected external users. Treat any live app as single-tenant even if the Entra portal registration is later changed; the application must be updated and security-tested before that configuration change is safe.

## Shared pilot feedback

Feedback persistence requires `Feedback:SqlEnabled=true`. For a fresh environment, review and apply `database/migrations/001_create_review_feedback.sql` before using the feedback endpoints.

1. Have the SQL Server/database owner apply it once using an approved SQL tool/account with DDL rights. Application startup never creates or changes schema.
2. For the finding-linked feedback model, have the database owner review and apply `database/migrations/003_link_feedback_to_review_findings.sql` after migrations 001 and 002. It adds a per-run finding ID and a stable cross-run fingerprint derived from configured `Review:ProjectId`, rule ID, and location. Existing feedback is linked to the newest exact match when possible; unmatched feedback remains available as legacy data. The migration changes associations, but does not delete feedback rows. It has been applied to the current `RAMADAS` / `BiAgent` database; all 4 existing feedback rows were linked.
3. Have the database owner review and apply `database/migrations/004_add_feedback_submission_status.sql`. It adds `Open`/`Closed` state; legacy responses default to `Closed`. It has been applied to the current `RAMADAS` / `BiAgent` database without changing the 2 runs, 8 findings, or 4 feedback records.
4. Verify the app identity has `SELECT`, `INSERT`, and `UPDATE` on `dbo.BiAgentReviewFeedback` and `SELECT` on the history tables.
5. Start the app and add a rating or note, then select **Submit feedback** to close it. Select **Reopen** to put it back into the Waiting queue. Rating the same issue in a later run creates a separate response linked to that run. JSON export remains explicit.

Set `Review:ProjectId` to a stable, unique identifier for each configured project. Keep it unchanged when project display names or locations change; use a distinct ID for a different project. `FindingFingerprint` is for comparing the same rule/location across review runs; `ReviewFindingId` is the exact foreign-key association used for feedback. Linked feedback is deleted when its run expires (30 days by default), so approve that retention behavior before production.

The reviewer ID is a random browser-local identifier, not an authenticated company identity. The platform-level App Service authentication gate protects access but does not associate feedback with a verified app user. Comments and finding text are stored in `BiAgent`; do not use the feature for sensitive content before that data flow and retention policy are approved.

Run the regression tests from the workspace root:

```powershell
dotnet test "tests\BISentinel.Api.Tests\BISentinel.Api.Tests.csproj"
```

The tests use temporary PBIP fixtures; they do not modify the sample project or SQL database.

If `dotnet` is not recognized after installing the SDK, restart the terminal or VS Code so it reloads the updated PATH.

## Configuration for another developer or environment

Each developer configures their own connection locally through user-secrets or an approved environment-specific secret/configuration provider. The app requires the `ConnectionStrings:BiAgent` setting. Do not commit credentials, tokens, or developer-specific connection strings. The current connection uses Integrated Authentication, so it contains no SQL username or password.

For a Windows-hosted demo, confirm that the identity running the application—not just the developer's interactive account—has the required SQL permissions. For Azure SQL, provision an approved application identity and configure database authentication for the selected company hosting platform.

### Switching Azure SQL authentication

The API and all SQL stores already read the single `ConnectionStrings:BiAgent` setting through `Microsoft.Data.SqlClient`. The current on-prem Windows-authenticated connection remains unchanged. When an Azure SQL database is provisioned, either Azure SQL authentication or Microsoft Entra authentication can be selected by changing this configuration value; no application code or data-provider change is needed. Restart the API after changing the setting.

For a demo using an Azure SQL contained SQL user, have the database owner create a dedicated application user and assign the least-privilege `bi_sentinel_app` role described below. Then set this connection string locally in .NET user-secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:BiAgent" "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;User ID=<app-user>;Password=<password>;Authentication=SqlPassword;Encrypt=True;TrustServerCertificate=False;Connect Timeout=30" --project "src\BISentinel.Api\BISentinel.Api.csproj"
```

Replace the placeholders locally. Never paste the real password into source code, this README, tickets, or chat. .NET user-secrets are for local development and are not an encrypted production secret store. For a deployed app, use a Key Vault reference or the hosting platform's protected configuration to provide `ConnectionStrings__BiAgent`; limit SQL authentication to a temporary/demo setup and follow company password rotation policy.

The Azure SQL database owner can create the contained SQL user and role in the target database (use the same generated password in the protected app configuration; do not check it in):

```sql
CREATE USER [bi_sentinel_sql] WITH PASSWORD = '<generated-strong-password>';
CREATE ROLE [bi_sentinel_app];
GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentReviewRuns TO [bi_sentinel_app];
GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentReviewFindings TO [bi_sentinel_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.BiAgentReviewFeedback TO [bi_sentinel_app];
GRANT SELECT, UPDATE ON OBJECT::dbo.BiAgentReviewConfiguration TO [bi_sentinel_app];
GRANT SELECT, UPDATE ON OBJECT::dbo.BiAgentReviewConfigurationSettings TO [bi_sentinel_app];
GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentReviewConfigurationValues TO [bi_sentinel_app];
GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentReviewRulePolicies TO [bi_sentinel_app];
GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentModuleAvailability TO [bi_sentinel_app];
GRANT INSERT ON OBJECT::dbo.BiAgentReviewConfigurationAudit TO [bi_sentinel_app];
GRANT INSERT ON OBJECT::dbo.BiAgentReviewConfigurationAuditSettings TO [bi_sentinel_app];
GRANT INSERT ON OBJECT::dbo.BiAgentReviewConfigurationAuditValues TO [bi_sentinel_app];
GRANT INSERT ON OBJECT::dbo.BiAgentReviewRulePolicyAudit TO [bi_sentinel_app];
GRANT INSERT ON OBJECT::dbo.BiAgentModuleAvailabilityAudit TO [bi_sentinel_app];
ALTER ROLE [bi_sentinel_app] ADD MEMBER [bi_sentinel_sql];
```

To move from SQL authentication to an Azure-hosted managed identity later, enable the app host's managed identity, create its Entra database user, and grant that user the application role. Then replace the same setting with:

```text
Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;Connect Timeout=30
```

For local development with an approved Entra developer account, use `Authentication=Active Directory Default` instead of `Active Directory Managed Identity`. The Azure-hosted managed identity and the SQL-auth user are separate database principals; provision and test the new identity's grants before switching, and retain the SQL-auth secret only until Entra connectivity and app workflows are verified. Do not add the connection string to `appsettings.json`.

### Moving the database to Azure

For this application, Azure SQL Database is the simplest cloud target: the app uses ordinary SQL tables, parameterized T-SQL, transactions, temporary tables, and foreign keys, and does not depend on SQL Agent or instance-level features. Choose Azure SQL Managed Instance instead only if the organization needs near-full SQL Server instance compatibility, SQL Agent, cross-database access, or other instance-scoped features. Confirm the source SQL Server version and run a compatibility assessment before migration.

Recommended migration and configuration steps:

1. Provision an Azure SQL logical server and database in the approved subscription/region. Set the Microsoft Entra administrator, require encrypted connections, and prefer private networking (private endpoint/VNet) over a public database endpoint.
2. For the preferred production configuration, create a separate system-assigned managed identity for the app host (for example, Azure App Service). A temporary SQL-authenticated demo is also supported; if used, create a dedicated contained SQL user and store its password only in a secret provider.
3. Create a contained database user for the managed identity and grant only the application permissions needed. Keep schema changes under a separate deployment/migration identity; the app must not receive DDL permissions. Example for the database owner (replace the identity name and run once per target database):

   ```sql
   CREATE USER [<app-managed-identity-name>] FROM EXTERNAL PROVIDER;
   CREATE ROLE [bi_sentinel_app];
   GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentReviewRuns TO [bi_sentinel_app];
   GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentReviewFindings TO [bi_sentinel_app];
   GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.BiAgentReviewFeedback TO [bi_sentinel_app];
   GRANT SELECT, UPDATE ON OBJECT::dbo.BiAgentReviewConfiguration TO [bi_sentinel_app];
   GRANT SELECT, UPDATE ON OBJECT::dbo.BiAgentReviewConfigurationSettings TO [bi_sentinel_app];
   GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentReviewConfigurationValues TO [bi_sentinel_app];
   GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentReviewRulePolicies TO [bi_sentinel_app];
   GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.BiAgentModuleAvailability TO [bi_sentinel_app];
   GRANT INSERT ON OBJECT::dbo.BiAgentReviewConfigurationAudit TO [bi_sentinel_app];
   GRANT INSERT ON OBJECT::dbo.BiAgentReviewConfigurationAuditSettings TO [bi_sentinel_app];
   GRANT INSERT ON OBJECT::dbo.BiAgentReviewConfigurationAuditValues TO [bi_sentinel_app];
   GRANT INSERT ON OBJECT::dbo.BiAgentReviewRulePolicyAudit TO [bi_sentinel_app];
   GRANT INSERT ON OBJECT::dbo.BiAgentModuleAvailabilityAudit TO [bi_sentinel_app];
   ALTER ROLE [bi_sentinel_app] ADD MEMBER [<app-managed-identity-name>];
   ```
4. Apply the approved migrations `001` through `006` to the Azure database using a controlled deployment job or database-owner account. Migration 005 creates and seeds the relational review configuration schema; migration 006 converts legacy JSON configuration schemas when present. The app does not create schema at startup.
5. Configure the deployed app with an encrypted connection string using its selected authentication mode. Prefer Microsoft Entra managed identity in production:

   ```text
   Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;Connect Timeout=30
   ```

   Store this in the host's configuration/secret provider, not source control. For local developer access, use an approved Entra developer login and `Authentication=Active Directory Default` with the current `Microsoft.Data.SqlClient` package, rather than copying the production managed-identity connection string.
6. Verify the object-level grants against the deployed API paths and confirm retention deletes/cascades work. Do not grant `db_owner` or broad `db_datawriter` access to the runtime identity.
7. If existing review history must move, choose a controlled offline or online migration (for example, Azure Database Migration Service where compatible, or a reviewed `SqlPackage` export/import). Test migrations, row counts, feedback links, retention cleanup, and `/health/ready` against a non-production target before cutover.
8. Deploy the API with its managed identity enabled, verify outbound network access to the database, and test live/readiness health plus review-save/load and feedback workflows. Keep the on-prem connection available until the Azure cutover is approved.

Database access is already abstracted through `ConnectionStrings:BiAgent` and `Microsoft.Data.SqlClient`; a straightforward move to Azure SQL should be configuration and database provisioning, not application code. Keep TLS certificate validation enabled (`TrustServerCertificate=False`) in Azure.

The Azure SQL permission grants should be scoped to the application tables, not `db_owner` or broad database roles. The runtime needs to read/insert/delete review runs, read/insert/delete findings (including overwrite/expiry cascades), read/insert/update feedback, update active normalized review configuration tables, and append/read normalized configuration audit records. Have the database owner verify cascade behavior and the exact grants in the target database before deployment.

## Delivery guide

See [BI Sentinel delivery and implementation plan](./BI-SENTINEL-DELIVERY-AND-IMPLEMENTATION-PLAN.md) for the phase-by-phase plan, BA activities, developer work, acceptance criteria, migration notes, and open decisions.
