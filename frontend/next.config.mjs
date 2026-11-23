/** @type {import('next').NextConfig} */
const nextConfig = {
  /* config options here */
  output: "standalone", // self-contained server for the Docker image
  cacheComponents: true,
  partialPrefetching: true,
  // The reset link's token is in the fragment; no Referer leaves this page.
  async headers() {
    return [
      { source: "/reset-password", headers: [{ key: "Referrer-Policy", value: "no-referrer" }] },
      // The barcode reader WASM (public/wasm, copied from node_modules).
      {
        source: "/wasm/:path*",
        headers: [
          { key: "Content-Type", value: "application/wasm" },
          { key: "Cache-Control", value: "public, max-age=86400" },
        ],
      },
    ];
  },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
