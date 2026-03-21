"use client";

import React, { useEffect, useRef, useState, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { RefreshCw } from "lucide-react";

interface PullToRefreshProps {
  onRefresh: () => Promise<void> | void;
  children: React.ReactNode;
  /** Minimum pull distance in px to trigger refresh */
  threshold?: number;
  /** Whether this is active (only on mobile/touch) */
  disabled?: boolean;
}

const THRESHOLD = 72;
const MAX_PULL = 120;

/**
 * Walk up the DOM from `node` to find the nearest ancestor whose computed
 * overflow-y is "auto" or "scroll" (i.e. an actual scroll container).
 * Falls back to `null` when nothing is found (meaning the page-level
 * window scroll applies).
 */
function findScrollParent(node: HTMLElement | null): HTMLElement | null {
  let el = node?.parentElement ?? null;
  while (el && el !== document.documentElement) {
    const { overflowY } = getComputedStyle(el);
    if (overflowY === "auto" || overflowY === "scroll") return el;
    el = el.parentElement;
  }
  return null;
}

export function PullToRefresh({
  onRefresh,
  children,
  threshold = THRESHOLD,
  disabled = false,
}: PullToRefreshProps) {
  const [pullDistance, setPullDistance] = useState(0);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isPulling, setIsPulling] = useState(false);

  const startYRef = useRef<number | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const scrollParentRef = useRef<HTMLElement | null>(null);
  const isRefreshingRef = useRef(false);

  // Resolve the scroll parent once the component mounts (the DOM is stable).
  useEffect(() => {
    scrollParentRef.current = findScrollParent(containerRef.current);
  }, []);

  /** Returns true when a drawer/dialog overlay is open above the page. */
  const isDrawerOpen = useCallback(() => {
    return !!document.querySelector("[data-drawer-event-blocker], [vaul-drawer]");
  }, []);

  /** Get the scroll-top of the nearest scroll container (or window). */
  const getScrollTop = useCallback(() => {
    if (scrollParentRef.current) {
      return scrollParentRef.current.scrollTop;
    }
    return window.scrollY || document.documentElement.scrollTop;
  }, []);

  const handleTouchStart = useCallback(
    (e: TouchEvent) => {
      if (disabled || isRefreshingRef.current) return;
      // Don't interfere with drawer touch handling
      if (isDrawerOpen()) return;
      const el = containerRef.current;
      if (!el) return;
      // Only trigger if the scroll container is at the top
      if (getScrollTop() > 2) return;
      startYRef.current = e.touches[0]?.clientY ?? null;
    },
    [disabled, isDrawerOpen, getScrollTop]
  );

  const handleTouchMove = useCallback(
    (e: TouchEvent) => {
      if (disabled || isRefreshingRef.current || startYRef.current === null) return;
      // Don't interfere with drawer touch handling
      if (isDrawerOpen()) {
        startYRef.current = null;
        setPullDistance(0);
        setIsPulling(false);
        return;
      }
      const touch = e.touches[0];
      if (!touch) return;
      const deltaY = touch.clientY - startYRef.current;
      if (deltaY < 0) {
        // User swiping up — not our concern
        setPullDistance(0);
        setIsPulling(false);
        return;
      }
      // If the scroll container has scrolled since touchstart, abort the
      // pull gesture — the user is scrolling content, not pulling to refresh.
      if (getScrollTop() > 2) {
        startYRef.current = null;
        setPullDistance(0);
        setIsPulling(false);
        return;
      }
      // Resist pull with rubber-band easing
      const resistance = 0.4;
      const clamped = Math.min(deltaY * resistance, MAX_PULL);
      setPullDistance(clamped);
      setIsPulling(true);
      // Prevent default scroll behaviour while pulling
      if (clamped > 4) {
        e.preventDefault();
      }
    },
    [disabled, isDrawerOpen, getScrollTop]
  );

  const handleTouchEnd = useCallback(async () => {
    if (disabled || isRefreshingRef.current) return;
    startYRef.current = null;

    if (pullDistance >= threshold) {
      isRefreshingRef.current = true;
      setIsRefreshing(true);
      setPullDistance(THRESHOLD * 0.8); // Hold at indicator height
      setIsPulling(false);

      try {
        await onRefresh();
      } finally {
        isRefreshingRef.current = false;
        setIsRefreshing(false);
        setPullDistance(0);
      }
    } else {
      setPullDistance(0);
      setIsPulling(false);
    }
  }, [disabled, pullDistance, threshold, onRefresh]);

  useEffect(() => {
    const el = window;
    el.addEventListener("touchstart", handleTouchStart, { passive: true });
    el.addEventListener("touchmove", handleTouchMove, { passive: false });
    el.addEventListener("touchend", handleTouchEnd, { passive: true });
    return () => {
      el.removeEventListener("touchstart", handleTouchStart);
      el.removeEventListener("touchmove", handleTouchMove);
      el.removeEventListener("touchend", handleTouchEnd);
    };
  }, [handleTouchStart, handleTouchMove, handleTouchEnd]);

  const progress = Math.min(pullDistance / threshold, 1);
  const showIndicator = isPulling || isRefreshing;
  const indicatorY = isRefreshing ? THRESHOLD * 0.8 : pullDistance;

  return (
    <div ref={containerRef} className="relative w-full">
      {/* Pull indicator */}
      <AnimatePresence>
        {showIndicator && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0, transition: { duration: 0.2 } }}
            className="pointer-events-none absolute left-0 right-0 top-0 z-50 flex items-center justify-center"
            style={{ height: indicatorY }}
          >
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-background shadow-md border border-border">
              <motion.div
                animate={
                  isRefreshing
                    ? { rotate: 360 }
                    : { rotate: progress * 270 }
                }
                transition={
                  isRefreshing
                    ? { duration: 0.8, repeat: Infinity, ease: "linear" }
                    : { duration: 0 }
                }
              >
                <RefreshCw
                  className="h-4 w-4"
                  style={{
                    color: progress >= 1
                      ? "hsl(var(--primary))"
                      : "hsl(var(--muted-foreground))",
                    transition: "color 0.2s ease",
                  }}
                />
              </motion.div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Content shifts down while pulling */}
      <motion.div
        style={{
          transform: showIndicator ? `translateY(${indicatorY}px)` : "translateY(0)",
          transition: isRefreshing || (!isPulling && pullDistance === 0)
            ? "transform 0.3s cubic-bezier(0.2, 0, 0, 1)"
            : "none",
        }}
      >
        {children}
      </motion.div>
    </div>
  );
}
