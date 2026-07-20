"use client";

import {
  Activity,
  Library,
  List,
  Plug,
  Sparkles,
} from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type React from "react";

import { useIsAdmin, usePermission } from "@/hooks/use-permission";
import { useDownloadsMetrics } from "@/lib/api/hooks/useDownloads";

/* ─── Section config ───────────────────────────────────────────────────── */

export interface SectionDef {
  name: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
  /** Numeric badge — shown as a small pill next to the label. */
  badge?: number;
  /** When true, show a small pulsing dot (used for live activity). */
  live?: boolean;
}

/**
 * Hook returning the permission-gated, badge-decorated list of sections that
 * appear in the command bar's section pills.
 */
export function useSections(): SectionDef[] {
  const canViewLibrary = usePermission("canViewLibrary");
  const canBrowse = usePermission("canBrowseSources");
  const canViewQueue = usePermission("canViewQueue");
  const canViewStatus = usePermission("canViewStatistics");
  const isAdmin = useIsAdmin();
  const { data: metrics } = useDownloadsMetrics();

  const sections: SectionDef[] = [];

  if (canViewLibrary) {
    sections.push({ name: "Library", href: "/library", icon: Library });
  }
  if (canBrowse) {
    sections.push({ name: "Discover", href: "/cloud-latest", icon: Sparkles });
  }
  if (canViewQueue) {
    const activeCount = metrics?.downloads ?? 0;
    const failedCount = metrics?.failed ?? 0;
    sections.push({
      name: "Queue",
      href: "/queue",
      icon: List,
      badge: activeCount + failedCount > 0 ? activeCount + failedCount : undefined,
      live: activeCount > 0,
    });
  }
  if (canViewStatus) {
    sections.push({ name: "Status", href: "/status", icon: Activity });
  }
  if (isAdmin) {
    sections.push({ name: "Sources", href: "/providers", icon: Plug });
  }

  return sections;
}

/* ─── Section Pills (Desktop) ──────────────────────────────────────────── */

/**
 * Horizontal pill bar. Active pill is filled with the brand primary; inactive
 * pills are muted text with hover state. Live dot pulses on active downloads.
 */
export function SectionPills() {
  const pathname = usePathname();
  const sections = useSections();

  const isActive = (href: string) =>
    pathname === href || pathname.startsWith(href + "/");

  return (
    <nav
      aria-label="Primary"
      className="flex items-center gap-1 overflow-x-auto scrollbar-hide"
    >
      {sections.map(({ name, href, icon: Icon, badge, live }) => {
        const active = isActive(href);
        return (
          <Link
            key={href}
            href={href}
            aria-current={active ? "page" : undefined}
            className={`relative inline-flex items-center gap-1.5 rounded-full h-8 px-3 text-sm font-medium whitespace-nowrap transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background ${
              active
                ? "bg-primary text-primary-foreground shadow-sm"
                : "text-muted-foreground hover:bg-accent/60 hover:text-foreground"
            }`}
          >
            <Icon className="h-4 w-4 shrink-0" />
            <span>{name}</span>

            {/* Live pulsing dot (active downloads) */}
            {live && (
              <span className="relative ml-0.5 inline-flex h-1.5 w-1.5 shrink-0">
                <span
                  className={`absolute inset-0 inline-flex h-full w-full rounded-full opacity-75 animate-ping ${
                    active ? "bg-primary-foreground" : "bg-primary"
                  }`}
                />
                <span
                  className={`relative inline-flex rounded-full h-1.5 w-1.5 ${
                    active ? "bg-primary-foreground" : "bg-primary"
                  }`}
                />
              </span>
            )}

            {/* Numeric badge */}
            {badge && badge > 0 && (
              <span
                className={`ml-0.5 inline-flex h-4 min-w-[16px] items-center justify-center rounded-full px-1 text-[10px] font-bold tabular-nums ${
                  active
                    ? "bg-primary-foreground/20 text-primary-foreground"
                    : "bg-primary/15 text-primary"
                }`}
              >
                {badge > 99 ? "99+" : badge}
              </span>
            )}
          </Link>
        );
      })}
    </nav>
  );
}

/* ─── Mobile section drawer content ────────────────────────────────────── */

/**
 * Mobile/narrow-viewport version: a vertical stack of section links rendered
 * inside a Sheet drawer. Matches the desktop pill set 1:1.
 */
export function SectionList({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  const sections = useSections();

  const isActive = (href: string) =>
    pathname === href || pathname.startsWith(href + "/");

  return (
    <nav aria-label="Primary" className="flex flex-col gap-1 p-2">
      {sections.map(({ name, href, icon: Icon, badge, live }) => {
        const active = isActive(href);
        return (
          <Link
            key={href}
            href={href}
            onClick={onNavigate}
            aria-current={active ? "page" : undefined}
            className={`relative flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors ${
              active
                ? "bg-primary/10 text-primary"
                : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"
            }`}
          >
            {active && (
              <div className="absolute left-0 top-2 bottom-2 w-0.5 rounded-full bg-primary" />
            )}
            <Icon className="h-5 w-5 shrink-0" />
            <span className="flex-1">{name}</span>

            {live && (
              <span className="relative inline-flex h-1.5 w-1.5 shrink-0">
                <span className="absolute inset-0 inline-flex h-full w-full rounded-full bg-primary opacity-75 animate-ping" />
                <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-primary" />
              </span>
            )}

            {badge && badge > 0 && (
              <span className="inline-flex h-4.5 min-w-[18px] items-center justify-center rounded-full bg-primary px-1 text-[10px] font-bold text-primary-foreground">
                {badge > 99 ? "99+" : badge}
              </span>
            )}
          </Link>
        );
      })}
    </nav>
  );
}
