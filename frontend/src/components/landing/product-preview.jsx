import { CountUp } from "@/components/landing/count-up";
import { InView } from "@/components/landing/in-view";
import { Parallax } from "@/components/landing/parallax";
import { SimCard } from "@/components/landing/sim-card";
import { Tilt } from "@/components/landing/tilt";
import { riskLevel } from "@/components/data/risk-bar";
import { getSampleDashboard } from "@/lib/data";
import { formatNumber, formatPhone } from "@/lib/format";
import { copy } from "@/lib/copy";
import { cn } from "@/lib/utils";

const text = copy.landing;
const board = copy.dashboard;

// The real dashboard at "Last 30 days", built from the same mock data on
// the server, so the preview cannot drift from it.
const DASHBOARD = getSampleDashboard(30);

const KPIS = [
  { key: "activeSims", hero: false },
  { key: "flagged", hero: true },
  { key: "toReview", hero: false },
].map(({ key, hero }) => ({ key, hero, label: board.kpis[key], value: DASHBOARD.kpis[key] }));

const REVIEW = DASHBOARD.needsReview.slice(0, 3).map((row) => ({
  id: row.id,
  number: formatPhone(row.phoneNumber),
  employee: row.employeeName ?? copy.sims.unassigned,
  risk: Math.round(row.riskScore * 100),
  level: riskLevel(row.riskScore),
}));

const RISK_FILL = {
  flagged: "bg-risk-flagged-strong",
  escalated: "bg-risk-escalated-strong",
  cleared: "bg-risk-cleared-strong",
};

const SPARK = [6, 9, 7, 12, 10, 15, 13, 18];

function points(values, width, height, max) {
  const step = width / (values.length - 1);
  return values.map((value, index) => `${(index * step).toFixed(1)},${(height - (value / max) * height).toFixed(1)}`).join(" ");
}

function MiniNav() {
  return (
    <div className="flex items-center justify-between gap-3 px-3 pt-3 sm:px-4 sm:pt-4">
      <span className="size-7 shrink-0 rounded-full bg-brand-solid" />
      <div className="hidden items-center gap-1 rounded-full border border-glass-border bg-glass-strong p-1 sm:flex">
        {[copy.nav.dashboard, copy.nav.sims, copy.nav.alerts, copy.nav.usage].map((label, index) => (
          <span
            key={label}
            className={cn(
              "rounded-full px-3 py-1 text-[11px] font-medium",
              index === 0 ? "bg-primary text-primary-foreground" : "text-muted-foreground"
            )}
          >
            {label}
          </span>
        ))}
      </div>
      <div className="flex gap-1">
        {[0, 1, 2].map((index) => (
          <span key={index} className="size-7 rounded-full border border-glass-border bg-glass-strong" />
        ))}
      </div>
    </div>
  );
}

/** The dashboard's "Data wasted" card, scaled down: total, share and one used against paid bar. */
function MiniDataWaste() {
  const { paidGb, usedGb, unusedGb, percent } = DASHBOARD.dataWaste;
  const usedShare = paidGb > 0 ? usedGb / paidGb : 0;
  // " GB" from the same unit copy as the dashboard, so only the number counts.
  const gbSuffix = copy.units.gb("");

  return (
    <div className="flex min-w-0 flex-col gap-3 rounded-2xl border border-glass-border bg-glass-strong p-3">
      <p className="text-xs font-medium">{board.dataWasted}</p>
      <div className="flex flex-col gap-0.5">
        <p className="text-2xl font-semibold tracking-tight sm:text-3xl">
          <CountUp value={unusedGb} suffix={gbSuffix} />
        </p>
        <p className="text-[11px] text-muted-foreground sm:text-xs">{board.wastedShare(percent)}</p>
      </div>
      {/* Sits under the share line, not at the bottom, so the usage chip
          over the box's lower left corner never covers it. */}
      <div className="flex flex-col gap-1.5">
        <span className="block h-2 overflow-hidden rounded-full bg-chart-track">
          <span className="fill-x block h-full rounded-full bg-foreground" style={{ "--v": usedShare }} />
        </span>
        <div className="flex justify-between gap-2 text-[11px]">
          <span className="truncate">
            <span className="text-muted-foreground">{board.used}</span>{" "}
            <span className="font-medium tabular-nums">{copy.units.gb(formatNumber(usedGb))}</span>
          </span>
          <span className="truncate">
            <span className="text-muted-foreground">{board.paid}</span>{" "}
            <span className="font-medium tabular-nums">{copy.units.gb(formatNumber(paidGb))}</span>
          </span>
        </div>
      </div>
    </div>
  );
}

function MiniReview() {
  return (
    <div className="glass-card-dark min-w-0 rounded-2xl border p-3">
      <p className="mb-3 text-xs font-medium">{board.needsReview}</p>
      <ul className="flex flex-col gap-2.5">
        {REVIEW.map(({ id, number, employee, risk, level }, index) => (
          <li key={id} className="grid grid-cols-[1fr_auto] items-center gap-x-2 gap-y-0.5 text-[11px]">
            <span className="truncate tabular-nums">{number}</span>
            <span className="tabular-nums">{copy.units.percent(risk)}</span>
            <span className="col-span-2 truncate opacity-70">{employee}</span>
            <span className="col-span-2 mt-1 h-1.5 overflow-hidden rounded-full bg-chart-track">
              <span
                className={cn("fill-x block h-full rounded-full", RISK_FILL[level])}
                style={{ "--v": risk / 100, "--i": index }}
              />
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function MiniDashboard() {
  return (
    <div className="flex min-w-0 flex-1 flex-col gap-3 p-3 sm:p-4">
      <div className="grid grid-cols-3 gap-2 sm:gap-3">
        {KPIS.map(({ key, label, value, hero }) => (
          <div
            key={key}
            className={cn(
              "rounded-2xl border border-glass-border bg-glass-strong p-2.5 sm:p-3.5",
              hero && "shadow-[var(--brand-glow)]"
            )}
          >
            <p className="truncate text-[11px] text-muted-foreground sm:text-xs">{label}</p>
            <p className="text-lg font-semibold sm:text-2xl">
              <CountUp value={value} />
            </p>
          </div>
        ))}
      </div>
      <div className="grid gap-2 sm:grid-cols-[1.5fr_1fr] sm:gap-3">
        <MiniDataWaste />
        <MiniReview />
      </div>
    </div>
  );
}

// The SIM card holds the top right corner, so the one chip sits bottom left.
function Chips() {
  return (
    <div className="pointer-events-none absolute inset-0 hidden md:block" aria-hidden="true">
      <Parallax speed={0.07} className="absolute -bottom-5 -left-3 xl:-left-14">
        <div className="chip-float glass-card flex items-center gap-3 px-3 py-2" style={{ "--delay": "-5s" }}>
          <div className="flex flex-col">
            <span className="text-xs text-muted-foreground">{text.chips.usage}</span>
            <span className="text-sm font-semibold tabular-nums">{text.chips.usageValue}</span>
          </div>
          <svg viewBox="0 0 80 28" className="h-7 w-20">
            <polyline points={points(SPARK, 80, 26, 20)} fill="none" stroke="var(--foreground)" strokeWidth="1.5" />
          </svg>
        </div>
      </Parallax>
    </div>
  );
}

/**
 * A built-in-code mock of the dashboard: KPIs and the wasted total count
 * up, and the used bar and risk bars fill, once, when it scrolls into view;
 * the frame tilts toward the pointer. The animated SIM card (the first "Needs review" row)
 * overlaps its top right corner; a usage chip drifts with parallax.
 */
export function ProductPreview() {
  return (
    <InView as="section" track threshold={0} aria-label={text.previewLabel} className="px-4 pt-6 pb-16 sm:px-6 md:pt-0">
      <div className="relative mx-auto max-w-5xl">
        <InView threshold={0.3}>
          <Tilt>
            <div
              role="img"
              aria-label={text.previewLabel}
              className="glass-card flex overflow-hidden p-0 shadow-[var(--glass-shadow-lift)] [--radius-card:28px]"
            >
              <div aria-hidden="true" className="flex min-w-0 flex-1 flex-col">
                <MiniNav />
                <MiniDashboard />
              </div>
            </div>
          </Tilt>
        </InView>
        <Chips />
        <SimCard
          number={REVIEW[0].number}
          risk={REVIEW[0].risk}
          className="absolute -top-[65px] -right-1.5 origin-top-right scale-[0.7] md:-top-[112px] md:-right-3 md:scale-100 lg:-right-4 xl:-right-10"
        />
      </div>
    </InView>
  );
}
