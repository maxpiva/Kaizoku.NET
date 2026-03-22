"use client";

import React from "react";
import KzkSidebar from "@/components/kzk/layout/sidebar";
import KzkHeader from "@/components/kzk/layout/header";
import { RequireAuth } from "@/components/auth/require-auth";

interface PageLayoutProps {
  children: React.ReactNode;
  seriesTitle?: string;
  /**
   * Extra classes for the <main> element (padding, gap overrides, etc.)
   */
  mainClassName?: string;
}

/**
 * Shared page shell — sidebar + header + content area.
 * All route layouts should use this instead of inlining KzkSidebar/KzkHeader.
 */
export function PageLayout({
  children,
  seriesTitle,
  mainClassName = "p-4 pb-16 sm:px-6 sm:py-0 sm:pb-4 overflow-y-auto",
}: PageLayoutProps) {
  return (
    <RequireAuth>
      <div className="flex h-dvh w-full bg-muted/40 overflow-hidden">
        {/* Sidebar renders its own spacer on desktop; nothing on mobile */}
        <KzkSidebar />

        {/* Content column */}
        <div className="flex flex-1 flex-col min-w-0 overflow-hidden">
          <KzkHeader seriesTitle={seriesTitle} />
          <main
            className={`flex-1 min-h-0 overscroll-contain ${mainClassName}`}
          >
            {children}
          </main>
        </div>
      </div>
    </RequireAuth>
  );
}
