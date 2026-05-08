// Blazor PWA service worker — production build
// The build process replaces the asset list with hashed filenames.

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with version after publishing
const assetsManifest = self.assetsManifest;
const assetUrls = assetsManifest.assets
    .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
    .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
    .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

async function onInstall(event) {
    self.skipWaiting();
    const assetsCache = await caches.open(cacheName);
    await assetsCache.addAll(assetUrls);
    await assetsCache.add('/offline.html');
}

async function onActivate(event) {
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    if (event.request.method !== 'GET') return fetch(event.request);

    const isNavigate = event.request.mode === 'navigate';
    const cache = await caches.open(cacheName);

    if (isNavigate) {
        // Try network first for navigation so we always get the freshest index.html,
        // fall back to offline page if the server is unreachable.
        try {
            return await fetch(event.request);
        } catch {
            return await cache.match('offline.html') || Response.error();
        }
    }

    // For assets: cache first, network fallback
    const cachedResponse = await cache.match(event.request);
    return cachedResponse || fetch(event.request);
}
