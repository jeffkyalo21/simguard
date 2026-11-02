import { Geist, Geist_Mono } from "next/font/google";
import { ThemeProvider } from "@/components/layout/theme-provider";
import { GlassBackdrop } from "@/components/shared/glass-backdrop";
import { Toaster } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import { copy } from "@/lib/copy";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata = {
  title: {
    default: copy.app.name,
    template: `%s · ${copy.app.name}`,
  },
};

// One glass theme for the whole product: .glass-theme sits on <body>, so
// every page and every portaled dialog, menu, sheet and toast inherits it.
export default function RootLayout({ children }) {
  return (
    <html
      lang="en"
      data-scroll-behavior="smooth"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
      suppressHydrationWarning
    >
      <body className="glass-theme min-h-full">
        <a
          href="#main"
          className="sr-only z-50 rounded-lg bg-background px-4 py-2 text-sm font-medium text-foreground shadow focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
        >
          {copy.a11y.skipToContent}
        </a>
        <GlassBackdrop />
        <ThemeProvider>
          <TooltipProvider>
            {children}
            <Toaster />
          </TooltipProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
