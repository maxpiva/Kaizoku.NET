"use client";

import { useEffect } from "react";
import { toast } from "sonner";
import { registerServiceWorker, activatePendingUpdate } from "@/lib/sw-register";

export function ServiceWorkerProvider() {
  useEffect(() => {
    registerServiceWorker(() => {
      // Called when a new SW version is waiting
      toast.info("New version available", {
        description: "Refresh to apply the update.",
        duration: Infinity,
        action: {
          label: "Refresh",
          onClick: () => {
            activatePendingUpdate();
          },
        },
        id: "sw-update", // Deduplicate multiple update toasts
      });
    });
  }, []);

  return null;
}
