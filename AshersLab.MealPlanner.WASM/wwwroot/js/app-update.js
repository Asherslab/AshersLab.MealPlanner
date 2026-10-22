// The installed app's update path (wwwroot/service-worker.published.js). Registered from index.html, relative to
// <base href>, so the worker's scope is the app's own path - "/", or Home Assistant's ingress path.
window.mealPlannerApp = {
    /** Registers the service worker. A browser without one (or a web view that refuses it) runs the app as before. */
    register() {
        if (!('serviceWorker' in navigator)) return;
        // 'none': the browser checks the worker and the asset list it imports against the server on every check, never
        // its HTTP cache - a new build is noticed the next time the app opens online.
        navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' })
            .catch(e => console.warn('No offline copy of the app:', e?.message ?? e));
    },

    /**
     * Reloads into the newest build: asks the server for it, lets it take over (it would otherwise wait for every window
     * to close), then reloads. Without a worker, or if anything fails, a plain reload - what the app did before it had
     * one. Called when the server says this build is too old to sync (Sync/SyncLoop.cs).
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
