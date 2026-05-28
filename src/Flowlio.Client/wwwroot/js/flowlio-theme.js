// Applies the user's theme choice to <html data-flowlio-theme>, which our CSS
// reads to override `prefers-color-scheme`. Runs synchronously in <head> before
// Blazor boots so the first paint matches the chosen theme.
(function () {
    const KEY = "flowlio-app-theme";
    const VALID = ["system", "light", "dark"];

    function apply(value) {
        const v = VALID.includes(value) ? value : "system";
        if (v === "system") {
            document.documentElement.removeAttribute("data-flowlio-theme");
        } else {
            document.documentElement.setAttribute("data-flowlio-theme", v);
        }
    }

    let stored = "system";
    try { stored = localStorage.getItem(KEY) || "system"; } catch { /* storage blocked */ }
    apply(stored);

    window.flowlioTheme = {
        get() {
            try { return localStorage.getItem(KEY) || "system"; } catch { return "system"; }
        },
        set(value) {
            const v = VALID.includes(value) ? value : "system";
            try { localStorage.setItem(KEY, v); } catch { /* storage blocked */ }
            apply(v);
        },
        // Resolves "system" to the actual light/dark choice, for components (e.g. charts)
        // that need a concrete mode rather than following CSS.
        effective() {
            const v = this.get();
            if (v === "light" || v === "dark") return v;
            const prefersDark = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
            return prefersDark ? "dark" : "light";
        }
    };
})();
