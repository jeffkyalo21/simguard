// Lets `node --test` import modules that use the "@/" alias (see jsconfig.json).
// Loaded with `node --import ./src/lib/test-alias.mjs` by `npm test`.
import { register } from "node:module";

register("./test-alias-hooks.mjs", import.meta.url);
