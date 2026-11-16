import { existsSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";

const SRC = new URL("../", import.meta.url);

export function resolve(specifier, context, next) {
  if (specifier.startsWith("@/")) {
    const base = fileURLToPath(new URL(specifier.slice(2), SRC));
    for (const candidate of [base, `${base}.js`, `${base}/index.js`]) {
      if (existsSync(candidate) && !candidate.endsWith("/") && /\.[a-z]+$/.test(candidate)) {
        return next(pathToFileURL(candidate).href, context);
      }
    }
  }
  return next(specifier, context);
}
