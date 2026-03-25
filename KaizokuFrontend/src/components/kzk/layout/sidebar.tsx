"use client";

import {
  List,
  LucideSettings,
  Plug,
  Sparkles,
  Library,
  Pin,
  PinOff,
  Sun,
  Moon,
  Monitor,
  ChevronRight,
  Download,
  Clock,
  AlertTriangle,
} from "lucide-react";
import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import React, { useState, useEffect, useRef, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { useTheme } from "next-themes";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { useDownloadsMetrics } from "@/lib/api/hooks/useDownloads";

// ─── Navigation Config ─────────────────────────────────────────────────────

interface NavItem {
  name: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
  badge?: number;
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

// Static items used for breadcrumb compatibility
const ALL_NAV_ITEMS = [
  { name: "Library", href: "/library", icon: Library },
  { name: "Browse", href: "/cloud-latest", icon: Sparkles },
  { name: "Sources", href: "/providers", icon: Plug },
  { name: "Queue", href: "/queue", icon: List },
  { name: "Settings", href: "/settings", icon: LucideSettings },
];

// Keep legacy sidebarItems for breadcrumb compatibility
export const sidebarItems = ALL_NAV_ITEMS.map((item) => {
  const Icon = item.icon;
  return {
    ...item,
    icon: <Icon className="h-5 w-5" />,
    topSide: true,
  };
});

// Hook to build nav groups (all items visible, no permission gating)
function useNavGroups(): NavGroup[] {
  const libraryItems: NavItem[] = [
    { name: "Library", href: "/library", icon: Library },
    { name: "Browse", href: "/cloud-latest", icon: Sparkles },
  ];

  const mgmtItems: NavItem[] = [
    { name: "Sources", href: "/providers", icon: Plug },
    { name: "Queue", href: "/queue", icon: List },
    { name: "Settings", href: "/settings", icon: LucideSettings },
  ];

  return [
    { label: "Library", items: libraryItems },
    { label: "Management", items: mgmtItems },
  ];
}

// ─── Download Counters (inline, sidebar-aware) ─────────────────────────────

function SidebarDownloadCounters({ expanded }: { expanded: boolean }) {
  const { data: metrics } = useDownloadsMetrics();

  if (!metrics) return null;

  const counters = [
    { value: metrics.downloads, color: "text-blue-400", Icon: Download, label: "Active" },
    { value: metrics.queued, color: "text-amber-400", Icon: Clock, label: "Queued" },
    { value: metrics.failed, color: "text-red-400", Icon: AlertTriangle, label: "Failed" },
  ].filter((c) => c.value > 0);

  if (counters.length === 0) return null;

  // When collapsed, only show the highest-priority counter to prevent stacking
  const visibleCounters = expanded ? counters : [counters[0]!];

  // Build tooltip text for collapsed state (shows full summary)
  const tooltipText = counters.map((c) => `${c.label}: ${c.value}`).join(", ");

  const linkContent = (
    <Link
      href="/queue"
      className={`flex items-center gap-2 rounded-lg hover:bg-accent/50 transition-colors group ${
        expanded ? "px-3 py-2" : "justify-center px-2 py-2"
      }`}
    >
      <div className="flex items-center gap-1.5">
        {visibleCounters.map(({ value, color, Icon, label }) => (
          <div key={label} className="flex items-center gap-0.5">
            <Icon className={`h-3.5 w-3.5 ${color} shrink-0`} />
            <span className={`text-xs font-semibold ${color}`}>{value}</span>
          </div>
        ))}
      </div>
      <AnimatePresence>
        {expanded && (
          <motion.span
            initial={{ opacity: 0, width: 0 }}
            animate={{ opacity: 1, width: "auto" }}
            exit={{ opacity: 0, width: 0 }}
            transition={{ duration: 0.2 }}
            className="text-xs text-muted-foreground whitespace-nowrap overflow-hidden"
          >
            Downloads
          </motion.span>
        )}
      </AnimatePresence>
    </Link>
  );

  if (expanded) return linkContent;

  return (
    <Tooltip delayDuration={0}>
      <TooltipTrigger asChild>{linkContent}</TooltipTrigger>
      <TooltipContent side="right">{tooltipText}</TooltipContent>
    </Tooltip>
  );
}

// ─── Theme Toggle ───────────────────────────────────────────────────────────

function SidebarThemeToggle({ expanded }: { expanded: boolean }) {
  const { theme, setTheme } = useTheme();
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  if (!mounted) return null;

  const themes = [
    { value: "light", Icon: Sun, label: "Light" },
    { value: "dark", Icon: Moon, label: "Dark" },
    { value: "system", Icon: Monitor, label: "System" },
  ];

  const current = themes.find((t) => t.value === theme) ?? themes[2]!;
  const CurrentIcon = current.Icon;

  const cycle = () => {
    const idx = themes.findIndex((t) => t.value === theme);
    const next = themes[(idx + 1) % themes.length]!;
    setTheme(next.value);
  };

  if (!expanded) {
    return (
      <Tooltip>
        <TooltipTrigger asChild>
          <button
            onClick={cycle}
            className="flex h-9 w-9 items-center justify-center rounded-lg text-muted-foreground hover:text-foreground hover:bg-accent/50 transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            aria-label={`Theme: ${current.label}. Click to cycle.`}
          >
            <CurrentIcon className="h-4 w-4" />
          </button>
        </TooltipTrigger>
        <TooltipContent side="right">Theme: {current.label}</TooltipContent>
      </Tooltip>
    );
  }

  return (
    <div className="flex items-center gap-1 rounded-lg bg-muted/50 p-1">
      {themes.map(({ value, Icon, label }) => (
        <button
          key={value}
          onClick={() => setTheme(value)}
          className={`flex flex-1 items-center justify-center gap-1.5 rounded-md py-1.5 px-2 text-xs transition-all ${
            theme === value
              ? "bg-background text-foreground shadow-sm"
              : "text-muted-foreground hover:text-foreground"
          }`}
          aria-label={`Set ${label} theme`}
          aria-pressed={theme === value}
        >
          <Icon className="h-3.5 w-3.5 shrink-0" />
          <span className="whitespace-nowrap">{label}</span>
        </button>
      ))}
    </div>
  );
}

// ─── Main Sidebar (Desktop) ─────────────────────────────────────────────────

const COLLAPSED_W = 64;
const EXPANDED_W = 260;
const PIN_KEY = "kzk_sidebar_pinned";

export default function KzkSidebar() {
  const pathname = usePathname();
  const navGroups = useNavGroups();

  // Pin state — persisted to sessionStorage
  const [pinned, setPinned] = useState(() => {
    if (typeof window === "undefined") return false;
    return sessionStorage.getItem(PIN_KEY) === "true";
  });

  const [hovered, setHovered] = useState(false);
  const expanded = pinned || hovered;

  const hoverTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleMouseEnter = useCallback(() => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    if (pinned) return;
    setHovered(true);
  }, [pinned]);

  const handleMouseLeave = useCallback(() => {
    if (pinned) return;
    hoverTimer.current = setTimeout(() => setHovered(false), 200);
  }, [pinned]);

  const togglePin = () => {
    setPinned((prev) => {
      const next = !prev;
      sessionStorage.setItem(PIN_KEY, String(next));
      return next;
    });
  };

  // Keyboard shortcut: Ctrl+B / Cmd+B
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === "b") {
        e.preventDefault();
        togglePin();
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);

  const isActive = (href: string) =>
    pathname === href || pathname.startsWith(href + "/");

  return (
    <>
      {/* Push content when pinned & expanded */}
      <div
        className="hidden lg:block shrink-0 transition-[width] duration-300"
        style={{ width: pinned ? EXPANDED_W : COLLAPSED_W }}
      />

      <motion.aside
        className="fixed inset-y-0 left-0 z-30 hidden lg:flex flex-col border-r bg-background/90 backdrop-blur-xl backdrop-saturate-150 overflow-hidden"
        animate={{ width: expanded ? EXPANDED_W : COLLAPSED_W }}
        transition={{ duration: 0.25, ease: [0.4, 0, 0.2, 1] }}
        onMouseEnter={handleMouseEnter}
        onMouseLeave={handleMouseLeave}
        style={{
          paddingTop: "env(safe-area-inset-top, 0px)",
          paddingBottom: "env(safe-area-inset-bottom, 0px)",
          paddingLeft: "env(safe-area-inset-left, 0px)",
        }}
      >
        {/* Logo */}
        <div className="flex h-14 items-center border-b px-3 shrink-0">
          <Link
            href="/library"
            className="flex items-center gap-3 min-w-0"
            aria-label="Kaizoku.NET Home"
          >
            <div className="shrink-0 flex h-8 w-8 items-center justify-center">
              <Image
                src="/kaizoku.net.png"
                alt="Kaizoku.NET"
                width={32}
                height={32}
                priority
                className="h-8 w-8 object-contain"
              />
            </div>
            <AnimatePresence>
              {expanded && (
                <motion.span
                  initial={{ opacity: 0, x: -8 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -8 }}
                  transition={{ duration: 0.18 }}
                  className="text-base font-semibold text-foreground whitespace-nowrap overflow-hidden"
                >
                  Kaizoku.NET
                </motion.span>
              )}
            </AnimatePresence>
          </Link>

          {/* Pin toggle */}
          <AnimatePresence>
            {expanded && (
              <motion.button
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.15 }}
                onClick={togglePin}
                className="ml-auto shrink-0 flex h-7 w-7 items-center justify-center rounded-md text-muted-foreground hover:text-foreground hover:bg-accent/50 transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                aria-label={pinned ? "Unpin sidebar" : "Pin sidebar"}
                title={`${pinned ? "Unpin" : "Pin"} sidebar (Ctrl+B)`}
              >
                {pinned ? <PinOff className="h-3.5 w-3.5" /> : <Pin className="h-3.5 w-3.5" />}
              </motion.button>
            )}
          </AnimatePresence>
        </div>

        {/* Nav groups */}
        <nav className="flex-1 overflow-hidden py-3 px-2 space-y-4 min-w-0">
          {navGroups.map((group) => (
            <div key={group.label}>
              {/* Group label */}
              <AnimatePresence>
                {expanded && (
                  <motion.p
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit={{ opacity: 0 }}
                    transition={{ duration: 0.15 }}
                    className="mb-1 px-2 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground/60 whitespace-nowrap"
                  >
                    {group.label}
                  </motion.p>
                )}
              </AnimatePresence>

              <div className="space-y-0.5">
                {group.items.map(({ name, href, icon: Icon, badge }) => {
                  const active = isActive(href);

                  const linkContent = (
                    <Link
                      href={href}
                      className={`relative flex items-center gap-3 rounded-lg px-2 py-2 text-sm font-medium transition-all duration-200 group focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
                        active
                          ? "bg-primary/10 text-primary"
                          : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"
                      }`}
                      aria-current={active ? "page" : undefined}
                    >
                      {/* Active indicator bar */}
                      {active && (
                        <div className="absolute left-0 top-1 bottom-1 w-0.5 rounded-full bg-primary" />
                      )}

                      <div className="relative shrink-0 flex h-5 w-5 items-center justify-center">
                        <Icon
                          className={`h-5 w-5 transition-transform duration-200 ${
                            active ? "scale-105" : "group-hover:scale-105"
                          }`}
                        />
                        {/* Notification dot when collapsed — count shown in tooltip */}
                        {!expanded && badge && badge > 0 && (
                          <span className="absolute -top-0.5 -right-0.5 h-2.5 w-2.5 rounded-full bg-primary ring-2 ring-background" />
                        )}
                      </div>

                      <AnimatePresence>
                        {expanded && (
                          <motion.span
                            initial={{ opacity: 0, x: -4 }}
                            animate={{ opacity: 1, x: 0 }}
                            exit={{ opacity: 0, x: -4 }}
                            transition={{ duration: 0.18 }}
                            className="whitespace-nowrap overflow-hidden flex-1"
                          >
                            {name}
                          </motion.span>
                        )}
                      </AnimatePresence>

                      {/* Badge on label when expanded */}
                      {expanded && badge && badge > 0 && (
                        <span className="ml-auto shrink-0 h-4.5 min-w-[18px] rounded-full bg-primary text-primary-foreground text-[9px] font-bold flex items-center justify-center px-1">
                          {badge > 99 ? '99+' : badge}
                        </span>
                      )}

                      {active && expanded && !badge && (
                        <motion.div
                          initial={{ opacity: 0 }}
                          animate={{ opacity: 1 }}
                          exit={{ opacity: 0 }}
                        >
                          <ChevronRight className="h-3.5 w-3.5 ml-auto shrink-0 opacity-40" />
                        </motion.div>
                      )}
                    </Link>
                  );

                  // Only wrap in Tooltip when collapsed — prevents Radix
                  // tooltip internal "open" state from leaking across the
                  // expand/collapse transition and rendering ghost tooltips.
                  if (expanded) {
                    return <React.Fragment key={href}>{linkContent}</React.Fragment>;
                  }

                  return (
                    <Tooltip key={href} delayDuration={0}>
                      <TooltipTrigger asChild>
                        {linkContent}
                      </TooltipTrigger>
                      <TooltipContent side="right" className="z-50">
                        {name}
                      </TooltipContent>
                    </Tooltip>
                  );
                })}
              </div>
            </div>
          ))}
        </nav>

        {/* Bottom area */}
        <div className="border-t px-2 py-3 space-y-2 shrink-0">
          {/* Download counters */}
          <SidebarDownloadCounters expanded={expanded} />

          {/* Theme toggle */}
          <div className={expanded ? "px-0" : "flex justify-center"}>
            <SidebarThemeToggle expanded={expanded} />
          </div>
        </div>
      </motion.aside>
    </>
  );
}

// ─── Mobile Navbar (used inside drawer) ─────────────────────────────────────

export function KzkNavbar({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  const navGroups = useNavGroups();

  const isActive = (href: string) =>
    pathname === href || pathname.startsWith(href + "/");

  return (
    <nav className="flex flex-col gap-1 px-2 py-4">
      {/* Logo row */}
      <div className="flex items-center gap-3 px-3 py-2 mb-4">
        <Image
          src="/kaizoku.net.png"
          alt="Kaizoku.NET"
          width={28}
          height={28}
          className="h-7 w-7 object-contain"
        />
        <span className="text-base font-semibold">Kaizoku.NET</span>
      </div>

      {navGroups.map((group) => (
        <div key={group.label} className="mb-3">
          <p className="mb-1.5 px-3 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground/60">
            {group.label}
          </p>
          {group.items.map(({ name, href, icon: Icon, badge }) => {
            const active = isActive(href);
            return (
              <Link
                key={href}
                href={href}
                onClick={onNavigate}
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
                {badge && badge > 0 && (
                  <span className="h-4.5 min-w-[18px] rounded-full bg-primary text-primary-foreground text-[9px] font-bold flex items-center justify-center px-1">
                    {badge > 99 ? '99+' : badge}
                  </span>
                )}
              </Link>
            );
          })}
        </div>
      ))}

      {/* Download counters */}
      <div className="mt-2 px-1">
        <MobileDownloadCounters />
      </div>
    </nav>
  );
}

function MobileDownloadCounters() {
  const { data: metrics } = useDownloadsMetrics();
  if (!metrics) return null;

  const counters = [
    { value: metrics.downloads, color: "text-blue-400 bg-blue-400/10", icon: <Download className="h-4 w-4" />, label: "Active" },
    { value: metrics.queued, color: "text-amber-400 bg-amber-400/10", icon: <Clock className="h-4 w-4" />, label: "Queued" },
    { value: metrics.failed, color: "text-red-400 bg-red-400/10", icon: <AlertTriangle className="h-4 w-4" />, label: "Failed" },
  ].filter((c) => c.value > 0);

  if (counters.length === 0) return null;

  return (
    <Link href="/queue" className="flex items-center gap-2 rounded-lg px-3 py-2.5 text-sm hover:bg-accent/50 transition-colors">
      {counters.map(({ value, color, icon, label }) => (
        <span key={label} className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-xs font-medium ${color}`}>
          {icon}
          {value}
        </span>
      ))}
      <span className="text-muted-foreground text-xs">Downloads</span>
    </Link>
  );
}
