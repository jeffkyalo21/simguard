"use client";

import { useState, useSyncExternalStore } from "react";
import Link from "next/link";
import { Circle, CircleCheck, X } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { ErrorState } from "@/components/shared/error-state";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { usePermission } from "@/hooks/use-permission";
import { useResource } from "@/hooks/use-resource";
import { getSetupProgress } from "@/lib/data";
import { formatDate } from "@/lib/format";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.setup;
const DISMISS_KEY = "simguard.setup-complete.dismissed";
const DISMISS_EVENT = "simguard:setup-dismissed";

// Dismissal is a per browser convenience, so it lives in localStorage. It
// reads as "not dismissed" where storage is blocked.
function subscribe(notify) {
  window.addEventListener("storage", notify);
  window.addEventListener(DISMISS_EVENT, notify);
  return () => {
    window.removeEventListener("storage", notify);
    window.removeEventListener(DISMISS_EVENT, notify);
  };
}

function readDismissed() {
  try {
    return window.localStorage.getItem(DISMISS_KEY) === "1";
  } catch {
    return false;
  }
}

function rememberDismissed() {
  try {
    window.localStorage.setItem(DISMISS_KEY, "1");
  } catch {
    // Nothing to do: the note hides for this visit through local state.
  }
  window.dispatchEvent(new Event(DISMISS_EVENT));
}

/** The steps in order, each with its done state, its detail line and its link. */
function stepsFor(progress) {
  const counted = (count) => (count > 0 ? text.counted(count) : text.todo);

  return [
    {
      key: "carriers",
      done: progress.carriers > 0,
      detail: counted(progress.carriers),
      href: "/settings?tab=carriers",
      action: progress.carriers > 0 ? text.actions.view : text.actions.add,
    },
    {
      key: "departments",
      done: progress.departments > 0,
      detail: counted(progress.departments),
      href: "/imports?type=departments",
    },
    {
      key: "employees",
      done: progress.employees > 0,
      detail: counted(progress.employees),
      href: "/imports?type=employees",
    },
    { key: "sims", done: progress.sims > 0, detail: counted(progress.sims), href: "/imports?type=sims" },
    {
      key: "allocations",
      done: progress.allocations > 0,
      detail: counted(progress.allocations),
      href: "/imports?type=allocations",
    },
    {
      key: "usage",
      done: progress.usageAsOf !== null,
      detail: progress.usageAsOf ? text.usageThrough(formatDate(progress.usageAsOf)) : text.todo,
      href: "/imports?type=usage",
    },
    {
      key: "scores",
      done: progress.scores > 0,
      detail: progress.scores > 0 ? counted(progress.scores) : progress.usageAsOf ? text.scoresReady : text.scoresWaiting,
      href: "/alerts",
      action: text.actions.view,
    },
  ];
}

function Step({ step }) {
  const Icon = step.done ? CircleCheck : Circle;
  const action = step.action ?? text.actions.import;

  return (
    <li
      className={cn(
        "flex items-center gap-3 rounded-xl border px-4 py-3",
        step.done ? "border-border" : "border-input bg-foreground/4"
      )}
    >
      <Icon className={cn("size-5 shrink-0", step.done ? "text-foreground" : "text-muted-foreground")} aria-hidden="true" />
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="text-sm font-medium">
          {text.steps[step.key]}
          <span className="sr-only">{`, ${step.done ? text.done : text.todo}`}</span>
        </span>
        <span className="text-sm text-muted-foreground">{step.detail}</span>
      </div>
      <Button asChild variant="outline" size="sm" className="touch-target h-10 shrink-0 px-3">
        <Link href={step.href}>
          {action}
          <span className="sr-only">{` ${text.steps[step.key]}`}</span>
        </Link>
      </Button>
    </li>
  );
}

function SetupSkeleton() {
  return (
    <GlassCard variant="strong" className="flex flex-col gap-3 p-5" role="status" aria-busy="true">
      <span className="sr-only">{copy.a11y.loading}</span>
      <Skeleton className="h-6 w-56" />
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {Array.from({ length: 6 }, (_, index) => (
          <Skeleton key={index} className="h-16 w-full" />
        ))}
      </div>
    </GlassCard>
  );
}

/**
 * First-day checklist for the Security Officer: one card per kind of data,
 * done when the list endpoints report anything. When every step is done it
 * shrinks to a "Setup complete" note that can be dismissed. Other roles see
 * nothing.
 */
export function SetupChecklist() {
  const importing = usePermission("imports.run");
  const dismissed = useSyncExternalStore(subscribe, readDismissed, () => false);
  const [hiddenNow, setHiddenNow] = useState(false);
  const resource = useResource(() => (importing.allowed ? getSetupProgress() : Promise.resolve(null)), [importing.allowed]);

  if (!importing.allowed) return null;
  if (resource.error) return <ErrorState error={resource.error} onRetry={resource.retry} className="py-6" />;
  if (!resource.data) return <SetupSkeleton />;

  const steps = stepsFor(resource.data);
  const complete = steps.every((step) => step.done);

  if (complete) {
    if (dismissed || hiddenNow) return null;

    return (
      <GlassCard variant="strong" className="flex items-center gap-3 px-5 py-3" role="status">
        <CircleCheck className="size-5 shrink-0" aria-hidden="true" />
        <p className="min-w-0 flex-1 text-sm">
          <span className="font-medium">{text.complete}</span>
          <span className="text-muted-foreground">{` ${text.completeNote}`}</span>
        </p>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="touch-target h-10 shrink-0 px-3"
          onClick={() => {
            setHiddenNow(true);
            rememberDismissed();
          }}
        >
          <X aria-hidden="true" />
          {text.dismiss}
        </Button>
      </GlassCard>
    );
  }

  return (
    <GlassCard as="section" variant="strong" aria-labelledby="setup-title" className="flex flex-col gap-4 p-5">
      <div className="flex flex-col gap-1">
        <h2 id="setup-title" className="text-base font-semibold">
          {text.title}
        </h2>
        <p className="text-sm text-muted-foreground">{text.intro}</p>
      </div>
      <ol aria-label={text.listLabel} className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {steps.map((step) => (
          <Step key={step.key} step={step} />
        ))}
      </ol>
    </GlassCard>
  );
}
