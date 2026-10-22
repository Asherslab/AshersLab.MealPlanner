// In development: always the network, never a cache - so a rebuild is what the next reload shows. The published build
// replaces this file with service-worker.published.js (the csproj's ServiceWorker item); see
// docs/modules/sync/offline-sync.md#where-offline-works.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('message', event => { if (event.data === 'skip-waiting') self.skipWaiting(); });
self.addEventListener('fetch', () => { });
