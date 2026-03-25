"use client";

import { useState, useRef, useEffect, useCallback } from "react";
import { Search, X, Menu } from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";

import KzkBreadcrumb from "@/components/kzk/layout/breadcrumb";
import { KzkNavbar } from "@/components/kzk/layout/sidebar";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTrigger } from "@/components/ui/sheet";
import { useSearch } from "@/contexts/search-context";

interface KzkHeaderProps {
  seriesTitle?: string;
}

export default function KzkHeader({ seriesTitle }: KzkHeaderProps = {}) {
  const { searchTerm, setSearchTerm, currentPage, isSearchDisabled } = useSearch();
  const [mobileSearchOpen, setMobileSearchOpen] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);
  const mobileInputRef = useRef<HTMLInputElement>(null);
  const pathname = usePathname();

  // Close sheet on navigation
  useEffect(() => {
    setSheetOpen(false);
  }, [pathname]);

  // Swipe-from-left gesture to open the sheet drawer
  const touchStartXRef = useRef<number | null>(null);
  const touchStartYRef = useRef<number | null>(null);

  const handleTouchStart = useCallback((e: TouchEvent) => {
    const t = e.touches[0];
    if (!t) return;
    // Only capture touches starting within the leftmost 24px edge
    if (t.clientX <= 24) {
      touchStartXRef.current = t.clientX;
      touchStartYRef.current = t.clientY;
    }
  }, []);

  const handleTouchMove = useCallback((e: TouchEvent) => {
    if (touchStartXRef.current === null || touchStartYRef.current === null) return;
    const t = e.touches[0];
    if (!t) return;
    const dx = t.clientX - touchStartXRef.current;
    const dy = Math.abs(t.clientY - touchStartYRef.current);
    // Horizontal swipe right at least 40px with less vertical drift than horizontal
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

  const getPlaceholder = () => {
    switch (currentPage) {
      case "library":
        return "Search series...";
      case "providers":
        return "Search sources...";
      case "queue":
        return "Search queue...";
      default:
        return "Search...";
    }
  };

  useEffect(() => {
    if (mobileSearchOpen && mobileInputRef.current) {
      mobileInputRef.current.focus();
    }
  }, [mobileSearchOpen]);

  return (
    <header
      className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b bg-background/80 backdrop-blur-md px-3 lg:px-5"
      style={{ paddingTop: "env(safe-area-inset-top, 0px)" }}
    >
      {/* Mobile: hamburger menu */}
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
          <span className="sr-only" id="nav-sheet-title">Navigation</span>
          <KzkNavbar onNavigate={() => setSheetOpen(false)} />
        </SheetContent>
      </Sheet>

      {/* Mobile: logo/title (center on mobile when search not open) */}
      <AnimatePresence>
        {!mobileSearchOpen && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="lg:hidden flex items-center gap-2 flex-1"
          >
            <Link href="/library" className="flex items-center gap-2">
              <Image
                src="/kaizoku.net.png"
                alt="Kaizoku.NET"
                width={24}
                height={24}
                className="h-6 w-6 object-contain"
              />
              <span className="text-sm font-semibold text-foreground">
                {seriesTitle ?? "Kaizoku.NET"}
              </span>
            </Link>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Desktop: breadcrumb */}
      <div className="hidden lg:flex flex-1 items-center min-w-0">
        <KzkBreadcrumb seriesTitle={seriesTitle} />
      </div>

      {/* Desktop: search bar (center/right) */}
      {!isSearchDisabled && (
        <div className="hidden lg:flex relative items-center ml-auto">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
          <Input
            type="search"
            placeholder={getPlaceholder()}
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            disabled={isSearchDisabled}
            className="w-52 lg:w-72 pl-9 h-9 bg-muted/50 border-border/60 focus:bg-background transition-colors"
          />
          {searchTerm && (
            <button
              onClick={() => setSearchTerm("")}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
              aria-label="Clear search"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
      )}

      {/* Mobile: search expand */}
      <div className="lg:hidden flex items-center gap-1 ml-auto">
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
                  placeholder={getPlaceholder()}
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  disabled={isSearchDisabled}
                  className="w-44 pl-8 pr-2 h-9 rounded-lg bg-muted/70 border border-border/60 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring/50 focus:bg-background transition-all"
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
    </header>
  );
}
