"use client";

import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

const CANVAS_WIDTH = 960;

/**
 * Tabs over scaled, non-interactive screen mocks that cross-fade. All
 * panels are in the server HTML: without JavaScript the tabs are hidden and
 * every panel shows in turn (landing.css). One ResizeObserver sets the
 * scale that fits the 960px canvas to the frame width.
 * @param {{ label: string, tabs: { key: string, label: string,
 *   caption: string, screen: React.ReactNode }[] }} props
 */
export function ProductTour({ label, tabs }) {
  const rootRef = useRef(null);
  const tabRefs = useRef([]);
  const [active, setActive] = useState(0);

  useEffect(() => {
    const root = rootRef.current;
    const frame = root.querySelector("[data-tour-frame]");

    // A little above 1 is fine: it fills the widest frame (about 1000px).
    function fit(width) {
      root.style.setProperty("--tour-scale", Math.min(width / CANVAS_WIDTH, 1.1).toFixed(4));
    }

    fit(frame.clientWidth);
    const observer = new ResizeObserver(([entry]) => fit(entry.contentRect.width));
    observer.observe(frame);
    return () => observer.disconnect();
  }, []);

  function select(index) {
    const next = (index + tabs.length) % tabs.length;
    setActive(next);
    tabRefs.current[next]?.focus();
  }

  function onKeyDown(event) {
    const moves = { ArrowRight: active + 1, ArrowLeft: active - 1, Home: 0, End: tabs.length - 1 };
    if (!(event.key in moves)) return;
    event.preventDefault();
    select(moves[event.key]);
  }

  return (
    <div ref={rootRef} className="tour flex flex-col items-center gap-6">
      <div
        role="tablist"
        aria-label={label}
        onKeyDown={onKeyDown}
        className="tour-tabs glass-pill reveal flex max-w-full gap-1 overflow-x-auto p-1"
        style={{ "--i": 1 }}
      >
        {tabs.map((tab, index) => (
          <button
            key={tab.key}
            ref={(element) => {
              tabRefs.current[index] = element;
            }}
            type="button"
            role="tab"
            id={`tour-tab-${tab.key}`}
            aria-selected={index === active}
            aria-controls={`tour-panel-${tab.key}`}
            tabIndex={index === active ? 0 : -1}
            onClick={() => setActive(index)}
            className={cn(
              "h-10 shrink-0 rounded-full px-4 text-sm font-medium whitespace-nowrap transition-colors duration-150 pointer-coarse:h-11",
              index === active ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-accent hover:text-foreground"
            )}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Inactive panels get visibility: hidden (scripting only), which also
          takes them out of the tab order and the accessibility tree. */}
      <div className="tour-panels reveal w-full" style={{ "--i": 2 }}>
        {tabs.map((tab, index) => (
          <div
            key={tab.key}
            role="tabpanel"
            id={`tour-panel-${tab.key}`}
            aria-labelledby={`tour-tab-${tab.key}`}
            data-active={index === active}
            tabIndex={0}
            className="tour-panel flex flex-col gap-4 rounded-card"
          >
            <p className="text-center text-muted-foreground">{tab.caption}</p>
            <div className="glass-card p-2 shadow-[var(--glass-shadow-lift)] sm:p-3">
              <div
                data-tour-frame={index === 0 ? "" : undefined}
                className="tour-frame relative overflow-hidden rounded-[18px]"
                aria-hidden="true"
                inert
              >
                <div className="tour-canvas absolute top-0 left-0 p-5">{tab.screen}</div>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
