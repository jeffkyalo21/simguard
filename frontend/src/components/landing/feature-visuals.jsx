import { ArrowRight } from "lucide-react";
import { RiskBadge } from "@/components/shared/risk-badge";
import { copy } from "@/lib/copy";

const text = copy.landing.visuals;

/** Detect: a risk bar sweeps to 94%, then the Flagged badge appears. */
export function DetectVisual() {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between text-sm">
        <span className="text-muted-foreground">{text.risk}</span>
        <span className="text-2xl font-semibold tabular-nums">{text.riskValue}</span>
      </div>
      <div className="h-2.5 overflow-hidden rounded-full bg-foreground/10">
        <div className="fill-x h-full rounded-full bg-risk-flagged-strong" style={{ "--v": 0.94 }} />
      </div>
      <div className="pop-in w-fit" style={{ "--delay": "1100ms" }}>
        <RiskBadge level="flagged" />
      </div>
    </div>
  );
}

/** Allocate: the plan marker moves from one tier to the next. */
export function AllocateVisual() {
  return (
    <div className="flex flex-col gap-5">
      <div className="flex items-center justify-between gap-3 text-sm font-medium">
        <span className="rounded-full border border-input px-3 py-1">{text.planFrom}</span>
        <ArrowRight className="size-4 text-muted-foreground" />
        <span className="rounded-full bg-foreground px-3 py-1 text-background">{text.planTo}</span>
      </div>
      <div className="relative h-2.5 rounded-full bg-foreground/10">
        <div className="slide-x absolute inset-0" style={{ "--x": "calc(100% - 20px)" }}>
          <span className="absolute top-1/2 left-0 size-5 -translate-y-1/2 rounded-full border-2 border-background bg-foreground shadow" />
        </div>
      </div>
    </div>
  );
}

/** Explain: "Why it fired" bars slide in one by one. */
export function ExplainVisual() {
  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm font-medium">{text.whyTitle}</p>
      <ul className="flex flex-col gap-3">
        {text.why.map(({ label, value }, index) => (
          <li key={label} className="slide-x grid grid-cols-[6.5rem_1fr_2.5rem] items-center gap-3 text-sm" style={{ "--i": index }}>
            <span className="truncate text-muted-foreground">{label}</span>
            <span className="h-2 overflow-hidden rounded-full bg-foreground/10">
              <span className="fill-x block h-full rounded-full bg-foreground" style={{ "--v": value / 100, "--i": index + 2 }} />
            </span>
            <span className="text-right tabular-nums">{copy.units.percent(value)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
