/**
 * Service Worker registration and update management.
 * Call registerServiceWorker() once on app mount (client side only).
 */

type UpdateCallback = () => void;

let updateCallback: UpdateCallback | null = null;
let waitingWorker: ServiceWorker | null = null;

export function registerServiceWorker(onUpdate?: UpdateCallback): void {
  if (typeof window === "undefined") return;
  if (!("serviceWorker" in navigator)) return;

  if (onUpdate) {
    updateCallback = onUpdate;
  }

  window.addEventListener("load", () => {
    navigator.serviceWorker
      .register("/sw.js", { scope: "/" })
      .then((registration) => {
        // Check for updates on every page load
        registration.update().catch(() => {
          // Silently ignore update check failures (e.g., offline)
        });

        // A new SW is waiting — notify the user
        if (registration.waiting) {
          waitingWorker = registration.waiting;
          updateCallback?.();
        }

        registration.addEventListener("updatefound", () => {
          const newWorker = registration.installing;
          if (!newWorker) return;

          newWorker.addEventListener("statechange", () => {
            if (
              newWorker.state === "installed" &&
              navigator.serviceWorker.controller
            ) {
              // New content is available
              waitingWorker = newWorker;
              updateCallback?.();
            }
          });
        });
      })
      .catch((err) => {
        console.warn("[SW] Registration failed:", err);
      });

    // When SW takes control, reload the page to use the new version
    let refreshing = false;
    navigator.serviceWorker.addEventListener("controllerchange", () => {
      if (refreshing) return;
      refreshing = true;
      window.location.reload();
    });
  });
}

/**
 * Tell the waiting service worker to skip waiting and activate immediately.
 * Should be called when the user clicks "Refresh" in the update toast.
 */
export function activatePendingUpdate(): void {
  if (!waitingWorker) return;
  waitingWorker.postMessage({ type: "SKIP_WAITING" });
}
