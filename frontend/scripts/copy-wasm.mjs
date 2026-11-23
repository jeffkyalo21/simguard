// Copies the barcode reader WASM file from node_modules to public/wasm so the
// scanner loads it from this site instead of a CDN. Runs after install and
// before next build; the copy is generated and not committed.
import { copyFileSync, existsSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const source = join(root, "node_modules", "zxing-wasm", "dist", "reader", "zxing_reader.wasm");
const target = join(root, "public", "wasm", "zxing_reader.wasm");

if (!existsSync(source)) {
  console.error(`copy-wasm: ${source} not found. Run npm install first.`);
  process.exit(1);
}

mkdirSync(dirname(target), { recursive: true });
copyFileSync(source, target);
