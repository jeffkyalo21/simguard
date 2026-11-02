import { KeyRound, MapPin, ScrollText, UserCheck } from "lucide-react";
import { InView } from "@/components/landing/in-view";
import { SectionHeading } from "@/components/landing/section-heading";
import { copy } from "@/lib/copy";

const text = copy.landing.trust;
const icons = { review: UserCheck, audit: ScrollText, roles: KeyRound, kenya: MapPin };

/** Four small trust points with icons. */
export function Trust() {
  return (
    <InView as="section" aria-labelledby="trust-title" threshold={0.1} className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-5xl flex-col gap-10">
        <SectionHeading id="trust-title" title={text.title} intro={text.intro} />
        <ul className="grid gap-x-6 gap-y-6 sm:grid-cols-2 lg:grid-cols-4">
          {text.items.map(({ key, title, text: body }, index) => {
            const Icon = icons[key];

            return (
              <li key={key} className="reveal flex gap-3" style={{ "--i": index + 1 }}>
                <span className="flex size-9 shrink-0 items-center justify-center rounded-full border border-glass-border bg-glass-strong">
                  <Icon className="size-4" aria-hidden="true" />
                </span>
                <div className="flex flex-col gap-0.5">
                  <h3 className="font-semibold">{title}</h3>
                  <p className="text-sm text-muted-foreground">{body}</p>
                </div>
              </li>
            );
          })}
        </ul>
      </div>
    </InView>
  );
}
