import { BarList } from "@/components/data/bar-list";
import { KpiCard } from "@/components/data/kpi-card";
import { ProgressBar } from "@/components/data/progress-bar";
import { RiskBar, riskLevel } from "@/components/data/risk-bar";
import { GlassCard } from "@/components/glass/glass-card";
import { ExplainVisual } from "@/components/landing/feature-visuals";
import { SAMPLE_DASHBOARD, SAMPLE_SIM, TOUR_ALERTS } from "@/components/landing/landing-data";
import { RiskBadge } from "@/components/shared/risk-badge";
import { StatusBadge } from "@/components/shared/status-badge";
import { formatNumber, formatPhone } from "@/lib/format";
import { copy } from "@/lib/copy";

// Non-interactive mocks of app screens, built from the app's own data
// components. ProductTour renders them aria-hidden and inert at 960 x 560,
// scaled to fit. Titles are plain text so the page outline stays clean.

function ScreenTitle({ children }) {
  return <p className="text-2xl font-semibold tracking-tight">{children}</p>;
}

function CardTitle({ children }) {
  return <p className="mb-4 text-base font-semibold">{children}</p>;
}

const formatGb = (value) => copy.units.gb(formatNumber(value));

/** The dashboard at "Last 30 days", from fixed sample data. */
export function DashboardScreen() {
  const dashboard = SAMPLE_DASHBOARD;
  const kpis = Object.fromEntries(Object.entries(dashboard.kpis).map(([key, value]) => [key, formatNumber(value)]));
  const unused = dashboard.dataWaste.departments.map(({ name, unusedGb }) => ({ key: name, label: name, value: unusedGb }));
  const review = dashboard.needsReview.slice(0, 4).map((row) => ({
    id: row.id,
    number: formatPhone(row.phoneNumber),
    score: row.riskScore,
  }));
  const labels = copy.dashboard.kpis;

  return (
    <div className="flex h-full flex-col gap-4">
      <ScreenTitle>{copy.pages.dashboard.title}</ScreenTitle>
      <div className="grid grid-cols-4 gap-4">
        <KpiCard label={labels.activeSims} value={kpis.activeSims} />
        <KpiCard label={labels.flagged} value={kpis.flagged} hero />
        <KpiCard label={labels.toReview} value={kpis.toReview} />
        <KpiCard label={labels.resolved} value={kpis.resolved} />
      </div>
      <div className="grid flex-1 grid-cols-[1.4fr_1fr] gap-4">
        <GlassCard>
          <CardTitle>{copy.dashboard.topDepartments}</CardTitle>
          <BarList items={unused} formatValue={formatGb} />
        </GlassCard>
        <GlassCard variant="dark">
          <CardTitle>{copy.dashboard.needsReview}</CardTitle>
          <ul className="flex flex-col gap-3 text-sm">
            {review.map(({ id, number, score }) => (
              <li key={id} className="flex items-center justify-between gap-3">
                <span className="tabular-nums">{number}</span>
                <RiskBar score={score} />
              </li>
            ))}
          </ul>
        </GlassCard>
      </div>
    </div>
  );
}

export function ProfileScreen() {
  const sim = SAMPLE_SIM;
  const text = copy.simProfile;
  const details = [
    [text.employee, sim.employee],
    [text.department, sim.department],
    [text.tier, copy.tiers[sim.tier]],
    [text.operator, sim.operator],
    [text.iccid, sim.iccid],
  ];

  return (
    <div className="flex h-full flex-col gap-4">
      <div className="flex items-center gap-3">
        <ScreenTitle>{sim.number}</ScreenTitle>
        <StatusBadge status={sim.status} />
        <RiskBadge level={riskLevel(sim.risk)} />
      </div>
      <div className="grid flex-1 grid-cols-[1fr_1.2fr] gap-4">
        <GlassCard variant="strong">
          <CardTitle>{text.details}</CardTitle>
          <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3 text-sm">
            {details.map(([label, value]) => (
              <div key={label} className="contents">
                <dt className="text-muted-foreground">{label}</dt>
                <dd className="tabular-nums">{value}</dd>
              </div>
            ))}
          </dl>
        </GlassCard>
        <div className="flex flex-col gap-4">
          <GlassCard>
            <CardTitle>{text.dataThisCycle}</CardTitle>
            <div className="flex flex-col gap-2 text-sm">
              <ProgressBar value={sim.usedGb} max={sim.limitGb} label={text.dataThisCycle} tone="flagged" />
              <span className="tabular-nums">{copy.units.ofGb(sim.usedGb, sim.limitGb)}</span>
            </div>
          </GlassCard>
          <GlassCard className="flex-1">
            <ExplainVisual />
          </GlassCard>
        </div>
      </div>
    </div>
  );
}

export function AlertsScreen() {
  const columns = "grid grid-cols-[1.3fr_1.2fr_1fr_0.8fr_0.6fr] items-center gap-4";

  return (
    <div className="flex h-full flex-col gap-4">
      <ScreenTitle>{copy.pages.alerts.title}</ScreenTitle>
      <GlassCard variant="strong" className="flex-1 p-0">
        <div className={`${columns} border-b border-border px-5 py-3 text-xs font-medium text-muted-foreground`}>
          <span>{copy.columns.sim}</span>
          <span>{copy.columns.type}</span>
          <span>{copy.columns.risk}</span>
          <span>{copy.columns.status}</span>
          <span>{copy.columns.date}</span>
        </div>
        <ul>
          {TOUR_ALERTS.map(({ id, number, type, score, date }) => (
            <li key={id} className={`${columns} border-b border-border px-5 py-3.5 text-sm last:border-0`}>
              <span className="tabular-nums">{number}</span>
              <span>{copy.alertTypes[type]}</span>
              <RiskBar score={score} />
              <span>
                <RiskBadge level={riskLevel(score)} />
              </span>
              <span className="text-muted-foreground">{date}</span>
            </li>
          ))}
        </ul>
      </GlassCard>
    </div>
  );
}
