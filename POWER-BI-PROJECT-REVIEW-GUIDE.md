# Power BI Project Review: Business and Decision Brief

## Executive review panel

| Review area | Assessment |
|---|---|
| Purpose | Apply the organization's Power BI modelling and project standards consistently, then give reviewers one place to inspect findings and record decisions. |
| Business value | Reduce repetitive manual checks, make standards explicit and configurable, and retain review outcomes for follow-up and continuous improvement. |
| Best-fit use | An internal, standards-driven review and feedback workflow for Power BI projects, especially when teams want their own rules and severities rather than relying only on a generic checklist. |
| Current maturity | Working pilot. Core upload, review, configuration, SQL history, and feedback flows are implemented; organizational security controls still need to be completed before broad exposure. |
| Recommendation | Continue a controlled pilot with non-sensitive sample projects. Restrict access to a trusted network and complete the security actions in this brief before organization-wide use. |

## Why this application

Power BI reviews can involve repeated checks of naming, model structure, relationships, measures, and DAX practices. When checks and feedback are handled manually or in separate tools, standards may be applied inconsistently and review decisions can be difficult to trace.

BI Sentinel brings those activities into a single workflow. It accepts a project upload, evaluates it against centrally configured review rules, presents findings with severity, and lets reviewers submit feedback linked to the finding. The application is advisory: it helps a person review a project and does not edit or publish the Power BI artifact.

## Market positioning

The strongest case for BI Sentinel is **fit to the organization's review process**, rather than a claim that it is universally better than every product in the market. A formal competitor benchmark has not been performed.

Its differentiating combination in this pilot is:

- Organization-configurable naming expressions, model conventions, required measures, rule enablement, and finding severity.
- Upload-based review of supported PBIP projects and PBIT templates, with PBIR structural checks when no semantic model is present.
- Finding-level reviewer feedback, a Waiting/Closed work queue, and searchable saved review history.
- Relational SQL storage for configuration, audit metadata, review summaries, findings, and submitted feedback.
- An advisory workflow that leaves the submitted Power BI project unchanged.

This makes the application a candidate for an **internal, policy-led review and feedback panel**. It should complement—not be presented as a proven replacement for—Power BI authoring tools, specialist model-editing utilities, or enterprise governance products. Selection against those products should be based on a separately agreed feature, security, cost, and operational comparison.

## What is covered today

### Project intake and review

The review workspace is available at `/reviews`. Select **Power BI Project Review** in the left navigation to open it.

- **PBIP project ZIP (preferred):** ZIP the complete project folder and preserve its folders and referenced files. A `.pbip` descriptor by itself is not a complete project.
- **PBIT template:** upload the template directly. The review host must have the compatible pbi-tools executable and Power BI dependencies available.
- **PBIR report ZIP:** a standalone report can be checked for report structure; model-specific rules are not run when there is no semantic model.
- Uploads are limited to 500 MB. PBIX files are not accepted.
- A stable project ID is optional. When it is not supplied, the uploaded file name is used.

The review service processes the upload temporarily. It does not retain the original project as a project artifact.

### Findings and feedback

The result summary reports the project name, number of findings, and counts of model tables, measures, and relationships checked. Reviewers can filter by severity and search finding text. Findings are advisory and include the applicable rule, location, or recommendation where available.

Each finding supports reviewer feedback and an optional note. **Submit feedback** records the response; a submitted item can be reopened. The dashboard separates saved review runs into **Waiting** and **Closed**. A run is Closed when all its findings have submitted feedback. Reviewers can search by project name to load a prior run.

Review summaries, findings, and submitted feedback are saved in the configured SQL database and retained for 30 days in this pilot. The uploaded project is not stored in that history. Unsubmitted feedback is held only in the current page session; if SQL is unavailable, use **Export feedback** before leaving the page. The browser-generated reviewer ID is not company sign-in.

## How review standards are configured

Open **Administration**, then select **Power BI Project Review**. The tab currently groups the configurable review behavior into three sections.

### Naming rules

Configure four .NET regular expressions:

| Setting | What it checks |
|---|---|
| Table name regex | Names of semantic-model tables |
| Column name regex | Names of model columns |
| Measure name regex | Names of measures |
| Placeholder column regex | Default-style placeholder column names |

Patterns ignore letter case. Use `^` at the start and `$` at the end to require the entire name to match. For example:

- `^[A-Z][A-Za-z0-9]*$` allows a name that starts with a letter and continues with letters or digits, such as `DimCustomer`.
- `^[A-Z][A-Za-z0-9]*( [A-Z][A-Za-z0-9]*)*$` allows title-cased words separated by single spaces, such as `Total Sales`.
- `^Column\d*$` identifies `Column` followed by zero or more digits, such as `Column` or `Column12`.

The page includes expandable regex guidance. Invalid expressions are rejected when saving.

### Model conventions

- **Canonical measure table name:** the expected name for the dedicated measure container.
- **Measure table candidates:** alternative table names that are accepted as measure containers; enter one per line.
- **Fact table prefix** and **Dimension table prefix:** conventions used by the relationship checks.
- **Generated table prefixes:** prefixes used to identify generated tables that should be excluded from selected checks; enter one per line.
- **Required measures:** measure names expected to exist in every reviewed semantic model; enter one per line.
- **Recommend DIVIDE for division in DAX:** controls whether direct division in DAX is flagged.
- **Warn when FORMAT is used in DAX:** controls whether use of `FORMAT` in measure expressions is flagged.

### Built-in rule policies

For each registered rule, select whether it is enabled and choose the finding severity: **Error**, **Warning**, or **Information**. Current rules cover project structure, table/column/measure naming, placeholder columns, measure-table conventions, required measures, relationship validity and direction, star-schema conventions, implicit numeric columns, DAX division, and DAX `FORMAT` usage.

Configuration can turn existing rules on or off and change their severity. Adding or changing the underlying rule logic requires an application code change.

## Save behavior

Use **Save review settings** to save only review standards and policies. Settings are validated and saved to SQL in a transaction. The page shows the new configuration version and update time. Changes apply to new reviews and do not alter findings saved from earlier runs. A stale save is rejected if the configuration has changed since it was loaded; reload before trying again.

## Organization security and risk review

The application handles uploaded Power BI project content and stores review findings and feedback. Treat it as an internal business application and review its access, data handling, and hosting controls before wider use.

| Area | Current behavior or protection | Organization consideration |
|---|---|---|
| Sign-in and administration | The pilot Administration API does not require sign-in or an administrator role. | Anyone who can reach the app may change review policy. Keep it on a trusted network until company sign-in and administrator authorization are enforced. |
| Uploaded project files | The upload is processed temporarily and the original project is not retained as a project artifact. Uploads are capped at 500 MB; archive extraction has a separate 512 MB expanded-output limit and file/path safety checks. | Project files can contain confidential business logic, names, and metadata. Use approved test data for the pilot and verify temporary-storage cleanup and hosting access. |
| Saved review records | Review summaries, findings, and submitted feedback are written to SQL. Pilot retention is 30 days. | Findings and comments can disclose project names, object names, locations, and internal standards. Confirm data classification, database access, backup/retention, and who may read these records. |
| Feedback identity | The browser-generated reviewer ID identifies a browser and is not company identity. | Do not treat feedback attribution as an authenticated or non-repudiable employee identity. |
| Database and secrets | Database credentials are supplied through deployment configuration rather than committed to source. Configuration writes are versioned, transactional, and audited in relational tables. | Use the organization's approved secret store, encrypted connections, least-privilege SQL permissions, and restricted network connectivity. Validate these in the target environment. |
| File extraction dependency | PBIT processing uses the bundled Windows pbi-tools executable. | Maintain and patch the dependency, review its license and supply-chain posture, and run representative security checks in the deployment environment. |

### Readiness actions before broader rollout

- Require company authentication and an administrator role for configuration changes.
- Restrict network access to approved users and deployment paths; do not expose the pilot administration surface publicly.
- Confirm SQL least-privilege permissions, secret management, encryption, backup, and retention with the platform owner.
- Confirm temporary upload/extraction storage is isolated, has sufficient capacity, and is cleaned after both successful and failed reviews.
- Approve the handling of project-derived findings and reviewer comments under the organization's data-classification policy.
- Complete a security and dependency review of the deployed application and its bundled extraction tool.

## Overall assessment

BI Sentinel's current value is a configurable, traceable review loop: apply internal Power BI standards, show actionable findings, capture reviewer decisions, and retain results for follow-up. This is a credible pilot proposition when teams need a shared internal review process. It is not yet a basis for claiming market-wide superiority or organization-wide security readiness. Proceed with a controlled pilot; broaden adoption after the readiness actions above are completed and the organization validates the feature fit against its existing tools.
