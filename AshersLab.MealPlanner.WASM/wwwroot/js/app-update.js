// The installed app's update path (wwwroot/service-worker.published.js). Registered from index.html, relative to
// <base href>, so the worker's scope is the app's own path - "/", or Home Assistant's ingress path.
//
// A new build installs beside the running one and waits - and a reload does not end the wait, since the old page is
// still open while the new one loads. A Home Assistant tab or an installed phone app is seldom closed outright, so on
// its own a new build would wait for days. So: look for one whenever the app comes back to the foreground, and when one
// is waiting, say so (Sync/UpdateReadyBanner.razor); its Reload is update() below.
const updateCheckEveryMs = 60_000;

window.mealPlannerApp = {
    _registration: null,
    _waiting: false,
    _listener: null,
    _lastCheck: 0,

    /** Registers the service worker. A browser without one (or a web view that refuses it) runs the app as before. */
    register() {
        if (!('serviceWorker' in navigator)) return;
        // 'none': the browser checks the worker and the asset list it imports against the server on every check, never
        // its HTTP cache - a new build is noticed the next time the app opens online.
        navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' })
            .then(registration => this._watch(registration))
            .catch(e => console.warn('No offline copy of the app:', e?.message ?? e));
    },

    _watch(registration) {
        this._registration = registration;
        this._lastCheck = Date.now();
        // Only a build replacing one already running is news: the very first install has nothing to replace.
        const installed = worker => worker && navigator.serviceWorker.controller && this._ready();
        if (registration.waiting) installed(registration.waiting);
        registration.addEventListener('updatefound', () => {
            const next = registration.installing;
            next?.addEventListener('statechange', () => { if (next.state === 'installed') installed(next); });
        });
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState !== 'visible' || Date.now() - this._lastCheck < updateCheckEveryMs) return;
            this._lastCheck = Date.now();
            registration.update().catch(() => { /* offline: the next time */ });
        });
    },

    _ready() {
        if (this._waiting) return;
        this._waiting = true;
        this._listener?.invokeMethodAsync('UpdateReady');
    },

    /** Tells <paramref name="listener"/> (UpdateReady) once a new build is waiting - at once if one already is. */
    onUpdateReady(listener) {
        this._listener = listener;
        if (this._waiting) listener.invokeMethodAsync('UpdateReady');
    },

    /** The phone's share sheet for a link; without one, the clipboard. True when the share sheet opened. */
    async share(title, url) {
        if (navigator.share) {
            try { await navigator.share({ title, url }); } catch { /* closed without sending */ }
            return true;
        }
        await navigator.clipboard.writeText(url);
        return false;
    },

    /**
     * Reloads into the newest build: asks the server for it, lets it take over (it would otherwise wait for every window
     * to close), then reloads. Without a worker, or if anything fails, a plain reload - what the app did before it had
     * one. Called when the server says this build is too old to sync (Sync/SyncLoop.cs), and by the new-version banner.
     */
    async update() {
        try {
            const registration = await navigator.serviceWorker?.getRegistration();
            if (registration) {
                await registration.update();
                const next = registration.installing ?? registration.waiting;
                if (next) {
                    const ready = new Promise(resolve => {
                        navigator.serviceWorker.addEventListener('controllerchange', resolve, { once: true });
                        setTimeout(resolve, 15000);
                    });
                    if (next.state === 'installing')
                        await new Promise(resolve => next.addEventListener('statechange', () => next.state !== 'installing' && resolve()));
                    next.postMessage('skip-waiting');
                    await ready;
                }
            }
        } catch (e) {
            console.warn('Could not fetch the new build first:', e?.message ?? e);
        }
        location.reload();
    },
};
