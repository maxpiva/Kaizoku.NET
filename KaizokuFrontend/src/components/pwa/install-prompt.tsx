"use client";

import { useEffect, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Download, X, Share } from "lucide-react";
import { Button } from "@/components/ui/button";

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

const DISMISS_KEY = "kzk_pwa_install_dismissed";
const DISMISS_COOLDOWN_MS = 7 * 24 * 60 * 60 * 1000; // 7 days

function isIOS(): boolean {
  if (typeof navigator === "undefined") return false;
  return /iphone|ipad|ipod/i.test(navigator.userAgent);
}

function isInStandaloneMode(): boolean {
  if (typeof window === "undefined") return false;
  return (
    window.matchMedia("(display-mode: standalone)").matches ||
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window.navigator as any).standalone === true
  );
}

function wasDismissedRecently(): boolean {
  if (typeof localStorage === "undefined") return false;
  const ts = localStorage.getItem(DISMISS_KEY);
  if (!ts) return false;
  return Date.now() - parseInt(ts, 10) < DISMISS_COOLDOWN_MS;
}

export function PWAInstallPrompt() {
  const [promptEvent, setPromptEvent] = useState<BeforeInstallPromptEvent | null>(null);
  const [visible, setVisible] = useState(false);
  const [isIOSDevice, setIsIOSDevice] = useState(false);

  useEffect(() => {
    // Don't show if already installed or recently dismissed
    if (isInStandaloneMode() || wasDismissedRecently()) return;

    const ios = isIOS();
    setIsIOSDevice(ios);

    if (ios) {
      // iOS doesn't fire beforeinstallprompt — show manual instructions after delay
      const timer = setTimeout(() => setVisible(true), 3000);
      return () => clearTimeout(timer);
    }

    const handler = (e: Event) => {
      e.preventDefault();
      setPromptEvent(e as BeforeInstallPromptEvent);
      setVisible(true);
    };
    window.addEventListener("beforeinstallprompt", handler);
    return () => window.removeEventListener("beforeinstallprompt", handler);
  }, []);

  const handleInstall = async () => {
    if (!promptEvent) return;
    await promptEvent.prompt();
    const choice = await promptEvent.userChoice;
    if (choice.outcome === "accepted") {
      setVisible(false);
    }
  };

  const handleDismiss = () => {
    localStorage.setItem(DISMISS_KEY, String(Date.now()));
    setVisible(false);
  };

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          initial={{ y: 80, opacity: 0 }}
          animate={{ y: 0, opacity: 1 }}
          exit={{ y: 80, opacity: 0 }}
          transition={{ type: "spring", stiffness: 340, damping: 30 }}
          className="
            fixed z-50 left-4 right-4 sm:left-auto sm:right-6
            sm:max-w-sm
            rounded-2xl
            bg-background/95 backdrop-blur-md
            border border-border
            shadow-xl shadow-black/20
            p-4
          "
          style={{ bottom: "calc(80px + env(safe-area-inset-bottom))" }}
          role="dialog"
          aria-label="Install Kaizoku.NET"
        >
          <button
            onClick={handleDismiss}
            className="absolute right-3 top-3 flex h-7 w-7 items-center justify-center rounded-full text-muted-foreground hover:text-foreground hover:bg-accent/50 transition-colors"
            aria-label="Dismiss install prompt"
          >
            <X className="h-4 w-4" />
          </button>

          <div className="flex items-start gap-3 pr-6">
            {/* App icon */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src="/android-chrome-192x192.png"
              alt="Kaizoku.NET icon"
              className="h-12 w-12 rounded-xl shrink-0 border border-border/60"
            />

            <div className="flex-1 min-w-0">
              <p className="text-sm font-semibold text-foreground leading-snug">
                Add Kaizoku to your home screen
              </p>
              {isIOSDevice ? (
                <p className="text-xs text-muted-foreground mt-1 leading-relaxed">
                  Tap <Share className="inline h-3.5 w-3.5 mx-0.5 -mt-0.5" aria-label="Share" />
                  then <strong>"Add to Home Screen"</strong> in Safari.
                </p>
              ) : (
                <p className="text-xs text-muted-foreground mt-1 leading-relaxed">
                  Get faster access and offline support.
                </p>
              )}
            </div>
          </div>

          {!isIOSDevice && (
            <div className="mt-3 flex gap-2 justify-end">
              <Button
                size="sm"
                variant="ghost"
                onClick={handleDismiss}
                className="h-8 text-xs"
              >
                Not now
              </Button>
              <Button
                size="sm"
                onClick={handleInstall}
                className="h-8 text-xs gap-1.5"
              >
                <Download className="h-3.5 w-3.5" />
                Install
              </Button>
            </div>
          )}
        </motion.div>
      )}
    </AnimatePresence>
  );
}
