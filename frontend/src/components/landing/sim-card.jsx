"use client";

import { useEffect, useRef } from "react";
import { ShieldCheck } from "lucide-react";
import { loadGsap } from "@/components/landing/motion";
import { Tilt } from "@/components/landing/tilt";
import { copy } from "@/lib/copy";
import { cn } from "@/lib/utils";

const text = copy.landing.simCard;

// 260 x 164 (a SIM's 85.6 x 54 mm ratio), rounded, top right corner clipped.
const SHAPE = "M16 0H228L260 32V148A16 16 0 0 1 244 164H16A16 16 0 0 1 0 148V16A16 16 0 0 1 16 0Z";

// Where the status mark sits (dot, then shield) and where the chip is.
const MARK = { x: 224, y: 84 };
const CHIP = { x: 41, y: 82 };

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

/**
 * The animated layers, drawn over the card in its own 260 x 164 space:
 * magnifying glass, scan line, chip glow, suspicious dot with its label and
 * the resolved shield. The shield is the still state (reduced motion).
 */
function Overlay() {
  return (
    <svg viewBox="0 0 260 164" className="pointer-events-none absolute inset-0 size-full" aria-hidden="true">
      <defs>
        <linearGradient id="sim-scan-trail" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="var(--sim-cyan)" stopOpacity="0" />
          <stop offset="1" stopColor="var(--sim-cyan)" stopOpacity="0.28" />
        </linearGradient>
        <radialGradient id="sim-chip-glow">
          <stop offset="0" stopColor="var(--sim-cyan)" stopOpacity="0.75" />
          <stop offset="1" stopColor="var(--sim-cyan)" stopOpacity="0" />
        </radialGradient>
      </defs>

      <ellipse data-sim="glow" cx={CHIP.x} cy={CHIP.y} rx="40" ry="32" fill="url(#sim-chip-glow)" opacity="0" />

      <g data-sim="scan" opacity="0">
        <rect x="0" y="-16" width="260" height="16" fill="url(#sim-scan-trail)" />
        <rect x="0" y="-1" width="260" height="2" fill="var(--sim-cyan)" />
      </g>

      <g data-sim="magnifier" opacity="0">
        <circle cx="0" cy="0" r="13" fill="var(--sim-cyan)" fillOpacity="0.12" className="stroke-foreground" strokeWidth="2.5" />
        <path d="M9.5 9.5L20 20" className="stroke-foreground" strokeWidth="4" strokeLinecap="round" />
      </g>

      <g transform={`translate(${MARK.x} ${MARK.y})`}>
        <circle data-sim="ring" r="6" fill="none" stroke="var(--sim-orange)" strokeWidth="1.5" opacity="0" />
        <circle data-sim="dot" r="5" fill="var(--sim-orange)" opacity="0" />
      </g>

      <g data-sim="label" opacity="0">
        <rect x="110" y="74" width="100" height="20" rx="10" className="fill-card" stroke="var(--sim-orange)" strokeWidth="1" />
        <text x="160" y="87.5" textAnchor="middle" fontSize="10" fontWeight="600" className="fill-foreground">
          {text.unusual}
        </text>
      </g>

      <g transform={`translate(${MARK.x} ${MARK.y})`}>
        <g data-sim="shield" className="sim-shield">
          <path
            d="M0 -13L11 -8.5V1C11 8 6 12 0 14.5C-6 12 -11 8 -11 1V-8.5Z"
            className="fill-risk-cleared stroke-risk-cleared-foreground"
            strokeWidth="1.5"
            strokeLinejoin="round"
          />
          <path
            data-sim="check"
            d="M-4.5 0.5L-1 4L5 -3"
            pathLength="1"
            fill="none"
            className="stroke-risk-cleared-foreground"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </g>
      </g>
    </svg>
  );
}

/**
 * Builds the one 10.5 s loop:
 * 0 to 2 idle (the card floats), 2 to 4.5 investigation (magnifier sweeps),
 * 4.5 to 6.5 scanning (cyan line passes down, the chip glows as it crosses),
 * 6.5 to 8.5 detection (orange dot pulses with its label), 8.5 to 10.5
 * resolution (the orange becomes a shield with a check, then it resets).
 */
function buildTimeline(gsap, root) {
  const pick = (name) => root.querySelector(`[data-sim="${name}"]`);
  const card = root.querySelector("[data-sim-float]");
  const [glow, scan, magnifier, ring, dot, label, shield, check] = [
    "glow", "scan", "magnifier", "ring", "dot", "label", "shield", "check",
  ].map(pick);

  gsap.set([glow, scan, magnifier, ring, dot, label, shield], { opacity: 0 });
  gsap.set(scan, { y: -16 });
  gsap.set(magnifier, { x: -40, y: 70 });
  gsap.set(label, { x: 8 });
  gsap.set(dot, { scale: 0, transformOrigin: "50% 50%" });
  gsap.set(shield, { scale: 0.5, transformOrigin: "50% 50%" });
  gsap.set(check, { strokeDasharray: 1, strokeDashoffset: 1 });

  const tl = gsap.timeline({ repeat: -1, defaults: { ease: "power2.out" } });

  // 0 to 2: idle float.
  tl.to(card, { y: -5, duration: 1, ease: "sine.inOut" }, 0).to(card, { y: 0, duration: 1, ease: "sine.inOut" }, 1);

  // 2 to 4.5: the magnifying glass slides in and sweeps across.
  tl.to(magnifier, { opacity: 1, x: 30, y: 58, duration: 0.6 }, 2)
    .to(magnifier, { x: 130, y: 100, duration: 0.8, ease: "sine.inOut" }, 2.6)
    .to(magnifier, { x: 215, y: 56, duration: 0.8, ease: "sine.inOut" }, 3.4)
    .to(magnifier, { opacity: 0, x: 250, duration: 0.3, ease: "power1.in" }, 4.2);

  // 4.5 to 6.5: the scan line passes down; the chip glows as it crosses (about 5.3 to 5.7).
  tl.to(scan, { opacity: 1, duration: 0.2 }, 4.5)
    .to(scan, { y: 164, duration: 1.8, ease: "none" }, 4.5)
    .to(scan, { opacity: 0, duration: 0.2 }, 6.3)
    .to(glow, { opacity: 1, duration: 0.25 }, 5.2)
    .to(glow, { opacity: 0, duration: 0.6, ease: "power2.in" }, 5.55);

  // 6.5 to 8.5: the orange dot pulses and its label appears.
  tl.to(dot, { opacity: 1, scale: 1, duration: 0.3, ease: "back.out(2)" }, 6.5)
    .to(label, { opacity: 1, x: 0, duration: 0.4 }, 6.7)
    .fromTo(
      ring,
      { opacity: 0.7, scale: 0.6, transformOrigin: "50% 50%" },
      { opacity: 0, scale: 2.8, duration: 0.8, ease: "power1.out", repeat: 1, immediateRender: false },
      6.7
    );

  // 8.5 to 10.5: the orange fades into a shield with a check, then everything resets.
  tl.to([dot, label], { opacity: 0, duration: 0.4 }, 8.5)
    .to(shield, { opacity: 1, scale: 1, duration: 0.5, ease: "back.out(1.7)" }, 8.6)
    .to(check, { strokeDashoffset: 0, duration: 0.35 }, 9)
    .to(shield, { opacity: 0, duration: 0.4, ease: "power1.in" }, 10.1);

  // Parked states for the next loop.
  tl.set(dot, { scale: 0 }, 10.5).set(shield, { scale: 0.5 }, 10.5).set(check, { strokeDashoffset: 1 }, 10.5);

  return tl;
}

/**
 * Decorative SIM card for the Landing hero: frosted glass in the SIM shape,
 * tilting toward the pointer, with a single GSAP timeline (about 10.5 s,
 * looping) that floats the card, sweeps a magnifying glass, scans it with a
 * cyan line, flags unusual activity in orange and resolves it to a shield.
 * The timeline pauses off screen and in a hidden tab. With reduced motion
 * nothing moves and the card shows a still shield with a check. The same SIM
 * and score appear as text in the preview's "Needs review" list.
 * @param {{ number: string, className?: string }} props
 */
export function SimCard({ number, className }) {
  const rootRef = useRef(null);

  useEffect(() => {
    const root = rootRef.current;
    if (!root) return undefined;

    let cancelled = false;
    let revert = () => {};

    loadGsap().then(({ gsap }) => {
      if (cancelled) return;

      const ctx = gsap.context(() => {
        const mm = gsap.matchMedia();

        mm.add("(prefers-reduced-motion: no-preference)", () => {
          const tl = buildTimeline(gsap, root);
          let onscreen = true;
          let visible = !document.hidden;
          const sync = () => tl.paused(!(onscreen && visible));

          const observer = new IntersectionObserver(([entry]) => {
            onscreen = entry.isIntersecting;
            sync();
          });
          observer.observe(root);

          const onVisibility = () => {
            visible = !document.hidden;
            sync();
          };
          document.addEventListener("visibilitychange", onVisibility);
          sync();

          return () => {
            observer.disconnect();
            document.removeEventListener("visibilitychange", onVisibility);
          };
        });
      }, root);

      revert = () => ctx.revert();
    });

    return () => {
      cancelled = true;
      revert();
    };
  }, []);

  return (
    <div ref={rootRef} aria-hidden="true" className={cn("sim-card", className)}>
      <div data-sim-float className="sim-float">
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
              <span className="flex items-center gap-1.5">
                <span className="flex size-5 items-center justify-center rounded-[5px] bg-brand-solid text-white">
                  <ShieldCheck className="size-3.5" />
                </span>
                <span className="text-xs font-semibold">{copy.app.name}</span>
              </span>
              <span className="flex items-center">
                <GoldChip />
              </span>
              <span className="text-sm font-semibold tracking-wide tabular-nums">{number}</span>
              <Overlay />
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
