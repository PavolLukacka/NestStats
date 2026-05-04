const CACHE_NAME = "neststats-static-v3";
const STATIC_ASSETS = [
  "/manifest.webmanifest",
  "/favicon.ico",
  "/logo.png",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
  "/css/site.css",
  "/css/product-surfaces.css",
  "/js/site.js"
];

self.addEventListener("install", event => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(STATIC_ASSETS))
      .catch(() => undefined)
  );
  self.skipWaiting();
});

self.addEventListener("activate", event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("message", event => {
  if (event.data?.type === "SKIP_WAITING") {
    self.skipWaiting();
  }
});

self.addEventListener("fetch", event => {
  const request = event.request;
  const url = new URL(request.url);

  if (request.method !== "GET" || url.origin !== self.location.origin) {
    return;
  }

  if (request.mode === "navigate" ||
      url.searchParams.has("handler") ||
      url.searchParams.has("LoadToken") ||
      url.searchParams.has("QuietRefresh") ||
      url.pathname.startsWith("/Identity/") ||
      url.pathname.startsWith("/Account/") ||
      url.pathname.startsWith("/signin-google") ||
      url.pathname.startsWith("/signin-facebook")) {
    event.respondWith(fetch(request));
    return;
  }

  if (url.pathname.startsWith("/api/") ||
      url.pathname.startsWith("/Identity/") ||
      url.pathname.startsWith("/Account/") ||
      url.pathname.startsWith("/startup-status") ||
      url.pathname.startsWith("/firmwares/download")) {
    return;
  }

  event.respondWith(
    caches.match(request).then(cached => {
      if (cached) return cached;

      return fetch(request).then(response => {
        if (!response || response.status !== 200 || response.type !== "basic") {
          return response;
        }

        const copy = response.clone();
        caches.open(CACHE_NAME).then(cache => cache.put(request, copy));
        return response;
      });
    })
  );
});
