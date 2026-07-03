"use client";

import React from "react";

import { ActivityDock } from "@/components/comp/layout/activity-dock";
import { CommandBar } from "@/components/comp/layout/command-bar";
import { RibbonProvider } from "@/components/comp/layout/ribbon";
import { RequireAuth } from "@/components/auth/require-auth";

interface PageLayoutProps {
  children: React.ReactNode;
  /**
   * Optional series title — preserved for callers that pass it (the legacy
   * series-detail breadcrumb relied on it). The new shell doesn't render a
   * breadcrumb, so the value is currently unused but kept on the public API
   * so we don't churn every caller in one pass.
   */
  seriesTitle?: string;
  /**
   * Extra classes for the <main> element (padding, gap overrides, etc.).
   */
  mainClassName?: string;
}

/**
 * Shared page shell — command bar + contextual ribbon + main + activity dock.
 *
 * Pages use this layout via their per-route `layout.tsx`. They render any
 * contextual controls (status tabs, filters, sort, …) inside a `<RibbonSlot>`
 * which hoists those controls into the bar below the command bar.
 */
export function PageLayout({
  children,
  mainClassName = "p-4 pb-16 sm:px-6 sm:py-0 sm:pb-4 overflow-y-auto",
}: PageLayoutProps) {
  return (
    <RequireAuth>
      <RibbonProvider>
        <div className="flex h-dvh w-full flex-col bg-muted/40 overflow-hidden">
          <CommandBar />
          <main
            className={`page-shell-main flex-1 min-h-0 overscroll-contain ${mainClassName}`}
          >
            {children}
          </main>
          <ActivityDock />
        </div>
      </RibbonProvider>
    </RequireAuth>
  );
}
