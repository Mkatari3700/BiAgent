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
- Review screen at `/` for findings, saved review history, and pilot feedback
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
dotnet run --project "src\BISentinel.Api\BISentinel.Api.csproj" --launch-profile http
```

Before starting the app, apply migrations 001–006 and 008. Register a single-tenant Microsoft Entra web application with the local redirect URI `http://localhost:5077/signin-oidc` (or `https://localhost:7053/signin-oidc` for the HTTPS launch profile) and the deployed HTTPS callback URL. Use a confidential-client credential from user-secrets locally and an approved Key Vault reference or certificate configuration in App Service. The bootstrap setting is the Entra **user object ID**, not an email address. Never commit credentials or replace the placeholders above with real values in documentation.

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
- App setting `Feedback__SqlEnabled` to `true` after applying database migrations 001–006. `appsettings.Production.json` also enables it; leave this setting false until the feedback, review-configuration, and module-availability migrations are applied.
- No `Review__ProjectPath` or server-side project directory is required; review input comes from uploads.
- Apply database migrations `001`–`006` and `008` before using the authenticated app; grant the app identity the documented permissions. Migration `008` is independent of the deferred Governance Audit migration `007`.
- The published `web.config`, ASP.NET endpoint, and multipart form limits allow a 500 MiB file plus multipart overhead. If a reverse proxy, WAF, or other ingress sits in front of App Service, configure its request-body limit to at least 501 MiB as well.
- Allow sufficient writable temporary storage for the buffered upload and expanded project. ZIP/PBIT expanded output is capped separately at 512 MiB; confirm the App Service plan has enough temporary disk capacity and test with representative files.
- The deployment package includes the supplied pbi-tools 1.2.0 Windows executable and its `.exe.config` under `tools\`; no separate App Service installation or `Review__PbiToolsPath` setting is needed. PBIT extraction uses this bundled executable by default. A non-empty `Review__PbiToolsPath` overrides it and may be an absolute path or a path relative to the application directory. Validate PBIT extraction on the target Windows App Service with representative files before production use.

Keep `ASPNETCORE_ENVIRONMENT` as `Production`. The UI is served at `/`; Swagger remains disabled in Production. App Service must have `Authentication__Entra__TenantId`, `Authentication__Entra__ClientId`, `Authentication__Entra__ClientSecret` (or approved certificate configuration), and `Authentication__BootstrapAdminObjectId` configured before startup. Entra authentication is enabled by default and the app fails startup if the tenant, client, or bootstrap administrator ID is missing. All application pages and APIs require a signed-in account from the configured tenant. Only users granted the SQL-backed **ApplicationAdmin** role can change review/module configuration or assign other admins; every newly signed-in tenant user receives basic Review access. The review APIs accept untrusted uploads and write review/feedback data to SQL.

For local development, `appsettings.Development.json` contains non-secret Development defaults and is loaded when `ASPNETCORE_ENVIRONMENT=Development` (as selected by the local launch profiles). For production, `appsettings.Production.json` contains the Production defaults and is loaded when the App Service environment is `Production`. Higher-priority App Service environment variables and connection-string settings override either JSON file.

pbi-tools 1.2.0 is bundled from the supplied Windows ZIP. It targets .NET Framework 4.7.2, so the Windows App Service runtime must support it. pbi-tools is a separate open-source component distributed under AGPL-3.0; review the [license](https://github.com/pbi-tools/pbi-tools/blob/main/LICENSE) and required notices/source-distribution obligations with your company's open-source/legal team before distributing this deployment package. See the [pbi-tools CLI documentation](https://pbi.tools/cli/) for supported versions. Verify runtime availability and PBIT extraction using a representative file on the target App Service before production use.

The `http` launch profile opens the interactive OpenAPI page in your browser at `http://localhost:5066/swagger`. Configure the local Entra app registration and redirect URI first; Swagger, the review UI, and their APIs require sign-in. Swagger UI and the OpenAPI JSON document are enabled only in the Development environment.

The review screen is at `http://localhost:5066/`. Select a ZIP containing a complete PBIP/PBIR project (preferred) or a PBIT template and choose **Upload & review**. Standalone PBIR reports receive report-structure checks only; model-specific rules require a semantic model. Then filter findings, record whether each result is helpful/incorrect/not applicable, and add an optional note. Saved reviews are not loaded automatically; search by project name to find and open a saved run. Select **Submit feedback** to save and close a response; editing does not save until submission. The dashboard lists this browser's findings as **Waiting** or **Closed**, with actions to open a saved review and reopen a submitted response. Submitted ratings and notes reload from SQL when you reopen that exact review. The app does not persist feedback drafts in browser storage; if SQL is unavailable, unsaved changes remain only in the current page session. Use **Export feedback** to explicitly download a JSON copy.

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

Select **Administration** in the left navigation to enable or disable workspace modules, and use **Power BI Project Review** to edit naming regexes, measure-table candidates, prefixes, required measures, DAX toggles, and built-in rule enablement/severity. Each editable tab has its own save action: saving Workspace only updates availability; saving Power BI Project Review only updates review standards and rule policies. Both actions use the configuration version to reject stale edits and preserve the other tab's saved values. The **Users & access** tab lists users after their first sign-in and lets an administrator grant or remove the in-app Admin role. The bootstrap administrator cannot be demoted, and the final administrator cannot be removed. Module visibility is not user authorization. Settings can change registered rules, but adding new executable rule logic still requires a reviewed code change.

## Saved review history

In Development, **Upload & review** saves the summary and complete findings to SQL Server. The recent-review selector loads a saved run and its findings. Repeating a review with the same project name overwrites the saved review rather than creating another dashboard row; legacy timestamp-suffixed duplicates are consolidated the next time that project is reviewed. Feedback is preserved for findings that still match (same project, rule, and location), and feedback for findings that no longer exist is removed. The source file is not modified. After a successful upload review, a compact confirmation popup shows the file name, local review date/time, and check/finding counts; selecting **OK — view findings** scrolls to the review results. Review history is retained for 30 days by default, configurable through `ReviewHistory:RetentionDays` (allowed range 1–3650). Expired runs are excluded from reads and deleted at startup and every 24 hours while the Development app is running; a new review also removes expired rows. Application startup does not create schema. Although app access now uses Entra sign-in, feedback attribution still uses the existing browser-generated reviewer ID and has not yet been linked to the signed-in user.

The `RAMADAS` / `BiAgent` database is not the active application target. The Azure SQL target is `powerbipracticeserver.database.windows.net` / `SmartAgent`; its normalized Review configuration schema and application access schema are present, and Governance migration 007 has now been applied there. For a fresh database, apply migrations 001–006, then 007 and 008. Grant the application identity only the table-level permissions documented for each feature. Review history stores finding messages and locations, but never the full PBIP artifact. The pilot review retention is 30 days. Governance history currently has no automatic purge; agree a retention policy before storing production tenant access data.

## Governance Audit (read-only pilot)

Governance Audit uses Power BI REST APIs for live data; it does not enumerate every tenant workspace by default. A BI Sentinel administrator discovers workspaces visible to their signed-in Power BI user, explicitly selects an allowlist, and saves it. Each scan is limited to the saved scope and to `GovernanceAudit:MaximumWorkspacesPerScan` (default 50, maximum 100). The scan calls only GET endpoints to read selected workspace metadata, reports, semantic models, and workspace principals. Standard advisory findings cover workspaces with no visible Admin principal or no artifacts; reports whose semantic model is not returned in that workspace or has no returned semantic-model reference; semantic models not referenced by reports in that workspace or without returned `configuredBy` metadata; guest principals identified by Entra's `#EXT#` marker; and Member/Contributor edit-level workspace access. Shared models, models used outside the workspace, guest collaboration, edit-level roles, and missing metadata can all be valid. Findings are investigation prompts, not proof of orphaning, excessive access, or policy violations. Report-level owner metadata is not provided by the report-list response; workspace principals and semantic-model `configuredBy` are shown where available. Scan results and access principals are stored in normalized SQL tables and can be reopened from the recent audit history. Configuration changes have a separate relational audit trail.

Refresh history and failed-refresh detection are deferred to preserve the read-only permission boundary. Although the refresh-history endpoint is an HTTP GET and accepts the `Dataset.Read.All` scope, Microsoft requires the caller to have Write permission on each dataset. Enabling this feature requires an explicit permission-model decision and a test identity/workspace; this audit currently does not request or use dataset Write access. Certification status, sensitivity labels, lineage, automated schedules/alerts, configurable organizational standards, and tenant-wide admin inventory are not implemented. Do not infer missing data as a clean audit: API permission failures stop the scan and are surfaced as errors.

### Required identity and authorization setup

The Power BI connector uses the same signed-in BI Sentinel administrator as a delegated user:

- Add the Power BI Service delegated API permissions `Workspace.Read.All`, `Report.Read.All`, and `Dataset.Read.All` to the app registration; obtain tenant admin consent if required by organizational policy. Governance Audit requests these scopes only when an administrator selects **Authorize Power BI read access**. This delegated authorization is separate from signing in to BI Sentinel and is normally a one-time step, not something needed for each scan. The user's existing Power BI workspace permissions remain the limit on discovered and scanned data.
- The refresh-history endpoint documentation lists `Dataset.Read.All` or `Dataset.ReadWrite.All` as the required scope, and also requires the caller to have Write permissions on the dataset. Possessing the OAuth scope alone is insufficient. See [Get Refresh History In Group](https://learn.microsoft.com/en-us/rest/api/power-bi/datasets/get-refresh-history-in-group).
- This design sends only Power BI GET requests and does not require the service-principal tenant setting or granting the connector Member/Admin workspace roles. The app scans only the separately saved workspace allowlist. Power BI may still enforce additional user privileges for specific endpoints or workspace metadata; failures are surfaced and do not produce a clean audit.
- BI Sentinel sign-in is application-wide Microsoft Entra OpenID Connect using the authorization-code flow with PKCE. Governance Audit APIs currently require the SQL-backed `ApplicationAdmin` role; the application middleware loads that role for governance requests, and ordinary Review users are denied. Dedicated Governance Reader/Admin assignments can be introduced separately if needed.
- `GovernanceAudit:Enabled` is `true` for Development and `false` for Production. Development still requires the delegated Power BI permissions/consent before API calls succeed. Production remains disabled until a least-privilege test account/workspace is approved and retention is agreed. Migration 007 is applied to Azure `SmartAgent`.
- The token cache is in-memory for this initial pilot. Production deployments should remain single-instance until a protected distributed token cache is configured and tested; in-memory caches are lost on restart and are not shared across instances.

Migration `database\migrations\007_create_governance_audit_configuration.sql` creates the current workspace allowlist, configuration audit snapshots, and normalized audit run/workspace/user/artifact/finding tables. It has been applied to Azure `SmartAgent`; migration 007 is still required on fresh deployments after migrations 001–006. The API requires `SELECT`, `INSERT`, and `UPDATE` on `dbo.BiAgentGovernanceAuditConfiguration`; `SELECT`, `INSERT`, and `DELETE` on `dbo.BiAgentGovernanceAuditWorkspaces`; `INSERT` on the configuration audit tables; `SELECT`/`INSERT` on the five Governance Audit run tables; and `SELECT` on the parent run table for history reads. Review/adjust grants against the SQL script before deployment. Application startup does not create schema.

### Application sign-in and in-app roles

Microsoft Entra ID authenticates all users with accounts in the configured single tenant. The app does not use Entra app roles for BI Sentinel Administration. After the first sign-in, user identities are keyed by the Entra tenant/object IDs in `dbo.BiAgentApplicationUsers`; the in-app `ApplicationAdmin` role is stored in `dbo.BiAgentApplicationUserRoles`, with changes recorded in `dbo.BiAgentApplicationRoleAudit`. Tenant users receive basic Review access when they sign in. The Users & access administration tab lists signed-in users and lets an administrator grant or remove the in-app Admin role. The configured bootstrap object ID is permanently protected from demotion, and the final administrator cannot be removed.

Apply `database\migrations\008_create_application_access.sql` after migrations 001–006. It is independent of migration 007 and does not enable Governance Audit. Grant the application database principal `SELECT`, `INSERT`, and `UPDATE` on `dbo.BiAgentApplicationUsers`; `SELECT`, `INSERT`, and `DELETE` on `dbo.BiAgentApplicationUserRoles`; and `INSERT` on `dbo.BiAgentApplicationRoleAudit`. Application startup does not apply schema changes. The app provisions users at sign-in and resolves current roles from SQL on session/admin requests; the signed-in tenant boundary protects all other APIs.

The same Entra tenant is a compatible identity foundation for a future Microsoft Teams integration, but Teams SSO/token exchange is not implemented. Teams SSO uses a Teams-provided Entra token and a separate server-side token-validation/on-behalf-of flow; it should be added deliberately rather than treating a Teams token as the existing browser cookie.

Governance scans store workspace principal identifiers, names, email addresses where returned, artifact names/URLs, and findings. Decide data classification and retention with the data owner before running on sensitive workspaces; migration 007 does not implement automatic audit-history purging. Restrict results to BI Sentinel application administrators, use a private network, protect the SQL connection with Key Vault/managed identity as appropriate, and agree a retention policy before production use.

### Microsoft references

- [Power BI workspace list API and required `Workspace.Read.All` scope](https://learn.microsoft.com/en-us/rest/api/power-bi/groups/get-groups)
- [List workspaces visible to the caller](https://learn.microsoft.com/en-us/rest/api/power-bi/groups/get-groups)
- [List workspace principals](https://learn.microsoft.com/en-us/rest/api/power-bi/groups/get-group-users)
- [List reports in a workspace](https://learn.microsoft.com/en-us/rest/api/power-bi/reports/get-reports-in-group)
- [List semantic models in a workspace](https://learn.microsoft.com/en-us/rest/api/power-bi/datasets/get-datasets-in-group)
- [Refresh history permissions](https://learn.microsoft.com/en-us/rest/api/power-bi/datasets/get-refresh-history-in-group)

## Module availability

The left navigation is driven by rows in `dbo.BiAgentModuleAvailability`, can be collapsed to an icon rail, and provides more workspace width when collapsed. Administration controls module availability, with bookmarkable routes under `/admin/`; Power BI Project Review settings are at `/admin/reviews`. The review workspace is at `/reviews`. Direct navigation and refresh use the same workspace layout. Disabled modules redirect to the first enabled module. Module availability controls navigation and review API access, but does not replace authentication or authorization. Module configuration reads fail closed when the database-backed configuration cannot be read.

## Review rules and project structure

Review is organized around small components: `IPbipProjectReader` reads supported Power BI/TMDL input, `IPbipProjectReviewer` orchestrates a review, and each `IReviewRule` implements one rule and declares whether it requires a semantic model. Rules are registered independently in dependency injection, so format readers, extraction, orchestration, and individual rules can be changed/tested separately.

The active review policies are database-backed and edited in **Administration**. The `Review` section of `appsettings.Development.json` now contains only host-specific values such as `PbiToolsPath` and the default project identifier.

The measure-table naming rule checks only the exact table names listed in `MeasureTableNameCandidates`. This avoids assuming that every table containing a measure is a dedicated measure container; dimension or fact tables may legitimately own measures. Leave the list empty to disable candidate detection, or configure known candidate table names. `MeasureTableName` is the expected canonical name for those candidates.

The review configuration applies to new reviews as soon as it is saved. Do not put secrets in review configuration.

To add a genuinely new kind of check, implement `IReviewRule`, give it a unique `Key`, register it in dependency injection, and add its default policy to the database seed/configuration model. Rule logic remains application code; the admin UI controls the policies of registered checks and does not execute code supplied in configuration.

The included deterministic checks cover required PBIP files, table/column/measure naming, placeholder columns, the measure-table name, configured required measures, relationship endpoints, Fact/Dimension naming, bidirectional filters, explicit measures, and basic static DAX practices. Findings are advisory and do not infer business meaning, calculate cardinality, or execute/fully parse DAX.

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
