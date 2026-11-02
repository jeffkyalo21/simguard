"use client";

import { ThemeProvider as NextThemesProvider } from "next-themes";

// Class-based theming with the system default. next-themes injects a
// blocking script so the right theme is applied before first paint.
export function ThemeProvider({ children }) {
  return (
    <NextThemesProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
    >
      {children}
    </NextThemesProvider>
  );
}
