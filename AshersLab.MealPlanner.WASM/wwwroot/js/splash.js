// The splash (index.html .boot): it stays for its few seconds even when the app is cached and renders at once, so
// opening the app is one calm moment rather than a flicker, then fades off the app Blazor has drawn underneath. Its
// bar fills over the same seconds (css/app.css --boot-hold); a slower start keeps it full until the app is there.
(() => {
    const splash = document.querySelector('.boot');
    const app = document.getElementById('app');
    if (!splash || !app) return;

    // Counted from the page's start, as the bar's fill is; read from the stylesheet so the two cannot drift.
    const hold = parseFloat(getComputedStyle(splash).getPropertyValue('--boot-hold')) || 4500;
    const held = new Promise(done => setTimeout(done, Math.max(0, hold - performance.now())));

    // Blazor's first render puts the layout into #app.
    const rendered = new Promise(done => {
        if (app.childElementCount) return done();
        const watch = new MutationObserver(() => {
            if (!app.childElementCount) return;
            watch.disconnect();
            done();
        });
        watch.observe(app, { childList: true });
    });

    Promise.all([held, rendered]).then(() => {
        splash.classList.add('boot-done');
        const remove = () => splash.remove();
        splash.addEventListener('transitionend', remove, { once: true });
        setTimeout(remove, 600);   // no transition under reduced motion, or the tab was hidden
    });
})();
