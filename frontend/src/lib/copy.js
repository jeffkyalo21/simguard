// All user-facing text lives here. Components import from this file.
// Rules: sentence case, no exclamation marks, no filler words.
// Nav labels 1-2 words, page titles 1-3 words, buttons a verb (max 2 words),
// empty states one short line, errors max 12 words with no status codes,
// field errors max 6 words. One term per concept: SIM, Employee, Alert,
// Allocation.

export const copy = {
  app: {
    name: "SimGuard",
  },

  nav: {
    label: "Main",
    dashboard: "Dashboard",
    sims: "SIMs",
    alerts: "Alerts",
    usage: "Usage",
    allocations: "Allocations",
    auditLog: "Audit log",
    settings: "Settings",
  },

  pages: {
    dashboard: { title: "Dashboard", empty: "No data yet." },
    sims: { title: "SIMs", empty: "No SIMs yet." },
    simDetail: { title: "SIM details", empty: "No details yet." },
    alerts: { title: "Alerts", empty: "No alerts." },
    alertDetail: { title: "Alert details", empty: "No details yet." },
    usage: { title: "Usage", empty: "No usage data yet." },
    allocations: { title: "Allocations", empty: "No allocations yet." },
    auditLog: { title: "Audit log", empty: "No activity yet." },
    settings: { title: "Settings", empty: "No settings yet." },
  },

  actions: {
    signIn: "Sign in",
    signOut: "Sign out",
    tryAgain: "Try again",
    goHome: "Go home",
    viewSims: "View SIMs",
    close: "Close",
    cancel: "Cancel",
    save: "Save",
    add: "Add",
    edit: "Edit",
    review: "Review",
    viewAll: "View all",
    clearFilters: "Clear filters",
    previous: "Previous",
    next: "Next",
    rowActions: (number) => `Actions for ${number}`,
  },

  topbar: {
    searchLabel: "Search",
    searchPlaceholder: "Search",
    searchShortcut: "/",
    notifications: "Notifications",
    openMenu: "Open menu",
    userMenu: "Account",
  },

  sidebar: {
    collapse: "Collapse sidebar",
    expand: "Expand sidebar",
  },

  theme: {
    toggle: "Theme",
    light: "Light",
    dark: "Dark",
    system: "System",
  },

  // Placeholder until auth lands in #15.
  user: {
    name: "Jeff",
    initials: "J",
  },

  a11y: {
    skipToContent: "Skip to content",
    loading: "Loading",
    breadcrumb: "Breadcrumb",
    more: "More",
    pagination: "Pagination",
  },

  landing: {
    nav: {
      label: "Sections",
      features: "Features",
      how: "How it works",
    },
    heroLead: "Control",
    heroAccent: "every SIM.",
    heroSubtitle: "Track usage. Catch misuse. Cut waste.",
    chips: {
      flagged: "Flagged",
      flaggedValue: "94%",
      savings: "Est. savings",
      savingsValue: "KES 184,000",
      usage: "Usage",
      usageValue: "28 GB",
    },
    previewLabel: "Product preview",
    preview: {
      kpis: [
        { key: "active", label: "Active SIMs", value: 1000 },
        { key: "flagged", label: "Flagged", value: 14 },
        { key: "review", label: "To review", value: 62 },
      ],
      chart: "Risk over time",
      bars: "Alerts by type",
    },
    stats: [
      { key: "types", label: "Misuse types detected", value: 4 },
      { key: "plans", label: "Plans right-sized", value: 31 },
      { key: "hours", label: "Hours saved per month", value: 40 },
    ],
    featuresTitle: "Features",
    features: [
      { key: "detect", title: "Detect", text: "Flag misuse early." },
      { key: "allocate", title: "Allocate", text: "Right-size every plan." },
      { key: "explain", title: "Explain", text: "See why an alert fired." },
    ],
    visuals: {
      risk: "Risk",
      riskValue: "94%",
      planFrom: "Support",
      planTo: "Professional",
      whyTitle: "Why it fired",
      why: [
        { label: "Night usage", value: 82 },
        { label: "New devices", value: 64 },
        { label: "Data spikes", value: 47 },
      ],
    },
    howTitle: "How it works",
    steps: [
      { key: "connect", title: "Connect", text: "Import SIMs and usage." },
      { key: "score", title: "Score", text: "Rate every SIM for risk." },
      { key: "act", title: "Act", text: "Review, reassign, save." },
    ],
    ctaTitle: "Take control today.",
    footer: "SimGuard",
  },

  login: {
    title: "Sign in",
    email: "Email",
    emailPlaceholder: "name@company.com",
    password: "Password",
    passwordPlaceholder: "Password",
    forgotPassword: "Forgot password?",
    forgotHint: "Ask an admin to reset it.",
    errors: {
      emailRequired: "Enter your email.",
      emailInvalid: "Enter a valid email.",
      passwordRequired: "Enter your password.",
    },
  },

  risk: {
    cleared: "Cleared",
    escalated: "Escalated",
    flagged: "Flagged",
  },

  simStatus: {
    label: "Status",
    assigned: "Assigned",
    available: "Available",
    inactive: "Inactive",
  },

  tiers: {
    label: "Tier",
    Support: "Support",
    Professional: "Professional",
    Manager: "Manager",
    Field: "Field",
  },

  alertTypes: {
    inactiveBilling: "Inactive billing",
    sharing: "Sharing",
    reselling: "Reselling",
    ghostUsage: "Ghost usage",
  },

  units: {
    gb: (value) => `${value} GB`,
    ofGb: (used, limit) => `${used} of ${limit} GB`,
    ofMin: (used, limit) => `${used} of ${limit} min`,
    ofCount: (used, limit) => `${used} of ${limit}`,
    percent: (value) => `${value}%`,
    day: (value) => `Day ${value}`,
  },

  filters: {
    all: "All",
  },

  pagination: {
    range: (from, to, total) =>
      `${from.toLocaleString("en-KE")} to ${to.toLocaleString("en-KE")} of ${total.toLocaleString("en-KE")}`,
  },

  dashboard: {
    period: "Period",
    periods: {
      7: "Last 7 days",
      30: "Last 30 days",
      90: "Last 90 days",
    },
    kpis: {
      activeSims: "Active SIMs",
      flagged: "Flagged",
      toReview: "To review",
      savings: "Est. savings",
    },
    riskOverTime: "Risk over time",
    riskSeries: "Risk",
    peak: "Peak",
    alertsByType: "Alerts by type",
    needsReview: "Needs review",
    tierChanges: "Tier changes",
    tierUpgrade: "Upgrade",
    tierDowngrade: "Downgrade",
    tierKeep: "Keep",
    emptyChart: "No risk data for this period.",
    emptyAlerts: "No alerts for this period.",
    emptyReview: "Nothing to review.",
    emptyTiers: "No tier changes.",
  },

  columns: {
    sim: "SIM",
    number: "Number",
    employee: "Employee",
    department: "Department",
    tier: "Tier",
    dataUsed: "Data used",
    status: "Status",
    risk: "Risk",
    type: "Type",
    date: "Date",
    from: "From",
    to: "To",
    time: "Time",
    action: "Action",
    actions: "Actions",
  },

  sims: {
    add: "Add SIM",
    searchLabel: "Search SIMs",
    searchPlaceholder: "Number or employee",
    department: "Department",
    count: (total) => `${total.toLocaleString("en-KE")} ${total === 1 ? "SIM" : "SIMs"}`,
    noMatch: "No SIMs match.",
    unassigned: "Unassigned",
    setInactive: "Set inactive",
    setAvailable: "Set available",
    allocatedReason: "Return the SIM first",
    form: {
      addTitle: "Add SIM",
      editTitle: "Edit SIM",
      phone: "Number",
      phonePlaceholder: "0712 345 678",
      iccid: "ICCID",
      iccidPlaceholder: "8925 4010 0000 0012",
      operator: "Operator",
      plan: "Plan",
      cost: "Monthly cost (KES)",
      errors: {
        phoneRequired: "Enter a number.",
        phoneInvalid: "Use a valid Kenyan number.",
        phoneTaken: "Number already exists.",
        iccidRequired: "Enter an ICCID.",
        iccidTooLong: "Use 30 characters or fewer.",
        iccidTaken: "ICCID already exists.",
        operatorRequired: "Choose an operator.",
        planRequired: "Choose a plan.",
        costInvalid: "Enter 0 or more.",
      },
    },
    confirm: {
      inactiveTitle: "Set SIM inactive?",
      inactiveText: (number) => `${number} stops working until reactivated.`,
      availableTitle: "Set SIM available?",
      availableText: (number) => `${number} can be allocated again.`,
    },
    toasts: {
      added: (number) => `${number} added.`,
      saved: (number) => `${number} saved.`,
      inactive: (number) => `${number} set inactive.`,
      available: (number) => `${number} set available.`,
      tierChanged: (tier) => `Tier changed to ${tier}.`,
    },
  },

  simProfile: {
    changeTier: "Change tier",
    suspend: "Suspend",
    activate: "Activate",
    tierTitle: "Change tier",
    tierOption: (tier, gb) => `${tier}, ${gb} GB`,
    details: "Details",
    employee: "Employee",
    department: "Department",
    tier: "Tier",
    operator: "Operator",
    iccid: "ICCID",
    assigned: "Assigned",
    dataThisCycle: "Data this cycle",
    dataSeries: "Data used",
    limit: "Limit",
    data: "Data",
    voice: "Voice",
    sms: "SMS",
    tabs: {
      label: "SIM records",
      alerts: "Alerts",
      allocations: "Allocations",
      activity: "Activity",
    },
    current: "Current",
    noAlerts: "No alerts.",
    noAllocations: "No allocations.",
    noActivity: "No activity.",
    notFound: "SIM not found.",
    activity: {
      created: "SIM added",
      edited: "Details edited",
      allocated: (name) => `Allocated to ${name}`,
      returned: (name) => `Returned by ${name}`,
      alert: (type) => `Alert raised: ${type}`,
      tier: (tier) => `Tier changed to ${tier}`,
      inactive: "Set inactive",
      available: "Set available",
    },
  },

  notFound: {
    title: "Page not found",
    text: "Check the address or go home.",
  },

  errors: {
    generic: "Something went wrong. Try again.",
    load: "Could not load data. Try again.",
    network: "Cannot reach the server. Check your connection.",
    validation: "Some fields need attention. Fix them and retry.",
    notFound: "That item no longer exists. Refresh the page.",
    conflict: "This clashes with newer data. Refresh and retry.",
    server: "The server had a problem. Try again shortly.",
  },

  common: {
    noValue: "—",
  },
};
