import { Check, Circle } from "lucide-react";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.auth.changePassword;

/**
 * The password rules, ticking as they are met. `rules` comes from
 * passwordRules(); the list id is what the password field points to.
 * @param {{ rules: { key: string, met: boolean }[], titleId?: string }} props
 */
export function PasswordRules({ rules, titleId = "password-rules-title" }) {
  return (
    <section aria-labelledby={titleId} className="rounded-xl border border-glass-border bg-background/60 p-4">
      <h2 id={titleId} className="text-sm font-medium">
        {text.rulesTitle}
      </h2>
      <ul id="password-rules" className="mt-2 flex flex-col gap-1.5 text-sm">
        {rules.map((rule) => (
          <li key={rule.key} className={cn("flex items-center gap-2", rule.met ? "text-foreground" : "text-muted-foreground")}>
            {rule.met ? <Check className="size-4" aria-hidden="true" /> : <Circle className="size-4" aria-hidden="true" />}
            <span>
              {text.rules[rule.key]}
              <span className="sr-only">, {rule.met ? text.ruleMet : text.ruleNotMet}</span>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
