# BI Sentinel Delivery and Implementation Plan

**Audience:** Business Analyst (BA), Power BI developers, .NET developers, data/platform engineers, security reviewers, and pilot stakeholders  
**Status:** Initial delivery baseline; decisions marked **TBD** require stakeholder confirmation  
**Product:** BI Sentinel  
**Last updated:** 2026-10-26

## 1. Purpose

This document turns the BI Sentinel enterprise blueprint into a delivery guide that a BA and development team can use to scope, build, test, demo, and evolve the product. It favors a small, low-cost demo that uses the existing on-premises SQL Server, while keeping identity, database, AI, and tenant-specific settings configurable for a later company deployment.

The initial product is an **advisory review assistant** for Power BI development. It inspects supported artifacts or metadata, runs deterministic checks, and returns actionable findings. It does not publish reports, modify models, or block releases in the demo.

## 2. Current baseline and assumptions

### Known starting point

- An existing on-premises Microsoft SQL Server (MSSQL) is available.
- The SQL Server name is `RAMADAS`, and the target database is `BiAgent`.
- The initial database connection should use Windows Integrated Authentication.
- Review intake is upload-only: PBIP/PBIR projects are uploaded as ZIPs (preferred), and PBIT templates are uploaded directly (uses bundled pbi-tools 1.2.0). PBIX is not accepted. Uploads are capped at 500 MiB; expanded ZIP/PBIT output is separately capped at 512 MiB.
- The first review scope is basic structural validations and deterministic checks. Organization-specific naming and modeling rules will be added after they are agreed.
- The first demonstration should avoid unnecessary cloud services and recurring costs.
- Company credentials, tenant settings, and production hosting will be supplied later.
- The target product direction includes Power BI/Fabric integration, Teams, AI-assisted explanations, governance, documentation, and release-readiness support.

### Planning assumptions to validate

1. The first demo can run on a Windows developer machine or an approved Windows host that can reach the SQL Server.
2. The first pilot can use a small set of approved sample artifacts and/or metadata exports without production tenant access.
3. The initial user experience can be a simple web application; Teams is not required to prove the core review flow.
4. A single, modular application is preferred over microservices for the demo.
5. The exact PBIP version/features to support, SQL Server version, hosting location, and organization-specific review rules are not yet confirmed.

These assumptions are not commitments. Confirm them during Phase 0 before treating the sprint plan or acceptance targets as approved.

### Verified environment facts

- From the current Windows development environment, a read-only connection to `Server=RAMADAS;Database=BiAgent` succeeded using Windows Integrated Authentication.
- This verifies connectivity and access for the current Windows identity only. It does not verify that another developer, a hosted app/service identity, or a production deployment can connect.
- The API readiness check uses `SELECT 1` only. It does not create or alter schema objects or write application data. Separate Development-only feedback and review-history workflows persist data after their explicit migrations.

### Implementation status — 2026-10-24

- **Phase 0 environment verification:** SQL Server and database names are confirmed, and the current Windows identity can connect. Pilot users and organization-specific rules still require BA/product-owner decisions.
- **Phase 1 foundation:** A .NET 10 ASP.NET Core API is scaffolded with live and SQL-readiness endpoints. Its local connection setting is stored using .NET user-secrets and is not committed to source control.
- **Validation:** The API builds without warnings, the package vulnerability check reports no vulnerable packages, and the user confirmed that both live and database-readiness health APIs work from their VS Code terminal.
- **Database impact:** The original health/readiness checks are read-only. Later, two explicit migrations created the Development pilot feedback and review-history tables; application startup itself remains schema-read-only.
- **Initial MVP input and scope agreed:** Start with a Power BI Project (PBIP), basic structural validations, and deterministic checks. This initial PBIP-only scope was later expanded; current supported formats and limitations are recorded below. Agree organization-specific standards with the BA/BI standards owner.
- **Sample project located:** `D:\Biagentpbi` contains `SampleDemoProject.pbip`, its `.Report` folder, and its `.SemanticModel` folder with TMDL definitions. The report descriptor references the semantic model by relative path. The project `.gitignore` excludes `.pbi/localSettings.json` and `.pbi/cache.abf`; keep those exclusions.
- **Starter standards agreed for the sample:** PascalCase table/column names, `Dim`/`Fact` table prefixes, a `Measures` container, four sample-only required measures (`Total Sales`, `Total Quantity Sold`, `Total Product Cost`, `Total Profit`), Fact/Dim relationship checks, single-direction filtering by default, `DIVIDE` for division, and avoiding `FORMAT` in numeric measures. Generated local date tables are excluded from ordinary naming/relationship checks.
- **Upload-only review intake:** The original configured-path sample route has been removed. Reviews begin with a browser upload; the API accepts PBIT or a ZIP containing a complete PBIP project or standalone PBIR report. PBIX is rejected. Uploads are limited to 500 MiB; archive and PBIT expansion have separate 512 MiB safety limits. The review does not modify the source artifact.
- **Rule configuration and SOLID structure:** PBIP reading is behind `IPbipProjectReader`; orchestration is behind `IPbipProjectReviewer`; each independently registered `IReviewRule` has a configurable enabled/severity policy. Naming regexes, table prefixes, generated-table exclusions, required measures, and DAX check switches are environment-configurable. New rule behavior is added as a small rule implementation plus DI registration; arbitrary executable rule code is not loaded from configuration.
- **Regression tests:** `tests/BISentinel.Api.Tests` covers PBIP parsing/counting, generated-table exclusion, path traversal rejection, rule enablement/severity overrides, missing required measures, configurable measure naming, explicit measure-table candidates, standalone PBIR, and extracted TMDL. All 13 tests pass; tests use temporary project files and do not write to SQL Server or the supplied PBIP sample.
- **Interactive API documentation:** Development-only Swagger UI is available at `/swagger`, with endpoint descriptions and response shapes; the local launch profiles open the UI automatically.
- **Pilot review screen:** Development-only web screen at `/` runs the configured review, summarizes counts, filters findings, and captures per-finding feedback. Feedback is persisted in SQL; JSON is generated only through explicit export.
- **Pilot UI validation:** Earlier browser smoke tests confirmed review rendering, filtering, and explicit JSON export. Current behavior no longer persists feedback in browser storage; SQL is the persistence source, with current-page-only feedback available for explicit JSON export if SQL cannot save it.
- **Shared pilot feedback step:** Added Development-only POST/list feedback APIs, bounded input validation, parameterized SQL upsert/read, browser-local reviewer identifier, and explicit JSON export. Applied the idempotent script `database/migrations/001_create_review_feedback.sql` to `RAMADAS` / `BiAgent`; the table and indexes are present, and the current Windows-authenticated app identity has read/write permissions. Verified API save/read round-trip and removed the temporary verification record. Feedback that cannot reach SQL is held only in page memory and is lost on refresh/close unless explicitly exported.
- **Pilot review history:** Added Development-only review run, recent-history, and load-saved-review APIs plus a UI selector. A run stores its summary and full findings in SQL; the configured retention is 30 days. Applied migration `database/migrations/002_create_review_history.sql` to `RAMADAS` / `BiAgent`, verified runtime permissions, and tested create/list/reopen. Expired history is filtered from reads, deleted by a daily Development cleanup service, and purged on new saved reviews.
- **Security boundary:** Feedback and review-history endpoints are registered only in Development. The generated reviewer ID is pseudonymous and is not identity. App-wide Entra sign-in and in-app administrator roles are implemented; tenant app registration, database migration 008, and deployment configuration are prerequisites before enabling company access. Review-history/feedback retention and comment-content policies remain operational decisions.
- **Finding-linked feedback model:** Feedback now references an exact persisted `ReviewFindingId`; a SHA-256 fingerprint of `ProjectId`, `RuleId`, and `Location` supports comparison across runs without using editable message text. Migration `003_link_feedback_to_review_findings.sql` backfills legacy feedback to the newest exact match and preserves unmatched rows. Applied and reran migration 003 successfully on `RAMADAS` / `BiAgent`; all 4 existing feedback rows were linked, and counts remained 2 review runs, 8 findings, and 4 feedback records. Applying migration 003 in each other target database is required before deploying this code; linked feedback will expire with its review run.
- **Feedback submission and dashboard:** Feedback edits are now drafts until explicit submission; submitting closes the finding, and **Reopen** returns it to Waiting. A Development-only dashboard grid shows current-browser status for unexpired finding occurrences, with a link to open the originating review. Saved-review feedback loads by exact review-run ID and reviewer ID rather than scanning a recent-feedback page, preventing older responses from being missed by the list limit. Migration `004_add_feedback_submission_status.sql` was applied idempotently to `RAMADAS` / `BiAgent`; all 4 existing rows were retained as Closed.
- **Upload-only multi-format intake:** The UI no longer accepts API-host paths or performs folder discovery. It accepts PBIT uploads and ZIPs containing one complete PBIP project or standalone PBIR report; PBIX is rejected. PBIP ZIP is preferred; PBIT uses bundled pbi-tools 1.2.0 (AGPL-3.0; company license review required). Uploads are capped at 500 MiB; expanded ZIP/PBIT output is capped at 512 MiB, with entry-count, symbolic-link, and path-traversal checks. App Service ingress, .NET Framework runtime availability, and temporary-storage capacity must be verified for representative files.
- **Global database-backed review configuration:** Review standards and module availability are stored in relational tables, not SQL JSON columns. Scalar configuration uses typed columns; repeated values, rule policies, and module flags use child tables. Audit history preserves normalized snapshots in corresponding audit tables. Migration `005_create_review_configuration.sql` creates the relational schema for fresh databases. Migration `006_normalize_review_configuration.sql` converts databases with the earlier JSON-based schema, copies existing audit history into relational rows, and drops the JSON columns. The conversion script was executed directly against the locally configured Azure SQL `SmartAgent` database after a read-only preflight; both legacy JSON columns are removed and existing configuration/audit rows were verified. The separate `RAMADAS` / `BiAgent` database still needs migrations 005 and 006. Configuration writes use version checks and a transaction for the update plus its audit snapshot. The Administration tab exposes these settings; review and module configuration writes require the in-app ApplicationAdmin role.
- **Database-controlled module navigation (2026-10-26):** Administration includes enable/disable settings for the Power BI Project Review workspace and all six additional modules in `ArchitectIQ_Modules_Requirements.docx`. Reviews and Governance Audit are enabled by default; other planned modules are disabled. Each module has a distinct bookmarkable UI URL under `/modules/`; direct navigation and refresh are supported, and disabled module URLs redirect to an enabled module. Governance Audit has a selected-workspace scan foundation; other not-yet-built modules display module-specific placeholders when enabled. Reviews API requests are also blocked when the Reviews module is disabled. Module flags are persisted in `dbo.BiAgentModuleAvailability` with relational audit snapshots; they control availability, not user authorization.
- **Module-specific Administration:** Administration is split into Workspace and one bookmarkable subtab per module. Module availability is managed in Workspace, while Power BI Project Review regexes/conventions/rules remain in that module's tab. Subtab navigation preserves unsaved form values. Workspace availability and Power BI Project Review rules each have separate save actions; each update preserves the other tab's saved values and uses the shared version/audit transaction. The workspace sidebar can collapse to an icon rail, expanding content width. Direct module routes share a single layout and hide the review screen until database module settings load.
- **Application sign-in and internal roles:** Microsoft Entra OpenID Connect now protects app pages and APIs for the configured single tenant. All signed-in tenant users receive basic Review access. BI Sentinel Admin is an app-managed role stored in normalized SQL tables, not an Entra app role; the initial admin is bootstrapped by Entra object ID and additional admins are managed in the Users & access tab. The bootstrap admin and final remaining admin cannot be demoted. Migration `008_create_application_access.sql` is required and is independent of Governance Audit migration 007. Teams SSO is not implemented; a later Teams integration will need its own token-validation/on-behalf-of flow while using the same Entra tenant.
- **Governance Audit — read-only selected-workspace foundation:** Added application-admin-protected Governance Audit endpoints, a delegated Power BI REST connector using the signed-in administrator's permissions, explicit workspace allowlisting, read-only workspace/report/semantic-model/principal inventory, owner/empty/shared-model advisory findings, normalized saved run history, and versioned configuration audit. The application middleware loads SQL-backed application roles on Governance Audit requests; only `ApplicationAdmin` can currently administer, scan, or read audit history. The scan is manually triggered and capped at 50 workspaces by default (server-configurable up to 100). Migration `007_create_governance_audit_configuration.sql` is applied to Azure `SmartAgent`. Delegated scopes `Workspace.Read.All`, `Report.Read.All`, and `Dataset.Read.All` are requested only through the Governance Audit sign-in action. Refresh history is excluded because its Power BI API requires dataset write permission; certification, sensitivity labels, lineage, broader orphan analysis, schedules, and tenant-wide admin inventory remain future scope. Keep Governance Audit disabled until permissions/consent and retention for stored principal/artifact data are approved and a least-privilege workspace scan is verified.
- **Administration regex help:** Added examples of common naming patterns and explanations of regex anchors, character classes, repetition, grouping, and alternatives. Browser verification confirmed the help expands and only the database-enabled module links are visible.
- **Review-run dashboard:** Dashboard rows now represent saved review runs and show finding totals, submitted/waiting counts, and run status. Reopening a closed run returns its submitted responses to Waiting; individual findings remain editable within the saved review.
- **Single saved review per project name:** Repeated reviews overwrite the saved summary and findings instead of adding timestamped dashboard rows. Older timestamp-suffixed copies are consolidated on the next review of that project. Feedback remains attached to findings that still match by project, rule, and location; feedback for removed findings is deleted. The consolidation and save use one serializable transaction.
- **Review completion UX:** Successful reviews and saved-review loads show a success message and scroll the page to the review results. The duplicate-name rename/version disclaimer has been removed.
- **Removal of host-path review:** Configured project-path review, path scanning, and file-picker workflows have been removed. Production configuration no longer requires `Review__ProjectPath` or an API-host project directory; the upload endpoint remains the single review intake. Review history and feedback features remain enabled.
- **Administration and security follow-up:** App sign-in and in-app administrator authorization are implemented. Configure the Entra tenant/client and bootstrap administrator, apply migration 008, and grant the app identity the documented SQL permissions before deployment. Review changes to Governance Audit's separate Entra-role policies after its access model is decided.
- **Workspace navigation and cloud direction:** The review UI now has a left navigation rail with Reviews active and space for future modules. Azure SQL Database is the recommended next database target; use Entra managed identity from the Azure app host, private networking, encrypted connections, and separately applied migrations. Use Managed Instance only when instance-level SQL Server features are required.
- **Feedback and history validation:** Eleven regression tests pass; the Release build succeeds. API checks confirmed loading existing feedback for its saved run, submitting/closing, dashboard status, reopening/waiting, and loading the reopened response. Browser validation confirmed saved ratings and notes render on review reopen. Temporary test rows were removed; database counts remain 2 runs, 8 findings, and 4 feedback rows. Production-mode checks confirmed the history endpoints are Development-only; the review UI and Swagger are not mapped.
- **Measure-container false-positive correction:** `NAM-004` now checks only explicitly configured `MeasureTableNameCandidates`, instead of warning on every table that has measures. The sample candidate is configured explicitly, and a regression test proves a measure stored on `DimCustomer` is not flagged unless the table is deliberately configured as a container candidate.
- **Pilot history privacy:** Review-run findings may include project object names, file locations, and rule messages. Store only approved pilot artifacts; the chosen retention is 30 days. Company authentication and access control remain necessary before network use.
- **Sample review validation:** The endpoint reported 3 non-generated tables, 4 measures, 1 non-generated relationship, and 140 checks. It correctly flagged the `measures (2)` container name and default `Column1` placeholder. The sample's required measures were present; no relationship or DAX warnings were emitted. The configured measure-name pattern accepts the sample's word-spaced names. A configuration override disabling the placeholder-column rule removed only that finding.
- **Known review limitations:** DAX checks are static heuristics and do not execute or fully parse DAX. Relationship checks verify endpoints, Fact/Dim naming, and explicit bidirectional filtering; they do not infer cardinality or business correctness.
- **Not yet built:** Teams SSO/token exchange, AI explanations, and any additional Governance Audit authorization decisions. Findings remain advisory; no release gate is enforced.

## 3. Product outcomes and success measures

### Desired outcomes

- Make BI review standards discoverable and consistently applied.
- Help developers fix issues earlier with clear, evidence-based findings.
- Reduce manual review effort without replacing accountable human review.
- Establish a reusable foundation that can move from the demo environment to company infrastructure.

### Baseline and pilot measures

The BA should record a current-state baseline before the pilot. Agree target values with stakeholders rather than treating the suggested measures below as guaranteed commitments.

| Measure | How to measure | Initial target approach |
|---|---|---|
| Review turnaround | Elapsed time from submission to review result | Record baseline first; agree a reduction target with pilot users |
| Finding actionability | Findings that reviewers mark useful/actionable divided by reviewed findings | Set a target after reviewing a representative sample |
| False-positive rate | Findings reviewers mark incorrect or not applicable divided by reviewed findings | Establish a baseline and reduce it before adding enforcement |
| Reproducibility | Same artifact and same rule version produce the same deterministic results | All deterministic checks must be reproducible |
| Pilot adoption | Active pilot users and completed review scenarios | Agree the pilot cohort and minimum scenario count in Phase 0 |
| Operational reliability | Successful review runs divided by total review runs | Define an initial service target before production rollout |

## 4. Recommended technical baseline

| Area | Demo baseline | Later company deployment |
|---|---|---|
| Application | ASP.NET Core on .NET 10 LTS; modular monolith | Same application, deployed on approved company hosting |
| Database | Existing on-prem SQL Server; Windows Integrated Authentication | Azure SQL or another approved platform; use an approved identity/authentication method |
| Data access | `Microsoft.Data.SqlClient`; use EF Core SQL Server if migrations and relational persistence are needed | Keep schema changes scripted/versioned and test SQL compatibility before migration |
| Identity | Local demo access only, restricted to the intended demo audience | Entra ID sign-in and least-privilege application access |
| Power BI inputs | Approved sample files, metadata exports, or a verified local workflow | Approved Power BI/Fabric APIs and tenant permissions, once available |
| Review rules | Versioned deterministic checks; BPA rules where suitable and validated | Same rules, governed through review, testing, and controlled releases |
| AI | Optional and disabled by default; core demo works without a model endpoint | Approved model/provider configured through deployment settings |
| User experience | Simple web interface for submitting/selecting an artifact and viewing results | Add Teams integration if justified by user research and tenant approval |
| Hosting | Developer machine or approved Windows demo host | Company-approved hosting, monitoring, backup, and operational support |

### Technology and hosting notes

- .NET 10 LTS is the recommended starting point for a new build. Confirm the organization’s approved runtime and support policy before implementation.
- Avoid microservices, Kubernetes, a vector database, or paid AI infrastructure for the first demo unless a validated requirement makes one necessary.
- Do not assume a developer's Windows identity will work as the deployed application's database identity. For an on-prem Windows host, agree on the service identity and SQL permissions. A cloud-hosted application generally cannot use a developer's Windows Integrated Authentication connection to reach on-prem SQL Server without a separately designed network and identity solution.
- On Azure SQL, use an approved Entra identity/managed identity or another company-approved method. Managed identity is available only when the chosen hosting and database configuration support it.
- Keep database access behind an application persistence boundary. If only SQL Server and Azure SQL are intended targets, prefer explicit provider configuration and tested migrations over a generalized multi-database abstraction.
- Do not store database passwords, API keys, tokens, or tenant secrets in source control or committed settings files.

## 5. Target architecture and configuration boundaries

### Logical components

1. **Web/API layer:** accepts a review request, validates input, and returns review status/results.
2. **Review orchestration:** coordinates artifact intake, metadata extraction, rule execution, and optional AI explanation.
3. **Artifact/metadata adapters:** isolate supported local file formats and future Power BI/Fabric APIs from review logic.
4. **Rule engine:** executes versioned deterministic checks and produces structured findings.
5. **Optional AI explanation service:** explains existing findings using approved standards; it does not determine deterministic pass/fail outcomes.
6. **Persistence layer:** stores the minimum required review metadata, rule versions, and feedback in SQL Server.
7. **Configuration and operations:** environment-specific options, logs, health checks, and deployment settings.

### Configuration requirements

Use environment-specific configuration, environment variables, and/or the approved secret store. Configuration should be validated at startup with clear errors for missing required settings.

Plan for these settings (names are illustrative, not a required naming convention):

| Setting group | Demo values | Company values later |
|---|---|---|
| Database | SQL Server instance/database; Integrated Security enabled | Azure SQL endpoint/database; approved identity mode |
| Authentication | Restricted demo access | Entra tenant/client/application settings as approved |
| Artifact intake | Local sample directory or supported upload | Approved repository, workspace, or API source |
| Power BI/Fabric | Disabled unless a verified test tenant is available | Tenant/workspace identifiers and least-privilege permissions |
| AI | Disabled | Approved provider, endpoint/deployment, limits, and content policy |
| Feature switches | Teams/API/AI features off unless actively used | Explicitly enabled per environment after approval |
| Logging | Local structured logs with sensitive values excluded | Company logging/monitoring destination and retention |

Do not add fake credentials or production-looking values to examples. Use placeholders and document where an authorized operator supplies real values.

### Persistence design principles

- Persist only what the demo needs. Prefer artifact identifier/hash, review timestamp, rule-set version, outcome, findings, and reviewer feedback over copying full report contents into SQL.
- Define data retention, access, backup, and deletion expectations before a real business artifact is processed.
- Store rule definitions or rule-set versions in a controlled, reviewable form; record the version used for each review.
- Keep schema changes in ordered, repeatable migrations/scripts. Test them against the demo SQL Server and Azure SQL before relying on them for migration.
- Do not store credentials or tokens in review records or logs.

## 6. Delivery phases

The plan below uses two-week sprints as an initial planning unit. It is not a committed schedule; confirm team capacity, approval lead times, and dependencies during Phase 0. Phases are sequential at the gate level, while documentation, security, testing, and stakeholder feedback continue throughout.

### Phase 0 — Discovery, scope, and readiness

**Purpose:** Remove ambiguity before spending effort on integrations or broad feature development.

**BA activities**

- Identify the business sponsor, product owner, technical owner, BI standards owner, security contact, SQL Server owner, and pilot users.
- Interview developers/reviewers about the current review workflow, recurring defects, review evidence, and pain points.
- Select one primary demo scenario and no more than a small, agreed set of secondary scenarios.
- Inventory proposed rules and classify each as mandatory, advisory, or informational.
- Define terms consistently: artifact, review, finding, severity, exception, false positive, and release-ready.
- Record baseline review effort and agree how pilot feedback and success measures will be collected.
- Capture non-functional needs: data sensitivity, supported artifact size/type, expected users, response time, retention, and accessibility.

**Development/platform activities**

- Confirm Windows/.NET SDK availability, SQL Server version/instance, network path, database provisioning process, and intended demo host.
- Verify the Windows identity that will run the app can authenticate to SQL Server; request only the permissions needed for the demo.
- Test the intended artifact intake path using representative, non-sensitive sample assets.
- Identify available BPA tooling/rules and license or execution constraints.
- List tenant, Entra, Power BI/Fabric, Teams, and AI approvals that are deliberately deferred or needed for the pilot.

**Deliverables**

- One-page MVP scope and exclusions.
- Prioritized backlog with story-level acceptance criteria.
- Stakeholder/RACI list and decision log.
- Data classification and retention assumptions.
- Integration/access dependency list with owners and due dates.
- Initial success-measure baseline plan and pilot cohort.

**Exit gate**

The product owner approves the MVP, the BI standards owner approves the first rule candidates, and infrastructure owners confirm a feasible demo path. If access is not yet available, the agreed sample-data fallback is documented.

### Phase 1 — Base application and local data foundation

**Purpose:** Establish a reproducible, low-cost application foundation connected to the existing SQL Server.

**Build**

- Create the ASP.NET Core application using the organization-approved .NET 10 LTS SDK/runtime.
- Configure the local developer connection outside source control. For this workspace, the connection is held in .NET user-secrets and uses `Server=RAMADAS;Database=BiAgent;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5`.
- Organize the code into clear application, review-domain, integration, and persistence boundaries without splitting into separately deployed services.
- Add a database health check and a minimal schema/migration process.
- Configure SQL Server access using Windows Integrated Authentication for the approved local/demo identity.
- Add structured logging, correlation IDs, request validation, and consistent error responses.
- Add a simple demo landing page or API endpoint that reports readiness without exposing configuration secrets.
- Add automated build and test commands that another developer can run from a clean checkout.
- Document local setup, SQL permissions, configuration, startup, and common troubleshooting.

**Definition of done**

- A second developer can follow the setup guide and run the app without copying secrets from another person's machine.
- The application connects to the designated SQL Server using the intended Windows identity.
- Health/readiness checks clearly distinguish application availability from database connectivity.
- Database schema changes are repeatable and tested; startup does not silently swallow database errors.
- No real credentials or sensitive customer artifacts are committed.

**Exit gate**

The app can start, report dependency health, and persist/read a harmless test record using the intended demo configuration.

### Phase 2 — Review MVP and first deterministic rules

**Purpose:** Demonstrate useful review value without requiring cloud AI or production tenant access.

**Build**

- Implement intake for PBIP, standalone PBIR, and PBIT with explicit format-specific limitations. PBIT extraction requires a configured, company-approved pbi-tools executable; do not claim support for untested serialization variants. Reject PBIX.
- Extract only the minimum metadata required by the agreed checks.
- Create structured findings with stable rule ID, rule-set version, severity, affected object, evidence, explanation, and suggested remediation.
- Implement a small initial rule set selected from real BI standards and validated against representative sample artifacts.
- Include clear states for completed review, unsupported input, invalid input, and review failure.
- Provide a simple UI/API to select or submit a sample, start a review, and inspect findings.
- Store the review summary and findings in SQL Server if persistence is needed by the approved use cases.

**Acceptance criteria**

- Given a supported sample artifact, the same rule version produces the same deterministic findings on repeated runs.
- Every finding links to a real rule and includes enough evidence for a developer to verify it.
- Unsupported or malformed input results in a clear, non-success response and is logged without leaking sensitive content.
- The demo can complete its primary scenario with AI, Teams, and production tenant access disabled.
- The BI standards owner reviews the rule definitions and expected results for the test set.

**Exit gate**

The product owner and BI standards owner accept the primary scenario and agree that findings are sufficiently understandable for pilot feedback.

### Phase 3 — Evaluation, usability, and controlled pilot

**Purpose:** Learn from real developer use before introducing enforcement or expanding integrations.

**Build and evaluate**

- Assemble a representative, approved test set covering normal, edge, invalid, and known-issue examples.
- Record expected results and review rule changes through pull requests/peer review.
- Add reviewer feedback for actionable, incorrect, duplicate, or not-applicable findings.
- Improve severity guidance, remediation language, and UI flow based on observed usage.
- Optionally add AI explanations behind a feature flag, using an approved service only. Keep the rule result authoritative and expose when text is AI-generated.
- Measure pilot metrics and report results to the product owner.
- Run a lightweight security/privacy review before processing sensitive company artifacts.

**Pilot controls**

- Start with an opt-in cohort and non-blocking advisory results.
- Define who can access pilot reviews and who can view stored findings.
- Agree artifact retention/deletion and incident escalation procedures.
- Provide a feedback channel and a named owner for triaging false positives.

**Exit gate**

Pilot stakeholders review the agreed success measures and decide whether to fix, expand, pause, or retire the MVP. No release-blocking rule is enabled solely because it exists in the rule engine.

### Phase 4 — Company identity and enterprise integrations

**Purpose:** Complete deployment approval for the implemented app identity foundation and add enterprise integrations only when tenant ownership, permissions, and support are ready.

**Potential work**

- Configure and verify the Entra single-tenant app registration, bootstrap administrator, SQL role tables, and app-wide sign-in in the target environment.
- Register the application through the company’s approved process.
- Add Power BI/Fabric APIs only for documented use cases, with least-privilege permissions and an agreed consent process.
- Add Teams as a user entry point if pilot feedback shows it improves workflow.
- Add a company-approved AI provider if the cost, data handling, and support model are accepted.
- Add audit events and operational monitoring suited to the hosting environment.

**Exit gate**

Security, identity, BI platform, and product owners approve the permission model, data flow, support process, and tenant deployment. Every integration has a documented disabled/offline behavior and test coverage.

### Phase 5 — Azure SQL migration and production rollout

**Purpose:** Move the database and application to approved company infrastructure without losing traceability or operability.

**Migration sequence**

1. Confirm Azure target region, service tier, networking, backup, recovery, and retention requirements with platform owners.
2. Provision Azure SQL through the company’s approved infrastructure process.
3. Configure `ConnectionStrings:BiAgent` for the selected authentication mode. Azure SQL username/password authentication can be used for an approved temporary demo; prefer a managed identity for the deployed production app. Do not reuse a developer's local Windows login or commit credentials.
4. Apply versioned schema migrations to an empty test database and run compatibility tests.
5. If demo/pilot data must be retained, classify it, approve the migration, rehearse export/import, validate row counts and integrity, and document rollback.
6. Deploy to a non-production environment; verify sign-in, authorization, database access, health checks, monitoring, and backup/restore.
7. Run a release-readiness review and a limited production rollout with a named support owner.
8. Retire or restrict the on-prem demo database only after owners approve the migration and retention outcome.

**Exit gate**

The product owner and platform/security owners approve production readiness, monitoring, recovery, support, and rollback evidence.

### Next module roadmap — ArchitectIQ modules

The module names and high-level requirements below come from `ArchitectIQ_Modules_Requirements.docx`. Governance Audit is the selected next module for planning. This roadmap is planning only: it does not authorize implementation, tenant access, or production changes. Estimate sprint durations only after the discovery and access gates are completed.

| Module | DOCX outcome | Planning status |
|---|---|---|
| Governance Audit | Audit workspaces, datasets, reports, ownership, standards, orphan artifacts, failed refreshes, and governance gaps using Power BI API metadata | Selected next; phase plan below |
| Performance Optimization | Identify DAX, dataset-design, cardinality, refresh, and query-performance bottlenecks | Later module; scope and input sources to be prioritized |
| Release Readiness | Assess naming, RLS, documentation, refresh validation, security, and deployment readiness | Later module; define how it consumes governance and review evidence |
| Documentation Generator | Generate technical design documents, data dictionaries, measure catalogs, release notes, and architecture documentation | Later module; agree output formats and descriptions source |
| Architecture Review | Assess semantic-model design, scalability, security, reusability, standards, and architectural maturity | Later module; define scored criteria and evidence |
| BI Delivery Manager | Turn business requirements into stories, semantic models, KPIs, measures, visuals, and estimates | Later module; define supported workflow and human approval points |

The remaining module order is not assumed. After the Governance Audit discovery gate, the product owner should choose the next module using user value, data/access readiness, dependencies, and delivery effort. Performance Optimization and Architecture Review may share model metadata, but should not be combined without confirming a coherent user outcome.

### Governance Audit — proposed phased delivery

**Module objective:** Provide a read-only, evidence-backed view of Power BI workspace, dataset, report, ownership, refresh, and governance status. Begin as advisory inventory and findings; do not automatically change, delete, refresh, or reassign tenant assets.

#### GA-0 — Discovery and access readiness

- Identify the tenant, pilot workspaces, environment boundaries, business owner, BI governance owner, security contact, and Power BI platform administrator.
- Agree the smallest useful pilot scope: selected workspaces versus tenant-wide inventory, supported asset types, refresh-history window, synchronization cadence, and intended users.
- Define auditable meanings for “orphan artifact,” “failed refresh,” “ownership gap,” “standards violation,” and each governance gap; identify required evidence and acceptable exceptions.
- Map each required metadata field to the relevant Power BI API and confirm API availability, tenant settings, delegated/application permission options, admin consent, licensing/capacity constraints, paging, throttling, and API limits with the tenant owner.
- Classify workspace names, artifact metadata, ownership details, and refresh information; approve access, retention, visibility, and data handling.
- Confirm authentication and authorization design, least privilege, tenant isolation, audit requirements, and network/hosting path before connecting to a real tenant.

**Deliverables:** approved module scope, API/permission matrix, data-flow and classification notes, definitions for proposed checks, pilot workspace list, and acceptance criteria.

**Exit gate:** Power BI/security owners approve the read-only access model and pilot scope; every proposed check has an owner, definition, and verifiable metadata source.

#### GA-1 — Secure Power BI metadata connection

- Implement a read-only Power BI API adapter behind an application boundary; keep API-specific request and response shapes out of the audit domain.
- Use the approved Entra identity and least-privilege permissions. Store credentials/secrets only in an approved secret store; do not expose tokens or tenant secrets in logs or the UI.
- Handle pagination, throttling, transient failures, cancellation, partial responses, and API timeouts explicitly; surface incomplete scans as incomplete rather than successful full audits.
- Add a connection/readiness diagnostic that reports safe status and last successful access without returning sensitive tenant configuration.
- Test with mocked API responses and an approved non-production tenant before any pilot scan.

**Exit gate:** security review accepts the permissions and token handling; automated tests cover pagination, throttling, errors, and incomplete data; the tenant owner confirms a successful least-privilege read-only test.

#### GA-2 — Inventory and relationships

- Retrieve the approved workspace, dataset, report, owner, and refresh metadata within the agreed scope.
- Normalize identifiers, names, workspace membership, dataset/report relationships, ownership, refresh status, and source timestamps into a stable internal inventory model.
- Establish snapshot/run identity, scan time, source freshness, and completeness state so users can distinguish current data from stale or partial results.
- Define incremental versus full synchronization, duplicate handling, deletion detection, and minimal persistence/retention with the data owner before storing inventory.
- Validate inventory counts and relationships against the Power BI service for the pilot workspaces.

**Exit gate:** representative inventory reconciles with service-side evidence; missing permissions, stale metadata, and partial scans are visible and cannot be mistaken for a clean audit.

#### GA-3 — Governance checks and findings

- Implement only the checks approved in GA-0, initially covering standards violations, orphan artifacts, failed refreshes, and ownership/governance gaps where reliable metadata exists.
- Give each check a stable identifier and version, owner, severity, definition, evidence source, and explanation; keep outcomes advisory and reproducible.
- Record evidence timestamps and explain unavailable or insufficient evidence rather than inferring a violation.
- Define exception/suppression ownership and auditability before allowing exceptions to hide findings.
- Evaluate each check against expected results assembled by the BI governance owner, including valid exceptions and incomplete metadata cases.

**Exit gate:** the governance owner accepts expected results and false-positive handling; each finding is traceable to API evidence and a versioned check.

#### GA-4 — Audit experience and controlled pilot

- Provide a Governance Audit workspace view with scan status, data freshness, inventory totals, findings, filters, and drill-through to affected workspace/artifact evidence.
- Make incomplete scans, API failures, stale snapshots, and permission gaps prominent; do not report them as a clean audit.
- Add export or reporting only after agreeing its audience, data sensitivity, and format.
- Pilot with a small opt-in group using read-only permissions and advisory findings; collect actionable/incorrect/not-applicable feedback.
- Measure scan completion, API failures/throttling, finding actionability, false positives, and audit time saved against a recorded baseline.
- Complete security/privacy review, operational runbook, retention/deletion checks, support ownership, and a product-owner decision to expand, revise, pause, or retire the module.

**Exit gate:** product, BI governance, security, and platform owners accept pilot evidence, support process, and any expansion of workspace scope. No finding automatically blocks a release or changes Power BI assets.

**Dependencies:** GA-0 precedes real tenant access; GA-1 is required before live inventory; GA-2 supplies the evidence for GA-3; GA-3 is required before a meaningful pilot in GA-4. App-wide Entra sign-in is implemented; tenant Power BI permission approval and Governance Audit authorization decisions remain cross-cutting prerequisites.

### Later module planning gates

Plan subsequent DOCX modules as separate workstreams after Governance Audit discovery. For each selected module, complete a scope/data-source/permission discovery gate, prove a representative input path, agree expected outcomes and human approval points, then implement a narrow MVP, validate it with an approved sample set, and decide whether to expand. Reuse shared metadata adapters only when the data contracts and permission boundaries match; do not assume that one module's API permission or stored inventory is sufficient for another module.

## 7. Initial backlog for BA and development team

These are backlog candidates, not yet estimated or approved. Refine them into small stories during Phase 0.

| ID | Candidate story | Acceptance evidence |
|---|---|---|
| B-01 | As a BA, I can document the current review workflow and pain points so the MVP solves a real problem. | Current-state workflow, actors, pain points, and baseline measures reviewed by stakeholders |
| B-02 | As a BI standards owner, I can approve the first review rules and severity definitions. | Rule catalogue identifies owner, rationale, severity, evidence, and remediation for every MVP rule |
| D-01 | As a developer, I can configure the app for the existing SQL Server without putting credentials in source control. | Clean-checkout setup succeeds with Windows Integrated Authentication and documented permissions |
| D-08 | As an operator, I can switch Azure SQL authentication without changing application code. | `ConnectionStrings:BiAgent` supports a protected SQL-auth secret for a temporary demo and managed identity for production; both modes pass readiness and review/feedback workflow checks |
| D-02 | As an operator, I can tell whether the app and database are healthy. | Health checks report dependency status without exposing connection details |
| D-03 | As a developer, I can review a supported sample artifact. | Primary scenario returns findings that match the approved expected-results test set |
| D-04 | As a reviewer, I can understand and verify each finding. | Finding includes rule/version, severity, affected object, evidence, and remediation |
| D-05 | As a pilot user, I can mark a finding useful or incorrect. | Feedback is recorded with access controls and can be reported without storing unnecessary artifact content |
| D-06 | As an operator, I can disable optional integrations per environment. | AI/Teams/API features remain off when not configured and fail clearly if deliberately enabled without required settings |
| D-07 | As a release owner, I can deploy schema changes consistently. | Versioned migration applies to fresh test SQL Server and Azure SQL databases, or documented incompatibilities are resolved |
| GA-01 | As a BI governance owner, I can define audit scope, terms, and expected checks before tenant integration. | Approved pilot scope, definitions, owners, API/permission matrix, and check acceptance criteria |
| GA-02 | As a security/platform owner, I can approve least-privilege read-only Power BI API access. | Entra permission review and a successful non-production connection test without secrets or tokens in logs |
| GA-03 | As a governance auditor, I can see a complete and fresh workspace/artifact inventory. | Inventory reconciles against approved service-side evidence and clearly marks stale, partial, or failed scans |
| GA-04 | As a governance owner, I can review evidence-backed findings for approved governance checks. | Expected-result tests cover standards, orphan artifacts, failed refreshes, ownership gaps, and insufficient evidence |
| GA-05 | As a pilot user, I can inspect audit status and filter findings without changing tenant assets. | Pilot UI shows scan freshness/completeness, evidence, and advisory findings; no automated tenant changes occur |

## 8. Definition of Ready and Definition of Done

### Definition of Ready

A story is ready when:

- The user/persona and business outcome are clear.
- Scope and exclusions are stated.
- Acceptance criteria are observable and testable.
- Data sensitivity and access implications are identified.
- Dependencies and decision owners are known.
- UX, sample input, or rule examples are available where applicable.
- The story is small enough to implement and review within a sprint.

### Definition of Done

A story is done when:

- Acceptance criteria pass and relevant automated tests are added.
- Code is peer-reviewed and follows the agreed conventions.
- Errors are explicit and logged safely; sensitive values are not logged.
- Configuration is externalized and documented.
- Security and privacy implications are reviewed at the story's risk level.
- User-facing behavior and directly related documentation are updated.
- A demo or test evidence is attached to the story.
- Product/BI owner accepts the outcome where acceptance is required.

## 9. Quality, security, and operational standards

### Testing

- Unit-test deterministic rule behavior, input validation, and severity mapping.
- Integration-test database repositories and schema migrations against SQL Server.
- Test Azure SQL compatibility before migration; SQL Server compatibility does not guarantee every feature behaves identically.
- Maintain a small golden test set of artifacts/metadata with reviewed expected findings.
- Test invalid, oversized, unsupported, and partially readable inputs.
- If AI is introduced, evaluate it separately for groundedness, correctness, unsafe recommendations, and regressions. Do not use AI output as a substitute for rule-engine tests.

### Security and privacy

- Apply least privilege to the app's SQL identity and any Power BI/Fabric permissions.
- Separate user sign-in from the application's database identity.
- Use approved secret storage for secrets; never log tokens, secrets, full connection strings, or unnecessary report data.
- Establish data classification, retention, deletion, and access controls before processing non-sample data.
- Treat uploaded files and extracted metadata as untrusted input; validate type and size and handle parser failures safely.
- Keep review results advisory until policy owners approve a specific rule and exception process for enforcement.

### Operations

- Provide structured logs with a correlation ID for each review.
- Monitor application errors, review duration, database failures, and AI/API usage if enabled.
- Define ownership for support, rule changes, configuration changes, and incidents.
- Document startup, deployment, database migration, backup/restore responsibilities, and rollback.
- Avoid broad exception catches or success-shaped fallback results. A failed review must not look like a clean review.

## 10. BA working notes and templates

### Stakeholder questions for Phase 0

1. Which review task wastes the most time or causes the most rework today?
2. Who submits a review, who consumes findings, and who can approve exceptions?
3. What is the first real user scenario the demo must show?
4. Which BI standards are already approved and who owns updates to them?
5. Which formats are in scope for the first review (for example, a defined metadata export or a verified Power BI project format)?
6. Are artifacts or metadata sensitive, regulated, or restricted from external AI services?
7. Who owns the SQL Server instance, database creation, Windows identity, and permission approval?
8. What is the approved demo host and who can access it?
9. What evidence would make stakeholders trust a finding?
10. What pilot outcome would justify Phase 4 or Phase 5 investment?

### Suggested user-story format

> As a **[persona]**, I want **[capability]**, so that **[measurable outcome]**.

Include:

- Context and business rule
- In-scope and out-of-scope behavior
- Preconditions and data classification
- Acceptance criteria in Given/When/Then form
- Error/edge cases
- Audit, privacy, and retention notes
- Dependencies, owner, and test evidence

### Example acceptance criteria

> **Given** a supported sample with an object that violates an approved naming rule,  
> **when** the user runs a review using the current rule-set version,  
> **then** the result includes the correct rule ID, severity, affected object, evidence, and remediation;  
> **and** a repeat run with the same input and rule version returns the same deterministic finding.

## 11. Risks and mitigations

| Risk | Impact | Mitigation / owner |
|---|---|---|
| Blueprint contains headings but not implementable detail | Scope churn and false confidence in estimates | Use this plan as a starting baseline; BA and product owner approve real stories/rules in Phase 0 |
| SQL Server identity works locally but not on the demo host | App cannot connect after deployment | Verify the actual runtime identity and network path early; SQL/platform owner provides least-privilege access |
| Artifact parsing or metadata access is more limited than expected | Core review scope slips | Run a Phase 0/1 spike with representative samples; explicitly document supported formats and fallbacks |
| False positives reduce developer trust | Low adoption and pressure to disable checks | Begin advisory-only, test representative artifacts, collect feedback, version rules, and require owner approval |
| AI output is incorrect or exposes restricted content | Incorrect advice, privacy, or compliance concern | Keep AI optional; use only an approved provider; ground explanations in findings/standards; apply content and evaluation controls |
| On-prem-to-Azure migration is treated as a connection-string-only change | Runtime or data migration failure | Version schema; test migrations and queries on Azure SQL; rehearse data migration and rollback |
| Enterprise permissions or approvals arrive late | Integration schedule slips | Track each approval as a dependency with a named owner; demo core workflow without production tenant access |
| Scope expands to all blueprint engines at once | MVP is delayed and hard to validate | Prioritize one workflow and a small rule set; defer Teams, Fabric breadth, scoring, and release blocking until validated |

## 12. Decisions to record before implementation

| Decision | Proposed default | Owner/status |
|---|---|---|
| First demo workflow | Review a PBIP project and return basic structural validations and deterministic findings | Agreed; refine with product owner |
| Supported input format | PBIP/PBIR ZIP upload (preferred) or direct PBIT upload; PBIX rejected; PBIT requires compatible pbi-tools | Upload intake implemented; PBIT extraction and target-host compatibility remain to validate |
| Next module | Governance Audit using Power BI workspace, dataset, report, ownership, and refresh metadata | Selected for planning; discovery and access approvals remain |
| Governance Audit access | Read-only Entra identity with least-privilege approved API permissions | Tenant/security/platform owners — TBD |
| Governance terms | Auditable definitions for orphan, failed refresh, ownership gap, and standards violation | BI governance owner — TBD |
| Next module after Governance Audit | Prioritize remaining DOCX modules using value, data readiness, dependencies, and effort | Product owner — TBD |
| Demo hosting | Windows developer machine or approved Windows host with SQL reachability | Platform/SQL owner — TBD |
| SQL database permissions | Least privilege required for app schema and records | SQL owner — TBD |
| Review history retention | Store minimal metadata; agree period before pilot data | Product/security owner — TBD |
| First rules | Sample starter checks agreed: naming, four sample-only required measures, relationship endpoint/star-schema checks, and static DAX practices | Agreed for demo; company rules remain TBD |
| Sample-only required measures | Total Sales; Total Quantity Sold; Total Product Cost; Total Profit | Agreed for current sample only |
| AI in demo | Disabled by default; optional only after approval | Product/security owner — TBD |
| Teams integration | Deferred until pilot value and tenant approval are established | Product owner — TBD |
| Release enforcement | Advisory only until specific rules and exceptions are governed | BI governance owner — TBD |
| Azure target and identity | Azure SQL with organization-approved Entra-based identity where supported | Platform/security owner — TBD |

## 13. Immediate next steps

1. Assign a product owner, BA, BI governance owner, Power BI tenant/platform administrator, security contact, and development lead for Governance Audit discovery.
2. Complete Governance Audit phase GA-0: select pilot workspaces; define orphan, ownership, refresh-failure, and standards-gap terms; classify the metadata; and agree measurable acceptance criteria.
3. Obtain the tenant owner’s API/Entra permission matrix and approve the read-only identity, consent, environment, and network path before any live tenant connection.
4. Prepare representative mocked API responses and, after approval, a non-production tenant test plan covering paging, throttling, incomplete scans, permissions, and reconciliation.
5. Prioritize the initial Governance Audit checks and evidence sources with the BI governance owner; keep findings advisory and disallow automatic asset changes.
6. Turn GA-0 deliverables into sprint-sized stories with dependencies and exit criteria. Do not begin live integration before its access/readiness gate is approved.
7. After Governance Audit discovery, ask the product owner to prioritize the next module from the DOCX using business value, data readiness, dependencies, and effort.

## 14. Scope exclusions for the initial demo

Unless Phase 0 explicitly changes the scope, the demo does **not**:

- Publish, modify, or delete Power BI reports, semantic models, or Fabric items.
- Block production releases or make autonomous governance decisions.
- Require Azure hosting, Teams, Fabric APIs, or an AI model to demonstrate core value.
- Support every Power BI file format or every tenant configuration.
- Store full report contents or secrets in the application database.
- Claim production readiness, enterprise security approval, or cost savings before those are validated.
