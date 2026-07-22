"use client";

import { AnimatePresence, motion } from "framer-motion";
import { Menu, Monitor, Moon, Search, Sun, X } from "lucide-react";
import { useTheme } from "next-themes";
import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";

import { SectionList, SectionPills } from "@/components/comp/layout/section-pills";
import { UserAvatarDropdown } from "@/components/comp/layout/user-menu";
import { DownloadStatus } from "@/components/comp/layout/download-status";
import { ExternalLinks } from "@/components/comp/layout/external-links";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetTrigger } from "@/components/ui/sheet";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { useRibbonMount } from "@/components/comp/layout/ribbon";
import { useSearch } from "@/contexts/search-context";

/**
 * Command Bar.
 *
 * Replaces the old vertical sidebar + horizontal header pair with a single
 * sticky 56px top bar plus a 48px contextual ribbon below it that pages fill
 * via <RibbonSlot>. The bar holds:
 *
 *   [ Logo ] [ Section pills · Library Browse Queue Sources Requests Settings ]
 *                                                      [ Search ] [ Theme ] [ User ]
 *
 * On narrow viewports the section pills move into a Sheet drawer behind a
 * hamburger, and the search collapses to an icon that expands inline.
 */
export function CommandBar() {
  const { searchTerm, setSearchTerm, currentPage, isSearchDisabled } =
    useSearch();
  const [sheetOpen, setSheetOpen] = useState(false);
  const [mobileSearchOpen, setMobileSearchOpen] = useState(false);
  const mobileInputRef = useRef<HTMLInputElement>(null);
  const desktopInputRef = useRef<HTMLInputElement>(null);
  const pathname = usePathname();
  const { setTarget: setRibbonTarget, active: ribbonActive } = useRibbonMount();

  // Close mobile sheet when route changes.
  useEffect(() => {
    setSheetOpen(false);
  }, [pathname]);

  // Cmd/Ctrl+K focuses the search input. Matches the convention pages
  // throughout the redesign use to signal a global search.
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        if (isSearchDisabled) return;
        e.preventDefault();
        if (window.matchMedia("(min-width: 1024px)").matches) {
          desktopInputRef.current?.focus();
          desktopInputRef.current?.select();
        } else {
          setMobileSearchOpen(true);
        }
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [isSearchDisabled]);

  // Auto-focus when the mobile search expands.
  useEffect(() => {
    if (mobileSearchOpen) {
      mobileInputRef.current?.focus();
    }
  }, [mobileSearchOpen]);

  // Swipe-from-left to open the nav drawer on mobile — preserved from the old
  // header so the existing muscle memory still works.
  const touchStartXRef = useRef<number | null>(null);
  const touchStartYRef = useRef<number | null>(null);

  const handleTouchStart = useCallback((e: TouchEvent) => {
    const t = e.touches[0];
    if (!t) return;
    if (t.clientX <= 24) {
      touchStartXRef.current = t.clientX;
      touchStartYRef.current = t.clientY;
    }
  }, []);

  const handleTouchMove = useCallback((e: TouchEvent) => {
    if (touchStartXRef.current === null || touchStartYRef.current === null) {
      return;
    }
    const t = e.touches[0];
    if (!t) return;
    const dx = t.clientX - touchStartXRef.current;
    const dy = Math.abs(t.clientY - touchStartYRef.current);
    if (dx > 40 && dy < dx * 0.8) {
      setSheetOpen(true);
      touchStartXRef.current = null;
      touchStartYRef.current = null;
    }
  }, []);

  const handleTouchEnd = useCallback(() => {
    touchStartXRef.current = null;
    touchStartYRef.current = null;
  }, []);

  useEffect(() => {
    const el = window;
    el.addEventListener("touchstart", handleTouchStart, { passive: true });
    el.addEventListener("touchmove", handleTouchMove, { passive: true });
    el.addEventListener("touchend", handleTouchEnd, { passive: true });
    return () => {
      el.removeEventListener("touchstart", handleTouchStart);
      el.removeEventListener("touchmove", handleTouchMove);
      el.removeEventListener("touchend", handleTouchEnd);
    };
  }, [handleTouchStart, handleTouchMove, handleTouchEnd]);

  const placeholder = getSearchPlaceholder(currentPage);

  return (
    <div
      className="sticky top-0 z-30 border-b bg-background/85 backdrop-blur-xl backdrop-saturate-150"
      style={{ paddingTop: "env(safe-area-inset-top, 0px)" }}
    >
      {/* ─── Primary bar (56px) ─────────────────────────────────────── */}
      <div className="relative flex h-14 items-center gap-3 px-3 lg:px-5">
        {/* Mobile: hamburger → sheet drawer with section list */}
        <Sheet open={sheetOpen} onOpenChange={setSheetOpen}>
          <SheetTrigger asChild>
            <Button
              size="icon"
              variant="ghost"
              className="lg:hidden h-9 w-9 shrink-0"
              aria-label="Open navigation menu"
            >
              <Menu className="h-5 w-5" />
            </Button>
          </SheetTrigger>
          <SheetContent
            side="left"
            className="w-72 p-0 overflow-auto"
            aria-describedby={undefined}
            style={{
              paddingTop: "env(safe-area-inset-top, 0px)",
              paddingBottom: "env(safe-area-inset-bottom, 0px)",
            }}
          >
            <div className="flex items-center gap-2 px-4 py-3 border-b">
              <Image
                src="/rensaio.png"
                alt="Rensaiō"
                width={24}
                height={24}
                className="h-6 w-6 object-contain"
              />
              <span className="text-sm font-semibold">Rensaiō</span>
            </div>
            <SectionList onNavigate={() => setSheetOpen(false)} />

            {/* Drawer footer — download status + project links, restored from
                the old sidebar bottom group. */}
            <div className="mt-2 space-y-3 border-t px-3 pt-3">
              <DownloadStatus variant="drawer" />
              <ExternalLinks />
            </div>
          </SheetContent>
        </Sheet>

        {/* Logo (always visible). Wordmark hidden when mobile search expands. */}
        <Link
          href="/library"
          className="flex items-center gap-2 shrink-0"
          aria-label="Rensaiō home"
        >
          <Image
            src="/rensaio.png"
            alt=""
            width={28}
            height={28}
            priority
            className="h-7 w-7 object-contain"
          />
          <AnimatePresence>
            {!mobileSearchOpen && (
              <motion.span
                initial={{ opacity: 0, width: 0 }}
                animate={{ opacity: 1, width: "auto" }}
                exit={{ opacity: 0, width: 0 }}
                transition={{ duration: 0.15 }}
                className="hidden md:inline text-sm font-semibold text-foreground whitespace-nowrap overflow-hidden"
              >
                Rensaiō
              </motion.span>
            )}
          </AnimatePresence>
        </Link>

        {/* Desktop section pills — absolutely centered in the bar so they sit in
            the visual center regardless of logo / right-cluster widths. Capped at
            60vw so a long section list can't overlap the logo or right cluster. */}
        <div className="hidden lg:flex absolute left-1/2 -translate-x-1/2 pointer-events-none max-w-[60vw]">
          <div className="pointer-events-auto min-w-0">
            <SectionPills />
          </div>
        </div>

        {/* Spacer — pushes search/avatar/theme cluster to the right edge. The
            desktop section pills are absolutely centered above this row, so we
            need a normal flex spacer at every breakpoint. */}
        <div className="flex-1" />

        {/* Desktop search input */}
        {!isSearchDisabled && (
          <div className="hidden lg:flex relative items-center shrink-0">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
            <Input
              ref={desktopInputRef}
              type="search"
              placeholder={placeholder}
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              disabled={isSearchDisabled}
              className="w-56 xl:w-72 pl-9 pr-16 h-9 bg-muted/50 border-border/60 focus:bg-background transition-colors [&::-webkit-search-cancel-button]:appearance-none [&::-webkit-search-decoration]:appearance-none"
            />
            {searchTerm ? (
              <button
                onClick={() => setSearchTerm("")}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                aria-label="Clear search"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            ) : (
              <kbd className="absolute right-2.5 top-1/2 -translate-y-1/2 pointer-events-none hidden xl:inline-flex items-center gap-0.5 rounded border border-border/60 bg-muted/60 px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                ⌘K
              </kbd>
            )}
          </div>
        )}

        {/* Mobile search expand */}
        <div className="lg:hidden flex items-center gap-1 shrink-0">
          <AnimatePresence>
            {mobileSearchOpen && (
              <motion.div
                initial={{ opacity: 0, width: 0 }}
                animate={{ opacity: 1, width: "auto" }}
                exit={{ opacity: 0, width: 0 }}
                className="overflow-hidden"
              >
                <div className="relative flex items-center">
                  <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
                  <input
                    ref={mobileInputRef}
                    type="search"
                    placeholder={placeholder}
                    value={searchTerm}
                    onChange={(e) => setSearchTerm(e.target.value)}
                    disabled={isSearchDisabled}
                    className="w-44 pl-8 pr-2 h-9 rounded-lg bg-muted/70 border border-border/60 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring/50 focus:bg-background transition-all [&::-webkit-search-cancel-button]:appearance-none [&::-webkit-search-decoration]:appearance-none"
                  />
                </div>
              </motion.div>
            )}
          </AnimatePresence>

          {!isSearchDisabled && (
            <Button
              size="icon"
              variant="ghost"
              className="h-9 w-9 shrink-0"
              onClick={() => {
                if (mobileSearchOpen && searchTerm) setSearchTerm("");
                setMobileSearchOpen((v) => !v);
              }}
              aria-label={mobileSearchOpen ? "Close search" : "Open search"}
            >
              {mobileSearchOpen ? (
                <X className="h-5 w-5" />
              ) : (
                <Search className="h-5 w-5" />
              )}
            </Button>
          )}
        </div>

        {/* Download status badges (desktop) — active / queued / failed counts,
            restored from the old sidebar. Links to the queue. */}
        <div className="hidden lg:flex items-center shrink-0">
          <DownloadStatus variant="bar" />
        </div>

        {/* Theme toggle (desktop) */}
        <div className="hidden lg:flex items-center shrink-0">
          <ThemeToggleButton />
        </div>

        {/* User avatar */}
        <div className="flex items-center shrink-0">
          <UserAvatarDropdown size="md" />
        </div>
      </div>

      {/* ─── Contextual ribbon (48px when a page has filled it) ──────── */}
      {/*
        We always render the portal target so RibbonSlot has somewhere to
        portal into immediately on mount — `display:none` keeps it
        invisible until a page registers a slot, so the layout doesn't
        reserve space on routes that don't use the ribbon.
      */}
      <div
        className={
          ribbonActive
            ? "border-t border-border/60 bg-background/60"
            : "hidden"
        }
      >
        <div
          ref={setRibbonTarget}
          className="flex h-12 items-center gap-2 px-3 lg:px-5 overflow-x-auto scrollbar-hide"
        />
      </div>
    </div>
  );
}

/* ─── Theme toggle ─────────────────────────────────────────────────────── */

function ThemeToggleButton() {
  const { theme, setTheme } = useTheme();
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  if (!mounted) {
    // Render a placeholder of the same size so the bar doesn't jiggle once the
    // hook hydrates.
    return <div className="h-9 w-9" />;
  }

  const cycle = () => {
    const order = ["light", "dark", "system"] as const;
    const idx = order.findIndex((t) => t === theme);
    const next = order[(idx + 1) % order.length]!;
    setTheme(next);
  };

  const label =
    theme === "light" ? "Light" : theme === "dark" ? "Dark" : "System";
  const Icon = theme === "light" ? Sun : theme === "dark" ? Moon : Monitor;

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          size="icon"
          variant="ghost"
          className="h-9 w-9"
          onClick={cycle}
          aria-label={`Theme: ${label}. Click to cycle.`}
        >
          <Icon className="h-4 w-4" />
        </Button>
      </TooltipTrigger>
      <TooltipContent side="bottom">Theme: {label}</TooltipContent>
    </Tooltip>
  );
}

/* ─── Helpers ──────────────────────────────────────────────────────────── */

function getSearchPlaceholder(
  currentPage:
    | "library"
    | "providers"
    | "queue"
    | "settings"
    | "series"
    | "other",
): string {
  switch (currentPage) {
    case "library":
      return "Filter series...";
    case "providers":
      return "Filter sources...";
    case "queue":
      return "Filter queue...";
    default:
      return "Filter...";
  }
}
