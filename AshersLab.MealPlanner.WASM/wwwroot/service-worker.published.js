// The published app's service worker: what lets the installed app open with no signal (docs/modules/sync/offline-sync.md
// #where-offline-works). Every file of this build is cached when it installs, under a cache named for the build, and
// served from there; a page load is answered with the cached shell. Data is never here - it is the browser's SQLite,
// and the sync calls (gRPC-Web POSTs) always go to the network.
//
// The base path is the worker's own scope, never "/": under Home Assistant ingress the app lives at
// /api/hassio_ingress/<token>/. For the same reason the shell is fetched as the server serves it - with <base href>
// rewritten for this path (Grpc/Features/Ingress/AppShell.cs) - not checked against the static index.html's hash.
//
// A new build installs beside the old and waits: it takes over when every window of the app has closed, or at once when
// the app asks (a "skip-waiting" message - sent when the server says this build is too old to sync, js/app-update.js).
self.importScripts('./service-worker-assets.js');

const cachePrefix = 'mealplanner-offline-';
const cacheName = `${cachePrefix}${self.assetsManifest.version}`;
const scope = new URL(self.registration.scope);
const shellUrl = new URL('index.html', scope).href;

// What is cached. The OCR engine (lib/tesseract, about 7 MB) is not: a Costco receipt is read at home, online, and
// every phone would otherwise download it on install whether it ever reads one or not.
const include = [/\.dll$/, /\.pdb$/, /\.wasm$/, /\.js$/, /\.mjs$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.svg$/, /\.ico$/,
                 /\.webmanifest$/, /\.dat$/, /\.blat$/];
const exclude = [/^service-worker\.js$/, /^index\.html$/, /^lib\/tesseract\//];

const assets = self.assetsManifest.assets
    .filter(a => include.some(p => p.test(a.url)) && !exclude.some(p => p.test(a.url)));
const assetUrls = new Set(assets.map(a => new URL(a.url, scope).href));

self.addEventListener('install', event => event.waitUntil((async () => {
    const cache = await caches.open(cacheName);
    await cache.addAll(assets.map(a => new Request(new URL(a.url, scope).href, { integrity: a.hash, cache: 'no-cache' })));
    await cache.put(shellUrl, await fetch(shellUrl, { cache: 'no-cache' }));
})()));

self.addEventListener('activate', event => event.waitUntil((async () => {
    for (const key of await caches.keys())
        if (key.startsWith(cachePrefix) && key !== cacheName)
            await caches.delete(key);
    await self.clients.claim();
})()));

self.addEventListener('message', event => { if (event.data === 'skip-waiting') self.skipWaiting(); });

// How long a page load waits for the network before opening the cached app: a shop's one bar of signal must not mean a
// blank screen.
const pageTimeoutMs = 3000;

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.method !== 'GET' || !request.url.startsWith(scope.href))
        return;
    const page = request.mode === 'navigate' && !assetUrls.has(request.url);
    if (page)
        event.respondWith(openPage(request));
    else if (assetUrls.has(request.url))
        event.respondWith((async () => (await (await caches.open(cacheName)).match(request)) ?? fetch(request))());
    // Anything else (healthz, the OCR engine) is not ours: the network, as without a worker.
});

/**
 * A page load - the app, or a deep link the WASM router answers. The network first, briefly, for one reason: in front of
 * the app there may be a sign-in (docs/modules/infrastructure/deployment.md), and an expired session answers with a
 * redirect to its login page - which the cached app would hide forever. So a redirect or a 401 is shown as it came. Any
 * other answer, no answer in time, or no network at all opens the cached shell - the one that matches the cached build,
 * even when the server has a newer one (that arrives as a new worker).
 */
async function openPage(request) {
    const cached = await (await caches.open(cacheName)).match(shellUrl);
    try {
        const response = await Promise.race([
            fetch(request),
            new Promise((_, reject) => setTimeout(() => reject(new Error('slow')), pageTimeoutMs)),
        ]);
        if (response.type === 'opaqueredirect' || response.redirected || response.status === 401)
            return response;
        return cached ?? response;
    } catch {
        return cached ?? Response.error();
    }
}
