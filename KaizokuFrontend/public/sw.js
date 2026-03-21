// Kaizoku.NET Service Worker
// Cache versioning — increment CACHE_VERSION on each deploy to bust old caches
const CACHE_VERSION = "v2";
const STATIC_CACHE = `kaizoku-static-${CACHE_VERSION}`;
const DYNAMIC_CACHE = `kaizoku-dynamic-${CACHE_VERSION}`;
const OFFLINE_URL = "/offline.html";

// Static assets to pre-cache on install
const STATIC_ASSETS = [
  "/",
  "/offline.html",
  "/kaizoku.net.png",
  "/favicon.ico",
  "/favicon-16x16.png",
  "/favicon-32x32.png",
  "/android-chrome-192x192.png",
  "/android-chrome-512x512.png",
  "/apple-touch-icon.png",
  "/site.webmanifest",
];

// ─── Install ─────────────────────────────────────────────────────────────────

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(STATIC_CACHE)
      .then((cache) =>
        // Cache each asset individually so a single failure (e.g. Cloudflare
        // Access redirect) doesn't prevent the SW from installing.
        Promise.all(
          STATIC_ASSETS.map((url) =>
            cache.add(url).catch((err) => {
              console.warn(`[SW] Failed to pre-cache ${url}:`, err.message);
            })
          )
        )
      )
      .then(() => self.skipWaiting())
  );
});

// ─── Activate ────────────────────────────────────────────────────────────────

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((cacheNames) => {
        return Promise.all(
          cacheNames
            .filter(
              (name) =>
                name !== STATIC_CACHE && name !== DYNAMIC_CACHE
            )
            .map((name) => caches.delete(name))
        );
      })
      .then(() => self.clients.claim())
  );
});

// ─── Fetch ────────────────────────────────────────────────────────────────────

self.addEventListener("fetch", (event) => {
  const { request } = event;
  const url = new URL(request.url);

  // Skip non-GET requests
  if (request.method !== "GET") return;

  // Skip browser-extension and chrome-extension requests
  if (!url.protocol.startsWith("http")) return;

  // Skip cross-origin requests (e.g. Cloudflare Access login redirects)
  if (url.origin !== self.location.origin) return;

  // ── API requests: network-first, no cache fallback ───────────────────────
  if (url.pathname.startsWith("/api/") || url.pathname.startsWith("/hub/")) {
    event.respondWith(
      fetch(request).catch(() => {
        // Return a JSON error for API failures while offline
        return new Response(
          JSON.stringify({ error: "You are offline. Please reconnect." }),
          {
            status: 503,
            headers: { "Content-Type": "application/json" },
          }
        );
      })
    );
    return;
  }

  // ── Next.js data fetching (_next/data): network-first ────────────────────
  if (url.pathname.startsWith("/_next/data/")) {
    event.respondWith(
      fetch(request)
        .then((response) => {
          if (response.ok) {
            const clone = response.clone();
            caches.open(DYNAMIC_CACHE).then((cache) => cache.put(request, clone));
          }
          return response;
        })
        .catch(async () => {
          const cached = await caches.match(request);
          return cached ?? new Response(JSON.stringify({}), {
            status: 503,
            headers: { "Content-Type": "application/json" },
          });
        })
    );
    return;
  }

  // ── Static assets (_next/static, images, fonts): cache-first ────────────
  if (
    url.pathname.startsWith("/_next/static/") ||
    url.pathname.startsWith("/_next/image/") ||
    /\.(png|jpg|jpeg|gif|webp|svg|ico|woff|woff2|ttf|eot|webmanifest)$/.test(url.pathname)
  ) {
    event.respondWith(
      caches.match(request).then((cached) => {
        if (cached) return cached;
        return fetch(request)
          .then((response) => {
            // Detect cross-origin redirects (e.g. Cloudflare Access login).
            // response.redirected will be true and the URL will differ from
            // the original request, but for opaque redirects the fetch may
            // simply fail.  Guard against non-OK responses too.
            if (
              response.ok &&
              response.url &&
              new URL(response.url).origin === self.location.origin
            ) {
              const clone = response.clone();
              caches.open(STATIC_CACHE).then((cache) => cache.put(request, clone));
            }
            return response;
          })
          .catch(() => {
            // Network error or CORS failure (e.g. Cloudflare Access redirect).
            // Return a minimal valid response so the browser doesn't retry
            // endlessly.  For webmanifest specifically, return an empty
            // manifest so the PWA system stays quiet.
            if (/\.webmanifest$/.test(url.pathname)) {
              return new Response("{}", {
                status: 200,
                headers: { "Content-Type": "application/manifest+json" },
              });
            }
            return new Response("", { status: 503 });
          });
      })
    );
    return;
  }

  // ── Navigation requests: network-first, offline fallback ─────────────────
  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request)
        .then((response) => {
          if (response.ok) {
            const clone = response.clone();
            caches.open(DYNAMIC_CACHE).then((cache) => cache.put(request, clone));
          }
          return response;
        })
        .catch(async () => {
          const cached = await caches.match(request);
          if (cached) return cached;
          const offlinePage = await caches.match(OFFLINE_URL);
          return offlinePage ?? new Response("Offline", { status: 503 });
        })
    );
    return;
  }

  // ── Default: stale-while-revalidate ──────────────────────────────────────
  event.respondWith(
    caches.match(request).then((cached) => {
      const fetchPromise = fetch(request).then((response) => {
        if (response.ok) {
          const clone = response.clone();
          caches.open(DYNAMIC_CACHE).then((cache) => cache.put(request, clone));
        }
        return response;
      });
      return cached ?? fetchPromise;
    })
  );
});

// ─── Message Handling ────────────────────────────────────────────────────────

self.addEventListener("message", (event) => {
  if (event.data?.type === "SKIP_WAITING") {
    self.skipWaiting();
  }
});
