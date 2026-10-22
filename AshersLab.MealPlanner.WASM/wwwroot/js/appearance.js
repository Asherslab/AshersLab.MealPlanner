// Light or dark: this device's copy of the planner's choice (Design/AppearanceState.cs keeps it with their sign-in).
// Loaded in <head>, before the stylesheets paint anything, so a phone left on dark never flashes the light while
// Blazor starts - the "Loading…" page is already the night's. Never the phone's own dark mode: the choice is theirs.
(() => {
    const themeKey = 'mealplanner.appearance';
    const unsentKey = 'mealplanner.appearance.unsent';
    // The app bar's colour, for the phone's status bar - css/app.css --band, by day and at night.
    const bar = { light: '#2F5D50', dark: '#2A5246' };

    const read = key => { try { return localStorage.getItem(key); } catch { return null; } };
    const write = (key, value) => {
        try { value === null ? localStorage.removeItem(key) : localStorage.setItem(key, value); } catch { /* private mode: this visit only */ }
    };

    function show(theme) {
        const dark = theme === 'dark';
        document.documentElement.dataset.theme = dark ? 'dark' : 'light';
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', dark ? bar.dark : bar.light);
    }

    window.mealPlannerAppearance = {
        /** What this device holds: [its choice - "light", "dark" or null - and "1" while the account does not have it yet]. */
        read: () => [read(themeKey), read(unsentKey)],
        /** Shows and keeps a choice; unsent until the account has it. */
        apply(theme, unsent) {
            write(themeKey, theme);
            write(unsentKey, unsent ? '1' : null);
            show(theme);
        },
    };

    show(read(themeKey));
})();
