// All user-facing text lives here. Components import from this file.
// Rules: sentence case, no exclamation marks, no filler words.
// Nav labels 1-2 words, page titles 1-3 words, buttons a verb (max 2 words),
// empty states one short line, errors max 12 words with no status codes.

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
    close: "Close",
    cancel: "Cancel",
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
  },

  landing: {
    heroTitle: "Control every SIM.",
    heroSubtitle: "Track usage. Catch misuse. Cut waste.",
    previewLabel: "Product preview",
    features: [
      { key: "detect", title: "Detect", text: "Flag misuse early." },
      { key: "allocate", title: "Allocate", text: "Right-size every plan." },
      { key: "explain", title: "Explain", text: "See why an alert fired." },
    ],
    footer: "SimGuard",
  },

  login: {
    title: "Sign in",
    email: "Email",
    password: "Password",
    forgotPassword: "Forgot password?",
    unavailable: "Sign-in is not available yet.",
  },

  risk: {
    cleared: "Cleared",
    escalated: "Escalated",
    flagged: "Flagged",
  },

  notFound: {
    title: "Page not found",
    text: "Check the address or go home.",
  },

  errors: {
    generic: "Something went wrong. Try again.",
    network: "Cannot reach the server. Check your connection and retry.",
    validation: "Some fields need attention. Fix them and retry.",
    notFound: "That item no longer exists. Go back and refresh.",
    conflict: "This change clashes with current data. Refresh and retry.",
    server: "The server had a problem. Try again shortly.",
  },

  common: {
    noValue: "—",
  },
};
