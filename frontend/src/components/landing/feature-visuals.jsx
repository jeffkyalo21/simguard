import { copy } from "@/lib/copy";

const text = copy.landing.visuals;

/** Explain: "Why it fired" bars slide in one by one. */
export function ExplainVisual() {
  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm font-medium">{text.whyTitle}</p>
      <ul className="flex flex-col gap-3">
        {text.why.map(({ label, value }, index) => (
          <li key={label} className="slide-x grid grid-cols-[6.5rem_1fr_2.5rem] items-center gap-3 text-sm" style={{ "--i": index }}>
            <span className="truncate text-muted-foreground">{label}</span>
            <span className="h-2 overflow-hidden rounded-full bg-chart-track">
              <span className="fill-x block h-full rounded-full bg-foreground" style={{ "--v": value / 100, "--i": index + 2 }} />
            </span>
            <span className="text-right tabular-nums">{copy.units.percent(value)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
