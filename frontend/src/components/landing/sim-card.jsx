import { ShieldCheck, ShieldX } from "lucide-react";
import { Tilt } from "@/components/landing/tilt";
import { copy } from "@/lib/copy";
import { cn } from "@/lib/utils";

const text = copy.landing.simCard;

// 260 x 164 (a SIM's 85.6 x 54 mm ratio), rounded, top right corner clipped.
const SHAPE = "M16 0H228L260 32V148A16 16 0 0 1 244 164H16A16 16 0 0 1 0 148V16A16 16 0 0 1 16 0Z";

function GoldChip() {
  return (
    <svg viewBox="0 0 46 36" className="h-9 w-[46px]">
      <defs>
        <linearGradient id="sim-gold" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#f1dc9c" />
          <stop offset="0.5" stopColor="#c99f45" />
          <stop offset="1" stopColor="#e8c977" />
        </linearGradient>
      </defs>
      <rect x="0.5" y="0.5" width="45" height="35" rx="7" fill="url(#sim-gold)" stroke="#9c7a2c" strokeOpacity="0.6" />
      {/* Contact pads: a centre pad with three pads either side. */}
      <g fill="none" stroke="#7d5f1f" strokeOpacity="0.55">
        <path d="M0.5 12H15M0.5 24H15M31 12H45.5M31 24H45.5M23 0.5V7M23 29V35.5" />
        <rect x="15" y="7" width="16" height="22" rx="3" />
      </g>
    </svg>
  );
}

/** Two stacked chips; CSS cross-fades them after each beam sweep. */
function Status({ risk }) {
  const chip = "sim-status col-start-1 row-start-1 inline-flex h-7 items-center gap-1 rounded-full px-2.5 text-xs font-medium";

  return (
    <div className="grid justify-items-end">
      <span className={cn(chip, "sim-status-clear bg-risk-cleared text-risk-cleared-foreground")}>
        <ShieldCheck className="size-3.5" />
        {text.clear}
      </span>
      <span className={cn(chip, "sim-status-flagged bg-risk-flagged text-risk-flagged-foreground")}>
        <ShieldX className="size-3.5" />
        {text.flagged}
        <span className="font-semibold tabular-nums">{copy.units.percent(risk)}</span>
      </span>
    </div>
  );
}

/**
 * Decorative SIM card for the Landing hero: frosted glass in the SIM shape,
 * floating, tilting toward the pointer, and swept by a light beam that
 * flips its status to flagged for a moment. All motion is CSS on transform
 * and opacity (landing.css), pauses off screen and is off for reduced
 * motion, which shows the flagged state still. The same SIM and score
 * appear as text in the preview's "Needs review" list.
 * @param {{ number: string, risk: number, className?: string }} props
 */
export function SimCard({ number, risk, className }) {
  return (
    <div aria-hidden="true" className={cn("sim-card", className)}>
      <div className="sim-float">
        <Tilt max={8}>
          <div className="relative h-[164px] w-[260px]">
            <svg viewBox="0 0 260 164" className="absolute inset-0 size-full overflow-visible">
              <defs>
                <filter id="sim-shadow" x="-30%" y="-30%" width="160%" height="180%">
                  <feGaussianBlur stdDeviation="12" />
                </filter>
              </defs>
              <path d={SHAPE} transform="translate(0 14)" fill="var(--sim-shadow)" filter="url(#sim-shadow)" />
            </svg>

            <div className="sim-body absolute inset-0 flex flex-col justify-between p-[18px]" style={{ clipPath: `path("${SHAPE}")` }}>
              <span className="sim-beam" />
              <span className="flex items-center gap-1.5">
                <span className="flex size-5 items-center justify-center rounded-[5px] bg-brand-solid text-white">
                  <ShieldCheck className="size-3.5" />
                </span>
                <span className="text-xs font-semibold">{copy.app.name}</span>
              </span>
              <span className="flex items-center justify-between gap-3">
                <GoldChip />
                <Status risk={risk} />
              </span>
              <span className="text-sm font-semibold tracking-wide tabular-nums">{number}</span>
            </div>

            <svg viewBox="0 0 260 164" className="pointer-events-none absolute inset-0 size-full overflow-visible">
              <path d={SHAPE} fill="none" stroke="var(--sim-edge)" strokeWidth="1.25" />
            </svg>
          </div>
        </Tilt>
      </div>
    </div>
  );
}
