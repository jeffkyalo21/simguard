# Frontend - Next.js

The SimGuard web app: a dashboard for monitoring a company SIM fleet (SIMs,
allocations, usage and misuse alerts). It will talk to the ASP.NET Core API in
`backend/SimGuard.Api`.

Built so far: the scaffold (#16) and the Landing, Login, Dashboard, SIM list
and SIM profile screens (#17). **All screens run on mock data**: there are no
API calls, no auth and no CORS needed yet. Auth arrives in #15, the remaining
screens in #18 and #19.

## Prerequisites

- Node.js 24 and npm 11 (npm only, nothing installed globally)

## Setup

```bash
npm install
cp .env.example .env.local
npm run dev
```

Open http://localhost:3000. Any email and password signs in (mock login).

`NEXT_PUBLIC_API_URL` in `.env.local` sets the API base URL (default
`http://localhost:5275`). Nothing reads it until the mock layer is swapped
for real calls. `.env.local` is git-ignored; never commit it.

## Scripts

| Script          | What it does                     |
| --------------- | -------------------------------- |
| `npm run dev`   | Development server on port 3000  |
| `npm run build` | Production build                 |
| `npm run start` | Serve the production build       |
| `npm run lint`  | ESLint                           |

## Folder layout

```
src/
  app/
    layout.jsx              root layout: font, glass theme, backdrop, toaster
    icon.svg                favicon (the SimGuard mark)
    (marketing)/page.jsx    landing page at "/"
    (auth)/login/page.jsx   login
    (app)/layout.jsx        app shell + mock store provider
    (app)/dashboard, sims, sims/[id]        built screens
    (app)/alerts, alerts/[id], usage, allocations, audit-log, settings   placeholders
    not-found.jsx, error.jsx, loading.jsx
  components/
    landing/    landing sections, landing.css and its motion helpers (used by "/" only)
    dashboard/  dashboard-view
    sims/       sims-view, sim-profile, sim-form-dialog, sim-status-dialog, tier-dialog
    data/       data-table, pagination, filter-select, search-input, kpi-card,
                section-card, bar-list, line-chart, progress-bar, risk-bar, skeletons
    shared/     status-badge, form-dialog, confirm-dialog, form-field, busy-button,
                error-state, empty-state, disabled-reason, page-breadcrumb,
                not-found-content, glass-backdrop
    layout/     app-sidebar, topbar, theme-toggle, page-header, page-transition, logo
    glass/      glass-card
    auth/       login-form
    ui/         shadcn/ui components (generated, keep edits minimal)
  hooks/
    use-resource.js       load data with loading, error and retry
    use-opening-key.js    reset a dialog's form each time it opens
    use-return-focus.js   return focus to whatever opened a dialog
  lib/
    data.js        the only data access point for screens (mock for now)
    mock/          fixtures, seeded random, in-memory db and its React store
    validation.js  phone normalising and SIM form rules (mirrors the backend)
    format.js      dates, KES, numbers, phone numbers, data sizes
    copy.js        every user-facing string
    api.js         fetch wrapper for when real calls start
```

## Mock data layer

Screens never import from `lib/mock` directly. They call functions in
`src/lib/data.js` (`listSims`, `getSim`, `getDashboard`, `createSim`,
`setSimStatus` and so on), which return the same shapes the backend returns
(`{ items, page, pageSize, total }` for lists, `ApiError` with a field `errors`
map for validation) after a ~300 ms delay. Swapping to the real API later
means changing the function bodies in `data.js` only.

- **Fixtures** (`lib/mock/fixtures.js`) are deterministic: a seeded random
  generator builds 120 SIMs with fictional, generic employees and the
  departments Flight ops, Ground handling, Finance, IT, Cargo and Customer
  care. Operators are "Carrier A/B/C". The clock is fixed at 29 Oct 2026.
- **Writes persist for the session.** The `(app)` layout holds a reducer
  store (`lib/mock/store.jsx`); adding, editing or changing a SIM updates the
  list, the profile and the dashboard during client navigation. A full page
  reload resets to the fixtures.
- **State checks:** add `?mock=error` or `?mock=empty` to any app URL to see
  the error or empty states.

Use `useResource(load, key)` from `src/hooks` to load data in a screen (`key`
is an array of the inputs, like effect deps). It gives
`{ data, error, loading, retry }` and reloads when the store changes.

## Theme

**One glass theme everywhere.** The root layout puts `.glass-theme` on
`<body>` and renders one shared `GlassBackdrop` behind every page. It is static
on all pages and only animated on Landing (`<GlassBackdrop animated />`). Use
`GlassCard` (`variant="strong"` for tables, forms and long text) instead of
per-page surfaces. Portaled dialogs, menus, selects and sheets pick up the
same glass styles automatically.

Light and dark mode come from `next-themes` (system default). The toggle
cross-fades with the View Transitions API where the browser supports it.

### Palette tokens

Defined in `src/app/globals.css` under `.glass-theme` and exposed to Tailwind
through `@theme`:

| Token | Use |
| ----- | --- |
| `--brand` (`#ED1C24`), `--brand-text`, `--brand-soft` | Brand red: the logo, active nav marker, focus ring, text links (`.link-accent`), the hero KPI |
| `--primary` (near-black in light, near-white in dark) | Primary buttons |
| `--risk-flagged-*`, `--risk-escalated-*`, `--risk-cleared-*` | Status badges and risk bars, each with a background, foreground and strong bar colour |
| `--glass-bg`, `--glass-bg-strong`, `--glass-border` | Glass surfaces |
| `--chart-grid`, `--chart-axis` | Chart lines and labels |

**Accent rule:** brand red is an accent, not a fill. Never use it for primary
buttons, chart data or risk levels; risk colours are separate tokens so a
flagged badge is never confused with branding. shadcn's own `--accent` stays
the neutral hover colour, which is why the brand token is called `--brand`.

## Motion

- **Landing only** gets rich motion: reveals on scroll, count-ups, chart
  draw-in, tilt, parallax and the drifting backdrop. It uses CSS plus
  IntersectionObserver (no animation library), only animates `transform` and
  `opacity`, and pauses when off screen or when the tab is hidden.
- **Everywhere else** motion is limited to hover, focus, press and short
  transitions (page enter, dialogs, menus): 150 to 250 ms using the
  `--dur-fast`, `--dur-base`, `--dur-slow` and `--ease-out` tokens, on
  `transform` and `opacity` only. Charts render without animation.
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
- **Branding.** The product is "SimGuard" with its generic shield mark. Do not
  name real companies, carriers or partners anywhere (code, copy, mock data,
  file names, alt text) and do not add third-party logos or "trusted by"
  strips. Mock people and departments stay fictional and generic.

## Adding a screen

1. Add `src/app/(app)/<route>/page.jsx`. Keep it a thin server component that
   renders one client view (`components/<area>/<area>-view.jsx`). If the view
   reads `useSearchParams`, wrap it in `<Suspense>` with a skeleton fallback.
2. Add the data functions to `src/lib/data.js` (with mock bodies under
   `src/lib/mock`), returning the backend's response shape.
3. Load data with `useResource`, and build from `PageHeader`, `GlassCard`,
   `SectionCard`, `DataTable`, `FilterSelect`, `Pagination`, `FormDialog` and
   `ConfirmDialog` so states (loading, empty, error) come for free.
4. Put every string in `copy.js` and add the route to
   `components/layout/nav-items.js` if it belongs in the sidebar.

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
