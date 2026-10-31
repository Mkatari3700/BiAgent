const reviewerStorageKey = "bi-sentinel.reviewerId.v1";
const legacyReviewerStorageKey = "bitla.reviewerId.v1";

const elements = {
  appLoadingIndicator: document.querySelector("#app-loading-indicator"),
  appLoadingMessage: document.querySelector("#app-loading-message"),
  topbar: document.querySelector(".topbar"),
  workspaceLayout: document.querySelector("#workspace-layout"),
  pageContent: document.querySelector(".page"),
  workspaceLoading: document.querySelector("#workspace-loading"),
  applicationStatePage: document.querySelector("#application-state-page"),
  applicationStateTitle: document.querySelector("#application-state-title"),
  applicationStateDescription: document.querySelector("#application-state-description"),
  applicationStateRetry: document.querySelector("#application-state-retry"),
  sidebarToggle: document.querySelector("#sidebar-toggle"),
  accountControls: document.querySelector("#account-controls"),
  accountName: document.querySelector("#account-name"),
  reviewsNavigation: document.querySelector("#reviews-navigation"),
  administrationNavigation: document.querySelector("#administration-navigation"),
  moduleNavigations: [...document.querySelectorAll("[data-module-navigation]")],
  moduleNavigationStatus: document.querySelector("#module-navigation-status"),
  reviewWorkspace: document.querySelector("#review-workspace"),
  reviewPageTabs: [...document.querySelectorAll("[data-review-page-tab]")],
  reviewPages: [...document.querySelectorAll("[data-review-page]")],
  governanceWorkspace: document.querySelector("#governance-workspace"),
  governanceScanWorkspaces: document.querySelector("#governance-scan-workspaces"),
  governanceWorkspaceNameFilter: document.querySelector("#governance-workspace-name-filter"),
  governanceWorkspaceCapacityFilter: document.querySelector("#governance-workspace-capacity-filter"),
  governanceSelectFilteredWorkspaces: document.querySelector("#governance-select-filtered-workspaces"),
  governanceArtifactSelection: document.querySelector("#governance-artifact-selection"),
  governanceArtifactItems: document.querySelector("#governance-artifact-items"),
  governanceScanTabs: [...document.querySelectorAll("[data-governance-scan-tab]")],
  governanceScanPanels: [...document.querySelectorAll("[data-governance-scan-panel]")],
  governancePageTabs: [...document.querySelectorAll("[data-governance-page-tab]")],
  governancePages: [...document.querySelectorAll("[data-governance-page]")],
  governancePowerBiAccessStatus: document.querySelector("#governance-powerbi-access-status"),
  governanceWorkspaceLoadStatus: document.querySelector("#governance-workspace-load-status"),
  governanceWorkspaceLoadMessage: document.querySelector("#governance-workspace-load-message"),
  authorizeGovernancePowerBi: document.querySelector("#authorize-governance-powerbi"),
  checkGovernancePowerBiAccess: document.querySelector("#check-governance-powerbi-access"),
  governanceAccessNext: document.querySelector("#governance-access-next"),
  governanceCheckSelection: document.querySelector("#governance-check-selection"),
  runGovernanceScan: document.querySelector("#run-governance-scan"),
  governanceWorkspacesNext: document.querySelector("#governance-workspaces-next"),
  governanceArtifactsBack: document.querySelector("#governance-artifacts-back"),
  governanceArtifactsNext: document.querySelector("#governance-artifacts-next"),
  governanceChecksBack: document.querySelector("#governance-checks-back"),
  governanceScanConfirmationDialog: document.querySelector("#governance-scan-confirmation"),
  governanceScanConfirmationSummary: document.querySelector("#governance-scan-confirmation-summary"),
  confirmGovernanceScan: document.querySelector("#confirm-governance-scan"),
  cancelGovernanceScan: document.querySelector("#cancel-governance-scan"),
  governanceStatus: document.querySelector("#governance-status"),
  governanceError: document.querySelector("#governance-error"),
  governanceResults: document.querySelector("#governance-results"),
  governanceResultSummary: document.querySelector("#governance-result-summary"),
  governanceInventoryCounts: document.querySelector("#governance-inventory-counts"),
  governanceResultCounts: document.querySelector("#governance-result-counts"),
  governanceResultFacets: document.querySelector("#governance-result-facets"),
  governanceResultFilterSummary: document.querySelector("#governance-result-filter-summary"),
  governanceResultFilterToggle: document.querySelector("#governance-result-filter-toggle"),
  governanceFindingFilters: [...document.querySelectorAll("[data-governance-finding-filter]")],
  governanceFindingScope: document.querySelector("#governance-finding-scope"),
  governanceFindingScopeLabel: document.querySelector("#governance-finding-scope-label"),
  clearGovernanceFindingScope: document.querySelector("#clear-governance-finding-scope"),
  governanceFindingList: document.querySelector("#governance-finding-list"),
  governanceResultsDialog: document.querySelector("#governance-results-dialog"),
  governanceResultsDialogSummary: document.querySelector("#governance-results-dialog-summary"),
  governanceResultsDialogCounts: document.querySelector("#governance-results-dialog-counts"),
  openGovernanceResults: document.querySelector("#open-governance-results"),
  exportGovernanceAudit: document.querySelector("#export-governance-audit"),
  governanceExceptionDialog: document.querySelector("#governance-exception-dialog"),
  governanceExceptionForm: document.querySelector("#governance-exception-form"),
  governanceExceptionTitle: document.querySelector("#governance-exception-title"),
  governanceExceptionTarget: document.querySelector("#governance-exception-target"),
  governanceExceptionOwner: document.querySelector("#governance-exception-owner"),
  governanceExceptionExpiry: document.querySelector("#governance-exception-expiry"),
  governanceExceptionReasonLabel: document.querySelector("#governance-exception-reason-label"),
  governanceExceptionReason: document.querySelector("#governance-exception-reason"),
  governanceExceptionApprovalFields: document.querySelector(".governance-exception-approval-fields"),
  governanceExceptionError: document.querySelector("#governance-exception-error"),
  cancelGovernanceException: document.querySelector("#cancel-governance-exception"),
  submitGovernanceException: document.querySelector("#submit-governance-exception"),
  governanceWorkspaceRows: document.querySelector("#governance-workspace-rows"),
  governanceGatewayRows: document.querySelector("#governance-gateway-rows"),
  governanceGatewayEmpty: document.querySelector("#governance-gateway-empty"),
  governanceNoFindings: document.querySelector("#governance-no-findings"),
  governanceAccessRows: document.querySelector("#governance-access-rows"),
  governanceConnectionRows: document.querySelector("#governance-connection-rows"),
  governanceArtifactRows: document.querySelector("#governance-artifact-rows"),
  governanceHistoryRows: document.querySelector("#governance-history-rows"),
  governanceHistoryEmpty: document.querySelector("#governance-history-empty"),
  governanceHistorySearch: document.querySelector("#governance-history-search"),
  governanceHistoryCount: document.querySelector("#governance-history-count"),
  governanceHistoryError: document.querySelector("#governance-history-error"),
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
  dashboardTitle: document.querySelector("#dashboard-title"),
  dashboardDescription: document.querySelector("#dashboard-description"),
  submissionControls: document.querySelector("#submission-controls"),
  submissionStatus: document.querySelector("#submission-status"),
  submitReviewWorkflow: document.querySelector("#submit-review-workflow"),
  exportButton: document.querySelector("#export-feedback"),
  exportFeedbackStatus: document.querySelector("#export-feedback-status"),
  status: document.querySelector("#status"),
  error: document.querySelector("#error"),
  reviewErrorDialog: document.querySelector("#review-error-dialog"),
  reviewErrorMessage: document.querySelector("#review-error-message"),
  closeReviewError: document.querySelector("#close-review-error"),
  reviewSuccessDialog: document.querySelector("#review-success-dialog"),
  reviewSuccessMessage: document.querySelector("#review-success-message"),
  openReviewResults: document.querySelector("#open-review-results"),
  reviewResults: document.querySelector("#review-results"),
  findingsScrollPanel: document.querySelector("#findings-scroll-panel"),
  overview: document.querySelector("#overview"),
  project: document.querySelector("#project-name"),
  findingsCount: document.querySelector("#finding-count"),
  tables: document.querySelector("#table-count"),
  measures: document.querySelector("#measure-count"),
  relationships: document.querySelector("#relationship-count"),
  list: document.querySelector("#findings-list"),
  findingCategoryTabs: [...document.querySelectorAll("[data-finding-category]")],
  empty: document.querySelector("#empty-state"),
  resultCount: document.querySelector("#result-count"),
  severity: document.querySelector("#severity-filter"),
  search: document.querySelector("#search-filter")
};

let activeLoadingCount = 0;
let loadingIndicatorTimer = null;
const nativeFetch = window.fetch.bind(window);

function beginGlobalLoading(message = "Working…") {
  activeLoadingCount++;
  if (activeLoadingCount === 1) {
    elements.appLoadingMessage.textContent = message;
    loadingIndicatorTimer = window.setTimeout(() => {
      if (activeLoadingCount > 0) {
        elements.appLoadingIndicator.hidden = false;
        elements.appLoadingIndicator.setAttribute("aria-hidden", "false");
        document.body.setAttribute("aria-busy", "true");
      }
    }, 180);
  }

  let completed = false;
  return () => {
    if (completed) {
      return;
    }
    completed = true;
    activeLoadingCount = Math.max(0, activeLoadingCount - 1);
    if (activeLoadingCount === 0) {
      window.clearTimeout(loadingIndicatorTimer);
      elements.appLoadingIndicator.hidden = true;
      elements.appLoadingIndicator.setAttribute("aria-hidden", "true");
      document.body.removeAttribute("aria-busy");
    }
  };
}

const responseBodyMethods = new Set(["arrayBuffer", "blob", "formData", "json", "text"]);

window.fetch = async (...args) => {
  const finishLoading = beginGlobalLoading();
  let response;
  try {
    response = await nativeFetch(...args);
  } catch (error) {
    finishLoading();
    throw error;
  }

  if (!response.body) {
    finishLoading();
    return response;
  }

  let bodyConsumptionStarted = false;
  window.setTimeout(() => {
    if (!bodyConsumptionStarted) {
      finishLoading();
    }
  }, 0);

  return new Proxy(response, {
    get(target, property) {
      const value = Reflect.get(target, property, target);
      if (typeof value !== "function") {
        return value;
      }
      if (responseBodyMethods.has(property)) {
        return (...methodArgs) => {
          bodyConsumptionStarted = true;
          try {
            return Promise.resolve(value.apply(target, methodArgs)).finally(finishLoading);
          } catch (error) {
            finishLoading();
            throw error;
          }
        };
      }
      return value.bind(target);
    }
  });
};

let currentFindings = [];
let activeFindingCategory = "all";
let currentProject = "";
let currentReviewRunId = "";
let feedback = {};
let sharedFeedbackAvailable = false;
let feedbackAvailabilityChecked = false;
let feedbackAvailabilityError = "";
let feedbackAvailabilityRequest = null;
let dashboardItems = [];
let dashboardStatus = "Open";
let configurationVersion = 0;
let savedConfiguration = null;
let governanceConfigurationVersion = 0;
let moduleAvailability = null;
let activeWorkspaceView = "reviews";
let governanceCheckCatalogLoaded = false;
let governanceScanScopeLoaded = false;
let governanceScanScopeLoadingPromise = null;
let governanceArtifactCatalogs = new Map();
let governanceArtifactSelections = new Map();
const governanceGlobalArtifactCatalogs = { Apps: null, Gateways: null };
const governanceGlobalArtifactErrors = {};
let governanceArtifactCatalogWorkspaceKey = "";
let governanceArtifactCatalogRequest = 0;
let pendingGovernanceScan = null;
let governanceScanPreparing = false;
let applicationSession = null;
let governancePowerBiAuthorized = null;
let governanceFindingFilter = "all";
let governanceFindingCheckFilter = "";
let currentGovernanceFindings = [];
let currentGovernanceAudit = null;
let currentGovernanceRunId = "";
let governanceExceptionTarget = null;
let currentSubmission = null;
let submissionDrafts = {};
let reviewerResponseDrafts = {};
let reviewerSubmissionMode = false;
const reviewerId = loadReviewerId();

elements.sidebarToggle.addEventListener("click", toggleSidebar);
elements.applicationStateRetry.addEventListener("click", () => window.location.reload());
for (const tab of elements.reviewPageTabs) {
  tab.addEventListener("click", () => navigateReviewPage(tab.dataset.reviewPageTab));
}
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
for (const tab of elements.governanceScanTabs) {
  tab.addEventListener("click", () => {
    void requestGovernanceScanTab(tab.dataset.governanceScanTab);
  });
}
document.querySelector(".governance-scan-tabs").addEventListener("keydown", handleGovernanceScanTabKeydown);
for (const tab of elements.governancePageTabs) {
  tab.addEventListener("click", () => navigateGovernancePage(tab.dataset.governancePageTab));
}
elements.governanceHistorySearch.addEventListener("input", filterGovernanceAuditHistory);
elements.governanceScanWorkspaces.addEventListener("change", () => {
  governanceArtifactCatalogWorkspaceKey = "";
  if (document.querySelector('[data-governance-scan-tab="artifacts"][aria-selected="true"]')) {
    void loadGovernanceArtifactCatalogs();
  }
});
elements.governanceArtifactSelection.addEventListener("change", event => {
  const artifactType = event.target.dataset.governanceArtifact;
  if (artifactType) {
    for (const checkbox of elements.governanceArtifactItems.querySelectorAll(
      `[data-governance-artifact-item="${artifactType}"]`
    )) {
      checkbox.disabled = !event.target.checked;
    }
    if (event.target.checked &&
        (artifactType === "Apps" || artifactType === "Gateways") &&
        document.querySelector('[data-governance-scan-tab="artifacts"][aria-selected="true"]')) {
      void loadGovernanceArtifactCatalogs();
    }
  }
});
elements.governanceArtifactItems.addEventListener("change", event => {
  const checkbox = event.target;
  if (!checkbox.matches("[data-governance-artifact-item]")) {
    return;
  }

  const artifactType = checkbox.dataset.governanceArtifactItem;
  const selectionKey = governanceArtifactSelectionKey(artifactType, checkbox.dataset.workspaceId);
  const selectedIds = governanceArtifactSelections.get(selectionKey) || new Set();
  if (checkbox.checked) {
    selectedIds.add(checkbox.dataset.artifactId);
  } else {
    selectedIds.delete(checkbox.dataset.artifactId);
  }
  governanceArtifactSelections.set(selectionKey, selectedIds);
});
window.addEventListener("popstate", routeWorkspaceFromLocation);
elements.reloadConfiguration.addEventListener("click", loadReviewConfiguration);
elements.runGovernanceScan.addEventListener("click", runGovernanceScan);
elements.governanceWorkspacesNext.addEventListener("click", continueToGovernanceArtifacts);
elements.governanceArtifactsBack.addEventListener("click", () => void requestGovernanceScanTab("workspaces"));
elements.governanceArtifactsNext.addEventListener("click", continueToGovernanceChecks);
elements.governanceChecksBack.addEventListener("click", () => void requestGovernanceScanTab("artifacts"));
elements.checkGovernancePowerBiAccess.addEventListener("click", () => {
  void loadGovernancePowerBiAccess();
});
elements.governanceAccessNext.addEventListener("click", () => {
  void requestGovernanceScanTab("workspaces");
});
elements.openGovernanceResults.addEventListener("click", () => {
  elements.governanceResultsDialog.close();
  navigateGovernancePage("results");
});
elements.governanceResultCounts.addEventListener("click", handleGovernanceResultCardClick);
elements.governanceResultsDialogCounts.addEventListener("click", handleGovernanceResultCardClick);
elements.governanceResultFacets.addEventListener("change", applyGovernanceResultFilters);
elements.governanceResultFilterToggle.addEventListener("click", () => {
  const checkboxes = [...elements.governanceResultFacets.querySelectorAll("input[type=checkbox]")];
  const clearFilters = elements.governanceResultFilterToggle.textContent === "Clear filters";
  for (const checkbox of checkboxes) {
    checkbox.checked = clearFilters;
  }
  applyGovernanceResultFilters();
});
elements.clearGovernanceFindingScope.addEventListener("click", () => {
  governanceFindingCheckFilter = "";
  renderGovernanceFindingList();
});
elements.exportGovernanceAudit.addEventListener("click", exportGovernanceAudit);
for (const filter of elements.governanceFindingFilters) {
  filter.addEventListener("click", () => {
    governanceFindingCheckFilter = "";
    governanceFindingFilter = filter.dataset.governanceFindingFilter;
    for (const candidate of elements.governanceFindingFilters) {
      const selected = candidate === filter;
      candidate.classList.toggle("is-active", selected);
      candidate.setAttribute("aria-pressed", String(selected));
    }
    elements.governanceFindingList.addEventListener("click", handleGovernanceExceptionAction);
    elements.governanceExceptionForm.addEventListener("submit", submitGovernanceException);
    elements.cancelGovernanceException.addEventListener("click", () => {
      elements.governanceExceptionDialog.close();
    });
    renderGovernanceFindingList();
  });
}
elements.confirmGovernanceScan.addEventListener("click", () => {
  if (!pendingGovernanceScan) {
    return;
  }

  const selection = pendingGovernanceScan;
  pendingGovernanceScan = null;
  elements.governanceScanConfirmationDialog.close();
  void executeGovernanceScan(selection);
});
elements.cancelGovernanceScan.addEventListener("click", () => {
  pendingGovernanceScan = null;
  elements.governanceScanConfirmationDialog.close();
});
elements.governanceScanConfirmationDialog.addEventListener("cancel", () => {
  pendingGovernanceScan = null;
});
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
  navigateReviewPage("results");
});
elements.history.addEventListener("change", loadSelectedReview);
elements.reviewSearchForm.addEventListener("submit", searchReviewHistory);
elements.refreshDashboard.addEventListener("click", loadDashboard);
elements.waitingTab.addEventListener("click", () => setDashboardStatus("Open"));
elements.closedTab.addEventListener("click", () => setDashboardStatus("Closed"));
elements.submitReviewWorkflow.addEventListener("click", submitCurrentReviewWorkflow);
elements.exportButton.addEventListener("click", exportFeedback);
elements.severity.addEventListener("change", renderFindings);
elements.search.addEventListener("input", renderFindings);
elements.governanceWorkspaceNameFilter.addEventListener("input", filterGovernanceScanWorkspaces);
elements.governanceWorkspaceCapacityFilter.addEventListener("change", filterGovernanceScanWorkspaces);
elements.governanceSelectFilteredWorkspaces.addEventListener("click", () => {
  for (const label of elements.governanceScanWorkspaces.querySelectorAll(
    "[data-governance-workspace-option]"
  )) {
    label.querySelector("[data-governance-scan-workspace]").checked = !label.hidden;
  }
});
for (const tab of elements.findingCategoryTabs) {
  tab.addEventListener("click", () => {
    activeFindingCategory = tab.dataset.findingCategory || "all";
    elements.findingsScrollPanel.scrollTop = 0;
    renderFindings();
  });
  tab.addEventListener("keydown", event => {
    const currentIndex = elements.findingCategoryTabs.indexOf(tab);
    const nextIndex = event.key === "ArrowRight"
      ? (currentIndex + 1) % elements.findingCategoryTabs.length
      : event.key === "ArrowLeft"
        ? (currentIndex - 1 + elements.findingCategoryTabs.length) % elements.findingCategoryTabs.length
        : event.key === "Home"
          ? 0
          : event.key === "End"
            ? elements.findingCategoryTabs.length - 1
            : -1;
    if (nextIndex < 0) {
      return;
    }
    event.preventDefault();
    elements.findingCategoryTabs[nextIndex].focus();
    elements.findingCategoryTabs[nextIndex].click();
  });
}
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
  "dax-format": "Flags FORMAT calls in measure expressions.",
  "unused-tables": "Finds tables with no detected DAX or report usage; relationships alone do not establish table usage.",
  "unused-columns": "Finds columns with no detected relationship, DAX, or report references.",
  "unused-measures": "Finds measures with no detected DAX or report references."
};

function setWorkspaceView(view) {
  const isAdministration = view === "administration";
  const wasAdministration = activeWorkspaceView === "administration";
  const wasGovernanceAudit = activeWorkspaceView === "governanceAudit";
  const selectedModule = modules.find(module => module.key === view);
  if (!isAdministration && (!selectedModule || !isModuleAvailableToCurrentUser(selectedModule))) {
    return;
  }

  activeWorkspaceView = view;
  elements.workspaceLoading.hidden = true;
  const isReviews = view === "reviews";
  elements.reviewWorkspace.hidden = !isReviews;
  if (isReviews) {
    const requestedPage = new URLSearchParams(window.location.search).get("page");
    setReviewPage(requestedPage || "run");
  }
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
  if (isGovernanceAudit && (!wasGovernanceAudit || governancePowerBiAuthorized === null)) {
    void loadGovernancePowerBiAccess();
  }
}

function isModuleAvailableToCurrentUser(module) {
  return moduleAvailability?.[module.key] === true &&
    !(module.key === "governanceAudit" &&
      applicationSession?.authenticationEnabled &&
      applicationSession.isAdmin !== true);
}

function showNoAvailableModules() {
  activeWorkspaceView = "unavailable";
  elements.workspaceLoading.hidden = true;
  elements.reviewWorkspace.hidden = true;
  elements.governanceWorkspace.hidden = true;
  elements.adminConfiguration.hidden = true;
  elements.modulePlaceholder.hidden = false;
  elements.modulePlaceholderEyebrow.textContent = "NO MODULES AVAILABLE";
  elements.modulePlaceholderTitle.textContent = "No workspace modules are enabled";
  elements.modulePlaceholderDescription.textContent =
    "There are no modules available for your account.";
  elements.modulePlaceholderStatus.textContent =
    "Contact your BI Sentinel administrator to enable a workspace module.";
  for (const navigation of elements.moduleNavigations) {
    navigation.hidden = true;
  }
}

function routeWorkspaceFromLocation() {
  const route = window.location.pathname;
  if (route === "/admin" || route.startsWith("/admin/")) {
    if (applicationSession?.authenticationEnabled && !applicationSession.isAdmin) {
      const availableModule = modules.find(isModuleAvailableToCurrentUser);
      if (availableModule) {
        navigateWorkspace(availableModule.key, true);
      } else {
        showNoAvailableModules();
      }
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
  if (selectedModule && isModuleAvailableToCurrentUser(selectedModule)) {
    setWorkspaceView(selectedModule.key);
    if (selectedModule.key === "governanceAudit") {
      const requestedPage = new URLSearchParams(window.location.search).get("page");
      setGovernancePage(requestedPage || "run");
    }
    return;
  }

  const fallbackModule = modules.find(isModuleAvailableToCurrentUser);
  if (fallbackModule) {
    navigateWorkspace(fallbackModule.key, true);
  } else {
    showNoAvailableModules();
  }
}

function navigateWorkspace(view, replace = false) {
  const selectedModule = modules.find(module => module.key === view);
  if (selectedModule && !isModuleAvailableToCurrentUser(selectedModule)) {
    return;
  }
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

function navigateGovernancePage(pageName) {
  if (activeWorkspaceView !== "governanceAudit") {
    return;
  }
  const requestedPage = pageName === "results" && !currentGovernanceAudit ? "run" : pageName;
  if (!elements.governancePages.some(page => page.dataset.governancePage === requestedPage)) {
    return;
  }

  const query = new URLSearchParams(window.location.search);
  if (requestedPage === "run") {
    query.delete("page");
  } else {
    query.set("page", requestedPage);
  }
  const suffix = query.toString();
  const url = `${window.location.pathname}${suffix ? `?${suffix}` : ""}${window.location.hash}`;
  if (`${window.location.pathname}${window.location.search}${window.location.hash}` !== url) {
    window.history.pushState(null, "", url);
  }
  setGovernancePage(requestedPage);
}

function navigateReviewPage(pageName) {
  if (activeWorkspaceView !== "reviews") {
    return;
  }
  const requestedPage = pageName === "results" && !currentReviewRunId ? "run" : pageName;
  if (!elements.reviewPages.some(page => page.dataset.reviewPage === requestedPage)) {
    return;
  }

  const query = new URLSearchParams(window.location.search);
  if (requestedPage === "run") {
    query.delete("page");
  } else {
    query.set("page", requestedPage);
  }
  const suffix = query.toString();
  const url = `${window.location.pathname}${suffix ? `?${suffix}` : ""}${window.location.hash}`;
  if (`${window.location.pathname}${window.location.search}${window.location.hash}` !== url) {
    window.history.pushState(null, "", url);
  }
  setReviewPage(requestedPage);
}

function setReviewPage(pageName) {
  const page = pageName === "results" && !currentReviewRunId ? "run" : pageName;
  const selectedPage = elements.reviewPages.find(item => item.dataset.reviewPage === page)
    ?? elements.reviewPages.find(item => item.dataset.reviewPage === "run");
  for (const section of elements.reviewPages) {
    section.hidden = section !== selectedPage;
  }
  for (const tab of elements.reviewPageTabs) {
    const isSelected = tab.dataset.reviewPageTab === selectedPage.dataset.reviewPage;
    tab.classList.toggle("is-active", isSelected);
    tab.disabled = tab.dataset.reviewPageTab === "results" && !currentReviewRunId;
    if (isSelected) {
      tab.setAttribute("aria-current", "page");
    } else {
      tab.removeAttribute("aria-current");
    }
  }
  elements.pageContent.scrollTo({ top: 0, behavior: "smooth" });
  if (selectedPage.dataset.reviewPage === "history") {
    void loadDashboard();
  }
}

function setGovernancePage(pageName) {
  const page = pageName === "results" && !currentGovernanceAudit ? "run" : pageName;
  const selectedPage = elements.governancePages.find(item => item.dataset.governancePage === page)
    ?? elements.governancePages.find(item => item.dataset.governancePage === "run");
  for (const section of elements.governancePages) {
    section.hidden = section !== selectedPage;
  }
  for (const tab of elements.governancePageTabs) {
    const isSelected = tab.dataset.governancePageTab === selectedPage.dataset.governancePage;
    tab.classList.toggle("is-active", isSelected);
    if (isSelected) {
      tab.setAttribute("aria-current", "page");
    } else {
      tab.removeAttribute("aria-current");
    }
  }
  elements.pageContent.scrollTo({ top: 0, behavior: "smooth" });
  if (selectedPage.dataset.governancePage === "history") {
    void loadGovernanceAuditHistory();
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
    tab.classList.toggle("is-active", isSelected);
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
    showApplicationStatePage(
      "Workspace modules couldn’t be loaded",
      "BI Sentinel could not retrieve the workspace settings. Please try again. If the problem continues, contact your BI Sentinel administrator."
    );
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
      const assignedRoles = [];
      if (user.isAdmin) assignedRoles.push(user.isBootstrapAdmin ? "Admin (bootstrap)" : "Admin");
      if (user.isManager) assignedRoles.push("Manager");
      if (user.isLead) assignedRoles.push("Lead");
      roleCell.textContent = assignedRoles.join(", ") || "Submitter";
      roleButton.type = "button";
      roleButton.className = "button button-secondary";
      roleButton.textContent = user.isAdmin ? "Remove admin" : "Make admin";
      roleButton.disabled = user.isBootstrapAdmin;
      roleButton.addEventListener("click", () => {
        void setApplicationUserAdmin(user, !user.isAdmin, roleButton);
      });
      actionCell.append(roleButton);
      for (const [roleName, label, isGranted] of [
        ["ReviewManager", user.isManager ? "Remove Manager" : "Make Manager", user.isManager],
        ["ReviewLead", user.isLead ? "Remove Lead" : "Make Lead", user.isLead]
      ]) {
        const roleToggle = document.createElement("button");
        roleToggle.type = "button";
        roleToggle.className = "button button-secondary button-small";
        roleToggle.textContent = label;
        roleToggle.addEventListener("click", () => {
          void setApplicationUserReviewRole(user, roleName, !isGranted, roleToggle);
        });
        actionCell.append(roleToggle);
      }
      row.append(nameCell, emailCell, firstSeenCell, roleCell, actionCell);
      elements.applicationUsersRows.append(row);
    }

    async function setApplicationUserReviewRole(user, roleName, isGranted, button) {
      button.disabled = true;
      elements.applicationUsersError.hidden = true;
      try {
        const response = await fetch(
          `/api/admin/application-users/${encodeURIComponent(user.userId)}/review-role`,
          {
            method: "PUT",
            headers: { "Content-Type": "application/json", Accept: "application/json" },
            body: JSON.stringify({ roleName, isGranted })
          });
        if (!response.ok) {
          throw new Error(await readApiError(response, "Reviewer role could not be changed."));
        }

        await loadApplicationUsers();
      } catch (error) {
        button.disabled = false;
        elements.applicationUsersError.textContent =
          error instanceof Error ? error.message : "Reviewer role could not be changed.";
        elements.applicationUsersError.hidden = false;
      }
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
  } catch {
    elements.workspaceLoading.hidden = true;
    showApplicationStatePage(
      "We couldn’t verify your access",
      "Your workspace is temporarily unavailable. Please try again. If the problem continues, contact your BI Sentinel administrator."
    );
    return;
  }

  await loadModuleNavigation();
}

function showApplicationStatePage(title, description) {
  elements.topbar.hidden = true;
  elements.workspaceLayout.hidden = true;
  elements.applicationStateTitle.textContent = title;
  elements.applicationStateDescription.textContent = description;
  elements.applicationStatePage.hidden = false;
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
    window.location.assign("/auth/sign-in");
    return;
  }

  elements.accountControls.hidden = false;
  elements.accountName.textContent =
    applicationSession.displayName || applicationSession.emailAddress || "Signed-in user";
  elements.administrationNavigation.hidden = !applicationSession.isAdmin;
  updateExportButton();
  if (isReviewReviewer()) {
    void checkSharedFeedbackAvailability();
  }
}

function renderModuleAvailability(availability) {
  if (!availability || typeof availability !== "object") {
    throw new Error("Module availability settings are missing from the saved configuration.");
  }

  moduleAvailability = Object.fromEntries(
    modules.map(module => [module.key, availability[module.key] === true])
  );
  for (const navigation of elements.moduleNavigations) {
    const module = modules.find(item => item.key === navigation.dataset.moduleNavigation);
    navigation.hidden = !module || !isModuleAvailableToCurrentUser(module) ||
      (navigation.dataset.adminOnly === "true" && applicationSession?.isAdmin !== true);
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
    const firstEnabledModule = modules.find(isModuleAvailableToCurrentUser);
    if (firstEnabledModule) {
      navigateWorkspace(firstEnabledModule.key, true);
    } else {
      showNoAvailableModules();
    }
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
    governanceScanScopeLoaded = false;
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

function filterGovernanceScanWorkspaces() {
  const nameQuery = elements.governanceWorkspaceNameFilter.value.trim().toLocaleLowerCase();
  const capacity = elements.governanceWorkspaceCapacityFilter.value;
  for (const label of elements.governanceScanWorkspaces.querySelectorAll(
    "[data-governance-workspace-option]"
  )) {
    const nameMatches = label.dataset.workspaceName.toLocaleLowerCase().includes(nameQuery);
    const isDedicated = label.dataset.dedicatedCapacity === "true";
    const capacityMatches = capacity === "all" ||
      (capacity === "dedicated" && isDedicated) ||
      (capacity === "shared" && !isDedicated);
    label.hidden = !nameMatches || !capacityMatches;
  }
}

function useGovernanceWorkspaceSelection() {
  const selectedIds = [...elements.governanceWorkspaceDiscovery.querySelectorAll(
    "[data-governance-workspace]:checked"
  )].map(checkbox => checkbox.dataset.governanceWorkspace);
  elements.governanceWorkspaceIds.value = selectedIds.join("\n");
}

async function loadGovernanceScanScope() {
  if (governanceScanScopeLoaded) {
    return true;
  }
  if (governanceScanScopeLoadingPromise) {
    return governanceScanScopeLoadingPromise;
  }

  governanceScanScopeLoadingPromise = fetchGovernanceScanScope();
  try {
    return await governanceScanScopeLoadingPromise;
  } finally {
    governanceScanScopeLoadingPromise = null;
  }
}

async function fetchGovernanceScanScope() {
  elements.governanceScanWorkspaces.replaceChildren();
  try {
    const scopeResponse = await fetch("/api/governance-audit/scan-scope", {
      headers: { Accept: "application/json" }
    });
    if (!scopeResponse.ok) {
      throw new Error(await readApiError(scopeResponse, "The approved scan scope could not be loaded."));
    }

    const scope = await scopeResponse.json();
    const workspaceIds = [...new Set(scope.workspaceIds)];
    if (workspaceIds.length === 0) {
      const emptyState = document.createElement("p");
      emptyState.className = "dashboard-empty";
      emptyState.textContent =
        "No workspaces are approved yet. Add the approved workspace IDs under Administration → Governance Audit.";
      elements.governanceScanWorkspaces.append(emptyState);
      governanceScanScopeLoaded = true;
      return true;
    }
    if (workspaceIds.length > scope.maximumWorkspaces) {
      throw new Error(
        "The approved scope exceeds the server's maximum scan size. Reduce it under Administration → Governance Audit."
      );
    }

    const workspaceResponse = await fetch("/api/governance-audit/scan-workspaces", {
      headers: { Accept: "application/json" }
    });
    if (!workspaceResponse.ok) {
      throw new Error(await readApiError(workspaceResponse, "Approved workspace names could not be loaded."));
    }
    const workspaces = await workspaceResponse.json();
    const workspaceDetails = new Map(workspaces.map(workspace => [
      workspace.id.toLocaleLowerCase(),
      workspace
    ]));
    const missingNames = workspaceIds.filter(workspaceId => {
      const workspace = workspaceDetails.get(workspaceId.toLocaleLowerCase());
      return !workspace || !workspace.name;
    });
    if (missingNames.length > 0) {
      throw new Error(
        `${missingNames.length} approved workspace name(s) could not be verified from Power BI. Check the delegated access and workspace allowlist.`
      );
    }

    for (const workspaceId of workspaceIds) {
      const label = document.createElement("label");
      label.className = "module-availability-option";
      label.dataset.governanceWorkspaceOption = "";
      label.dataset.workspaceName = workspaceDetails.get(workspaceId.toLocaleLowerCase()).name;
      label.dataset.dedicatedCapacity = String(
        workspaceDetails.get(workspaceId.toLocaleLowerCase()).isOnDedicatedCapacity === true
      );
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.dataset.governanceScanWorkspace = workspaceId;
      checkbox.checked = true;
      const name = document.createElement("span");
      name.textContent = workspaceDetails.get(workspaceId.toLocaleLowerCase()).name;
      label.append(checkbox, name);
      elements.governanceScanWorkspaces.append(label);
    }
    filterGovernanceScanWorkspaces();
    governanceScanScopeLoaded = true;
    return true;
  } catch (error) {
    elements.governanceScanWorkspaces.textContent =
      error instanceof Error ? error.message : "The approved scan scope could not be loaded.";
    return false;
  }
}

async function prepareGovernanceWorkspaceStep() {
  if (!governanceScanScopeLoaded) {
    activateGovernanceScanTab("access");
    elements.governanceScanWorkspaces.replaceChildren();
  }
  elements.governanceWorkspaceLoadStatus.hidden = false;
  elements.governanceWorkspaceLoadStatus.classList.remove("is-error", "is-loaded");
  elements.governanceWorkspaceLoadMessage.textContent = "Loading approved workspace names from Power BI…";
  elements.governanceAccessNext.disabled = true;
  const workspaceTab = elements.governanceScanTabs.find(tab => tab.dataset.governanceScanTab === "workspaces");
  workspaceTab.disabled = true;

  const loaded = await loadGovernanceScanScope();
  if (!loaded) {
    elements.governanceWorkspaceLoadStatus.classList.add("is-error");
    const detail = elements.governanceScanWorkspaces.textContent.trim();
    elements.governanceWorkspaceLoadMessage.textContent = detail
      ? `Workspace names could not be verified: ${detail}`
      : "Workspace names could not be verified. Check Power BI access and the approved workspace scope, then check access again.";
    return false;
  }

  const workspaceCount = elements.governanceScanWorkspaces.querySelectorAll(
    "[data-governance-scan-workspace]"
  ).length;
  elements.governanceWorkspaceLoadStatus.classList.add("is-loaded");
  elements.governanceWorkspaceLoadMessage.textContent = workspaceCount > 0
    ? `${workspaceCount} approved workspace${workspaceCount === 1 ? "" : "s"} ready.`
    : "No workspaces approved yet. Add one in Governance Audit Administration.";
  workspaceTab.disabled = false;
  elements.governanceAccessNext.disabled = workspaceCount === 0;
  return true;
}

function activateGovernanceScanTab(tabName) {
  if (tabName !== "access" && governancePowerBiAuthorized !== true) {
    return;
  }
  for (const tab of elements.governanceScanTabs) {
    const isActive = tab.dataset.governanceScanTab === tabName;
    tab.setAttribute("aria-selected", String(isActive));
    tab.tabIndex = isActive ? 0 : -1;
    tab.classList.toggle("is-active", isActive);
  }
  for (const panel of elements.governanceScanPanels) {
    panel.hidden = panel.dataset.governanceScanPanel !== tabName;
  }
}

async function requestGovernanceScanTab(tabName) {
  if (tabName === "access") {
    activateGovernanceScanTab("access");
    return;
  }
  if (governancePowerBiAuthorized !== true) {
    return;
  }
  if (tabName === "workspaces") {
    if (await prepareGovernanceWorkspaceStep()) {
      activateGovernanceScanTab("workspaces");
    }
    return;
  }
  if (tabName === "artifacts") {
    await continueToGovernanceArtifacts();
  } else if (tabName === "checks") {
    await continueToGovernanceChecks();
  }
}

async function loadGovernancePowerBiAccess() {
  elements.checkGovernancePowerBiAccess.disabled = true;
  elements.governancePowerBiAccessStatus.classList.remove("is-authorized", "is-unauthorized");
  elements.governancePowerBiAccessStatus.querySelector("span:last-child").textContent =
    "Checking delegated Power BI access…";
  elements.governancePowerBiAccessStatus.querySelector(".governance-status-icon")
    .replaceChildren(createGovernanceIcon("information"));
  try {
    const response = await fetch("/api/governance-audit/access-status", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Power BI access could not be checked."));
    }

    const result = await response.json();
    governancePowerBiAuthorized = result.authorized === true;
    const statusText = elements.governancePowerBiAccessStatus.querySelector("span:last-child");
    const icon = elements.governancePowerBiAccessStatus.querySelector(".governance-status-icon");
    if (governancePowerBiAuthorized) {
      elements.governancePowerBiAccessStatus.classList.add("is-authorized");
      statusText.textContent = result.modelMetadataAuthorized === true
        ? "Power BI access is ready. OPT checks still require a Fabric administrator and enabled tenant metadata-scanning settings."
        : "Workspace access is ready, but Tenant.Read.All consent is missing. Authorize metadata access to run OPT checks; those checks will otherwise show insufficient evidence.";
      icon.replaceChildren(createGovernanceIcon("check"));
      elements.authorizeGovernancePowerBi.hidden = result.modelMetadataAuthorized === true;
      elements.governanceAccessNext.disabled = true;
      for (const tab of elements.governanceScanTabs) {
        tab.disabled = tab.dataset.governanceScanTab !== "access";
      }
      void loadGovernanceCheckCatalog();
      void prepareGovernanceWorkspaceStep();
      void loadGovernanceAuditHistory();
    } else {
      elements.governancePowerBiAccessStatus.classList.add("is-unauthorized");
      statusText.textContent =
        "Power BI read access has not been authorized. Authorize it to unlock workspace selection.";
      icon.replaceChildren(createGovernanceIcon("warning"));
      elements.authorizeGovernancePowerBi.hidden = false;
      elements.governanceAccessNext.disabled = true;
      elements.governanceWorkspaceLoadStatus.hidden = true;
      for (const tab of elements.governanceScanTabs) {
        tab.disabled = tab.dataset.governanceScanTab !== "access";
      }
      activateGovernanceScanTab("access");
    }
  } catch (error) {
    governancePowerBiAuthorized = false;
    elements.governancePowerBiAccessStatus.classList.add("is-unauthorized");
    elements.governancePowerBiAccessStatus.querySelector("span:last-child").textContent =
      error instanceof Error ? error.message : "Power BI access could not be checked.";
    elements.governancePowerBiAccessStatus.querySelector(".governance-status-icon")
      .replaceChildren(createGovernanceIcon("warning"));
    elements.authorizeGovernancePowerBi.hidden = false;
    elements.governanceAccessNext.disabled = true;
    elements.governanceWorkspaceLoadStatus.hidden = true;
    for (const tab of elements.governanceScanTabs) {
      tab.disabled = tab.dataset.governanceScanTab !== "access";
    }
    activateGovernanceScanTab("access");
  } finally {
    elements.checkGovernancePowerBiAccess.disabled = false;
  }
}

async function continueToGovernanceArtifacts() {
  const selectedWorkspaceCount = elements.governanceScanWorkspaces.querySelectorAll(
    "[data-governance-scan-workspace]:checked"
  ).length;
  if (selectedWorkspaceCount === 0) {
    showGovernanceScanError("Select at least one approved workspace before continuing.");
    return;
  }

  await loadGovernanceArtifactCatalogs();
  const workspaceKey = [...elements.governanceScanWorkspaces.querySelectorAll(
    "[data-governance-scan-workspace]:checked"
  )]
    .map(checkbox => checkbox.dataset.governanceScanWorkspace.toLocaleLowerCase())
    .sort()
    .join(",");
  if (governanceArtifactCatalogWorkspaceKey !== workspaceKey) {
    showGovernanceScanError("Reports and dashboards could not be loaded. Resolve the artifact-list error before continuing.");
    return;
  }

  elements.governanceError.hidden = true;
  elements.governanceScanTabs.find(tab => tab.dataset.governanceScanTab === "artifacts").disabled = false;
  activateGovernanceScanTab("artifacts");
}

async function continueToGovernanceChecks() {
  await loadGovernanceArtifactCatalogs();
  const selectedArtifactTypes = elements.governanceArtifactSelection.querySelectorAll(
    "[data-governance-artifact]:checked"
  );
  if (selectedArtifactTypes.length === 0) {
    showGovernanceScanError("Select at least one supported artifact type before continuing.");
    return;
  }

  const unavailableGlobalType = [...selectedArtifactTypes].find(checkbox =>
    (checkbox.dataset.governanceArtifact === "Apps" || checkbox.dataset.governanceArtifact === "Gateways") &&
    (governanceGlobalArtifactCatalogs[checkbox.dataset.governanceArtifact] === null ||
      governanceGlobalArtifactErrors[checkbox.dataset.governanceArtifact])
  );
  if (unavailableGlobalType) {
    showGovernanceScanError(governanceGlobalArtifactErrors[unavailableGlobalType.dataset.governanceArtifact] ||
      `The ${unavailableGlobalType.dataset.governanceArtifact.toLocaleLowerCase()} list could not be verified.`);
    return;
  }

  elements.governanceError.hidden = true;
  elements.governanceScanTabs.find(tab => tab.dataset.governanceScanTab === "checks").disabled = false;
  activateGovernanceScanTab("checks");
}

function showGovernanceScanError(message) {
  elements.governanceError.textContent = message;
  elements.governanceError.hidden = false;
  elements.governanceError.scrollIntoView({ behavior: "smooth", block: "center" });
}

function handleGovernanceScanTabKeydown(event) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
    return;
  }
  event.preventDefault();
  const enabledTabs = elements.governanceScanTabs.filter(tab => !tab.disabled);
  if (enabledTabs.length === 0) {
    return;
  }
  const activeIndex = Math.max(0, enabledTabs.findIndex(
    tab => tab.getAttribute("aria-selected") === "true"
  ));
  const nextIndex = event.key === "Home"
    ? 0
    : event.key === "End"
      ? enabledTabs.length - 1
      : (activeIndex + (event.key === "ArrowRight" ? 1 : -1) + enabledTabs.length) %
        enabledTabs.length;
  const nextTab = enabledTabs[nextIndex];
  nextTab.focus();
  void requestGovernanceScanTab(nextTab.dataset.governanceScanTab);
}

async function loadGovernanceArtifactCatalogs() {
  const selectedWorkspaces = [...elements.governanceScanWorkspaces.querySelectorAll(
    "[data-governance-scan-workspace]:checked"
  )];
  const workspaceIds = selectedWorkspaces.map(checkbox => checkbox.dataset.governanceScanWorkspace);
  const workspaceKey = workspaceIds.map(id => id.toLocaleLowerCase()).sort().join(",");
  const requestId = ++governanceArtifactCatalogRequest;
  const selectedArtifactTypes = [...elements.governanceArtifactSelection.querySelectorAll(
    "[data-governance-artifact]:checked"
  )].map(checkbox => checkbox.dataset.governanceArtifact);
  if (workspaceIds.length === 0) {
    governanceArtifactCatalogWorkspaceKey = "";
    elements.governanceArtifactItems.replaceChildren();
    const emptyState = document.createElement("p");
    emptyState.className = "dashboard-empty";
    emptyState.textContent = "Select at least one approved workspace to load reports and dashboards.";
    elements.governanceArtifactItems.append(emptyState);
    return;
  }
  elements.governanceArtifactItems.replaceChildren();
  const loading = document.createElement("p");
  loading.className = "dashboard-empty";
  loading.textContent = "Loading reports and dashboards for selected workspaces…";
  elements.governanceArtifactItems.append(loading);

  try {
    let nextWorkspaceIndex = 0;
    const workers = Array.from({ length: Math.min(4, workspaceIds.length) }, async () => {
      while (nextWorkspaceIndex < workspaceIds.length) {
        const workspaceId = workspaceIds[nextWorkspaceIndex++];
        const cacheKey = workspaceId.toLocaleLowerCase();
        if (governanceArtifactCatalogs.has(cacheKey)) {
          continue;
        }
        const response = await fetch(
          `/api/governance-audit/workspaces/${encodeURIComponent(workspaceId)}/artifacts`,
          { headers: { Accept: "application/json" } }
        );
        if (!response.ok) {
          throw new Error(await readApiError(response, `Artifacts for workspace could not be loaded.`));
        }
        const catalog = await response.json();
        if (catalog.workspaceId.toLocaleLowerCase() !== cacheKey) {
          throw new Error("Power BI returned an artifact catalog for a different workspace.");
        }
        governanceArtifactCatalogs.set(cacheKey, catalog);
        for (const [artifactType, artifacts] of [
          ["Reports", catalog.reports],
          ["Dashboards", catalog.dashboards]
        ]) {
          const selectionKey = governanceArtifactSelectionKey(artifactType, workspaceId);
          if (!governanceArtifactSelections.has(selectionKey)) {
            governanceArtifactSelections.set(selectionKey, new Set(artifacts.map(artifact => artifact.id)));
          }
        }
      }
    });
    const globalCatalogLoads = [];
    for (const [artifactType, endpoint] of [
      ["Apps", "/api/governance-audit/apps"],
      ["Gateways", "/api/governance-audit/gateways"]
    ]) {
      if (!selectedArtifactTypes.includes(artifactType) ||
          governanceGlobalArtifactCatalogs[artifactType] !== null) {
        continue;
      }
      globalCatalogLoads.push((async () => {
        try {
          const response = await fetch(endpoint, { headers: { Accept: "application/json" } });
          if (!response.ok) {
            throw new Error(await readApiError(response, `${artifactType} could not be loaded.`));
          }
          const artifacts = await response.json();
          governanceGlobalArtifactCatalogs[artifactType] = artifacts;
          governanceGlobalArtifactErrors[artifactType] = "";
          const selectionKey = governanceArtifactSelectionKey(artifactType, "account");
          if (!governanceArtifactSelections.has(selectionKey)) {
            governanceArtifactSelections.set(selectionKey, new Set(artifacts.map(artifact => artifact.id)));
          }
        } catch (error) {
          governanceGlobalArtifactErrors[artifactType] = error instanceof Error
            ? error.message
            : `${artifactType} could not be loaded from Power BI.`;
        }
      })());
    }
    const workerResults = await Promise.allSettled([...workers, ...globalCatalogLoads]);
    const failedWorker = workerResults.find(result => result.status === "rejected");
    if (failedWorker) {
      throw failedWorker.reason;
    }
    if (requestId !== governanceArtifactCatalogRequest) {
      return;
    }

    renderGovernanceArtifactCatalogs(selectedWorkspaces, workspaceIds, selectedArtifactTypes);
    governanceArtifactCatalogWorkspaceKey = workspaceKey;
  } catch (error) {
    if (requestId === governanceArtifactCatalogRequest) {
      elements.governanceArtifactItems.replaceChildren();
      const errorState = document.createElement("p");
      errorState.className = "error-banner";
      errorState.textContent = error instanceof Error
        ? error.message
        : "Reports and dashboards could not be loaded from Power BI.";
      elements.governanceArtifactItems.append(errorState);
      governanceArtifactCatalogWorkspaceKey = "";
    }
  }
}

function renderGovernanceArtifactCatalogs(selectedWorkspaces, workspaceIds, selectedArtifactTypes) {
  elements.governanceArtifactItems.replaceChildren();
  let artifactCount = 0;
  for (const [index, workspaceId] of workspaceIds.entries()) {
    const catalog = governanceArtifactCatalogs.get(workspaceId.toLocaleLowerCase());
    const workspaceName = selectedWorkspaces[index].parentElement.querySelector("span").textContent;
    for (const [artifactType, title, artifacts] of [
      ["Reports", "Reports", catalog.reports],
      ["Dashboards", "Dashboards", catalog.dashboards]
    ]) {
      const group = document.createElement("section");
      group.className = "governance-artifact-group";
      const heading = document.createElement("h3");
      heading.textContent = `${title} · ${workspaceName}`;
      group.append(heading);
      if (artifacts.length === 0) {
        const emptyState = document.createElement("p");
        emptyState.className = "dashboard-empty";
        emptyState.textContent = `No ${title.toLocaleLowerCase()} were returned for this workspace.`;
        group.append(emptyState);
      }
      const selection = governanceArtifactSelections.get(
        governanceArtifactSelectionKey(artifactType, workspaceId)
      );
      const typeEnabled = elements.governanceArtifactSelection.querySelector(
        `[data-governance-artifact="${artifactType}"]`
      ).checked;
      for (const artifact of artifacts) {
        const label = document.createElement("label");
        label.className = "module-availability-option";
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.dataset.governanceArtifactItem = artifactType;
        checkbox.dataset.artifactId = artifact.id;
        checkbox.dataset.workspaceId = workspaceId;
        checkbox.checked = selection.has(artifact.id);
        checkbox.disabled = !typeEnabled;
        const name = document.createElement("span");
        name.textContent = artifact.name;
        label.append(checkbox, name);
        group.append(label);
        artifactCount++;
      }
      elements.governanceArtifactItems.append(group);
    }
  }
  for (const artifactType of ["Apps", "Gateways"]) {
    if (!selectedArtifactTypes.includes(artifactType)) {
      continue;
    }
    const group = document.createElement("section");
    group.className = "governance-artifact-group";
    const heading = document.createElement("h3");
    heading.textContent = artifactType === "Apps"
      ? "Installed apps · signed-in account"
      : "Gateways · where the signed-in user is an admin";
    group.append(heading);
    if (governanceGlobalArtifactErrors[artifactType]) {
      const errorState = document.createElement("p");
      errorState.className = "error-banner";
      errorState.textContent = governanceGlobalArtifactErrors[artifactType];
      group.append(errorState);
      elements.governanceArtifactItems.append(group);
      continue;
    }
    const artifacts = governanceGlobalArtifactCatalogs[artifactType] || [];
    const selection = governanceArtifactSelections.get(
      governanceArtifactSelectionKey(artifactType, "account")
    ) || new Set();
    if (artifacts.length === 0) {
      const emptyState = document.createElement("p");
      emptyState.className = "dashboard-empty";
      emptyState.textContent = artifactType === "Gateways"
        ? "No gateways were returned for this user. Power BI lists only gateways where you are an administrator."
        : "No installed apps were returned for this user.";
      group.append(emptyState);
    }
    for (const artifact of artifacts) {
      const label = document.createElement("label");
      label.className = "module-availability-option";
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.dataset.governanceArtifactItem = artifactType;
      checkbox.dataset.artifactId = artifact.id;
      checkbox.dataset.workspaceId = "account";
      checkbox.checked = selection.has(artifact.id);
      const name = document.createElement("span");
      name.textContent = artifact.name;
      label.append(checkbox, name);
      group.append(label);
      artifactCount++;
    }
    elements.governanceArtifactItems.append(group);
  }
  if (artifactCount === 0) {
    const emptyState = document.createElement("p");
    emptyState.className = "dashboard-empty";
    emptyState.textContent = "No selectable artifacts were returned from the selected scope.";
    elements.governanceArtifactItems.append(emptyState);
  }
}

function governanceArtifactSelectionKey(artifactType, workspaceId) {
  return `${artifactType}:${workspaceId.toLocaleLowerCase()}`;
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
    governanceScanScopeLoaded = false;
    governanceArtifactCatalogs.clear();
    governanceArtifactSelections.clear();
    governanceArtifactCatalogWorkspaceKey = "";
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
  if (governanceScanPreparing) {
    return;
  }
  governanceScanPreparing = true;
  try {
    await prepareGovernanceScan();
  } finally {
    governanceScanPreparing = false;
  }
}

async function prepareGovernanceScan() {
  if (!governanceScanScopeLoaded) {
    const scopeLoaded = await loadGovernanceScanScope();
    if (!scopeLoaded) {
      elements.governanceError.textContent =
        "The approved workspace list could not be loaded. Resolve the scope error above before scanning.";
      elements.governanceError.hidden = false;
      return;
    }
  }

  if (!governanceCheckCatalogLoaded) {
    elements.governanceError.textContent = "The governance check catalog is unavailable. Reload the page before scanning.";
    elements.governanceError.hidden = false;
    return;
  }

  const selectedChecks = [...elements.governanceCheckSelection.querySelectorAll(
    "[data-governance-check]:checked"
  )].map(checkbox => checkbox.dataset.governanceCheck);
  if (selectedChecks.length === 0) {
    elements.governanceError.textContent = "Select at least one governance check before scanning.";
    elements.governanceError.hidden = false;
    return;
  }

  const selectedWorkspaceIds = [...elements.governanceScanWorkspaces.querySelectorAll(
    "[data-governance-scan-workspace]:checked"
  )].map(checkbox => checkbox.dataset.governanceScanWorkspace);
  if (selectedWorkspaceIds.length === 0) {
    elements.governanceError.textContent = "Select at least one approved workspace before scanning.";
    elements.governanceError.hidden = false;
    return;
  }

  const selectedArtifactTypes = [...elements.governanceArtifactSelection.querySelectorAll(
    "[data-governance-artifact]:checked"
  )].map(checkbox => checkbox.dataset.governanceArtifact);
  if (selectedArtifactTypes.length === 0) {
    elements.governanceError.textContent = "Select at least one supported artifact type before scanning.";
    elements.governanceError.hidden = false;
    return;
  }

  if (selectedArtifactTypes.some(type =>
    type === "Reports" || type === "Dashboards" || type === "Apps" || type === "Gateways")) {
    await loadGovernanceArtifactCatalogs();
    const workspaceKey = selectedWorkspaceIds.map(id => id.toLocaleLowerCase()).sort().join(",");
    if (governanceArtifactCatalogWorkspaceKey !== workspaceKey) {
      elements.governanceError.textContent =
        "The report and dashboard lists could not be verified for the selected workspaces. Resolve the artifact-list error before scanning.";
      elements.governanceError.hidden = false;
      return;
    }
    const unavailableGlobalType = selectedArtifactTypes.find(type =>
      (type === "Apps" || type === "Gateways") &&
      governanceGlobalArtifactCatalogs[type] === null);
    if (unavailableGlobalType) {
      elements.governanceError.textContent = governanceGlobalArtifactErrors[unavailableGlobalType] ||
        `The ${unavailableGlobalType.toLocaleLowerCase()} list could not be verified.`;
      elements.governanceError.hidden = false;
      return;
    }
    const currentWorkspaceIds = [...elements.governanceScanWorkspaces.querySelectorAll(
      "[data-governance-scan-workspace]:checked"
    )].map(checkbox => checkbox.dataset.governanceScanWorkspace);
    const currentArtifactTypes = [...elements.governanceArtifactSelection.querySelectorAll(
      "[data-governance-artifact]:checked"
    )].map(checkbox => checkbox.dataset.governanceArtifact);
    const currentCheckIds = [...elements.governanceCheckSelection.querySelectorAll(
      "[data-governance-check]:checked"
    )].map(checkbox => checkbox.dataset.governanceCheck);
    if (workspaceKey !== currentWorkspaceIds.map(id => id.toLocaleLowerCase()).sort().join(",") ||
        selectedArtifactTypes.join("|") !== currentArtifactTypes.join("|") ||
        selectedChecks.join("|") !== currentCheckIds.join("|")) {
      elements.governanceError.textContent =
        "Your scan selections changed while artifact lists were loading. Review the current selections and run again.";
      elements.governanceError.hidden = false;
      return;
    }
  }

  const reportSelection = selectedArtifactTypes.includes("Reports")
    ? getGovernanceArtifactSelection("Reports", selectedWorkspaceIds)
    : { filter: false, ids: [], names: [] };
  const dashboardSelection = selectedArtifactTypes.includes("Dashboards")
    ? getGovernanceArtifactSelection("Dashboards", selectedWorkspaceIds)
    : { filter: false, ids: [], names: [] };
  const appSelection = selectedArtifactTypes.includes("Apps")
    ? getGovernanceArtifactSelection("Apps", selectedWorkspaceIds)
    : { filter: false, ids: [], names: [] };
  const gatewaySelection = selectedArtifactTypes.includes("Gateways")
    ? getGovernanceArtifactSelection("Gateways", selectedWorkspaceIds)
    : { filter: false, ids: [], names: [] };
  for (const [artifactType, selection] of [
    ["Reports", reportSelection],
    ["Dashboards", dashboardSelection],
    ["Apps", appSelection],
    ["Gateways", gatewaySelection]
  ]) {
    if (selection.filter && selection.ids.length === 0) {
      elements.governanceError.textContent =
        `Select at least one ${artifactType === "SemanticModels" ? "semantic model" : artifactType.slice(0, -1)} or uncheck ${artifactType} before scanning.`;
      elements.governanceError.hidden = false;
      return;
    }
    if (selection.ids.some(id => !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(id))) {
      elements.governanceError.textContent =
        `The ${artifactType.toLocaleLowerCase()} list contains an invalid ID. Reload the list before scanning.`;
      elements.governanceError.hidden = false;
      return;
    }
  }
  const workspaceLabels = [...elements.governanceScanWorkspaces.querySelectorAll(
    "[data-governance-scan-workspace]:checked"
  )].map(checkbox => checkbox.parentElement.querySelector("span")?.textContent || checkbox.dataset.governanceScanWorkspace);
  const checkLabels = [...elements.governanceCheckSelection.querySelectorAll(
    "[data-governance-check]:checked"
  )].map(checkbox => checkbox.parentElement.querySelector("span")?.textContent || checkbox.dataset.governanceCheck);
  pendingGovernanceScan = {
    selectedChecks,
    selectedWorkspaceIds,
    selectedArtifactTypes,
    reportIds: reportSelection.ids,
    dashboardIds: dashboardSelection.ids,
    appIds: appSelection.ids,
    gatewayIds: gatewaySelection.ids,
    filterReports: reportSelection.filter,
    filterDashboards: dashboardSelection.filter,
    filterApps: appSelection.filter,
    filterGateways: gatewaySelection.filter
  };
  elements.governanceScanConfirmationSummary.replaceChildren();
  const confirmationSummary = [
    [`Workspaces (${workspaceLabels.length})`, workspaceLabels],
    ["Artifacts", selectedArtifactTypes.map(type => type === "SemanticModels" ? "Semantic models" : type)],
    ...(selectedArtifactTypes.includes("Reports")
      ? [["Reports", reportSelection.filter
        ? summarizeGovernanceConfirmationSelection(reportSelection)
        : ["All in selected workspaces"]]]
      : []),
    ...(selectedArtifactTypes.includes("Dashboards")
      ? [["Dashboards", dashboardSelection.filter
        ? summarizeGovernanceConfirmationSelection(dashboardSelection)
        : ["All in selected workspaces"]]]
      : []),
    ...(selectedArtifactTypes.includes("Apps")
      ? [["Apps", appSelection.filter
        ? summarizeGovernanceConfirmationSelection(appSelection)
        : ["All visible to your account"]]]
      : []),
    ...(selectedArtifactTypes.includes("Gateways")
      ? [["Gateways", gatewaySelection.filter
        ? summarizeGovernanceConfirmationSelection(gatewaySelection)
        : ["All visible to your account"]]]
      : []),
    [`Governance checks (${checkLabels.length})`, checkLabels],
    ...(selectedChecks.some(checkId => checkId.startsWith("OPT-"))
      ? [["Model metadata", ["Selected semantic models; unavailable data is reported as an evidence gap"]]]
      : [])
  ];
  for (const [label, values] of confirmationSummary) {
    const item = document.createElement("li");
    const heading = document.createElement("strong");
    heading.textContent = label;
    const detail = document.createElement("span");
    detail.textContent = values.length > 3
      ? `${values.slice(0, 3).join(", ")} + ${values.length - 3} more`
      : values.join(", ");
    item.append(heading, detail);
    elements.governanceScanConfirmationSummary.append(item);
  }
  elements.governanceScanConfirmationDialog.showModal();
}

function summarizeGovernanceConfirmationSelection(selection) {
  return [`${selection.names.length} selected`];
}

function summarizeGovernanceArtifactSelection(selection) {
  if (selection.names.length === 0) {
    return ["None selected"];
  }
  const names = selection.names.slice(0, 10);
  if (selection.names.length > names.length) {
    names.push(`and ${selection.names.length - names.length} more`);
  }
  return [`${selection.names.length} selected: ${names.join(", ")}`];
}

function getGovernanceArtifactSelection(artifactType, workspaceIds) {
  const ids = [];
  const names = [];
  let filter = false;
  if (artifactType === "Apps" || artifactType === "Gateways") {
    const artifacts = governanceGlobalArtifactCatalogs[artifactType];
    if (!artifacts) {
      throw new Error(governanceGlobalArtifactErrors[artifactType] ||
        `The ${artifactType.toLocaleLowerCase()} list could not be verified.`);
    }
    const selectedIds = governanceArtifactSelections.get(
      governanceArtifactSelectionKey(artifactType, "account")
    ) || new Set(artifacts.map(artifact => artifact.id));
    filter = selectedIds.size !== artifacts.length;
    for (const artifact of artifacts) {
      if (selectedIds.has(artifact.id)) {
        ids.push(artifact.id);
        names.push(artifact.name);
      }
    }
    return { filter, ids, names };
  }
  for (const workspaceId of workspaceIds) {
    const catalog = governanceArtifactCatalogs.get(workspaceId.toLocaleLowerCase());
    const artifacts = artifactType === "Reports" ? catalog.reports : catalog.dashboards;
    const selectedIds = governanceArtifactSelections.get(
      governanceArtifactSelectionKey(artifactType, workspaceId)
    ) || new Set(artifacts.map(artifact => artifact.id));
    if (selectedIds.size !== artifacts.length) {
      filter = true;
    }
    for (const artifact of artifacts) {
      if (selectedIds.has(artifact.id)) {
        ids.push(artifact.id);
        names.push(artifact.name);
      }
    }
  }
  return { filter, ids, names };
}

async function executeGovernanceScan({
  selectedChecks,
  selectedWorkspaceIds,
  selectedArtifactTypes,
  reportIds,
  dashboardIds,
  appIds,
  gatewayIds,
  filterReports,
  filterDashboards,
  filterApps,
  filterGateways
}) {
  elements.runGovernanceScan.disabled = true;
  elements.governanceError.hidden = true;
  elements.governanceResults.hidden = true;
  elements.governanceStatus.textContent = "Reading approved workspaces from Power BI…";
  const finishLoading = beginGlobalLoading("Scanning selected Power BI workspaces…");
  try {
    const query = new URLSearchParams();
    for (const checkId of selectedChecks) {
      query.append("checks", checkId);
    }
    for (const workspaceId of selectedWorkspaceIds) {
      query.append("workspaceIds", workspaceId);
    }
    for (const artifactType of selectedArtifactTypes) {
      query.append("artifactTypes", artifactType);
    }
    if (filterReports) {
      query.set("filterReports", "true");
      for (const reportId of reportIds) {
        query.append("reportIds", reportId);
      }
    }
    if (filterDashboards) {
      query.set("filterDashboards", "true");
      for (const dashboardId of dashboardIds) {
        query.append("dashboardIds", dashboardId);
      }
    }
    if (filterApps) {
      query.set("filterApps", "true");
      for (const appId of appIds) {
        query.append("appIds", appId);
      }
    }
    if (filterGateways) {
      query.set("filterGateways", "true");
      for (const gatewayId of gatewayIds) {
        query.append("gatewayIds", gatewayId);
      }
    }
    const response = await fetch(`/api/governance-audit/scan?${query}`, {
      method: "POST",
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Governance Audit scan failed."));
    }

    const result = await response.json();
    renderGovernanceAudit(result);
    await loadGovernanceAuditHistory();
    elements.governanceResults.hidden = false;
    elements.governanceStatus.textContent =
      `Read-only scan completed at ${new Date().toLocaleString()}.`;
    const counts = getGovernanceFindingCounts(result.findings || []);
    elements.governanceResultsDialogSummary.textContent =
      `${result.workspaces.length} workspace${result.workspaces.length === 1 ? "" : "s"} scanned.`;
    renderGovernanceResultCountCards(elements.governanceResultsDialogCounts, counts);
    elements.governanceResultsDialog.showModal();
  } catch (error) {
    elements.governanceError.textContent =
      error instanceof Error ? error.message : "Governance Audit scan failed.";
    elements.governanceError.hidden = false;
    elements.governanceStatus.textContent = "Governance Audit did not complete.";
  } finally {
    elements.runGovernanceScan.disabled = false;
    finishLoading();
  }
}

function renderGovernanceAudit(result) {
  currentGovernanceAudit = result;
  const resultsTab = elements.governancePageTabs.find(tab => tab.dataset.governancePageTab === "results");
  resultsTab.disabled = false;
  elements.governanceWorkspaceRows.replaceChildren();
  elements.governanceGatewayRows.replaceChildren();
  elements.governanceAccessRows.replaceChildren();
  elements.governanceConnectionRows.replaceChildren();
  elements.governanceArtifactRows.replaceChildren();

  currentGovernanceRunId = result.runId || "";
  currentGovernanceFindings = result.findings || [];
  governanceFindingCheckFilter = "";
  elements.exportGovernanceAudit.disabled = !currentGovernanceRunId;
  const reportCount = result.workspaces.reduce((count, audit) => count + audit.reports.length, 0);
  const dashboardCount = result.workspaces.reduce((count, audit) => count + (audit.dashboards || []).length, 0);
  const semanticModelCount = result.workspaces.reduce((count, audit) => count + audit.semanticModels.length, 0);
  const appCount = (result.apps || []).length;
  const gatewayCount = (result.gateways || []).length;
  let reportConnectionCount = 0;
  elements.governanceResultSummary.textContent =
    `${result.workspaces.length} workspaces · ${reportCount} reports · ${dashboardCount} dashboards · ${semanticModelCount} semantic models` +
    (appCount || gatewayCount ? ` · ${appCount} apps · ${gatewayCount} gateways` : "");
  renderGovernanceInventoryCounts(result);
  renderGovernanceResultFacets(result);
  const counts = getGovernanceFindingCounts(currentGovernanceFindings);
  renderGovernanceResultCountCards(elements.governanceResultCounts, counts);
  governanceFindingFilter = "all";
  for (const filter of elements.governanceFindingFilters) {
    const selected = filter.dataset.governanceFindingFilter === governanceFindingFilter;
    filter.classList.toggle("is-active", selected);
    filter.setAttribute("aria-pressed", String(selected));
  }
  for (const audit of result.workspaces) {
    const workspace = audit.workspace;
    const accessCounts = new Map();
    for (const user of audit.users || []) {
      const role = user.accessRight || "Unknown";
      accessCounts.set(role, (accessCounts.get(role) || 0) + 1);
    }
    const accessSummary = [...accessCounts.entries()]
      .map(([role, count]) => `${role}: ${count}`)
      .join(" · ") || "No principals returned";
    appendGovernanceRow(elements.governanceWorkspaceRows, [
      workspace.name,
      workspace.isOnDedicatedCapacity ? "Dedicated" : "Shared",
      audit.reports.length,
      (audit.dashboards || []).length,
      audit.semanticModels.length,
      accessSummary
    ], { workspaceId: workspace.id });

    for (const user of audit.users || []) {
      appendGovernanceRow(elements.governanceAccessRows, [
        workspace.name,
        user.displayName || user.emailAddress || user.identifier,
        user.emailAddress || user.identifier,
        user.principalType,
        user.accessRight
      ], {
        workspaceId: workspace.id,
        principalType: user.principalType,
        accessRole: user.accessRight
      });
    }

    for (const artifact of [...audit.reports, ...audit.semanticModels, ...(audit.dashboards || [])]) {
      const reportIds = artifact.type === "Report"
        ? [artifact.id]
        : audit.reports
          .filter(report => report.datasetId?.toLocaleLowerCase() === artifact.id.toLocaleLowerCase())
          .map(report => report.id);
      appendGovernanceRow(elements.governanceArtifactRows, [
        workspace.name,
        artifact.type,
        artifact.name,
        artifact.configuredBy || "Not returned by Power BI",
        artifact.status || "—"
      ], {
        workspaceId: workspace.id,
        artifactId: artifact.id,
        artifactType: artifact.type,
        reportIds
      });
    }

    for (const item of audit.reportConnections || []) {
      reportConnectionCount++;
      const gatewayName = item.gatewayId
        ? (result.gateways || []).find(gateway =>
          gateway.id.toLocaleLowerCase() === item.gatewayId.toLocaleLowerCase())?.name ||
          ((result.gateways || []).length > 0 ? "Gateway not in inventory" : "Gateway inventory not selected")
        : "No gateway";
      appendGovernanceRow(elements.governanceConnectionRows, [
        workspace.name,
        item.reportName,
        item.semanticModelName || "Not returned",
        item.sourceType || "Not returned",
        gatewayName,
        renderGovernanceHealthStatus(item.status, item.statusDetail)
      ], {
        workspaceId: workspace.id,
        reportId: item.reportId,
        reportIds: [item.reportId],
        artifactType: "Report",
        sourceType: item.sourceType,
        gatewayName
      });
    }
  }

  for (const gateway of result.gateways || []) {
    appendGovernanceRow(elements.governanceGatewayRows, [
      gateway.name,
      gateway.gatewayType || "Unknown",
      gateway.status || "Unknown"
    ], {
      artifactId: gateway.id,
      artifactType: "Gateway",
      gatewayName: gateway.name
    });
  }
  elements.governanceGatewayEmpty.hidden = (result.gateways || []).length > 0;

  if (reportConnectionCount === 0) {
    appendGovernanceRow(elements.governanceConnectionRows, [
      "No report connection details were collected for this run.",
      "", "", "", "", ""
    ]);
  }

  for (const artifact of [...(result.apps || []), ...(result.gateways || [])]) {
    if (artifact.type === "Gateway") {
      continue;
    }
    appendGovernanceRow(elements.governanceArtifactRows, [
      "Account-level",
      artifact.type,
      artifact.name,
      artifact.configuredBy || "Not returned by Power BI",
      artifact.status || "—"
    ], {
      artifactId: artifact.id,
      artifactType: artifact.type
    });
  }

  elements.governanceNoFindings.hidden = currentGovernanceFindings.length > 0;
  applyGovernanceResultFilters();
}

function renderGovernanceInventoryCounts(result) {
  const counts = [
    ["Workspaces", result.workspaces.length, "workspace"],
    ["Reports", result.workspaces.reduce((total, audit) => total + audit.reports.length, 0), "Report"],
    ["Dashboards", result.workspaces.reduce((total, audit) => total + (audit.dashboards || []).length, 0), "Dashboard"],
    ["Semantic models", result.workspaces.reduce((total, audit) => total + audit.semanticModels.length, 0), "Semantic model"],
    ["Apps", (result.apps || []).length, "App"],
    ["Gateways", (result.gateways || []).length, "Gateway"]
  ];
  elements.governanceInventoryCounts.replaceChildren();
  for (const [label, count, type] of counts) {
    const card = document.createElement("article");
    card.className = "governance-inventory-count";
    card.dataset.inventoryType = type;
    card.title = `${count} ${label.toLocaleLowerCase()}`;
    const name = document.createElement("span");
    name.textContent = label;
    const value = document.createElement("strong");
    value.dataset.inventoryCount = "";
    value.textContent = String(count);
    card.append(name, value);
    elements.governanceInventoryCounts.append(card);
  }
}

function renderGovernanceResultFacets(result) {
  const facets = [
    {
      key: "workspaceId",
      title: "Workspaces",
      values: result.workspaces.map(audit => ({
        value: audit.workspace.id,
        label: audit.workspace.name
      }))
    },
    {
      key: "reportIds",
      title: "Reports",
      values: result.workspaces.flatMap(audit => audit.reports.map(report => ({
        value: report.id,
        label: `${report.name} · ${audit.workspace.name}`
      })))
    },
    {
      key: "artifactType",
      title: "Type",
      values: [
        ...result.workspaces.flatMap(audit =>
          [...audit.reports, ...audit.semanticModels, ...(audit.dashboards || [])]
            .map(artifact => ({ value: artifact.type, label: artifact.type }))),
        ...(result.apps || []).map(artifact => ({ value: artifact.type, label: artifact.type })),
        ...(result.gateways || []).map(artifact => ({ value: artifact.type, label: artifact.type }))
      ]
    },
    {
      key: "tableName",
      title: "Tables",
      values: currentGovernanceFindings
        .filter(finding => finding.checkId === "OPT-001" && finding.artifactName)
        .map(finding => ({ value: finding.artifactName, label: finding.artifactName }))
    }
  ];

  elements.governanceResultFacets.replaceChildren();
  for (const facet of facets) {
    const values = [...new Map(facet.values.map(item => [item.value, item])).values()]
      .sort((left, right) => left.label.localeCompare(right.label));
    if (values.length === 0) {
      continue;
    }

    const fieldset = document.createElement("fieldset");
    fieldset.className = "governance-result-facet";
    const legend = document.createElement("legend");
    legend.textContent = facet.title;
    fieldset.append(legend);
    const options = document.createElement("div");
    options.className = "governance-result-facet-options";
    for (const option of values) {
      const label = document.createElement("label");
      label.className = "governance-result-facet-option";
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.checked = true;
      checkbox.dataset.resultFacet = facet.key;
      checkbox.value = option.value;
      const text = document.createElement("span");
      text.textContent = option.label;
      label.append(checkbox, text);
      options.append(label);
    }
    fieldset.append(options);
    elements.governanceResultFacets.append(fieldset);
  }
}

function getGovernanceResultFacetState() {
  const options = new Map();
  const selections = new Map();
  for (const checkbox of elements.governanceResultFacets.querySelectorAll("input[data-result-facet]")) {
    const key = checkbox.dataset.resultFacet;
    if (!options.has(key)) {
      options.set(key, 0);
      selections.set(key, new Set());
    }
    options.set(key, options.get(key) + 1);
    if (checkbox.checked) {
      selections.get(key).add(checkbox.value);
    }
  }
  return { options, selections };
}

function governanceFacetMatches(key, value, state) {
  if (!value || !state.options.has(key)) {
    return true;
  }
  return state.selections.get(key).has(value);
}

function governanceReportFacetMatches(reportIds, state) {
  if (!state.options.has("reportIds") || !reportIds || reportIds.length === 0) {
    return true;
  }
  const selectedIds = new Set(
    [...state.selections.get("reportIds")].map(reportId => reportId.toLocaleLowerCase())
  );
  return reportIds.some(reportId => selectedIds.has(reportId.toLocaleLowerCase()));
}

function getGovernanceFindingReportIds(finding) {
  if (!finding.artifactId || !currentGovernanceAudit) {
    return [];
  }
  const reportIds = [];
  for (const audit of currentGovernanceAudit.workspaces) {
    for (const report of audit.reports) {
      if (report.id.toLocaleLowerCase() === finding.artifactId.toLocaleLowerCase() ||
          report.datasetId?.toLocaleLowerCase() === finding.artifactId.toLocaleLowerCase()) {
        reportIds.push(report.id);
      }
    }
  }
  return reportIds;
}

function getGovernanceFacetFilteredFindings(state) {
  return currentGovernanceFindings.filter(finding => {
    if (!governanceFacetMatches("workspaceId", finding.workspaceId, state) ||
        !governanceFacetMatches("artifactType", finding.artifactType, state)) {
      return false;
    }
    const reportIds = getGovernanceFindingReportIds(finding);
    if (!governanceReportFacetMatches(reportIds, state)) {
      return false;
    }
    if (state.options.has("tableName") && finding.checkId === "OPT-001" &&
        finding.artifactName && !state.selections.get("tableName").has(finding.artifactName)) {
      return false;
    }
    return true;
  });
}

function applyGovernanceResultFilters() {
  const state = getGovernanceResultFacetState();
  let activeFacetCount = 0;
  for (const [key, count] of state.options) {
    if (state.selections.get(key).size < count) {
      activeFacetCount++;
    }
  }

  const resultRows = [
    elements.governanceWorkspaceRows,
    elements.governanceGatewayRows,
    elements.governanceAccessRows,
    elements.governanceConnectionRows,
    elements.governanceArtifactRows
  ];
  for (const body of resultRows) {
    for (const row of body.rows) {
      const reportIds = row.dataset.reportIds ? row.dataset.reportIds.split(",") : [];
      row.hidden = !governanceFacetMatches("workspaceId", row.dataset.workspaceId, state) ||
        !governanceFacetMatches("artifactType", row.dataset.artifactType, state) ||
        !governanceReportFacetMatches(reportIds, state);
    }
  }

  elements.governanceGatewayEmpty.hidden = [...elements.governanceGatewayRows.rows]
    .some(row => !row.hidden);
  elements.governanceGatewayEmpty.textContent = elements.governanceGatewayRows.rows.length > 0
    ? "No gateways match the selected filters."
    : currentGovernanceAudit?.selectedArtifactTypes?.includes("Gateways")
      ? "No gateways were returned for this account."
      : "Gateway inventory was not included in this scan.";
  elements.governanceResultFilterSummary.textContent = activeFacetCount
    ? `${activeFacetCount} filter${activeFacetCount === 1 ? "" : "s"} active`
    : "All audit data shown";
  elements.governanceResultFilterToggle.textContent = activeFacetCount ? "Clear filters" : "Clear all";

  const visibleInventoryCount = type => type === "workspace"
    ? [...elements.governanceWorkspaceRows.rows].filter(row => !row.hidden).length
    : [...elements.governanceArtifactRows.rows, ...elements.governanceGatewayRows.rows]
      .filter(row => !row.hidden && row.dataset.artifactType === type).length;
  for (const count of elements.governanceInventoryCounts.querySelectorAll("[data-inventory-count]")) {
    count.textContent = String(visibleInventoryCount(count.closest("[data-inventory-type]").dataset.inventoryType));
  }

  renderGovernanceFindingList();
}

async function exportGovernanceAudit() {
  if (!currentGovernanceRunId) {
    return;
  }

  elements.exportGovernanceAudit.disabled = true;
  try {
    const response = await fetch(
      `/api/governance-audit/runs/${encodeURIComponent(currentGovernanceRunId)}/export`,
      { headers: { Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } }
    );
    if (!response.ok) {
      throw new Error(await readApiError(response, "Governance Audit workbook could not be exported."));
    }

    const workbook = await response.blob();
    const downloadUrl = URL.createObjectURL(workbook);
    const link = document.createElement("a");
    link.href = downloadUrl;
    const suggestedName = /filename="([^"]+)"/i.exec(response.headers.get("Content-Disposition") || "")?.[1];
    link.download = suggestedName || `bi-sentinel-governance-audit-${new Date().toISOString().slice(0, 10)}.xlsx`;
    document.body.append(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(downloadUrl), 1000);
    elements.governanceStatus.textContent = "Excel workbook exported with findings, workspace access, artifacts, and connections.";
  } catch (error) {
    elements.governanceError.textContent = error instanceof Error
      ? error.message
      : "Governance Audit workbook could not be exported.";
    elements.governanceError.hidden = false;
  } finally {
    elements.exportGovernanceAudit.disabled = !currentGovernanceRunId;
  }
}

function getGovernanceFindingCounts(findings) {
  const counts = {
    total: findings.length,
    FAIL: 0,
    INSUFFICIENT_EVIDENCE: 0,
    EXCEPTION_APPROVED: 0,
    PASS: 0
  };
  for (const finding of findings) {
    if (Object.hasOwn(counts, finding.status)) {
      counts[finding.status] += 1;
    }
  }
  return counts;
}

function renderGovernanceResultCountCards(target, counts) {
  target.replaceChildren();
  for (const [status, label, iconName] of [
    ["total", "Findings", "list"],
    ["FAIL", "Needs review", "warning"],
    ["INSUFFICIENT_EVIDENCE", "Evidence gaps", "information"],
    ["EXCEPTION_APPROVED", "Exceptions", "check"],
    ["PASS", "Passed", "check"]
  ]) {
    const card = document.createElement("button");
    card.type = "button";
    card.className = `governance-count-card governance-count-${status.toLocaleLowerCase()}`;
    card.dataset.governanceStatusLink = status === "total" ? "all" : status;
    card.setAttribute("aria-label", `Show ${label.toLocaleLowerCase()} audit results.`);
    card.title = `Show ${label.toLocaleLowerCase()} audit results`;
    const icon = document.createElement("span");
    icon.className = "governance-count-icon";
    icon.setAttribute("aria-hidden", "true");
    icon.append(createGovernanceIcon(iconName));
    const count = document.createElement("strong");
    count.textContent = String(counts[status] || 0);
    const text = document.createElement("span");
    text.textContent = label;
    card.append(icon, count, text);
    target.append(card);
  }

  const optimizationChecks = [
    ["OPT-001", "Unused tables", "Potentially unused tables. Model tables with no detected references in DAX or scanned report visuals."],
    ["OPT-002", "Unused columns", "Potentially unused columns. Columns with no detected references in DAX or scanned report visuals."],
    ["OPT-003", "Unused measures", "Potentially unused measures. Measures not referenced by other measures or scanned report visuals."]
  ];
  for (const [checkId, label, description] of optimizationChecks) {
    const checkFindings = currentGovernanceFindings.filter(finding => finding.checkId === checkId);
    if (checkFindings.length === 0) {
      continue;
    }

    const candidateCount = checkFindings.filter(finding =>
      finding.status === "FAIL" || finding.status === "EXCEPTION_APPROVED").length;
    const evaluated = checkFindings.some(finding =>
      finding.status === "FAIL" || finding.status === "PASS");
    const partialEvidence = checkFindings.some(finding => finding.status === "INSUFFICIENT_EVIDENCE");
    const card = document.createElement("button");
    card.type = "button";
    card.className = "governance-count-card governance-count-optimization";
    card.dataset.governanceCheckLink = checkId;
    card.setAttribute(
      "aria-label",
      `${evaluated ? candidateCount : "No count"} ${label.toLocaleLowerCase()}. View matching findings.`
    );
    card.title = description;
    const icon = document.createElement("span");
    icon.className = "governance-count-icon";
    icon.setAttribute("aria-hidden", "true");
    icon.append(createGovernanceIcon("information"));
    const count = document.createElement("strong");
    count.textContent = evaluated ? String(candidateCount) : "—";
    const text = document.createElement("span");
    const coverageNote = evaluated
      ? partialEvidence ? " · Partial" : ""
      : " · Gap";
    text.textContent = `${label}${coverageNote}`;
    card.append(icon, count, text);
    target.append(card);
  }
}

function handleGovernanceResultCardClick(event) {
  const checkCard = event.target.closest("[data-governance-check-link]");
  const statusCard = event.target.closest("[data-governance-status-link]");
  if ((!checkCard && !statusCard) ||
      (checkCard && !currentGovernanceFindings.some(finding =>
        finding.checkId === checkCard.dataset.governanceCheckLink))) {
    return;
  }

  if (elements.governanceResultsDialog.open) {
    elements.governanceResultsDialog.close();
  }
  elements.governanceResults.hidden = false;
  governanceFindingCheckFilter = checkCard?.dataset.governanceCheckLink || "";
  governanceFindingFilter = statusCard?.dataset.governanceStatusLink || "all";
  for (const filter of elements.governanceFindingFilters) {
    const selected = filter.dataset.governanceFindingFilter === governanceFindingFilter;
    filter.classList.toggle("is-active", selected);
    filter.setAttribute("aria-pressed", String(selected));
  }
  renderGovernanceFindingList();
  window.requestAnimationFrame(() => {
    const group = governanceFindingCheckFilter
      ? document.querySelector(`#governance-finding-${CSS.escape(governanceFindingCheckFilter)}`)
      : elements.governanceFindingList.querySelector(".governance-finding-group");
    if (group) {
      if (governanceFindingCheckFilter) {
        group.open = true;
      }
      group.scrollIntoView({ behavior: "smooth", block: "start" });
      group.querySelector("summary")?.focus({ preventScroll: true });
    } else {
      elements.governanceFindingList.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  });
}

function renderGovernanceFindingList() {
  elements.governanceFindingList.replaceChildren();
  const facetFilteredFindings = getGovernanceFacetFilteredFindings(getGovernanceResultFacetState());
  const counts = getGovernanceFindingCounts(facetFilteredFindings);
  for (const [status, count] of Object.entries({
    all: counts.total,
    FAIL: counts.FAIL,
    INSUFFICIENT_EVIDENCE: counts.INSUFFICIENT_EVIDENCE,
    EXCEPTION_APPROVED: counts.EXCEPTION_APPROVED,
    PASS: counts.PASS
  })) {
    const countElement = document.querySelector(`[data-governance-finding-count="${status}"]`);
    if (countElement) {
      countElement.textContent = String(count);
    }
  }

  const checkScopedFindings = governanceFindingCheckFilter
    ? facetFilteredFindings.filter(finding => finding.checkId === governanceFindingCheckFilter)
    : facetFilteredFindings;
  const visibleFindings = governanceFindingFilter === "all"
    ? checkScopedFindings
    : checkScopedFindings.filter(finding => finding.status === governanceFindingFilter);
  elements.governanceFindingScope.hidden = !governanceFindingCheckFilter;
  if (governanceFindingCheckFilter) {
    const label = {
      "OPT-001": "Potentially unused tables",
      "OPT-002": "Potentially unused columns",
      "OPT-003": "Potentially unused measures"
    }[governanceFindingCheckFilter] || governanceFindingCheckFilter;
    elements.governanceFindingScopeLabel.textContent = `Showing ${label} findings`;
  }
  if (visibleFindings.length === 0) {
    elements.governanceNoFindings.hidden = false;
    elements.governanceNoFindings.textContent = currentGovernanceFindings.length === 0
      ? "No check results were returned for this scan."
      : facetFilteredFindings.length === 0
        ? "No findings match the selected filters."
        : "There are no findings in this category.";
    return;
  }

  elements.governanceNoFindings.hidden = true;
  const groups = new Map();
  for (const finding of visibleFindings) {
    const key = finding.checkId || finding.checkName || "Governance finding";
    if (!groups.has(key)) {
      groups.set(key, []);
    }
    groups.get(key).push(finding);
  }

  for (const [checkId, findings] of groups) {
    const group = document.createElement("details");
    group.className = "governance-finding-group";
    group.id = `governance-finding-${checkId}`;
    group.open = findings.length === 1;
    const summary = document.createElement("summary");
    const name = document.createElement("span");
    name.textContent = `${checkId} · ${findings[0].checkName || "Governance check"}`;
    const count = document.createElement("span");
    count.className = "governance-finding-group-count";
    if (checkId.startsWith("OPT-")) {
      const candidates = findings.filter(finding =>
        finding.status === "FAIL" || finding.status === "EXCEPTION_APPROVED").length;
      const gaps = findings.filter(finding => finding.status === "INSUFFICIENT_EVIDENCE").length;
      count.textContent = `${candidates} candidate${candidates === 1 ? "" : "s"}${gaps ? ` · ${gaps} evidence gap${gaps === 1 ? "" : "s"}` : ""}`;
    } else {
      count.textContent = `${findings.length} result${findings.length === 1 ? "" : "s"}`;
    }
    summary.append(name, count);
    group.append(summary);

    const cards = document.createElement("div");
    cards.className = "governance-finding-cards";
    if (checkId.startsWith("OPT-")) {
      const scopedFindings = new Map();
      for (const finding of findings) {
        const key = `${finding.workspaceName || ""}|${finding.status || ""}`;
        if (!scopedFindings.has(key)) {
          scopedFindings.set(key, []);
        }
        scopedFindings.get(key).push(finding);
      }
      for (const scoped of scopedFindings.values()) {
        cards.append(renderOptimizationFindingGroup(checkId, scoped));
      }
    } else {
      for (const finding of findings) {
        cards.append(renderGovernanceFinding(finding));
      }
    }
    group.append(cards);
    elements.governanceFindingList.append(group);
  }
}

function renderOptimizationFindingGroup(checkId, findings) {
  const card = document.createElement("article");
  card.className = "governance-finding-card";
  const first = findings[0];
  const candidates = findings.filter(finding =>
    finding.status === "FAIL" || finding.status === "EXCEPTION_APPROVED");
  const category = checkId === "OPT-001"
    ? "table"
    : checkId === "OPT-002" ? "column" : "measure";
  const header = document.createElement("div");
  header.className = "governance-finding-card-header";
  const status = document.createElement("span");
  status.className = `governance-finding-status governance-status-${(first.status || "").toLocaleLowerCase()}`;
  status.textContent = first.status === "FAIL"
    ? "Candidate findings"
    : first.status === "EXCEPTION_APPROVED"
      ? "Approved exception"
    : first.status === "PASS" ? "No candidates found" : "Insufficient evidence";
  header.append(status);
  const title = document.createElement("h3");
  title.textContent = candidates.length > 0
    ? `${candidates.length} potentially unused ${category}${candidates.length === 1 ? "" : "s"}`
    : first.status === "PASS"
      ? `No potentially unused ${category}s detected`
      : `${findings.length} ${category} check${findings.length === 1 ? "" : "s"} need evidence`;
  const location = document.createElement("p");
  location.className = "governance-finding-location";
  location.textContent = first.workspaceName || "Workspace";
  card.append(header, title, location);

  if (candidates.length > 0) {
    const names = document.createElement("ul");
    names.className = "governance-optimization-object-list";
    if (checkId === "OPT-002") {
      const columnsByTable = new Map();
      for (const finding of candidates) {
        const objectName = finding.artifactName || finding.title;
        const match = /^(.+)\[([^\]]+)\]$/.exec(objectName || "");
        const tableName = match ? match[1] : "Table not returned";
        const columnName = match ? match[2] : objectName || "Column name not returned";
        if (!columnsByTable.has(tableName)) {
          columnsByTable.set(tableName, []);
        }
        columnsByTable.get(tableName).push(columnName);
      }
      for (const [tableName, columns] of columnsByTable) {
        const item = document.createElement("li");
        const tableLabel = document.createElement("strong");
        tableLabel.textContent = `Table: ${tableName}`;
        const columnLabel = document.createElement("span");
        columnLabel.textContent = ` · Column${columns.length === 1 ? "" : "s"}: ${columns.join(", ")}`;
        item.append(tableLabel, columnLabel);
        if (applicationSession?.isAdmin === true) {
          for (const finding of candidates.filter(candidate => {
            const name = candidate.artifactName || "";
            return name.startsWith(`${tableName}[`);
          })) {
            appendOptimizationExceptionControl(item, finding);
          }
        }
        names.append(item);
      }
    } else {
      for (const finding of candidates) {
        const item = document.createElement("li");
        const objectName = finding.artifactName || finding.title || "Object name not returned";
        const objectLabel = document.createElement("strong");
        if (checkId === "OPT-003") {
          const match = /^(.+)\[([^\]]+)\]$/.exec(objectName);
          objectLabel.textContent = match
            ? `Measure: ${match[2]} · Table: ${match[1]}`
            : `Measure: ${objectName}`;
        } else {
          objectLabel.textContent = `Table: ${objectName}`;
        }
        item.append(objectLabel);
        if (applicationSession?.isAdmin === true) {
          appendOptimizationExceptionControl(item, finding);
        }
        names.append(item);
      }
    }
    card.append(names);
    const explanation = document.createElement("p");
    explanation.className = "governance-finding-short-detail";
    explanation.textContent = first.detail?.split(/\r?\n/).find(line => line.trim()) ||
      `No usage reference was detected for these ${category} candidates in the scanned evidence.`;
    card.append(explanation);
  } else {
    const explanation = document.createElement("p");
    explanation.className = "governance-finding-short-detail";
    explanation.textContent = first.detail?.split(/\r?\n/).find(line => line.trim()) ||
      "The check did not return an explanation.";
    card.append(explanation);
  }

  const evidence = document.createElement("details");
  evidence.className = "governance-finding-details";
  const summary = document.createElement("summary");
  summary.textContent = "Evidence and evaluation notes";
  evidence.append(summary);
  const content = document.createElement("div");
  content.className = "governance-finding-detail-content";
  for (const [label, value] of [
    ["Evidence source", first.evidenceSource || "Not returned"],
    ["Evidence timestamp", first.evidenceTimestamp ? new Date(first.evidenceTimestamp).toLocaleString() : "Unavailable"],
    ["Evaluation", first.evaluationLogic || "Not returned"],
    ["Rule version", `${first.checkId || checkId} · v${first.checkVersion || "—"}`]
  ]) {
    const item = document.createElement("p");
    const name = document.createElement("strong");
    name.textContent = `${label}: `;
    item.append(name, document.createTextNode(value));
    content.append(item);
  }
  const notes = [...new Set(candidates.flatMap(finding =>
    String(finding.detail || "").split(/\r?\n/).filter(line => line.trimStart().startsWith("- "))))];
  if (notes.length > 0) {
    const list = document.createElement("ul");
    list.className = "governance-finding-bullets";
    for (const note of notes) {
      const item = document.createElement("li");
      item.textContent = note.trimStart().slice(2);
      list.append(item);
    }
    content.append(list);
  }
  const rawEvidence = document.createElement("details");
  const rawSummary = document.createElement("summary");
  rawSummary.textContent = "Evidence captured for each object";
  const raw = document.createElement("pre");
  raw.textContent = JSON.stringify(findings.map(finding => ({
    object: finding.artifactName || finding.title,
    status: finding.status,
    evidence: finding.evidence
  })), null, 2);
  rawEvidence.append(rawSummary, raw);
  content.append(rawEvidence);
  evidence.append(content);
  card.append(evidence);
  return card;
}

function appendOptimizationExceptionControl(target, finding) {
  if (finding.exceptionRecord) {
    target.append(renderGovernanceExceptionRecord(finding.exceptionRecord, finding));
  } else {
    target.append(document.createTextNode(" "));
    target.append(createGovernanceExceptionButton(finding));
  }
}

function renderGovernanceFinding(finding) {
  const card = document.createElement("article");
  card.className = "governance-finding-card";
  const header = document.createElement("div");
  header.className = "governance-finding-card-header";
  const status = document.createElement("span");
  status.className = `governance-finding-status governance-status-${(finding.status || "").toLocaleLowerCase()}`;
  status.textContent = finding.status === "FAIL"
    ? "Needs review"
    : finding.status === "INSUFFICIENT_EVIDENCE"
      ? "Insufficient evidence"
      : finding.status === "EXCEPTION_APPROVED"
        ? "Approved exception"
        : finding.status === "PASS" ? "Passed" : finding.status || "Status unavailable";
  const severity = document.createElement("span");
  severity.className = "governance-finding-severity";
  severity.textContent = `${finding.severity || "Severity unavailable"} severity`;
  header.append(status, severity);

  const title = document.createElement("h3");
  title.textContent = finding.title || finding.checkName || finding.checkId || "Governance finding";
  const location = document.createElement("p");
  location.className = "governance-finding-location";
  location.textContent = [
    finding.artifactType,
    finding.artifactName,
    finding.workspaceName
  ].filter(Boolean).join(" · ") || "Workspace";
  const detailLines = String(finding.detail || "").split(/\r?\n/).filter(line => line.trim());
  const detail = document.createElement("p");
  detail.className = "governance-finding-short-detail";
  detail.textContent = detailLines[0] || "No additional explanation was returned.";
  card.append(header, title, location, detail);

  const extraLines = detailLines.slice(1);
  const bulletLines = extraLines.filter(line => line.trimStart().startsWith("- "));
  if (bulletLines.length > 0) {
    const list = document.createElement("ul");
    list.className = "governance-finding-bullets";
    for (const line of bulletLines) {
      const item = document.createElement("li");
      item.textContent = line.trimStart().slice(2);
      list.append(item);
    }
    card.append(list);
  }

  const evidence = document.createElement("details");
  evidence.className = "governance-finding-details";
  const evidenceSummary = document.createElement("summary");
  evidenceSummary.textContent = "Evidence and rule details";
  const evidenceContent = document.createElement("div");
  evidenceContent.className = "governance-finding-detail-content";
  for (const [label, value] of [
    ["Check version", `${finding.checkId || "—"} · v${finding.checkVersion || "—"}`],
    ["Evidence source", finding.evidenceSource || "Not returned"],
    ["Recorded", finding.evidenceTimestamp
      ? new Date(finding.evidenceTimestamp).toLocaleString()
      : "Timestamp unavailable"],
    ["Expected", finding.expectedValue || "Not specified"],
    ["Observed", finding.actualValue || "Not returned"],
    ["Rule", finding.evaluationLogic || "Not returned"],
    ["Exception guidance", finding.exceptionRules || "Not returned"]
  ]) {
    const item = document.createElement("p");
    const labelElement = document.createElement("strong");
    labelElement.textContent = `${label}: `;
    item.append(labelElement, document.createTextNode(value));
    evidenceContent.append(item);
  }
  const evidenceValues = document.createElement("details");
  const evidenceValuesSummary = document.createElement("summary");
  evidenceValuesSummary.textContent = "Raw evidence values";
  const evidenceValuesPre = document.createElement("pre");
  evidenceValuesPre.textContent = JSON.stringify(finding.evidence, null, 2);
  evidenceValues.append(evidenceValuesSummary, evidenceValuesPre);
  evidenceContent.append(evidenceValues);
  evidence.append(evidenceSummary, evidenceContent);
  card.append(evidence);
  if (finding.exceptionRecord) {
    card.append(renderGovernanceExceptionRecord(finding.exceptionRecord, finding));
  } else if (finding.status === "FAIL" && applicationSession?.isAdmin === true) {
    const actions = document.createElement("div");
    actions.className = "governance-exception-actions";
    actions.append(createGovernanceExceptionButton(finding));
    card.append(actions);
  }
  return card;
}

function createGovernanceExceptionButton(finding, action = "approve") {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "button button-secondary governance-exception-button";
  button.textContent = action === "revoke" ? "Revoke exception" : "Approve exception";
  button.dataset.governanceExceptionAction = action;
  button.dataset.findingIndex = String(currentGovernanceFindings.indexOf(finding));
  if (finding.exceptionRecord?.exceptionId) {
    button.dataset.exceptionId = finding.exceptionRecord.exceptionId;
  }
  return button;
}

function renderGovernanceExceptionRecord(exception, finding) {
  const container = document.createElement("section");
  container.className = `governance-exception-record governance-exception-${String(exception.status || "").toLocaleLowerCase()}`;
  const heading = document.createElement("strong");
  heading.textContent = exception.status === "Approved"
    ? `Approved exception · expires ${new Date(exception.expiresUtc).toLocaleDateString()}`
    : `${exception.status} exception`;
  const summary = document.createElement("p");
  summary.textContent =
    `Owner: ${exception.owner} · Approved by ${exception.approvedBy} on ${new Date(exception.approvedUtc).toLocaleString()}. ` +
    `Reason: ${exception.reason}`;
  container.append(heading, summary);

  if (finding.originalStatus) {
    const original = document.createElement("p");
    original.textContent = `Underlying finding status: ${finding.originalStatus}. The exception changes the determination only until expiry or revocation; evidence is unchanged.`;
    container.append(original);
  }

  if (Array.isArray(exception.auditTrail) && exception.auditTrail.length > 0) {
    const trail = document.createElement("details");
    const trailSummary = document.createElement("summary");
    trailSummary.textContent = `Audit trail (${exception.auditTrail.length} event${exception.auditTrail.length === 1 ? "" : "s"})`;
    const events = document.createElement("ul");
    for (const event of exception.auditTrail) {
      const item = document.createElement("li");
      item.textContent = `${event.eventType} · ${new Date(event.eventUtc).toLocaleString()} · ${event.actor}: ${event.notes}`;
      events.append(item);
    }
    trail.append(trailSummary, events);
    container.append(trail);
  }

  if (exception.status === "Approved" && applicationSession?.isAdmin === true) {
    container.append(createGovernanceExceptionButton(finding, "revoke"));
  } else if (exception.status !== "Approved" &&
      finding.status === "FAIL" &&
      applicationSession?.isAdmin === true) {
    container.append(createGovernanceExceptionButton(finding));
  }
  return container;
}

function handleGovernanceExceptionAction(event) {
  const button = event.target.closest("[data-governance-exception-action]");
  if (!button || applicationSession?.isAdmin !== true) {
    return;
  }

  const findingIndex = Number(button.dataset.findingIndex);
  const finding = currentGovernanceFindings[findingIndex];
  if (!finding || findingIndex < 0) {
    return;
  }

  const action = button.dataset.governanceExceptionAction;
  governanceExceptionTarget = {
    action,
    finding,
    exceptionId: button.dataset.exceptionId || finding.exceptionRecord?.exceptionId || ""
  };
  const isRevoking = action === "revoke";
  elements.governanceExceptionTitle.textContent = isRevoking
    ? "Revoke this exception"
    : "Approve a time-limited exception";
  elements.governanceExceptionTarget.textContent =
    `${finding.checkId} · ${finding.artifactName || finding.title} · ${finding.workspaceName}`;
  elements.governanceExceptionApprovalFields.hidden = isRevoking;
  elements.governanceExceptionOwner.required = !isRevoking;
  elements.governanceExceptionExpiry.required = !isRevoking;
  elements.governanceExceptionReasonLabel.textContent = isRevoking
    ? "Revocation reason"
    : "Approval justification";
  elements.submitGovernanceException.textContent = isRevoking ? "Revoke exception" : "Approve exception";
  elements.governanceExceptionError.hidden = true;
  elements.governanceExceptionReason.value = "";
  if (!isRevoking) {
    const expiry = new Date();
    expiry.setDate(expiry.getDate() + 30);
    const expiryLimit = new Date();
    expiryLimit.setFullYear(expiryLimit.getFullYear() + 1);
    expiryLimit.setDate(expiryLimit.getDate() - 1);
    elements.governanceExceptionExpiry.min = formatLocalDate(new Date());
    elements.governanceExceptionExpiry.max = formatLocalDate(expiryLimit);
    elements.governanceExceptionExpiry.value = formatLocalDate(expiry);
    elements.governanceExceptionOwner.value = "";
  }
  elements.governanceExceptionDialog.showModal();
}

function formatLocalDate(date) {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

async function submitGovernanceException(event) {
  event.preventDefault();
  if (!governanceExceptionTarget || applicationSession?.isAdmin !== true) {
    return;
  }

  const { action, finding, exceptionId } = governanceExceptionTarget;
  if (!currentGovernanceRunId) {
    elements.governanceExceptionError.textContent =
      "Save or reopen the audit run before managing an exception.";
    elements.governanceExceptionError.hidden = false;
    return;
  }

  const isRevoking = action === "revoke";
  const endpoint = isRevoking
    ? `/api/governance-audit/exceptions/${encodeURIComponent(exceptionId)}/revoke`
    : "/api/governance-audit/exceptions";
  const body = isRevoking
    ? { reason: elements.governanceExceptionReason.value.trim() }
    : {
        runId: currentGovernanceRunId,
        workspaceId: finding.workspaceId,
        checkId: finding.checkId,
        artifactId: finding.artifactId || null,
        artifactType: finding.artifactType || null,
        artifactName: finding.artifactName || finding.title,
        owner: elements.governanceExceptionOwner.value.trim(),
        reason: elements.governanceExceptionReason.value.trim(),
        expiresUtc: new Date(`${elements.governanceExceptionExpiry.value}T23:59:59`).toISOString()
      };
  elements.submitGovernanceException.disabled = true;
  elements.governanceExceptionError.hidden = true;
  try {
    const response = await fetch(endpoint, {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json"
      },
      body: JSON.stringify(body)
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Governance exception could not be saved."));
    }

    elements.governanceExceptionDialog.close();
    await loadGovernanceAuditRun(currentGovernanceRunId);
  } catch (error) {
    elements.governanceExceptionError.textContent = error instanceof Error
      ? error.message
      : "Governance exception could not be saved.";
    elements.governanceExceptionError.hidden = false;
  } finally {
    elements.submitGovernanceException.disabled = false;
  }
}

async function loadGovernanceCheckCatalog() {
  if (governanceCheckCatalogLoaded) {
    return;
  }

  elements.governanceCheckSelection.replaceChildren();
  try {
    const response = await fetch("/api/governance-audit/checks", {
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(await readApiError(response, "Governance checks could not be loaded."));
    }

    const checks = await response.json();
    for (const check of checks) {
      const option = document.createElement("div");
      option.className = "governance-check-option";
      const label = document.createElement("label");
      label.className = "governance-check-label";
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.checked = true;
      checkbox.dataset.governanceCheck = check.checkId;
      const title = document.createElement("span");
      title.className = "governance-check-title";
      title.textContent = `${check.checkId} · ${check.name}`;
      const severity = document.createElement("span");
      severity.className = "governance-check-severity";
      severity.textContent = check.severity;
      label.append(checkbox, title, severity);

      const info = document.createElement("details");
      info.className = "governance-check-info";
      const infoSummary = document.createElement("summary");
      infoSummary.setAttribute("aria-label", `More information about ${check.name}`);
      infoSummary.title = `More information about ${check.name}`;
      infoSummary.append(createGovernanceIcon("information"));
      const infoPanel = document.createElement("div");
      infoPanel.className = "governance-check-info-panel";
      for (const [heading, value] of [
        ["Purpose", check.description],
        ["Version and owner", `v${check.version} · ${check.owner}`],
        ["How it is evaluated", check.evaluationLogic],
        ["Evidence source", check.evidenceSource],
        ["Expected result", check.expectedResult],
        ["Exception guidance", check.exceptionRules]
      ]) {
        const section = document.createElement("section");
        const sectionHeading = document.createElement("strong");
        sectionHeading.textContent = heading;
        const sectionText = document.createElement("p");
        sectionText.textContent = value || "Not specified.";
        section.append(sectionHeading, sectionText);
        infoPanel.append(section);
      }
      info.append(infoSummary, infoPanel);
      option.append(label, info);
      elements.governanceCheckSelection.append(option);
    }
    governanceCheckCatalogLoaded = true;
  } catch (error) {
    elements.governanceCheckSelection.textContent =
      error instanceof Error ? error.message : "Governance checks could not be loaded.";
  }
}

function createGovernanceIcon(name) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 16 16");
  svg.setAttribute("aria-hidden", "true");
  const icon = document.createElementNS("http://www.w3.org/2000/svg", "use");
  icon.setAttribute("href", `#icon-${name}`);
  svg.append(icon);
  return svg;
}

async function loadGovernanceAuditHistory() {
  elements.governanceHistoryError.hidden = true;
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
      const scannedAt = new Date(run.scannedUtc).toLocaleString();
      const row = appendGovernanceRow(elements.governanceHistoryRows, [
        scannedAt,
        run.scannedBy,
        run.workspaceCount,
        run.reportCount,
        run.dashboardCount,
        run.semanticModelCount,
        run.appCount,
        run.gatewayCount,
        run.findingCount
      ]);
      row.dataset.auditSearch = [
        run.scannedUtc,
        scannedAt,
        run.scannedBy,
        run.workspaceCount,
        run.reportCount,
        run.dashboardCount,
        run.semanticModelCount,
        run.appCount,
        run.gatewayCount,
        run.findingCount
      ].join(" ").toLocaleLowerCase();
      const actionCell = document.createElement("td");
      const openButton = document.createElement("button");
      openButton.type = "button";
      openButton.className = "button button-secondary";
      openButton.textContent = "Open";
      openButton.addEventListener("click", () => loadGovernanceAuditRun(run.runId));
      actionCell.append(openButton);
      row.append(actionCell);
    }
    filterGovernanceAuditHistory();
  } catch (error) {
    elements.governanceHistoryError.textContent =
      error instanceof Error ? error.message : "Saved Governance Audit history could not be loaded.";
    elements.governanceHistoryError.hidden = false;
  }
}

function filterGovernanceAuditHistory() {
  const query = elements.governanceHistorySearch.value.trim().toLocaleLowerCase();
  const rows = [...elements.governanceHistoryRows.querySelectorAll("tr")];
  let visibleCount = 0;
  for (const row of rows) {
    const matches = !query || row.dataset.auditSearch.includes(query);
    row.hidden = !matches;
    visibleCount += matches ? 1 : 0;
  }
  elements.governanceHistoryCount.textContent =
    `Showing ${visibleCount} of ${rows.length} saved audit run${rows.length === 1 ? "" : "s"}.`;
  elements.governanceHistoryEmpty.hidden = visibleCount > 0;
  if (rows.length === 0) {
    elements.governanceHistoryEmpty.textContent = "No saved Governance Audit runs yet.";
  } else if (visibleCount === 0) {
    elements.governanceHistoryEmpty.textContent = "No audit runs match your search.";
  }
}

async function loadGovernanceAuditRun(runId) {
  elements.governanceStatus.textContent = "Loading saved Governance Audit run…";
  elements.governanceHistoryError.hidden = true;
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
    navigateGovernancePage("results");
  } catch (error) {
    elements.governanceHistoryError.textContent =
      error instanceof Error ? error.message : "Saved Governance Audit run could not be loaded.";
    elements.governanceHistoryError.hidden = false;
    elements.governanceStatus.textContent = "Saved audit run could not be loaded.";
  }
}

function appendGovernanceRow(target, values, filterData = {}) {
  const row = document.createElement("tr");
  for (const [key, value] of Object.entries(filterData)) {
    if (Array.isArray(value)) {
      row.dataset[key] = value.join(",");
    } else if (value !== null && value !== undefined) {
      row.dataset[key] = String(value);
    }
  }
  for (const value of values) {
    const cell = document.createElement("td");
    if (value instanceof Node) {
      cell.append(value);
    } else {
      cell.textContent = String(value ?? "—");
    }
    row.append(cell);
  }
  target.append(row);
  return row;
}

function renderGovernanceHealthStatus(status, detail) {
  const value = status || "Unknown";
  const container = document.createElement("span");
  container.className = "governance-health-status";
  const badge = document.createElement("strong");
  badge.className = `governance-health-badge governance-health-${value
    .toLocaleLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")}`;
  badge.textContent = value;
  container.append(badge);
  if (detail) {
    const description = document.createElement("span");
    description.textContent = detail;
    container.append(description);
  }
  return container;
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
  const body = await response.text();
  let problem = null;
  if (body) {
    try {
      problem = JSON.parse(body);
    } catch {
      return body.trim() || fallback;
    }
  }
  if (problem?.errors) {
    return Object.values(problem.errors).flat().join(" ");
  }
  if (problem?.detail || problem?.title) {
    return [problem.title, problem.detail]
      .filter(Boolean)
      .filter((value, index, values) => values.indexOf(value) === index)
      .join(" ");
  }
  if (response.status === 403) {
    return "Your account is signed in but is not authorized for this action. Contact a BI Sentinel administrator.";
  }
  return fallback;
}

async function readJsonResponse(response, operation) {
  const body = await response.text();
  if (!body.trim()) {
    throw new Error(
      `${operation} returned HTTP ${response.status} with an empty response. ` +
      "The server may have completed the operation without returning its result; check the API logs before retrying."
    );
  }

  try {
    return JSON.parse(body);
  } catch {
    const contentType = response.headers.get("content-type") || "unknown content type";
    throw new Error(
      `${operation} returned an invalid JSON response (HTTP ${response.status}, ${contentType}).`
    );
  }
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
  const finishLoading = beginGlobalLoading("Reviewing Power BI content…");

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

    const savedRun = await readJsonResponse(response, "Review upload");
    if (!savedRun || typeof savedRun !== "object" ||
        !savedRun.review || !Array.isArray(savedRun.findings)) {
      throw new Error(
        "Review upload returned JSON, but it did not contain the saved review and findings. " +
        "Check that the server and browser are running the same BI Sentinel version."
      );
    }
    await displaySavedRuns([savedRun], sourceName);
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
    finishLoading();
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
  activeFindingCategory = "all";
  reviewerSubmissionMode = false;
  currentSubmission = null;
  submissionDrafts = {};
  reviewerResponseDrafts = {};
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
  const resultsTab = elements.reviewPageTabs.find(tab => tab.dataset.reviewPageTab === "results");
  resultsTab.disabled = false;
  navigateReviewPage("results");
  elements.status.textContent =
    `Review completed successfully for ${savedRuns.length} ${savedRuns.length === 1 ? "project" : "projects"}.`;

  if (!isReviewReviewer()) {
    try {
      await loadMySubmission(currentReviewRunId);
      renderFindings();
    } catch (error) {
      showError(error instanceof Error
        ? `Review completed and saved, but your submission status could not be loaded: ${error.message}`
        : "Review completed and saved, but your submission status could not be loaded.");
    }
  } else {
    currentSubmission = null;
  }

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

async function loadReviewById(reviewRunId, submission = null) {
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
    activeFindingCategory = "all";
    if (submission) {
      currentSubmission = submission;
      reviewerSubmissionMode = true;
      submissionDrafts = {};
      reviewerResponseDrafts = {};
    } else {
      reviewerSubmissionMode = false;
      if (!isReviewReviewer()) {
        await loadMySubmission(currentReviewRunId);
      } else {
        currentSubmission = null;
      }
    }
    renderSummary({ ...entry.summary, findings: currentFindings });
    renderFindings();
    elements.overview.hidden = false;
    const resultsTab = elements.reviewPageTabs.find(tab => tab.dataset.reviewPageTab === "results");
    resultsTab.disabled = false;
    navigateReviewPage("results");
    elements.status.textContent =
      `Review loaded successfully · ${entry.summary.projectName} · ${currentFindings.length} findings`;
  } catch (error) {
    showError(error instanceof Error ? error.message : "Saved review could not be loaded.");
    elements.status.textContent = "Saved review could not be loaded.";
  }
}

function isReviewReviewer() {
  return applicationSession?.isAdmin === true ||
    applicationSession?.isManager === true ||
    applicationSession?.isLead === true;
}

async function loadMySubmission(reviewRunId) {
  currentSubmission = null;
  submissionDrafts = {};
  reviewerResponseDrafts = {};
  const response = await fetch(
    `/api/reviews/history/${encodeURIComponent(reviewRunId)}/submission`,
    { headers: { Accept: "application/json" } }
  );
  if (!response.ok) {
    throw new Error(await readApiError(response, "Your saved review submission could not be loaded."));
  }

  currentSubmission = await readJsonResponse(response, "Submission status");
}

async function submitCurrentReviewWorkflow() {
  if (!currentReviewRunId || currentFindings.length === 0) {
    return;
  }

  const button = elements.submitReviewWorkflow;
  button.disabled = true;
  elements.error.hidden = true;
  try {
    if (reviewerSubmissionMode && currentSubmission) {
      const findings = currentSubmission.findings.map(finding => {
        const draft = reviewerResponseDrafts[finding.reviewFindingId] || {};
        return {
          reviewFindingId: finding.reviewFindingId,
          disposition: draft.disposition || finding.reviewerDisposition || "",
          response: draft.response ?? finding.reviewerResponse ?? null
        };
      });
      if (findings.some(finding => !finding.disposition)) {
        throw new Error("Choose a reviewer decision for every finding before submitting the review.");
      }

      const response = await fetch(
        `/api/reviews/submissions/${encodeURIComponent(currentSubmission.submissionId)}/complete`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({ findings })
        }
      );
      if (!response.ok) {
        throw new Error(await readApiError(response, "Reviewer response could not be submitted."));
      }
      currentSubmission = await response.json();
      elements.status.textContent = "Reviewer decisions submitted. The submitter can now read them.";
    } else {
      const findings = currentFindings.map(finding => ({
        reviewFindingId: finding.reviewFindingId,
        justification: submissionDrafts[finding.reviewFindingId] || null
      }));
      const response = await fetch(
        `/api/reviews/history/${encodeURIComponent(currentReviewRunId)}/submission`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({ findings })
        }
      );
      if (!response.ok) {
        throw new Error(await readApiError(response, "Your review could not be submitted."));
      }
      currentSubmission = await response.json();
      elements.status.textContent = "Review submitted. Your justifications are locked while a reviewer responds.";
    }

    await loadDashboard();
    renderFindings();
  } catch (error) {
    elements.error.textContent = error instanceof Error ? error.message : "The review workflow could not be submitted.";
    elements.error.hidden = false;
  } finally {
    updateSubmissionControls();
  }
}

function updateSubmissionControls() {
  const showSubmitterControls = !isReviewReviewer() && currentFindings.length > 0;
  const showReviewerControls = reviewerSubmissionMode && currentSubmission?.status === "Submitted";
  elements.submissionControls.hidden = !showSubmitterControls && !showReviewerControls;
  if (showReviewerControls) {
    elements.submitReviewWorkflow.hidden = false;
    elements.submissionStatus.textContent =
      `Reviewing ${currentSubmission.projectName}, submitted by ${currentSubmission.submittedBy}. Set a decision for each finding; this response will be read-only to the submitter.`;
    elements.submitReviewWorkflow.querySelector("span").textContent = "Submit reviewer response";
    elements.submitReviewWorkflow.disabled = currentSubmission.findings.some(finding =>
      !(reviewerResponseDrafts[finding.reviewFindingId]?.disposition || finding.reviewerDisposition));
  } else if (showSubmitterControls && currentSubmission) {
    elements.submissionStatus.textContent = currentSubmission.status === "Reviewed"
      ? `Submitted ${new Date(currentSubmission.submittedUtc).toLocaleString()}. Reviewed by ${currentSubmission.reviewedBy || "a reviewer"} on ${new Date(currentSubmission.reviewedUtc).toLocaleString()}. Your justifications and the reviewer response are read-only.`
      : `Submitted ${new Date(currentSubmission.submittedUtc).toLocaleString()}. Your justifications are locked; a reviewer response will appear here when complete.`;
    elements.submitReviewWorkflow.hidden = true;
  } else if (showSubmitterControls) {
    elements.submissionStatus.textContent =
      "Add an optional justification under each finding, then submit once. Your submission is locked after it is sent.";
    elements.submitReviewWorkflow.hidden = false;
    elements.submitReviewWorkflow.querySelector("span").textContent = "Submit review";
    elements.submitReviewWorkflow.disabled = false;
  } else {
    elements.submitReviewWorkflow.hidden = false;
  }
  const hasStickyAction = !elements.submissionControls.hidden &&
    !elements.submitReviewWorkflow.hidden;
  elements.submissionControls.classList.toggle("submission-controls-sticky", hasStickyAction);
  elements.findingsScrollPanel.classList.toggle(
    "findings-scroll-panel-has-sticky-footer",
    hasStickyAction
  );
}

async function loadDashboard() {
  elements.refreshDashboard.disabled = true;
  try {
    const reviewer = isReviewReviewer();
    const response = await fetch(
      reviewer
        ? "/api/reviews/submissions?includeReviewed=true"
        : "/api/reviews/my-submissions",
      { headers: { Accept: "application/json" } }
    );
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || problem?.title || `Review dashboard failed (HTTP ${response.status}).`);
    }

    const dashboard = await readJsonResponse(response, "Review dashboard");
    if (!Array.isArray(dashboard)) {
      throw new Error("Review dashboard returned an unexpected response format.");
    }
    dashboardItems = dashboard.map(item => ({
      ...item,
      isSubmission: true,
      findingsCount: item.findingsCount,
      closedFindingsCount: item.status === "Reviewed" ? item.findingsCount : 0,
      waitingFindingsCount: item.status === "Submitted" ? item.findingsCount : 0,
      feedbackStatus: item.status === "Reviewed" ? "Closed" : "Open"
    }));
    elements.dashboardTitle.textContent = reviewer ? "Reviewer queue" : "My submitted reviews";
    elements.dashboardDescription.textContent = reviewer
      ? "Review submissions from signed-in users, add a decision and response for each finding, then submit the review."
      : "Track your submitted reviews and read manager, lead, or administrator responses. Submitted justifications cannot be edited.";
    renderDashboard();
  } catch (error) {
    showError(error instanceof Error
      ? `${error.message} Verify review-history migration 002, application-access migration 008, and review-submission migration 012.`
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
  elements.dashboardEmpty.textContent = isReviewReviewer()
    ? dashboardStatus === "Open"
      ? "No reviews are waiting for a Manager, Lead, or Admin."
      : "No completed reviewer responses yet."
    : dashboardStatus === "Open"
      ? "You have not submitted any reviews yet."
      : "No reviewer responses are available yet.";
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
  status.textContent = run.isSubmission
    ? run.status === "Reviewed" ? "Reviewer response ready" : "Awaiting reviewer"
    : run.feedbackStatus === "Closed" ? "Closed" : "Waiting";
  responseCell.append(progress, status);

  const actions = document.createElement("td");
  actions.className = "dashboard-actions";
  const openButton = document.createElement("button");
  openButton.type = "button";
  openButton.className = "button button-secondary button-small";
  openButton.textContent = "Open review";
  openButton.addEventListener("click", () => openDashboardRun(run));
  actions.append(openButton);

  if (!run.isSubmission && run.feedbackStatus === "Closed" && run.findingsCount > 0) {
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
  if (run.isSubmission && isReviewReviewer()) {
    try {
      const response = await fetch(`/api/reviews/submissions/${encodeURIComponent(run.submissionId)}`, {
        headers: { Accept: "application/json" }
      });
      if (!response.ok) {
        throw new Error(await readApiError(response, "Reviewer submission could not be loaded."));
      }

      await loadReviewById(run.reviewRunId, await response.json());
    } catch (error) {
      showError(error instanceof Error ? error.message : "Reviewer submission could not be loaded.");
      return;
    }
  } else {
    await loadReviewById(run.reviewRunId);
  }
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
  const categoryCounts = Object.fromEntries(
    ["tables", "columns", "measures", "relationships", "other"].map(category => [
      category,
      currentFindings.filter(finding => getFindingCategory(finding) === category).length
    ])
  );
  document.querySelector('[data-category-count="all"]').textContent = String(currentFindings.length);
  for (const [category, count] of Object.entries(categoryCounts)) {
    document.querySelector(`[data-category-count="${category}"]`).textContent = String(count);
  }
  for (const tab of elements.findingCategoryTabs) {
    tab.setAttribute("aria-selected", String(tab.dataset.findingCategory === activeFindingCategory));
    tab.tabIndex = tab.dataset.findingCategory === activeFindingCategory ? 0 : -1;
  }
  const visibleFindings = currentFindings.filter((finding) => {
    const matchesCategory = activeFindingCategory === "all" ||
      getFindingCategory(finding) === activeFindingCategory;
    const matchesSeverity = severity === "all" || finding.severity === severity;
    const searchable = [
      finding.ruleId,
      finding.message,
      finding.location,
      finding.recommendation
    ].join(" ").toLocaleLowerCase();
    return matchesCategory && matchesSeverity && searchable.includes(query);
  });

  elements.list.replaceChildren(...renderFindingGroups(visibleFindings));
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
  updateSubmissionControls();
  updateExportButton();
}

function getFindingCategory(finding) {
  const ruleId = (finding.ruleId || "").toUpperCase();
  if (ruleId === "OPT-001" || ruleId === "NAM-001") {
    return "tables";
  }
  if (ruleId === "OPT-002" || ruleId === "NAM-002" || ruleId === "NAM-003") {
    return "columns";
  }
  if (
    ruleId === "OPT-003" ||
    ruleId === "NAM-004" ||
    ruleId === "NAM-005" ||
    ruleId.startsWith("MEAS-") ||
    ruleId.startsWith("DAX-")
  ) {
    return "measures";
  }
  if (ruleId.startsWith("REL-")) {
    return "relationships";
  }
  return "other";
}

function renderFindingGroups(findings) {
  const entries = [];
  const groups = new Map();
  for (const finding of findings) {
    if (getFindingCategory(finding) !== "columns") {
      entries.push(renderFinding(finding));
      continue;
    }
    const match = (finding.location || "").match(/^(.+?)\[[^\]]+\]$/);
    const tableName = match?.[1] || "Other columns";
    let group = groups.get(tableName);
    if (!group) {
      const container = document.createElement("section");
      container.className = "finding-group";
      const heading = document.createElement("h3");
      heading.className = "finding-group-heading";
      const title = document.createElement("span");
      title.textContent = tableName;
      const count = document.createElement("span");
      count.className = "finding-group-count";
      count.textContent = "1 column finding";
      const cards = document.createElement("div");
      cards.className = "finding-group-items";
      heading.append(title, count);
      container.append(heading, cards);
      group = { container, count, cards, total: 0 };
      groups.set(tableName, group);
      entries.push(container);
    }
    group.total += 1;
    group.count.textContent = `${group.total} column ${group.total === 1 ? "finding" : "findings"}`;
    group.cards.append(renderFinding(finding));
  }
  return entries;
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
  const recommendation = document.createElement("div");
  recommendation.className = "finding-recommendation";
  const recommendationLines = (finding.recommendation || "").split(/\r?\n/);
  const bulletLines = recommendationLines.filter(line => line.trimStart().startsWith("- "));
  if (bulletLines.length > 0) {
    const intro = recommendationLines.find(line => line.trim() && !line.trimStart().startsWith("- "));
    if (intro) {
      const introText = document.createElement("p");
      introText.textContent = intro;
      recommendation.append(introText);
    }
    const list = document.createElement("ul");
    for (const line of bulletLines) {
      const item = document.createElement("li");
      item.textContent = line.trimStart().slice(2);
      list.append(item);
    }
    recommendation.append(list);
  } else {
    const recommendationText = document.createElement("p");
    recommendationText.textContent = finding.recommendation || "";
    recommendation.append(recommendationText);
  }

  if (applicationSession?.isAuthenticated) {
    card.append(header, message, recommendation);
    if (!isReviewReviewer() || (reviewerSubmissionMode && currentSubmission)) {
      card.append(renderSubmissionFinding(finding));
    }
    return card;
  }

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

function renderSubmissionFinding(finding) {
  const section = document.createElement("section");
  section.className = "submission-finding";
  const submittedFinding = currentSubmission?.findings.find(item =>
    item.reviewFindingId === finding.reviewFindingId);

  if (reviewerSubmissionMode && currentSubmission) {
    const justification = document.createElement("p");
    justification.className = "submission-justification";
    justification.textContent = `Submitter justification: ${submittedFinding?.justification || "No justification provided."}`;
    section.append(justification);
    if (currentSubmission.status === "Reviewed") {
      const decision = document.createElement("p");
      decision.className = "submission-review-response";
      decision.textContent = `Reviewer decision: ${submittedFinding?.reviewerDisposition || "—"}`;
      const response = document.createElement("p");
      response.className = "submission-review-response";
      response.textContent = `Reviewer response: ${submittedFinding?.reviewerResponse || "No additional note."}`;
      section.append(decision, response);
      return section;
    }

    const decisionLabel = document.createElement("label");
    decisionLabel.className = "submission-field";
    decisionLabel.textContent = "Reviewer decision";
    const decision = document.createElement("select");
    decision.className = "reviewer-disposition";
    decision.dataset.reviewFindingId = finding.reviewFindingId;
    for (const [value, label] of [
      ["", "Choose a decision"],
      ["Accepted", "Accepted"],
      ["NeedsChanges", "Needs changes"],
      ["Noted", "Noted"],
      ["IncorrectFinding", "Incorrect finding"]
    ]) {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = label;
      decision.append(option);
    }
    const draft = reviewerResponseDrafts[finding.reviewFindingId] || {};
    decision.value = draft.disposition || submittedFinding?.reviewerDisposition || "";
    decision.addEventListener("change", () => {
      reviewerResponseDrafts[finding.reviewFindingId] = {
        ...reviewerResponseDrafts[finding.reviewFindingId],
        disposition: decision.value
      };
      updateSubmissionControls();
    });
    decisionLabel.append(decision);
    const responseLabel = document.createElement("label");
    responseLabel.className = "submission-field";
    responseLabel.textContent = "Reviewer response";
    const response = document.createElement("textarea");
    response.maxLength = 1000;
    response.rows = 2;
    response.value = draft.response ?? submittedFinding?.reviewerResponse ?? "";
    response.placeholder = "Optional response for the submitter";
    response.addEventListener("input", () => {
      reviewerResponseDrafts[finding.reviewFindingId] = {
        ...reviewerResponseDrafts[finding.reviewFindingId],
        response: response.value
      };
    });
    responseLabel.append(response);
    section.append(decisionLabel, responseLabel);
    return section;
  }

  if (currentSubmission) {
    const justification = document.createElement("p");
    justification.className = "submission-justification";
    justification.textContent = `Your justification: ${submittedFinding?.justification || "No justification provided."}`;
    section.append(justification);
    if (currentSubmission.status === "Reviewed") {
      const decision = document.createElement("p");
      decision.className = "submission-review-response";
      decision.textContent = `Reviewer decision: ${submittedFinding?.reviewerDisposition || "—"}`;
      const response = document.createElement("p");
      response.className = "submission-review-response";
      response.textContent = `Reviewer response: ${submittedFinding?.reviewerResponse || "No additional note."}`;
      section.append(decision, response);
    } else {
      const pending = document.createElement("p");
      pending.className = "submission-pending";
      pending.textContent = "Reviewer response pending.";
      section.append(pending);
    }
    return section;
  }

  const label = document.createElement("label");
  label.className = "submission-field";
  label.textContent = "Your justification (optional)";
  const justification = document.createElement("textarea");
  justification.maxLength = 1000;
  justification.rows = 2;
  justification.value = submissionDrafts[finding.reviewFindingId] || "";
  justification.placeholder = "Add context or explain why this finding needs review";
  justification.addEventListener("input", () => {
    submissionDrafts[finding.reviewFindingId] = justification.value;
  });
  label.append(justification);
  section.append(label);
  return section;
}

function findingKey(finding) {
  return finding.reviewFindingId ||
    [finding.projectName || currentProject, finding.ruleId, finding.location, finding.message].join("::");
}

function updateExportButton() {
  const isReviewer = isReviewReviewer();
  elements.exportButton.hidden = !isReviewer;
  const hasFeedback = Object.values(feedback).some(entry =>
    entry.rating || (entry.comment && entry.comment.trim()));
  elements.exportButton.disabled = !hasFeedback && !sharedFeedbackAvailable;
  elements.exportFeedbackStatus.hidden = !isReviewer || !elements.exportButton.disabled;
  elements.exportFeedbackStatus.textContent = !feedbackAvailabilityChecked
    ? "Checking for saved feedback…"
    : feedbackAvailabilityError
      ? `Saved feedback availability could not be checked: ${feedbackAvailabilityError}`
      : "No reviewer feedback is available to export yet.";
}

async function checkSharedFeedbackAvailability() {
  if (!isReviewReviewer() || feedbackAvailabilityRequest) {
    return feedbackAvailabilityRequest;
  }

  feedbackAvailabilityChecked = false;
  feedbackAvailabilityError = "";
  updateExportButton();
  feedbackAvailabilityRequest = (async () => {
    try {
      const response = await fetch("/api/reviews/feedback?limit=1", {
        headers: { Accept: "application/json" }
      });
      if (!response.ok) {
        throw new Error(await readApiError(response, "Saved feedback availability could not be checked."));
      }

      const entries = await readJsonResponse(response, "Saved feedback");
      sharedFeedbackAvailable = entries.length > 0;
    } catch (error) {
      feedbackAvailabilityError = error instanceof Error
        ? error.message
        : "Saved feedback availability could not be checked.";
    } finally {
      feedbackAvailabilityChecked = true;
      feedbackAvailabilityRequest = null;
      updateExportButton();
    }
  })();
  return feedbackAvailabilityRequest;
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
