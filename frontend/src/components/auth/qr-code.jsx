"use client";

import { useMemo } from "react";
import { encode } from "uqr";

/**
 * QR code drawn as SVG from the otpauth URI. The module matrix comes from
 * uqr; each dark module is one path segment, so no HTML is injected. Always
 * black on white with a quiet zone, whatever the theme, so phones read it.
 * @param {{ value: string, label: string, size?: number }} props
 */
export function QrCode({ value, label, size = 192 }) {
  const { path, dimension } = useMemo(() => {
    const { data, size: count } = encode(value, { ecc: "M", border: 4 });
    let d = "";
    for (let y = 0; y < count; y += 1) {
      for (let x = 0; x < count; x += 1) {
        if (data[y][x]) d += `M${x} ${y}h1v1h-1z`;
      }
    }
    return { path: d, dimension: count };
  }, [value]);

  return (
    <svg
      role="img"
      aria-label={label}
      width={size}
      height={size}
      viewBox={`0 0 ${dimension} ${dimension}`}
      shapeRendering="crispEdges"
      className="rounded-lg"
    >
      <rect width={dimension} height={dimension} fill="#ffffff" />
      <path d={path} fill="#000000" />
    </svg>
  );
}
