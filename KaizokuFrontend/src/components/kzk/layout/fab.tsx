"use client";

import { useState, useEffect, useRef, useCallback } from "react";
import { Plus } from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import { usePathname } from "next/navigation";
import { AddSeries } from "@/components/kzk/series/add-series";

// Pages on which the FAB is visible
const FAB_PAGES = ["/library", "/cloud-latest"];

export function MobileFAB() {
  const pathname = usePathname();
  const [visible, setVisible] = useState(true);
  const [addSeriesOpen, setAddSeriesOpen] = useState(false);
  const lastScrollY = useRef(0);
  const scrollTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Only render on designated pages
  const isOnFABPage = FAB_PAGES.some(
    (p) => pathname === p || pathname.startsWith(p + "/")
  );

  // Hide FAB on scroll-down, show on scroll-up
  const handleScroll = useCallback(() => {
    const currentScrollY = window.scrollY;
    const delta = currentScrollY - lastScrollY.current;

    if (delta > 8) {
      setVisible(false);
    } else if (delta < -8) {
      setVisible(true);
    }

    lastScrollY.current = currentScrollY;

    // Always show after scroll stops
    if (scrollTimerRef.current) clearTimeout(scrollTimerRef.current);
    scrollTimerRef.current = setTimeout(() => {
      setVisible(true);
    }, 800);
  }, []);

  useEffect(() => {
    window.addEventListener("scroll", handleScroll, { passive: true });
    return () => {
      window.removeEventListener("scroll", handleScroll);
      if (scrollTimerRef.current) clearTimeout(scrollTimerRef.current);
    };
  }, [handleScroll]);

  if (!isOnFABPage) return null;

  return (
    <>
      <AnimatePresence>
        {visible && (
          <motion.button
            key="fab"
            initial={{ scale: 0, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0, opacity: 0 }}
            transition={{ type: "spring", stiffness: 380, damping: 28 }}
            whileTap={{ scale: 0.88 }}
            onClick={() => setAddSeriesOpen(true)}
            aria-label="Add series"
            className="
              sm:hidden
              fixed z-40
              flex items-center justify-center
              h-14 w-14 rounded-full
              bg-primary text-primary-foreground
              shadow-lg shadow-primary/30
              active:shadow-md active:shadow-primary/20
              focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2
              transition-shadow
            "
            style={{
              bottom: "calc(24px + env(safe-area-inset-bottom))",
              right: "24px",
            }}
          >
            <Plus className="h-6 w-6 stroke-[2.5]" />
          </motion.button>
        )}
      </AnimatePresence>

      {/* Add series dialog — controlled externally so the FAB button triggers it */}
      <AddSeries
        open={addSeriesOpen}
        onOpenChange={setAddSeriesOpen}
      />
    </>
  );
}
