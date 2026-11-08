# Frontend - Next.js

The SimGuard web app: a dashboard for monitoring a company SIM fleet (SIMs,
allocations, usage and misuse alerts). It talks to the ASP.NET Core API in
`backend/SimGuard.Api` through `lib/data.js`; every screen reads and writes
real backend data (#14). There is no mock data: a screen shows the backend's
answer or an error.

## Prerequisites

- Node.js 24 and npm 11 (npm only, nothing installed globally)

## Setup

```bash
npm install
cp .env.example .env.local
npm run dev
```

Open http://localhost:3000 and sign in with a backend account. The backend
must be running: `BACKEND_URL` in `.env.local` is its address (default
`http://localhost:5275`). It is read on the server only; the browser never
talks to the backend and never sees a token. `.env.local` is git-ignored;
never commit it. See [docs/auth-frontend.md](docs/auth-frontend.md) for the
design, the cookies, the route handlers and how to run it locally.

## Scripts

| Script          | What it does                     |
| --------------- | -------------------------------- |
| `npm run dev`   | Development server on port 3000  |
| `npm run build` | Production build                 |
| `npm run start` | Serve the production build       |
| `npm run lint`  | ESLint                           |
| `npm test`      | Unit tests (`node --test`)       |

## Folder layout

```
src/
  app/
    layout.jsx              root layout: font, glass theme, backdrop, toaster
    icon.svg                favicon (the SimGuard mark)
    (marketing)/page.jsx    landing page at "/"
    (auth)/login/...        sign in, mfa, mfa-setup, change-password
    (app)/layout.jsx        app shell: checks the session, checks the session, auth provider
    (app)/admin             Admin area: scoring status and Run now
    api/auth/...            route handlers: login, mfa, enrolment, password, logout, me
    api/backend/[...path]   forwards signed in calls to the backend
    (app)/dashboard, sims, sims/[id]        built screens
    (app)/alerts, alerts/[id], usage, allocations, audit-log, settings,
    tier-recommendations
    not-found.jsx, error.jsx
                (no root loading.jsx: the nav would then stream first; the
                dynamic routes set `instant = false` and block instead)
  proxy.js                  sends pages without a session cookie to /login, and
                            404s unknown SIM ids before /sims/[id] renders
                            (its prerendered shell would otherwise send a 200)
  components/
    landing/    landing sections, landing.css and its motion helpers (used by "/" only)
    dashboard/  dashboard-view
    admin/      admin-view (scoring status)
    tier-recommendations/  tier-recommendations-view
    sims/       sims-view, sim-profile, sim-form-dialog, sim-status-dialog, tier-dialog
    data/       data-table, pagination, filter-select, search-input, kpi-card,
                section-card, bar-list, bar-chart, progress-bar, risk-bar, skeletons
    shared/     status-badge, form-dialog, confirm-dialog, form-field, busy-button,
                error-state, empty-state, disabled-reason, page-breadcrumb,
                not-found-content, glass-backdrop
    layout/     pill-nav, icon-button, nav-items, theme-toggle, page-header,
                page-transition, logo
    glass/      glass-card
    auth/       login, mfa, mfa-setup and change-password forms, auth-provider,
                route-guard, sign-out-button, error-summary, qr-code
    ui/         shadcn/ui components (generated, keep edits minimal)
  hooks/
    use-resource.js       load data with loading, error and retry
    use-permission.js     whether the role may do an action, and why not
    use-opening-key.js    reset a dialog's form each time it opens
    use-return-focus.js   return focus to whatever opened a dialog
  lib/
    data.js        the only data access point for screens (backend calls)
    data-version.js  bumped after every write so loaded screens reload
    validation.js  phone normalising and SIM form rules (mirrors the backend)
    format.js      dates, KES, numbers, phone numbers, data sizes
    copy.js        every user-facing string
    api.js         fetch wrapper for /api/backend, ApiError, errorMessage
    auth/          core (pure helpers, unit tested), server (cookies, backend
                   calls) and client (route handler calls)
    permissions.js which role may open which page and do which action
```

## Data layer

Screens never call `fetch` themselves. They call functions in
`src/lib/data.js` (`listSims`, `getSim`, `getDashboard`, `createSim`,
`setSimStatus` and so on), which call the backend through `/api/backend` and
return its response shapes (`{ items, page, pageSize, total }` for lists).
A failed call throws `ApiError` with the status, the field `errors` map and
any extra ProblemDetails fields in `extensions`. Show it with
`errorMessage(error, { 409: "..." })` from `lib/api.js`, never the backend's
own sentence.

- **Writes refresh screens.** Every write bumps the data version
  (`lib/data-version.js`) on success and on 409, so every mounted
  `useResource` reloads.
- **Role gating.** Buttons for actions the role cannot do are disabled with a
  `DisabledReason` tooltip (`usePermission(action)`); the backend refuses the
  same actions with 403.
- **No browser clock.** No screen uses the current date as "now"; dates come
  from the backend.

Use `useResource(load, key)` from `src/hooks` to load data in a screen (`key`
is an array of the inputs, like effect deps). It gives
`{ data, error, loading, retry }` and reloads when the data version changes.

## Theme

**One cream glass theme everywhere.** The root layout puts `.glass-theme` on
`<body>` and renders one shared `GlassBackdrop` (a warm cream base with soft
blurred shapes) behind every page. It is static on all pages and only animated
on Landing (`<GlassBackdrop animated />`). Portaled dialogs, menus, selects and
sheets pick up the glass popover style automatically.

Use `GlassCard` (or `SectionCard`) with one of three surfaces:

| Variant | Use |
| ------- | --- |
| `default` | Most cards: KPIs, charts, details. Lifts 4 px on hover. |
| `strong` | Tables, forms and long text (more opaque, no lift). |
| `dark` | At most one per page, for the item that needs attention (dashboard: "Needs review"). |

`hero` adds the brand glow to one KPI per page (dashboard: Flagged).

Light and dark mode come from `next-themes` (system default). The toggle
cross-fades with the View Transitions API where the browser supports it.

### Palette tokens

Defined in `src/app/globals.css` under `.glass-theme` and exposed to Tailwind
through `@theme`:

| Token | Use |
| ----- | --- |
| `--ink` (`#141414`) | Text and chart lines |
| `--brand` (`#ED1C24`), `--brand-text`, `--brand-soft`, `--brand-glow` | Brand red accent (see the rule below) |
| `--primary` (`#111` in light, near-white in dark) | Primary buttons and the active nav pill |
| `--risk-flagged-*`, `--risk-escalated-*`, `--risk-cleared-*` | Status badges and risk bars, each with a background, foreground and strong bar colour |
| `--glass-bg`, `--glass-bg-strong`, `--glass-dark-bg`, `--glass-border` | The three glass surfaces |
| `--radius-card` (24 px), `--radius-pill`, `--radius-popover` (16 px) | Corner radii |
| `--chart-track`, `--chart-grid`, `--chart-axis`, `--warm-grey` | Chart and progress tracks, grid lines, labels |

**Accent rule:** brand red is only for the logo, text links (`.link-accent`),
focus rings, the notification dot, the Landing hero accent words and gradient
accents, and the glow of the one hero KPI. Never use it for buttons, the active
nav pill, chart data or risk levels. Risk colours are separate tokens (flagged
`#B42318`, escalated amber, cleared `#1E7F4F`), always shown with an icon and
text, so a flagged badge is never confused with branding. shadcn's own `--accent` stays
the neutral hover colour, which is why the brand token is called `--brand`.

## Navigation

The app shell (`components/layout/pill-nav.jsx`) is a floating pill bar, not a
sidebar: a round logo button (to `/dashboard`), a centre pill of the links from
`nav-items.js` with an ink indicator that slides to the active link
(`transform` only, 200 ms), and a cluster of round controls on the right:
search (opens on click or `/`, Escape closes; on pages with their own search
`/` focuses that), notifications, theme and the account menu with Sign out.
Below 1100 px the links move into a sheet behind a "Menu" button. Content is
centred at a 1280 px max width with 24 px side padding. Login has no nav.

## Motion

- **Landing only** gets rich motion: reveals on scroll, count-ups, chart
  draw-in, tilt, parallax and the drifting backdrop. It uses CSS plus
  IntersectionObserver (no animation library), only animates `transform` and
  `opacity`, and pauses when off screen or when the tab is hidden.
- **Login and the app** allow only: hover and focus (cards lift 4 px), the
  page content transition (fade, 6 px slide and a 4 px blur clearing, 200 to
  250 ms), the sliding nav indicator, dialogs, sheets, menus, tooltips, the
  search expand, the theme cross-fade, the spinner and the skeleton pulse.
  Durations come from the `--dur-fast`, `--dur-base`, `--dur-slow` and
  `--ease-out` tokens. Charts render without animation
  (`isAnimationActive={false}`).
- `prefers-reduced-motion: reduce` turns all of it off (spinners stay), and
  content is never hidden waiting for an animation.

## Accessibility

- WCAG 2.2 AA contrast for text in both themes; 44 px targets on touch
  screens; visible focus rings; a skip link; landmarks and labelled regions.
- Dialogs trap focus, close on Escape and return focus to whatever opened
  them. Forms validate on submit, show the message under each field and focus
  the first invalid one. Disabled actions say why (tooltip and screen reader
  text).
- Fallbacks: without `backdrop-filter`, glass turns into a near-solid
  surface; `prefers-reduced-transparency` makes every surface solid and stops
  the backdrop; `prefers-contrast: more` darkens muted text and borders.
- Landing content works without JavaScript; hidden start states only apply
  under `@media (scripting: enabled)`.

## Rules

- **JavaScript only.** No TypeScript: no `tsconfig.json`, no `.ts` or `.tsx`
  files, no `typescript` or `@types/*` packages. Components use `.jsx`, plain
  modules `.js`. `jsconfig.json` defines the `@/*` alias. Use short JSDoc
  comments for data shapes where they help. `components.json` has
  `"tsx": false`, so `npm exec shadcn@latest add <name>` generates `.jsx`.
- **All copy lives in `src/lib/copy.js`.** Sentence case, short, no status
  codes shown to users.
- **Branding.** The product is "SimGuard" with its generic shield mark. Never
  include real company names or logos anywhere (code, comments, copy, demo
  data, file names, alt text, docs), and no "trusted by" strips. Demo people
  and departments stay fictional and generic. No photo or image backdrops.

## Adding a screen

1. Add `src/app/(app)/<route>/page.jsx`. Keep it a thin server component that
   renders one client view (`components/<area>/<area>-view.jsx`). If the view
   reads `useSearchParams`, wrap it in `<Suspense>` with a skeleton fallback.
2. Add the data functions to `src/lib/data.js`, returning the backend's
   response shape.
3. Load data with `useResource`, and build from `PageHeader`, `GlassCard`,
   `SectionCard`, `DataTable`, `FilterSelect`, `Pagination`, `FormDialog` and
   `ConfirmDialog` so states (loading, empty, error) come for free. Pick the
   surface by content (`strong` for tables and forms) and keep to one `dark`
   card per page.
4. Put every string in `copy.js` and add the route to
   `components/layout/nav-items.js` if it belongs in the pill nav (it then
   appears in the centre pill and the Menu sheet).

## Dates, money and phone numbers

The API sends timestamps as UTC ISO strings. The UI always shows them in
Nairobi time (`Africa/Nairobi`, `en-KE`) with `formatDateTime`, `formatDate`
and `formatShortDate` from `src/lib/format.js`, never the browser's local
zone. Money is KES (`formatKes`, "KES 184,000"), phone numbers are E.164
(`formatPhone` groups Kenyan numbers as `+254 712 345 678`). Search and forms
accept local formats such as `0712 345 678` (`normalizePhone` in
`lib/validation.js`).

## API

`src/lib/api.js` exposes `api.get/post/put/patch/delete`. Paths are relative
to `/api` (for example `api.get("/sims", { query: { page: 1 } })`). Errors
throw `ApiError` with `status`, `title`, `detail` and the ProblemDetails
`errors` field map; `errorMessage(error)` gives the short user-facing text.
Lists return `{ items, page, pageSize, total }`. Not used by screens yet.

**CORS:** the backend does not enable CORS yet (#20).

## Design

Wireframes (frames "01 Landing" to "11 Settings"):
https://www.figma.com/design/sL6dcNAIrviVjQu2SsH7us. PNG exports of 01 to 05
are in `docs/SimGuard wireframes (1)`.
