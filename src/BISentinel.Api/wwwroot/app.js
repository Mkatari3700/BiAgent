const reviewerStorageKey = "bi-sentinel.reviewerId.v1";
const legacyReviewerStorageKey = "bitla.reviewerId.v1";

const elements = {
  workspaceLayout: document.querySelector("#workspace-layout"),
  workspaceLoading: document.querySelector("#workspace-loading"),
  sidebarToggle: document.querySelector("#sidebar-toggle"),
  accountControls: document.querySelector("#account-controls"),
  accountName: document.querySelector("#account-name"),
  reviewsNavigation: document.querySelector("#reviews-navigation"),
  administrationNavigation: document.querySelector("#administration-navigation"),
  moduleNavigations: [...document.querySelectorAll("[data-module-navigation]")],
  moduleNavigationStatus: document.querySelector("#module-navigation-status"),
  reviewWorkspace: document.querySelector("#review-workspace"),
  governanceWorkspace: document.querySelector("#governance-workspace"),
  runGovernanceScan: document.querySelector("#run-governance-scan"),
  governanceStatus: document.querySelector("#governance-status"),
  governanceError: document.querySelector("#governance-error"),
  governanceResults: document.querySelector("#governance-results"),
  governanceWorkspaceRows: document.querySelector("#governance-workspace-rows"),
  governanceFindingRows: document.querySelector("#governance-finding-rows"),
  governanceNoFindings: document.querySelector("#governance-no-findings"),
  governanceAccessRows: document.querySelector("#governance-access-rows"),
  governanceArtifactRows: document.querySelector("#governance-artifact-rows"),
  governanceHistoryRows: document.querySelector("#governance-history-rows"),
  governanceHistoryEmpty: document.querySelector("#governance-history-empty"),
  governanceConfigStatus: document.querySelector("#governance-config-status"),
  governanceConfigError: document.querySelector("#governance-config-error"),
  governanceConfigVersion: document.querySelector("#governance-config-version"),
  governanceWorkspaceIds: document.querySelector("#governance-workspace-ids"),
  discoverGovernanceWorkspaces: document.querySelector("#discover-governance-workspaces"),
  governanceWorkspaceDiscovery: document.querySelector("#governance-workspace-discovery"),
  useGovernanceWorkspaceSelection: document.querySelector("#use-governance-workspace-selection"),
  saveGovernanceConfiguration: document.querySelector("#save-governance-configuration"),
  modulePlaceholder: document.querySelector("#module-placeholder"),
  modulePlaceholderEyebrow: document.querySelector("#module-placeholder-eyebrow"),
  modulePlaceholderTitle: document.querySelector("#module-placeholder-title"),
  modulePlaceholderDescription: document.querySelector("#module-placeholder-description"),
  modulePlaceholderStatus: document.querySelector("#module-placeholder-status"),
  adminConfiguration: document.querySelector("#admin-configuration"),
  adminTabs: [...document.querySelectorAll("[data-admin-tab]")],
  adminPanels: [...document.querySelectorAll("[data-admin-panel]")],
  applicationUsersStatus: document.querySelector("#application-users-status"),
  applicationUsersError: document.querySelector("#application-users-error"),
  applicationUsersRows: document.querySelector("#application-users-rows"),
  applicationUsersEmpty: document.querySelector("#application-users-empty"),
  configurationForm: document.querySelector("#configuration-form"),
  configurationStatus: document.querySelector("#configuration-status"),
  configurationError: document.querySelector("#configuration-error"),
  configurationVersion: document.querySelector("#configuration-version"),
  configurationUpdated: document.querySelector("#configuration-updated"),
  moduleAvailabilityList: document.querySelector("#module-availability-list"),
  rulePolicyList: document.querySelector("#rule-policy-list"),
  reloadConfiguration: document.querySelector("#reload-configuration"),
  saveConfigurationButtons: [...document.querySelectorAll("[data-configuration-section]")],
  projectId: document.querySelector("#project-id"),
  projectZip: document.querySelector("#project-zip"),
  uploadProject: document.querySelector("#upload-project"),
  reviewSearchForm: document.querySelector("#review-search-form"),
  reviewSearch: document.querySelector("#review-search"),
  history: document.querySelector("#review-history"),
  refreshDashboard: document.querySelector("#refresh-dashboard"),
  waitingTab: document.querySelector("#waiting-tab"),
  closedTab: document.querySelector("#closed-tab"),
  waitingCount: document.querySelector("#waiting-count"),
  closedCount: document.querySelector("#closed-count"),
  dashboardRows: document.querySelector("#dashboard-rows"),
  dashboardEmpty: document.querySelector("#dashboard-empty"),
  exportButton: document.querySelector("#export-feedback"),
  status: document.querySelector("#status"),
  error: document.querySelector("#error"),
  reviewErrorDialog: document.querySelector("#review-error-dialog"),
  reviewErrorMessage: document.querySelector("#review-error-message"),
  closeReviewError: document.querySelector("#close-review-error"),
  reviewSuccessDialog: document.querySelector("#review-success-dialog"),
  reviewSuccessMessage: document.querySelector("#review-success-message"),
  openReviewResults: document.querySelector("#open-review-results"),
  reviewResults: document.querySelector("#review-results"),
  overview: document.querySelector("#overview"),
  project: document.querySelector("#project-name"),
  findingsCount: document.querySelector("#finding-count"),
  tables: document.querySelector("#table-count"),
  measures: document.querySelector("#measure-count"),
  relationships: document.querySelector("#relationship-count"),
  list: document.querySelector("#findings-list"),
  empty: document.querySelector("#empty-state"),
  resultCount: document.querySelector("#result-count"),
  severity: document.querySelector("#severity-filter"),
  search: document.querySelector("#search-filter")
};

let currentFindings = [];
let currentProject = "";
let currentReviewRunId = "";
let feedback = {};
let sharedFeedbackAvailable = false;
let dashboardItems = [];
let dashboardStatus = "Open";
let configurationVersion = 0;
let savedConfiguration = null;
let governanceConfigurationVersion = 0;
let moduleAvailability = null;
let activeWorkspaceView = "reviews";
let applicationSession = null;
const reviewerId = loadReviewerId();

elements.sidebarToggle.addEventListener("click", toggleSidebar);
for (const navigation of elements.moduleNavigations) {
  navigation.addEventListener("click", event => {
    event.preventDefault();
    navigateWorkspace(navigation.dataset.moduleNavigation);
  });
}
elements.administrationNavigation.addEventListener("click", event => {
  event.preventDefault();
  navigateWorkspace("administration");
});
for (const tab of elements.adminTabs) {
  tab.addEventListener("click", event => {
    event.preventDefault();
    navigateAdministrationTab(tab.dataset.adminTab);
  });
}
document.querySelector(".admin-tabs").addEventListener("keydown", handleAdministrationTabKeydown);
window.addEventListener("popstate", routeWorkspaceFromLocation);
elements.reloadConfiguration.addEventListener("click", loadReviewConfiguration);
elements.runGovernanceScan.addEventListener("click", runGovernanceScan);
elements.discoverGovernanceWorkspaces.addEventListener("click", discoverGovernanceWorkspaces);
elements.useGovernanceWorkspaceSelection.addEventListener("click", useGovernanceWorkspaceSelection);
elements.saveGovernanceConfiguration.addEventListener("click", saveGovernanceConfiguration);
elements.configurationForm.addEventListener("submit", event => {
  event.preventDefault();
  const activeTab = elements.adminTabs.find(tab => tab.getAttribute("aria-selected") === "true");
  void saveReviewConfiguration(activeTab?.dataset.adminTab);
});
for (const button of elements.saveConfigurationButtons) {
  button.addEventListener("click", () => {
    void saveReviewConfiguration(button.dataset.configurationSection);
  });
}
elements.uploadProject.addEventListener("click", uploadProject);
elements.closeReviewError.addEventListener("click", () => elements.reviewErrorDialog.close());
elements.openReviewResults.addEventListener("click", () => {
  elements.reviewSuccessDialog.close();
  elements.reviewResults.scrollIntoView({ behavior: "smooth", block: "start" });
});
elements.history.addEventListener("change", loadSelectedReview);
elements.reviewSearchForm.addEventListener("submit", searchReviewHistory);
elements.refreshDashboard.addEventListener("click", loadDashboard);
elements.waitingTab.addEventListener("click", () => setDashboardStatus("Open"));
elements.closedTab.addEventListener("click", () => setDashboardStatus("Closed"));
elements.exportButton.addEventListener("click", exportFeedback);
elements.severity.addEventListener("change", renderFindings);
elements.search.addEventListener("input", renderFindings);
void initializeWorkspace();

const modules = [
  {
    key: "reviews",
    route: "/reviews",
    adminRoute: "/admin/reviews",
    title: "Power BI Project Review",
    description: "Review supported Power BI project uploads and inspect findings.",
    status: "This module is available."
  },
  {
    key: "governanceAudit",
    route: "/modules/governance-audit",
    adminRoute: "/admin/governance-audit",
    title: "Governance Audit",
    description: "Inventory approved workspace access, reports, semantic models, and ownership coverage.",
    status: "Selected-workspace audits use your delegated Power BI read access. Sign in to authorize the required scopes before discovering or scanning workspaces."
  },
  {
    key: "performanceOptimization",
    route: "/modules/performance-optimization",
    adminRoute: "/admin/performance-optimization",
    title: "Performance Optimization",
    description: "Assess DAX, dataset design, cardinality, refresh operations, and query performance.",
    status: "Planned module. Performance analysis is not implemented yet."
  },
  {
    key: "releaseReadiness",
    route: "/modules/release-readiness",
    adminRoute: "/admin/release-readiness",
    title: "Release Readiness",
    description: "Assess naming, RLS, documentation, refresh validation, security, and deployment readiness.",
    status: "Planned module. Release readiness checks are not implemented yet."
  },
  {
    key: "documentationGenerator",
    route: "/modules/documentation-generator",
    adminRoute: "/admin/documentation-generator",
    title: "Documentation Generator",
    description: "Generate technical design documents, data dictionaries, measure catalogs, release notes, and architecture documentation.",
    status: "Planned module. Documentation generation is not implemented yet."
  },
  {
    key: "architectureReview",
    route: "/modules/architecture-review",
    adminRoute: "/admin/architecture-review",
    title: "Architecture Review",
    description: "Assess semantic-model design, scalability, security, reusability, standards, and architectural maturity.",
    status: "Planned module. Architecture assessment is not implemented yet."
  },
  {
    key: "biDeliveryManager",
    route: "/modules/bi-delivery-manager",
    adminRoute: "/admin/bi-delivery-manager",
    title: "BI Delivery Manager",
    description: "Translate business requirements into stories, semantic models, KPIs, measures, visuals, and estimates.",
    status: "Planned module. BI delivery planning is not implemented yet."
  }
];

const ruleDescriptions = {
  "project-structure": "Checks required PBIP and PBIR project files.",
  "table-naming": "Checks table names against the configured expression.",
  "column-naming": "Checks column names against the configured expression.",
  "placeholder-column": "Finds default placeholder column names.",
  "measure-table-name": "Checks configured measure-container table names.",
  "measure-naming": "Checks measure names against the configured expression.",
  "required-measures": "Finds configured measures that are missing.",
  "relationship-columns": "Checks relationship endpoints exist in the model.",
  "star-schema-relationships": "Checks configured Fact and Dimension conventions.",
  "single-direction-relationships": "Flags bidirectional relationships.",
  "explicit-measures": "Flags implicit numeric columns.",
  "dax-division": "Recommends DIVIDE for DAX division.",
  "dax-format": "Flags FORMAT calls in measure expressions."
};

function setWorkspaceView(view) {
  const isAdministration = view === "administration";
  const wasAdministration = activeWorkspaceView === "administration";
  const selectedModule = modules.find(module => module.key === view);
  if (!isAdministration && (!selectedModule || moduleAvailability?.[view] !== true)) {
    return;
  }

  activeWorkspaceView = view;
  elements.workspaceLoading.hidden = true;
  const isReviews = view === "reviews";
  elements.reviewWorkspace.hidden = !isReviews;
  const isGovernanceAudit = view === "governanceAudit";
  elements.governanceWorkspace.hidden = !isGovernanceAudit;
  elements.modulePlaceholder.hidden = isAdministration || isReviews || isGovernanceAudit;
  elements.adminConfiguration.hidden = !isAdministration;
  for (const navigation of elements.moduleNavigations) {
    const isCurrent = navigation.dataset.moduleNavigation === view;
    navigation.classList.toggle("active", isCurrent);
    if (isCurrent) {
      navigation.setAttribute("aria-current", "page");
    } else {
      navigation.removeAttribute("aria-current");
    }
  }
  elements.administrationNavigation.classList.toggle("active", isAdministration);
  if (isAdministration) {
    elements.administrationNavigation.setAttribute("aria-current", "page");
  } else {
    elements.administrationNavigation.removeAttribute("aria-current");
  }
  if (selectedModule && !isReviews) {
    elements.modulePlaceholderEyebrow.textContent = "PLANNED MODULE";
    elements.modulePlaceholderTitle.textContent = selectedModule.title;
    elements.modulePlaceholderDescription.textContent = selectedModule.description;
    elements.modulePlaceholderStatus.textContent = selectedModule.status;
  }
  if (isAdministration && !wasAdministration) {
    void loadReviewConfiguration();
  }
  if (isGovernanceAudit) {
    void loadGovernanceAuditHistory();
  }
}

function routeWorkspaceFromLocation() {
  const route = window.location.pathname;
  if (route === "/admin" || route.startsWith("/admin/")) {
    if (applicationSession?.authenticationEnabled && !applicationSession.isAdmin) {
      const availableModule = modules.find(module => moduleAvailability?.[module.key] === true);
      navigateWorkspace(availableModule?.key ?? "reviews", true);
      return;
    }

    setWorkspaceView("administration");
    const activeTab = elements.adminTabs.find(tab => {
      return tab.getAttribute("href") === route;
    });
    setAdministrationTab(activeTab?.dataset.adminTab ?? "workspace");
    return;
  }

  const selectedModule = modules.find(module => module.route === route);
  if (selectedModule && moduleAvailability?.[selectedModule.key] === true) {
    setWorkspaceView(selectedModule.key);
    return;
  }

  const fallbackModule = modules.find(module => moduleAvailability?.[module.key] === true);
  const fallbackView = fallbackModule?.key ?? "administration";
  navigateWorkspace(fallbackView, true);
}

function navigateWorkspace(view, replace = false) {
  if (view === "administration" &&
      applicationSession?.authenticationEnabled &&
      !applicationSession.isAdmin) {
    return;
  }

  const route = view === "administration"
    ? "/admin/workspace"
    : modules.find(module => module.key === view)?.route;
  if (!route) {
    return;
  }

  if (window.location.pathname !== route) {
    if (replace) {
      window.history.replaceState(null, "", route);
    } else {
      window.history.pushState(null, "", route);
    }
  }
  setWorkspaceView(view);
  if (view === "administration") {
    setAdministrationTab("workspace");
  }
}

function navigateAdministrationTab(tabKey, replace = false) {
  const tab = elements.adminTabs.find(item => item.dataset.adminTab === tabKey);
  if (!tab) {
    return;
  }

  const route = tab.getAttribute("href");
  if (window.location.pathname !== route) {
    if (replace) {
      window.history.replaceState(null, "", route);
    } else {
      window.history.pushState(null, "", route);
    }
  }
  setWorkspaceView("administration");
  setAdministrationTab(tabKey);
}

function setAdministrationTab(tabKey) {
  const selectedTab = elements.adminTabs.find(tab => tab.dataset.adminTab === tabKey)
    ?? elements.adminTabs.find(tab => tab.dataset.adminTab === "workspace");
  for (const tab of elements.adminTabs) {
    const isSelected = tab === selectedTab;
    tab.setAttribute("aria-selected", String(isSelected));
    tab.tabIndex = isSelected ? 0 : -1;
    tab.classList.toggle("active", isSelected);
  }
  for (const panel of elements.adminPanels) {
    panel.hidden = panel.dataset.adminPanel !== selectedTab.dataset.adminTab;
  }
  if (selectedTab.dataset.adminTab === "governanceAudit") {
    void loadGovernanceConfiguration();
  } else if (selectedTab.dataset.adminTab === "applicationUsers") {
    void loadApplicationUsers();
  }
}

function handleAdministrationTabKeydown(event) {
  const currentIndex = elements.adminTabs.indexOf(event.target);
  if (currentIndex < 0) {
    return;
  }

  let nextIndex;
  if (event.key === "ArrowRight") {
    nextIndex = (currentIndex + 1) % elements.adminTabs.length;
  } else if (event.key === "ArrowLeft") {
    nextIndex = (currentIndex - 1 + elements.adminTabs.length) % elements.adminTabs.length;
  } else if (event.key === "Home") {
    nextIndex = 0;
  } else if (event.key === "End") {
    nextIndex = elements.adminTabs.length - 1;
  } else {
    return;
  }

  event.preventDefault();
  const nextTab = elements.adminTabs[nextIndex];
  navigateAdministrationTab(nextTab.dataset.adminTab);
  nextTab.focus();
}

async function loadModuleNavigation() {
  elements.moduleNavigationStatus.hidden = true;
  try {
    const response = await fetch("/api/admin/review-configuration", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Module configuration could not be loaded."));
    }

    const result = await response.json();
    renderModuleAvailability(result.configuration.modules);
    elements.workspaceLoading.hidden = true;
    routeWorkspaceFromLocation();
    if (moduleAvailability.reviews) {
      await loadDashboard();
    }
  } catch (error) {
    elements.workspaceLoading.hidden = true;
    for (const navigation of elements.moduleNavigations) {
      navigation.hidden = true;
    }
    elements.reviewWorkspace.hidden = true;
    elements.modulePlaceholder.hidden = false;
    elements.modulePlaceholderEyebrow.textContent = "MODULE SETTINGS UNAVAILABLE";
    elements.modulePlaceholderTitle.textContent = "Workspace modules could not be loaded";
    elements.modulePlaceholderDescription.textContent =
      "The database-backed module configuration is unavailable. Contact an administrator or retry after the configuration database is ready.";
    elements.modulePlaceholderStatus.textContent = "Module tabs are hidden until their database configuration can be read.";
    elements.moduleNavigationStatus.textContent =
      error instanceof Error ? error.message : "Module configuration could not be loaded.";
    elements.moduleNavigationStatus.hidden = false;
  }
}

async function loadApplicationUsers() {
  elements.applicationUsersError.hidden = true;
  elements.applicationUsersStatus.textContent = "Loading users…";
  elements.applicationUsersRows.replaceChildren();
  elements.applicationUsersEmpty.hidden = true;

  try {
    const response = await fetch("/api/admin/application-users", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Users could not be loaded."));
    }

    const users = await response.json();
    for (const user of users) {
      const row = document.createElement("tr");
      const nameCell = document.createElement("td");
      const emailCell = document.createElement("td");
      const firstSeenCell = document.createElement("td");
      const roleCell = document.createElement("td");
      const actionCell = document.createElement("td");
      const roleButton = document.createElement("button");

      nameCell.textContent = user.displayName;
      emailCell.textContent = user.emailAddress || "(not provided)";
      firstSeenCell.textContent = new Date(user.firstSeenUtc).toLocaleString();
      roleCell.textContent = user.isAdmin
        ? (user.isBootstrapAdmin ? "Admin (bootstrap)" : "Admin")
        : "Review user";
      roleButton.type = "button";
      roleButton.className = "button button-secondary";
      roleButton.textContent = user.isAdmin ? "Remove admin" : "Make admin";
      roleButton.disabled = user.isBootstrapAdmin;
      roleButton.addEventListener("click", () => {
        void setApplicationUserAdmin(user, !user.isAdmin, roleButton);
      });
      actionCell.append(roleButton);
      row.append(nameCell, emailCell, firstSeenCell, roleCell, actionCell);
      elements.applicationUsersRows.append(row);
    }

    elements.applicationUsersEmpty.hidden = users.length > 0;
    elements.applicationUsersStatus.textContent =
      `${users.length} user${users.length === 1 ? "" : "s"} have signed in.`;
  } catch (error) {
    elements.applicationUsersStatus.textContent = "Users could not be loaded.";
    elements.applicationUsersError.textContent =
      error instanceof Error ? error.message : "Users could not be loaded.";
    elements.applicationUsersError.hidden = false;
  }
}

async function setApplicationUserAdmin(user, isAdmin, button) {
  button.disabled = true;
  elements.applicationUsersError.hidden = true;
  elements.applicationUsersStatus.textContent = isAdmin
    ? `Granting administrator access to ${user.displayName}…`
    : `Removing administrator access from ${user.displayName}…`;

  try {
    const response = await fetch(
      `/api/admin/application-users/${encodeURIComponent(user.userId)}/admin`,
      {
        method: "PUT",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({ isAdmin })
      });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Application role could not be changed."));
    }

    await loadApplicationUsers();
  } catch (error) {
    button.disabled = false;
    elements.applicationUsersStatus.textContent = "Application role was not changed.";
    elements.applicationUsersError.textContent =
      error instanceof Error ? error.message : "Application role could not be changed.";
    elements.applicationUsersError.hidden = false;
  }
}

function toggleSidebar() {
  const collapsed = elements.workspaceLayout.classList.toggle("sidebar-collapsed");
  elements.sidebarToggle.setAttribute("aria-expanded", String(!collapsed));
  const action = collapsed ? "Expand" : "Collapse";
  elements.sidebarToggle.setAttribute("aria-label", `${action} navigation`);
  elements.sidebarToggle.title = `${action} navigation`;
  elements.sidebarToggle.querySelector("span").textContent = collapsed ? "›" : "‹";
}

async function initializeWorkspace() {
  try {
    await loadAuthenticationSession();
  } catch (error) {
    elements.workspaceLoading.hidden = true;
    elements.modulePlaceholder.hidden = false;
    elements.modulePlaceholderEyebrow.textContent = "SIGN-IN UNAVAILABLE";
    elements.modulePlaceholderTitle.textContent = "Application access could not be verified";
    elements.modulePlaceholderDescription.textContent =
      "The sign-in session or application access database is unavailable. Contact a BI Sentinel administrator.";
    elements.modulePlaceholderStatus.textContent =
      error instanceof Error ? error.message : "Application access could not be verified.";
    return;
  }

  await loadModuleNavigation();
}

async function loadAuthenticationSession() {
  const response = await fetch("/api/auth/session", {
    headers: { Accept: "application/json" }
  });
  if (!response.ok) {
    throw new Error(await readApiError(response, "Sign-in status could not be loaded."));
  }

  applicationSession = await response.json();
  if (!applicationSession.authenticationEnabled) {
    elements.accountControls.hidden = true;
    elements.administrationNavigation.hidden = false;
    return;
  }

  if (!applicationSession.isAuthenticated) {
    window.location.reload();
    return;
  }

  elements.accountControls.hidden = false;
  elements.accountName.textContent =
    applicationSession.displayName || applicationSession.emailAddress || "Signed-in user";
  elements.administrationNavigation.hidden = !applicationSession.isAdmin;
}

function renderModuleAvailability(availability) {
  if (!availability || typeof availability !== "object") {
    throw new Error("Module availability settings are missing from the saved configuration.");
  }

  moduleAvailability = Object.fromEntries(
    modules.map(module => [module.key, availability[module.key] === true])
  );
  for (const navigation of elements.moduleNavigations) {
    navigation.hidden = moduleAvailability[navigation.dataset.moduleNavigation] !== true;
  }

  elements.moduleAvailabilityList.replaceChildren();
  for (const module of modules) {
    const label = document.createElement("label");
    label.className = "module-availability-option";
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.dataset.moduleEnabled = module.key;
    enabled.checked = moduleAvailability[module.key];
    const title = document.createElement("span");
    title.textContent = module.title;
    label.append(enabled, title);
    elements.moduleAvailabilityList.append(label);
  }

  if (activeWorkspaceView !== "administration" && moduleAvailability[activeWorkspaceView] !== true) {
    const firstEnabledModule = modules.find(module => moduleAvailability[module.key]);
    const fallbackView = firstEnabledModule?.key ?? "administration";
    navigateWorkspace(fallbackView, true);
  }
}

async function loadReviewConfiguration() {
  elements.reloadConfiguration.disabled = true;
  setConfigurationSaveButtonsDisabled(true);
  elements.configurationError.hidden = true;
  elements.configurationStatus.textContent = "Loading configuration from SQL…";

  try {
    const response = await fetch("/api/admin/review-configuration", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Configuration could not be loaded."));
    }

    const result = await response.json();
    configurationVersion = result.version;
    savedConfiguration = result.configuration;
    renderReviewConfiguration(result);
    elements.configurationStatus.textContent = "Configuration loaded.";
  } catch (error) {
    showConfigurationError(error instanceof Error ? error.message : "Configuration could not be loaded.");
    elements.configurationStatus.textContent = "Configuration was not loaded.";
  } finally {
    elements.reloadConfiguration.disabled = false;
    setConfigurationSaveButtonsDisabled(savedConfiguration === null);
  }
}

async function loadGovernanceConfiguration() {
  elements.governanceConfigStatus.textContent = "Loading approved workspace scope…";
  elements.governanceConfigError.hidden = true;
  elements.saveGovernanceConfiguration.disabled = true;
  try {
    const response = await fetch("/api/governance-audit/configuration", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Governance Audit configuration could not be loaded."));
    }

    const result = await response.json();
    governanceConfigurationVersion = result.version;
    elements.governanceWorkspaceIds.value = result.workspaceIds.join("\n");
    elements.governanceConfigVersion.textContent =
      `Configuration version ${result.version} · Updated ${new Date(result.updatedUtc).toLocaleString()} · ${result.updatedBy}`;
    elements.governanceConfigStatus.textContent = "Approved workspace scope loaded.";
  } catch (error) {
    elements.governanceConfigStatus.textContent = "Governance Audit configuration is unavailable.";
    elements.governanceConfigError.textContent =
      error instanceof Error ? error.message : "Governance Audit configuration could not be loaded.";
    elements.governanceConfigError.hidden = false;
  } finally {
    elements.saveGovernanceConfiguration.disabled = false;
  }
}

async function discoverGovernanceWorkspaces() {
  elements.discoverGovernanceWorkspaces.disabled = true;
  elements.governanceConfigError.hidden = true;
  elements.governanceConfigStatus.textContent = "Discovering workspaces visible to the Power BI identity…";
  try {
    const response = await fetch("/api/governance-audit/workspaces", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Power BI workspace discovery failed."));
    }

    const workspaces = await response.json();
    elements.governanceWorkspaceDiscovery.replaceChildren();
    for (const workspace of workspaces) {
      const label = document.createElement("label");
      label.className = "module-availability-option";
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.dataset.governanceWorkspace = workspace.id;
      checkbox.checked = readLines("governance-workspace-ids")
        .some(id => id.toLocaleLowerCase() === workspace.id.toLocaleLowerCase());
      const name = document.createElement("span");
      name.textContent = `${workspace.name} · ${workspace.id}`;
      label.append(checkbox, name);
      elements.governanceWorkspaceDiscovery.append(label);
    }
    elements.governanceConfigStatus.textContent =
      `Discovered ${workspaces.length} workspace${workspaces.length === 1 ? "" : "s"} visible to the configured identity.`;
  } catch (error) {
    elements.governanceConfigError.textContent =
      error instanceof Error ? error.message : "Power BI workspace discovery failed.";
    elements.governanceConfigError.hidden = false;
    elements.governanceConfigStatus.textContent = "Workspace discovery failed.";
  } finally {
    elements.discoverGovernanceWorkspaces.disabled = false;
  }
}

function useGovernanceWorkspaceSelection() {
  const selectedIds = [...elements.governanceWorkspaceDiscovery.querySelectorAll(
    "[data-governance-workspace]:checked"
  )].map(checkbox => checkbox.dataset.governanceWorkspace);
  elements.governanceWorkspaceIds.value = selectedIds.join("\n");
}

async function saveGovernanceConfiguration() {
  const workspaceIds = readLines("governance-workspace-ids");
  const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
  if (workspaceIds.some(id => !guidPattern.test(id)) ||
      new Set(workspaceIds.map(id => id.toLocaleLowerCase())).size !== workspaceIds.length) {
    elements.governanceConfigError.textContent =
      "Enter distinct Power BI workspace GUIDs, one per line, or choose them from discovery.";
    elements.governanceConfigError.hidden = false;
    return;
  }

  elements.saveGovernanceConfiguration.disabled = true;
  elements.governanceConfigError.hidden = true;
  elements.governanceConfigStatus.textContent = "Saving approved workspace scope…";
  try {
    const response = await fetch("/api/governance-audit/configuration", {
      method: "PUT",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ expectedVersion: governanceConfigurationVersion, workspaceIds })
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Governance Audit scope could not be saved."));
    }

    const result = await response.json();
    governanceConfigurationVersion = result.version;
    elements.governanceWorkspaceIds.value = result.workspaceIds.join("\n");
    elements.governanceConfigVersion.textContent =
      `Configuration version ${result.version} · Updated ${new Date(result.updatedUtc).toLocaleString()} · ${result.updatedBy}`;
    elements.governanceConfigStatus.textContent = "Approved workspace scope saved.";
  } catch (error) {
    elements.governanceConfigError.textContent =
      error instanceof Error ? error.message : "Governance Audit scope could not be saved.";
    elements.governanceConfigError.hidden = false;
    elements.governanceConfigStatus.textContent = "Workspace scope was not saved.";
  } finally {
    elements.saveGovernanceConfiguration.disabled = false;
  }
}

async function runGovernanceScan() {
  elements.runGovernanceScan.disabled = true;
  elements.governanceError.hidden = true;
  elements.governanceResults.hidden = true;
  elements.governanceStatus.textContent = "Reading approved workspaces from Power BI…";
  try {
    const response = await fetch("/api/governance-audit/scan", {
      method: "POST",
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Governance Audit scan failed."));
    }

    renderGovernanceAudit(await response.json());
    await loadGovernanceAuditHistory();
    elements.governanceResults.hidden = false;
    elements.governanceStatus.textContent =
      `Read-only scan completed at ${new Date().toLocaleString()}.`;
  } catch (error) {
    elements.governanceError.textContent =
      error instanceof Error ? error.message : "Governance Audit scan failed.";
    elements.governanceError.hidden = false;
    elements.governanceStatus.textContent = "Governance Audit did not complete.";
  } finally {
    elements.runGovernanceScan.disabled = false;
  }
}

function renderGovernanceAudit(result) {
  elements.governanceWorkspaceRows.replaceChildren();
  elements.governanceFindingRows.replaceChildren();
  elements.governanceAccessRows.replaceChildren();
  elements.governanceArtifactRows.replaceChildren();
  elements.governanceHistoryRows.replaceChildren();

  for (const audit of result.workspaces) {
    const workspace = audit.workspace;
    const admins = audit.users.filter(user => user.accessRight.toLocaleLowerCase() === "admin").length;
    appendGovernanceRow(elements.governanceWorkspaceRows, [
      workspace.name,
      audit.reports.length,
      audit.semanticModels.length,
      admins
    ]);

    for (const user of audit.users) {
      appendGovernanceRow(elements.governanceAccessRows, [
        workspace.name,
        user.displayName || user.emailAddress || user.identifier,
        user.principalType,
        user.accessRight
      ]);
    }

    for (const artifact of [...audit.reports, ...audit.semanticModels]) {
      appendGovernanceRow(elements.governanceArtifactRows, [
        workspace.name,
        artifact.type,
        artifact.name,
        artifact.configuredBy || "Not returned by Power BI"
      ]);
    }
  }

  for (const finding of result.findings) {
    appendGovernanceRow(elements.governanceFindingRows, [
      finding.severity,
      finding.workspaceName,
      `${finding.code} · ${finding.title}`,
      finding.detail
    ]);
  }
  elements.governanceNoFindings.hidden = result.findings.length > 0;
}

async function loadGovernanceAuditHistory() {
  try {
    const response = await fetch("/api/governance-audit/runs?limit=20", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Saved Governance Audit history could not be loaded."));
    }

    const runs = await response.json();
    elements.governanceHistoryRows.replaceChildren();
    for (const run of runs) {
      const row = appendGovernanceRow(elements.governanceHistoryRows, [
        new Date(run.scannedUtc).toLocaleString(),
        run.scannedBy,
        run.workspaceCount,
        run.reportCount,
        run.semanticModelCount,
        run.findingCount
      ]);
      const actionCell = document.createElement("td");
      const openButton = document.createElement("button");
      openButton.type = "button";
      openButton.className = "button button-secondary";
      openButton.textContent = "Open";
      openButton.addEventListener("click", () => loadGovernanceAuditRun(run.runId));
      actionCell.append(openButton);
      row.append(actionCell);
    }
    elements.governanceHistoryEmpty.hidden = runs.length > 0;
  } catch (error) {
    elements.governanceError.textContent =
      error instanceof Error ? error.message : "Saved Governance Audit history could not be loaded.";
    elements.governanceError.hidden = false;
  }
}

async function loadGovernanceAuditRun(runId) {
  elements.governanceStatus.textContent = "Loading saved Governance Audit run…";
  elements.governanceError.hidden = true;
  try {
    const response = await fetch(`/api/governance-audit/runs/${encodeURIComponent(runId)}`, {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Saved Governance Audit run could not be loaded."));
    }

    const result = await response.json();
    renderGovernanceAudit(result);
    elements.governanceResults.hidden = false;
    elements.governanceStatus.textContent =
      `Showing saved read-only audit from ${new Date(result.scannedUtc).toLocaleString()}.`;
  } catch (error) {
    elements.governanceError.textContent =
      error instanceof Error ? error.message : "Saved Governance Audit run could not be loaded.";
    elements.governanceError.hidden = false;
    elements.governanceStatus.textContent = "Saved audit run could not be loaded.";
  }
}

function appendGovernanceRow(target, values) {
  const row = document.createElement("tr");
  for (const value of values) {
    const cell = document.createElement("td");
    cell.textContent = String(value ?? "—");
    row.append(cell);
  }
  target.append(row);
  return row;
}

function renderReviewConfiguration(result) {
  const configuration = result.configuration;
  renderModuleAvailability(configuration.modules);
  const fields = {
    "config-table-pattern": configuration.tableNamePattern,
    "config-column-pattern": configuration.columnNamePattern,
    "config-measure-pattern": configuration.measureNamePattern,
    "config-placeholder-pattern": configuration.placeholderColumnPattern,
    "config-measure-table-name": configuration.measureTableName,
    "config-measure-candidates": configuration.measureTableNameCandidates,
    "config-fact-prefix": configuration.factTablePrefix,
    "config-dimension-prefix": configuration.dimensionTablePrefix,
    "config-generated-prefixes": configuration.generatedTablePrefixes,
    "config-required-measures": configuration.requiredMeasures
  };
  for (const [id, value] of Object.entries(fields)) {
    const field = document.getElementById(id);
    field.value = Array.isArray(value) ? value.join("\n") : value || "";
  }
  document.querySelector("#config-require-divide").checked = configuration.requireDivideForDivision;
  document.querySelector("#config-warn-format").checked = configuration.warnOnFormatInDax;
  renderRulePolicies(configuration.rules || {});
  renderConfigurationMetadata(result);
}

function renderConfigurationMetadata(result) {
  elements.configurationVersion.textContent = `Configuration version ${result.version}`;
  const updated = result.updatedUtc ? new Date(result.updatedUtc).toLocaleString() : "Unknown";
  elements.configurationUpdated.textContent = `Updated ${updated} · ${result.updatedBy || "Unknown"}`;
}

function setConfigurationSaveButtonsDisabled(disabled) {
  for (const button of elements.saveConfigurationButtons) {
    button.disabled = disabled;
  }
}

function renderRulePolicies(policies) {
  elements.rulePolicyList.replaceChildren();
  for (const [key, description] of Object.entries(ruleDescriptions)) {
    const policy = policies[key] || { enabled: true, severity: key === "required-measures" ? "Error" : "Warning" };
    const row = document.createElement("div");
    row.className = "rule-policy";
    row.dataset.ruleKey = key;

    const detail = document.createElement("div");
    const label = document.createElement("label");
    label.className = "rule-policy-enabled";
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = policy.enabled !== false;
    enabled.dataset.policyEnabled = "";
    const name = document.createElement("span");
    name.textContent = key;
    label.append(enabled, name);
    const help = document.createElement("p");
    help.className = "rule-policy-description";
    help.textContent = description;
    detail.append(label, help);

    const severity = document.createElement("select");
    severity.setAttribute("aria-label", `${key} severity`);
    severity.dataset.policySeverity = "";
    for (const value of ["Error", "Warning", "Information"]) {
      const option = new Option(value, value, false, policy.severity === value);
      severity.add(option);
    }
    row.append(detail, severity);
    elements.rulePolicyList.append(row);
  }
}

async function saveReviewConfiguration(section) {
  if (!["workspace", "reviews"].includes(section)) {
    showConfigurationError("Select a settings tab that has configurable options.");
    return;
  }

  if (savedConfiguration === null) {
    showConfigurationError("Load the current configuration before saving.");
    return;
  }

  if (section === "reviews" && !elements.configurationForm.reportValidity()) {
    return;
  }

  const configuration = section === "workspace"
    ? {
        ...savedConfiguration,
        modules: Object.fromEntries(modules.map(module => [
          module.key,
          document.querySelector(`[data-module-enabled="${module.key}"]`).checked
        ]))
      }
    : {
        ...savedConfiguration,
        tableNamePattern: document.querySelector("#config-table-pattern").value.trim(),
        columnNamePattern: document.querySelector("#config-column-pattern").value.trim(),
        measureNamePattern: document.querySelector("#config-measure-pattern").value.trim(),
        placeholderColumnPattern: document.querySelector("#config-placeholder-pattern").value.trim(),
        measureTableName: document.querySelector("#config-measure-table-name").value.trim(),
        measureTableNameCandidates: readLines("config-measure-candidates"),
        factTablePrefix: document.querySelector("#config-fact-prefix").value.trim(),
        dimensionTablePrefix: document.querySelector("#config-dimension-prefix").value.trim(),
        generatedTablePrefixes: readLines("config-generated-prefixes"),
        requiredMeasures: readLines("config-required-measures"),
        requireDivideForDivision: document.querySelector("#config-require-divide").checked,
        warnOnFormatInDax: document.querySelector("#config-warn-format").checked,
        rules: Object.fromEntries([...elements.rulePolicyList.querySelectorAll(".rule-policy")].map(row => [
          row.dataset.ruleKey,
          {
            enabled: row.querySelector("[data-policy-enabled]").checked,
            severity: row.querySelector("[data-policy-severity]").value
          }
        ]))
      };

  setConfigurationSaveButtonsDisabled(true);
  elements.reloadConfiguration.disabled = true;
  elements.configurationError.hidden = true;
  elements.configurationStatus.textContent = section === "workspace"
    ? "Saving workspace settings…"
    : "Saving review settings…";
  try {
    const response = await fetch("/api/admin/review-configuration", {
      method: "PUT",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ expectedVersion: configurationVersion, configuration })
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Configuration could not be saved."));
    }

    const result = await response.json();
    configurationVersion = result.version;
    savedConfiguration = result.configuration;
    if (section === "workspace") {
      renderModuleAvailability(result.configuration.modules);
    }
    renderConfigurationMetadata(result);
    elements.configurationStatus.textContent = section === "workspace"
      ? "Workspace settings saved."
      : "Review settings saved. They will apply to new reviews.";
  } catch (error) {
    showConfigurationError(error instanceof Error ? error.message : "Configuration could not be saved.");
    elements.configurationStatus.textContent = "Configuration was not saved.";
  } finally {
    setConfigurationSaveButtonsDisabled(false);
    elements.reloadConfiguration.disabled = false;
  }
}

function readLines(id) {
  return document.getElementById(id).value
    .split(/\r?\n/)
    .map(value => value.trim())
    .filter(Boolean);
}

async function readApiError(response, fallback) {
  if (response.status === 401) {
    window.location.reload();
    return "Your sign-in expired. Re-authenticating now.";
  }
  if (response.status === 403) {
    return "Your account is signed in but is not authorized for this action. Contact a BI Sentinel administrator.";
  }
  const problem = await response.json().catch(() => null);
  if (problem?.errors) {
    return Object.values(problem.errors).flat().join(" ");
  }
  return problem?.detail || problem?.title || fallback;
}

function showConfigurationError(message) {
  elements.configurationError.textContent = message;
  elements.configurationError.hidden = false;
}

async function saveFeedback(key) {
  const entry = feedback[key];
  if (!entry || (!entry.rating && !entry.comment)) {
    elements.status.textContent = "Choose a rating or add a note before submitting feedback.";
    updateExportButton();
    return;
  }
  if (!entry.reviewFindingId) {
    showError("This finding is not linked to a saved review and cannot be submitted.");
    return;
  }

  try {
    const response = await fetch("/api/reviews/feedback", {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({
        reviewerId,
        reviewFindingId: entry.reviewFindingId,
        rating: entry.rating,
        comment: entry.comment || null
      })
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `SQL feedback save failed (HTTP ${response.status}).`);
    }

    entry.synced = true;
    entry.feedbackStatus = "Closed";
    entry.dirty = false;
    entry.submittedRating = entry.rating;
    entry.submittedComment = entry.comment;
    elements.status.textContent = "Feedback submitted and closed.";
    elements.error.hidden = true;
    renderFindings();
    await loadDashboard();
  } catch (error) {
    entry.synced = false;
    elements.status.textContent = "Feedback was not saved. Export it before leaving this page.";
    showError(error instanceof Error
      ? `${error.message} This entry is only in the current page session; use Export feedback before leaving.`
      : "Feedback could not be saved. It is only in the current page session; export it before leaving.");
  }
  renderFindings();
  updateExportButton();
}

async function runReview(url, requestOptions, triggerButton, sourceName) {
  triggerButton.disabled = true;
  elements.error.hidden = true;
  elements.status.textContent = "Reviewing the Power BI artifact and saving the result to SQL…";

  try {
    const response = await fetch(url, {
      ...requestOptions,
      method: requestOptions.method || "POST",
      headers: { Accept: "application/json", ...requestOptions.headers }
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      const detail = problem?.detail || problem?.title || `Review or history save failed (HTTP ${response.status}).`;
      throw new Error(response.status === 503
        ? `Review completed but could not be saved: ${detail}`
        : detail);
    }

    await displaySavedRuns([await response.json()], sourceName);
  } catch (error) {
    showReviewError(
      error instanceof Error ? error.message : "The review could not be completed.",
      sourceName
    );
    elements.status.textContent = error instanceof Error && error.message.startsWith("Review completed but could not be saved:")
      ? "Review completed but was not saved."
      : "Review did not complete.";
  } finally {
    triggerButton.disabled = false;
  }
}

async function displaySavedRuns(savedRuns, sourceName) {
  if (elements.reviewErrorDialog.open) {
    elements.reviewErrorDialog.close();
  }
  elements.error.hidden = true;
  currentReviewRunId = savedRuns[0]?.reviewRunId || "";
  const projectNames = savedRuns.map(run => run.review.projectName);
  currentProject = projectNames.join(", ");
  currentFindings = savedRuns.flatMap(run =>
    (run.findings || run.review.findings || []).map(finding => ({
      ...finding,
      projectName: run.review.projectName
    })));
  for (const run of savedRuns) {
    await loadSavedFeedback(run.reviewRunId, run.findings || run.review.findings || []);
  }

  const summary = savedRuns.reduce((total, run) => ({
    projectName: projectNames.length === 1 ? projectNames[0] : `${projectNames.length} projects`,
    tablesChecked: total.tablesChecked + run.review.tablesChecked,
    measuresChecked: total.measuresChecked + run.review.measuresChecked,
    relationshipsChecked: total.relationshipsChecked + run.review.relationshipsChecked,
    checksRun: total.checksRun + run.review.checksRun,
    findings: currentFindings
  }), {
    tablesChecked: 0,
    measuresChecked: 0,
    relationshipsChecked: 0,
    checksRun: 0
  });
  renderSummary(summary);
  renderFindings();
  elements.overview.hidden = false;
  elements.status.textContent =
    `Review completed successfully for ${savedRuns.length} ${savedRuns.length === 1 ? "project" : "projects"}.`;
  await loadDashboard();
  const latestReviewedUtc = savedRuns
    .map(run => run.reviewedUtc)
    .sort()
    .at(-1);
  showReviewSuccess(summary, sourceName, latestReviewedUtc);
}

async function uploadProject() {
  const file = elements.projectZip.files[0];
  if (!file) {
    showReviewError(
      "Choose a PBIT template or a ZIP containing one complete PBIP/PBIR project.",
      "No file selected"
    );
    return;
  }

  const extension = file.name.split(".").pop()?.toLocaleLowerCase();
  if (!["pbit", "zip"].includes(extension)) {
    showReviewError(
      "Upload a PBIT template or a ZIP of the complete PBIP/PBIR project. PBIX files are not supported. A standalone .pbip file is only a descriptor; it does not include the referenced project folders.",
      file.name
    );
    return;
  }
  if (file.size > 500 * 1024 * 1024) {
    showReviewError("The selected file exceeds the 500 MB upload limit.", file.name);
    return;
  }

  const form = new FormData();
  form.append("file", file);
  const projectId = elements.projectId.value.trim();
  const query = projectId ? `?projectId=${encodeURIComponent(projectId)}` : "";
  await runReview(
    `/api/reviews/upload${query}`,
    { body: form },
    elements.uploadProject,
    file.name
  );
}

async function searchReviewHistory(event) {
  event.preventDefault();
  const projectName = elements.reviewSearch.value.trim();
  if (!projectName) {
    showError("Enter a project name to search saved reviews.");
    return;
  }

  const searchButton = elements.reviewSearchForm.querySelector("button[type='submit']");
  searchButton.disabled = true;
  try {
    const response = await fetch(
      `/api/reviews/history?projectName=${encodeURIComponent(projectName)}&limit=100`,
      {
        headers: { Accept: "application/json" }
      }
    );
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `Review history failed (HTTP ${response.status}).`);
    }

    const runs = await response.json();
    elements.history.replaceChildren(new Option(
      runs.length === 0 ? "No saved reviews match this project name" : "Select a matching saved review",
      ""
    ));
    for (const run of runs) {
      const reviewed = new Date(run.reviewedUtc).toLocaleString();
      elements.history.add(new Option(
        `${run.projectName} · ${reviewed} · ${run.findingsCount} findings`,
        run.reviewRunId
      ));
    }
    elements.error.hidden = true;
    elements.status.textContent = `Found ${runs.length} saved ${runs.length === 1 ? "review" : "reviews"} for "${projectName}".`;
  } catch (error) {
    showError(error instanceof Error
      ? `${error.message} Verify migration 002 and the app's SQL permissions.`
      : "Review history could not be loaded. Verify migration 002 and the app's SQL permissions.");
  } finally {
    searchButton.disabled = false;
  }
}

async function loadSelectedReview() {
  const reviewRunId = elements.history.value;
  if (!reviewRunId) {
    return;
  }
  await loadReviewById(reviewRunId);
}

async function loadReviewById(reviewRunId) {
  elements.status.textContent = "Loading saved review…";
  elements.error.hidden = true;
  try {
    const response = await fetch(`/api/reviews/history/${encodeURIComponent(reviewRunId)}`, {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `Saved review could not be loaded (HTTP ${response.status}).`);
    }

    const entry = await response.json();
    currentReviewRunId = entry.summary.reviewRunId;
    currentProject = entry.summary.projectName;
    currentFindings = entry.findings || [];
    await loadSavedFeedback(currentReviewRunId);
    renderSummary({ ...entry.summary, findings: currentFindings });
    renderFindings();
    elements.overview.hidden = false;
    elements.status.textContent =
      `Review loaded successfully · ${entry.summary.projectName} · ${currentFindings.length} findings`;
    elements.reviewResults.scrollIntoView({ behavior: "smooth", block: "start" });
  } catch (error) {
    showError(error instanceof Error ? error.message : "Saved review could not be loaded.");
    elements.status.textContent = "Saved review could not be loaded.";
  }
}

async function loadDashboard() {
  elements.refreshDashboard.disabled = true;
  try {
    const response = await fetch(
      `/api/reviews/dashboard?reviewerId=${encodeURIComponent(reviewerId)}&limit=1000`,
      { headers: { Accept: "application/json" } }
    );
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `Review dashboard failed (HTTP ${response.status}).`);
    }

    dashboardItems = await response.json();
    renderDashboard();
  } catch (error) {
    showError(error instanceof Error
      ? `${error.message} Verify feedback migration 004 and review-history access.`
      : "Review dashboard could not be loaded.");
  } finally {
    elements.refreshDashboard.disabled = false;
  }
}

function setDashboardStatus(status) {
  dashboardStatus = status;
  renderDashboard();
}

function renderDashboard() {
  const waitingRuns = dashboardItems.filter(run => run.feedbackStatus === "Open");
  const closedRuns = dashboardItems.filter(run => run.feedbackStatus === "Closed");
  elements.waitingCount.textContent = String(waitingRuns.length);
  elements.closedCount.textContent = String(closedRuns.length);
  elements.waitingTab.setAttribute("aria-selected", String(dashboardStatus === "Open"));
  elements.closedTab.setAttribute("aria-selected", String(dashboardStatus === "Closed"));

  const runs = dashboardStatus === "Open" ? waitingRuns : closedRuns;
  elements.dashboardRows.replaceChildren(...runs.map(renderDashboardRow));
  elements.dashboardEmpty.hidden = runs.length > 0;
  elements.dashboardEmpty.textContent = dashboardStatus === "Open"
    ? "No findings are waiting for your feedback."
    : "No findings have been submitted and closed yet.";
}

function renderDashboardRow(run) {
  const row = document.createElement("tr");
  const projectCell = document.createElement("td");
  const project = document.createElement("strong");
  project.textContent = run.projectName;
  const reviewed = document.createElement("span");
  reviewed.className = "dashboard-secondary";
  reviewed.textContent = new Date(run.reviewedUtc).toLocaleString();
  projectCell.append(project, reviewed);

  const findingCell = document.createElement("td");
  const findingCount = document.createElement("strong");
  findingCount.textContent = `${run.findingsCount} ${run.findingsCount === 1 ? "finding" : "findings"}`;
  findingCell.append(findingCount);

  const responseCell = document.createElement("td");
  const progress = document.createElement("span");
  progress.className = "dashboard-progress";
  progress.textContent = `${run.closedFindingsCount} submitted · ${run.waitingFindingsCount} waiting`;
  const status = document.createElement("span");
  status.className = `dashboard-status${run.feedbackStatus === "Closed" ? " dashboard-status-closed" : ""}`;
  status.textContent = run.feedbackStatus === "Closed" ? "Closed" : "Waiting";
  responseCell.append(progress, status);

  const actions = document.createElement("td");
  actions.className = "dashboard-actions";
  const openButton = document.createElement("button");
  openButton.type = "button";
  openButton.className = "button button-secondary button-small";
  openButton.textContent = "Open review";
  openButton.addEventListener("click", () => openDashboardRun(run));
  actions.append(openButton);

  if (run.feedbackStatus === "Closed" && run.findingsCount > 0) {
    const reopenButton = document.createElement("button");
    reopenButton.type = "button";
    reopenButton.className = "button button-secondary button-small";
    reopenButton.textContent = "Reopen review";
    reopenButton.addEventListener("click", () => reopenReview(run.reviewRunId));
    actions.append(reopenButton);
  }

  row.append(projectCell, findingCell, responseCell, actions);
  return row;
}

async function openDashboardRun(run) {
  await loadReviewById(run.reviewRunId);
  elements.overview.scrollIntoView({ behavior: "smooth", block: "start" });
}

async function reopenReview(reviewRunId) {
  try {
    const response = await fetch(
      `/api/reviews/history/${encodeURIComponent(reviewRunId)}/reopen`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({ reviewerId })
      }
    );
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `Review could not be reopened (HTTP ${response.status}).`);
    }

    elements.status.textContent = "Review reopened and moved to Waiting.";
    await loadDashboard();
  } catch (error) {
    showError(error instanceof Error ? error.message : "Review could not be reopened.");
  }
}

async function reopenFeedback(reviewFindingId) {
  try {
    const response = await fetch(
      `/api/reviews/feedback/${encodeURIComponent(reviewFindingId)}/reopen`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({ reviewerId })
      }
    );
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `Feedback could not be reopened (HTTP ${response.status}).`);
    }

    if (feedback[reviewFindingId]) {
      feedback[reviewFindingId].feedbackStatus = "Open";
      feedback[reviewFindingId].dirty = false;
      feedback[reviewFindingId].synced = true;
    }
    elements.status.textContent = "Feedback reopened and moved to Waiting.";
    renderFindings();
    await loadDashboard();
  } catch (error) {
    showError(error instanceof Error ? error.message : "Feedback could not be reopened.");
  }
}

async function loadSavedFeedback(reviewRunId, runFindings = currentFindings) {
  try {
    const response = await fetch(
      `/api/reviews/history/${encodeURIComponent(reviewRunId)}/feedback?reviewerId=${encodeURIComponent(reviewerId)}`,
      {
      headers: { Accept: "application/json" }
      });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `Could not load saved feedback (HTTP ${response.status}).`);
    }

    const entries = await response.json();
    for (const finding of runFindings) {
      delete feedback[finding.reviewFindingId];
    }
    sharedFeedbackAvailable = sharedFeedbackAvailable || entries.length > 0;
    for (const entry of entries) {
      if (!entry.reviewFindingId) {
        continue;
      }

      const key = entry.reviewFindingId;
      feedback[key] = {
        project: entry.projectName,
        reviewFindingId: entry.reviewFindingId,
        findingFingerprint: entry.findingFingerprint,
        ruleId: entry.ruleId,
        severity: entry.severity,
        message: entry.message,
        location: entry.location,
        rating: entry.rating || "",
        comment: entry.comment || "",
        submittedRating: entry.rating || "",
        submittedComment: entry.comment || "",
        feedbackStatus: entry.feedbackStatus,
        updatedAt: entry.updatedUtc,
        synced: true,
        dirty: false
      };
    }
    elements.error.hidden = true;
    renderFindings();
  } catch (error) {
    sharedFeedbackAvailable = false;
    showError(error instanceof Error
      ? `${error.message} Feedback will not persist unless SQL is available; export entries before leaving this page.`
      : "Saved feedback could not be loaded. Feedback will not persist unless SQL is available.");
  }
}

function renderSummary(result) {
  elements.project.textContent = result.projectName || "Uploaded artifact";
  elements.findingsCount.textContent = String((result.findings || []).length);
  elements.tables.textContent = String(result.tablesChecked ?? 0);
  elements.measures.textContent = String(result.measuresChecked ?? 0);
  elements.relationships.textContent = String(result.relationshipsChecked ?? 0);
}

function renderFindings() {
  const severity = elements.severity.value;
  const query = elements.search.value.trim().toLocaleLowerCase();
  const visibleFindings = currentFindings.filter((finding) => {
    const matchesSeverity = severity === "all" || finding.severity === severity;
    const searchable = [
      finding.ruleId,
      finding.message,
      finding.location,
      finding.recommendation
    ].join(" ").toLocaleLowerCase();
    return matchesSeverity && searchable.includes(query);
  });

  elements.list.replaceChildren(...visibleFindings.map(renderFinding));
  elements.empty.hidden = visibleFindings.length > 0;
  elements.empty.querySelector("h3").textContent =
    currentFindings.length === 0 ? "No findings in this review" : "No matching findings";
  elements.empty.querySelector("p").textContent =
    currentFindings.length === 0
      ? "The configured checks completed without producing any findings."
      : "Change the severity or search filters to see other results.";
  elements.resultCount.textContent =
    currentFindings.length === 0
      ? "No findings"
      : `Showing ${visibleFindings.length} of ${currentFindings.length}`;
  updateExportButton();
}

function renderFinding(finding) {
  const card = document.createElement("article");
  card.className = "finding";
  card.dataset.severity = finding.severity || "Warning";
  card.dataset.findingId = finding.reviewFindingId || "";

  const header = document.createElement("div");
  header.className = "finding-header";
  const severity = document.createElement("span");
  severity.className = "severity";
  severity.dataset.severity = finding.severity || "Warning";
  severity.textContent = finding.severity || "Warning";
  const rule = document.createElement("span");
  rule.className = "rule-id";
  rule.textContent = finding.ruleId || "RULE";
  const location = document.createElement("span");
  location.className = "finding-location";
  location.textContent = finding.location || "Project";
  header.append(severity, rule, location);
  if (finding.projectName) {
    const project = document.createElement("span");
    project.className = "finding-project";
    project.textContent = finding.projectName;
    header.append(project);
  }

  const message = document.createElement("p");
  message.className = "finding-message";
  message.textContent = finding.message || "Review finding";
  const recommendation = document.createElement("p");
  recommendation.className = "finding-recommendation";
  recommendation.textContent = finding.recommendation || "";

  const feedbackRow = document.createElement("div");
  feedbackRow.className = "feedback";
  const label = document.createElement("span");
  label.className = "feedback-label";
  label.textContent = "Was this useful?";
  feedbackRow.append(label);

  const key = findingKey(finding);
  const saved = feedback[key] || {};
  for (const [value, text] of [["helpful", "Helpful"], ["incorrect", "Incorrect"], ["not-applicable", "Not applicable"]]) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "feedback-choice";
    button.textContent = text;
    button.setAttribute("aria-pressed", String(saved.rating === value));
    button.setAttribute("aria-label", `${text}: ${finding.message || finding.ruleId}`);
    button.addEventListener("click", () => {
      const previous = feedback[key] || {};
      const submittedRating = previous.submittedRating ?? previous.rating ?? "";
      const submittedComment = previous.submittedComment ?? previous.comment ?? "";
      feedback[key] = {
        ...previous,
        project: finding.projectName || currentProject,
        reviewFindingId: finding.reviewFindingId,
        findingFingerprint: finding.findingFingerprint,
        ruleId: finding.ruleId,
        severity: finding.severity || "Warning",
        message: finding.message,
        location: finding.location,
        rating: value,
        comment: feedback[key]?.comment || "",
        submittedRating,
        submittedComment,
        updatedAt: new Date().toISOString(),
        synced: feedback[key]?.synced || false,
        feedbackStatus: feedback[key]?.feedbackStatus || "Open",
        dirty: value !== submittedRating ||
          (feedback[key]?.comment || "") !== submittedComment
      };
      renderFindings();
    });
    feedbackRow.append(button);
  }

  const comment = document.createElement("input");
  comment.type = "text";
  comment.className = "feedback-comment";
  comment.maxLength = 500;
  comment.value = saved.comment || "";
  comment.placeholder = "Optional note for your team";
  comment.setAttribute("aria-label", `Feedback note for ${finding.ruleId}`);
  comment.addEventListener("input", () => {
    const previous = feedback[key] || {};
    const submittedRating = previous.submittedRating ?? previous.rating ?? "";
    const submittedComment = previous.submittedComment ?? previous.comment ?? "";
    const rating = previous.rating || "";
    const draftComment = comment.value.trim();
    feedback[key] = {
        ...previous,
        project: finding.projectName || currentProject,
        reviewFindingId: finding.reviewFindingId,
        findingFingerprint: finding.findingFingerprint,
        ruleId: finding.ruleId,
        severity: finding.severity || "Warning",
        message: finding.message,
        location: finding.location,
        rating,
        comment: draftComment,
        submittedRating,
        submittedComment,
        updatedAt: new Date().toISOString(),
        synced: previous.synced || false,
        feedbackStatus: previous.feedbackStatus || "Open",
        dirty: rating !== submittedRating || draftComment !== submittedComment
    };
    const feedbackStatus = feedbackRow.querySelector(".feedback-status");
    feedbackStatus.textContent = feedback[key].feedbackStatus === "Closed"
      ? feedback[key].dirty ? "Edited · submit changes to close" : "Submitted · Closed"
      : feedback[key].synced ? "Reopened · waiting for submission" : "Waiting for submission";
    const submitButton = feedbackRow.querySelector(".feedback-submit");
    submitButton.textContent = feedback[key].feedbackStatus === "Closed"
      ? "Submit changes"
      : "Submit feedback";
    submitButton.disabled = !((rating || draftComment) &&
      (feedback[key].feedbackStatus !== "Closed" || feedback[key].dirty));
  });
  feedbackRow.append(comment);

  const actions = document.createElement("div");
  actions.className = "feedback-actions";
  const feedbackStatus = document.createElement("span");
  feedbackStatus.className = "feedback-status";
  feedbackStatus.textContent = saved.feedbackStatus === "Closed" && !saved.dirty
    ? "Submitted · Closed"
    : saved.feedbackStatus === "Closed"
      ? saved.dirty ? "Edited · submit changes to close" : "Submitted · Closed"
      : saved.synced ? "Reopened · waiting for submission" : "Waiting for submission";
  actions.append(feedbackStatus);

  const submitButton = document.createElement("button");
  submitButton.type = "button";
  submitButton.className = "button button-primary button-small feedback-submit";
  submitButton.textContent = saved.feedbackStatus === "Closed"
    ? "Submit changes"
    : "Submit feedback";
  submitButton.disabled = !((saved.rating || saved.comment) &&
    (saved.feedbackStatus !== "Closed" || saved.dirty));
  submitButton.addEventListener("click", () => saveFeedback(key));
  actions.append(submitButton);

  if (saved.feedbackStatus === "Closed" && !saved.dirty) {
    const reopenButton = document.createElement("button");
    reopenButton.type = "button";
    reopenButton.className = "button button-secondary button-small";
    reopenButton.textContent = "Reopen";
    reopenButton.addEventListener("click", () => reopenFeedback(finding.reviewFindingId));
    actions.append(reopenButton);
  }

  feedbackRow.append(actions);
  card.append(header, message, recommendation, feedbackRow);
  return card;
}

function findingKey(finding) {
  return finding.reviewFindingId ||
    [finding.projectName || currentProject, finding.ruleId, finding.location, finding.message].join("::");
}

function updateExportButton() {
  const hasFeedback = Object.values(feedback).some(entry =>
    entry.rating || (entry.comment && entry.comment.trim()));
  elements.exportButton.disabled = !hasFeedback && !sharedFeedbackAvailable;
}

async function exportFeedback() {
  let entries;
  let source;
  try {
    const response = await fetch("/api/reviews/feedback?limit=1000", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `Shared feedback export failed (HTTP ${response.status}).`);
    }

    entries = await response.json();
    source = "shared-sql-store";
    const unsynced = Object.values(feedback).filter(entry => !entry.synced &&
      (entry.rating || (entry.comment && entry.comment.trim())));
    entries = mergeFeedback(entries, unsynced.map(entry => ({ ...entry, reviewerId })));
    sharedFeedbackAvailable = entries.length > 0;
    if (unsynced.length > 0) {
      source = "shared-sql-store-and-current-session";
    }
    elements.error.hidden = true;
  } catch (error) {
    entries = Object.values(feedback).filter(entry =>
    entry.rating || (entry.comment && entry.comment.trim()));
    entries = entries.map(entry => ({ ...entry, reviewerId }));
    source = "current-session-only";
    showError(error instanceof Error
      ? `${error.message} Exported only feedback from this page session.`
      : "Shared feedback export failed. Exported only feedback from this page session.");
  }

  const blob = new Blob([JSON.stringify({
    exportedAt: new Date().toISOString(),
    source,
    feedback: entries
  }, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `bi-sentinel-feedback-${new Date().toISOString().slice(0, 10)}.json`;
  link.click();
  URL.revokeObjectURL(url);
  elements.status.textContent = source === "shared-sql-store"
    ? `Exported ${entries.length} shared feedback entries.`
    : source === "shared-sql-store-and-current-session"
      ? `Exported ${entries.length} shared entries, including current-session feedback.`
      : `Exported ${entries.length} current-session entries as JSON.`;
}

function mergeFeedback(shared, local) {
  const merged = new Map();
  for (const entry of [...shared, ...local]) {
    const key = [
      entry.reviewerId,
      entry.reviewFindingId || [
        entry.projectName || entry.project,
        entry.ruleId,
        entry.location,
        entry.message
      ].join("::")
    ].join("::");
    merged.set(key, entry);
  }
  return [...merged.values()];
}

function loadReviewerId() {
  try {
    let id = localStorage.getItem(reviewerStorageKey);
    if (!id) {
      id = localStorage.getItem(legacyReviewerStorageKey) || crypto.randomUUID();
      localStorage.setItem(reviewerStorageKey, id);
    }
    return id;
  } catch {
    showError("Browser storage is unavailable. Pilot feedback cannot be attributed to this browser.");
    return crypto.randomUUID();
  }
}

function showError(message) {
  elements.error.textContent = message;
  elements.error.hidden = false;
}

function showReviewError(message, sourceName) {
  elements.error.hidden = true;
  elements.reviewErrorMessage.textContent = sourceName
    ? `File or path: ${sourceName}\n\n${message}`
    : message;
  if (!elements.reviewErrorDialog.open) {
    elements.reviewErrorDialog.showModal();
  }
}

function showReviewSuccess(review, sourceName, reviewedUtc) {
  if (elements.reviewErrorDialog.open) {
    elements.reviewErrorDialog.close();
  }
  const fileName = getShortFileName(sourceName || review.projectName);
  const reviewedAt = new Date(reviewedUtc);
  const reviewedDateTime = Number.isNaN(reviewedAt.getTime())
    ? ""
    : ` · ${reviewedAt.toLocaleString()}`;
  elements.reviewSuccessMessage.replaceChildren();

  const file = document.createElement("strong");
  file.className = "review-success-file";
  file.textContent = fileName;

  const dateTime = document.createElement("span");
  dateTime.className = "review-success-date";
  dateTime.textContent = reviewedDateTime.replace(/^ · /, "");

  const details = document.createElement("span");
  details.textContent =
    `${review.checksRun} checks · ${currentFindings.length} ${currentFindings.length === 1 ? "finding" : "findings"}. Select OK to view.`;

  elements.reviewSuccessMessage.append(file, dateTime, details);
  elements.reviewSuccessDialog.showModal();
}

function getShortFileName(sourceName) {
  const fileName = sourceName.split(/[\\/]/).pop() || sourceName;
  const nameWithoutExtension = fileName.replace(/\.[^.]+$/, "") || fileName;
  const maximumLength = 36;
  return nameWithoutExtension.length > maximumLength
    ? `${nameWithoutExtension.slice(0, maximumLength - 1).trimEnd()}…`
    : nameWithoutExtension;
}

updateExportButton();
