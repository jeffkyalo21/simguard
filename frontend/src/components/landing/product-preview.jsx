import { ShieldX } from "lucide-react";
import { CountUp } from "@/components/landing/count-up";
import { InView } from "@/components/landing/in-view";
import { Parallax } from "@/components/landing/parallax";
import { Tilt } from "@/components/landing/tilt";
import { copy } from "@/lib/copy";
import { cn } from "@/lib/utils";

const text = copy.landing;

// Fixed points for the mini chart (0 to 100, rising with a late peak).
const CHART = [30, 34, 31, 40, 38, 45, 43, 52, 49, 58, 56, 63, 61, 72, 66, 70];
const SPARK = [6, 9, 7, 12, 10, 15, 13, 18];

function points(values, width, height, max) {
  const step = width / (values.length - 1);
  return values.map((value, index) => `${(index * step).toFixed(1)},${(height - (value / max) * height).toFixed(1)}`).join(" ");
}

const BARS = [
  { key: "inactiveBilling", value: 23 },
  { key: "sharing", value: 18 },
  { key: "reselling", value: 12 },
  { key: "ghostUsage", value: 9 },
];

function MiniSidebar() {
  return (
    <div className="hidden w-14 shrink-0 flex-col items-center gap-3 border-r border-border py-4 sm:flex">
      <span className="mb-2 size-6 rounded-md bg-brand-solid" />
      {Array.from({ length: 6 }, (_, index) => (
        <span
          key={index}
          className={cn(
            "relative h-2 w-6 rounded-full bg-foreground/15",
            index === 0 && "bg-brand-soft before:absolute before:top-0 before:-left-3 before:h-2 before:w-[3px] before:rounded-full before:bg-brand"
          )}
        />
      ))}
    </div>
  );
}

function MiniDashboard() {
  return (
    <div className="flex min-w-0 flex-1 flex-col gap-3 p-3 sm:p-4">
      <div className="grid grid-cols-3 gap-2 sm:gap-3">
        {text.preview.kpis.map(({ key, label, value }) => (
          <div
            key={key}
            className={cn(
              "rounded-xl border border-glass-border bg-glass-strong p-2.5 sm:p-3",
              key === "flagged" && "shadow-[var(--brand-glow)]"
            )}
          >
            <p className="truncate text-[11px] text-muted-foreground sm:text-xs">{label}</p>
            <p className={cn("text-lg font-semibold sm:text-2xl", key === "flagged" && "text-brand-text")}>
              <CountUp value={value} />
            </p>
          </div>
        ))}
      </div>
      <div className="grid gap-2 sm:grid-cols-[1.6fr_1fr] sm:gap-3">
        <div className="rounded-xl border border-glass-border bg-glass-strong p-3">
          <p className="mb-2 text-xs font-medium">{text.preview.chart}</p>
          <div className="draw-mask">
            <div className="draw-inner">
              <svg viewBox="0 0 300 100" className="h-24 w-full sm:h-28" preserveAspectRatio="none">
                {[25, 50, 75].map((y) => (
                  <line key={y} x1="0" x2="300" y1={y} y2={y} stroke="var(--chart-grid)" />
                ))}
                <polyline
                  points={points(CHART, 300, 96, 80)}
                  fill="none"
                  stroke="var(--foreground)"
                  strokeWidth="2"
                  vectorEffect="non-scaling-stroke"
                />
              </svg>
            </div>
          </div>
        </div>
        <div className="rounded-xl border border-glass-border bg-glass-strong p-3">
          <p className="mb-3 text-xs font-medium">{text.preview.bars}</p>
          <ul className="flex flex-col gap-2.5">
            {BARS.map(({ key, value }, index) => (
              <li key={key} className="grid grid-cols-[1fr_1.4fr] items-center gap-2 text-[11px]">
                <span className="truncate">{copy.alertTypes[key]}</span>
                <span className="h-1.5 overflow-hidden rounded-full bg-foreground/10">
                  <span
                    className="fill-x block h-full rounded-full bg-foreground"
                    style={{ "--v": value / 23, "--i": index }}
                  />
                </span>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}

function Chips() {
  return (
    <div className="pointer-events-none absolute inset-0 hidden md:block" aria-hidden="true">
      <Parallax speed={0.12} className="absolute top-10 -left-6 lg:-left-14">
        <div className="chip-float glass-card flex items-center gap-2 rounded-full px-3 py-2 text-sm" style={{ "--delay": "-1s" }}>
          <span className="inline-flex h-6 items-center gap-1 rounded-full bg-risk-flagged px-2 text-xs font-medium text-risk-flagged-foreground">
            <ShieldX className="size-3.5" />
            {text.chips.flagged}
          </span>
          <span className="font-semibold tabular-nums">{text.chips.flaggedValue}</span>
        </div>
      </Parallax>
      <Parallax speed={0.2} className="absolute -right-6 bottom-16 lg:-right-14">
        <div className="chip-float glass-card flex flex-col gap-0.5 px-4 py-3" style={{ "--delay": "-3s" }}>
          <span className="text-xs text-muted-foreground">{text.chips.savings}</span>
          <span className="text-lg font-semibold tabular-nums">{text.chips.savingsValue}</span>
        </div>
      </Parallax>
      <Parallax speed={0.07} className="absolute -top-6 right-10 lg:right-24">
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
 * A built-in-code mock of the dashboard: KPIs count up, the chart draws
 * itself and the bars fill when it scrolls into view; the frame tilts
 * toward the pointer. Floating chips drift around it with parallax.
 */
export function ProductPreview() {
  return (
    <InView as="section" track threshold={0} aria-label={text.previewLabel} className="px-4 pb-16 sm:px-6">
      <div className="relative mx-auto max-w-5xl">
        <InView threshold={0.3}>
          <Tilt>
            <div
              role="img"
              aria-label={text.previewLabel}
              className="glass-card flex overflow-hidden p-0 shadow-[var(--glass-shadow-lift)] [--radius-glass:28px]"
            >
              <div aria-hidden="true" className="flex min-w-0 flex-1">
                <MiniSidebar />
                <MiniDashboard />
              </div>
            </div>
          </Tilt>
        </InView>
        <Chips />
      </div>
    </InView>
  );
}
