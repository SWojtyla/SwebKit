// Applies the last-used theme and font size before first paint. The source of truth is the
// sidecar's user-settings.json, which only arrives once the sidecar answers — without this the
// app painted the default theme first and visibly switched a moment later. A classic (non-module)
// script in <head> runs synchronously, before the body renders. Kept in step with the
// `view-pref:boot-*` mirrors written by src/lib/stores/settings.ts and AppLayout.
(function () {
    function read(key) {
        try {
            return JSON.parse(localStorage.getItem("view-pref:" + key) || "null");
        } catch (e) {
            return null;
        }
    }
    var theme = read("boot-theme") || "dark";
    var root = document.documentElement;
    if (theme === "dark" || theme === "clay-dark") root.classList.add("dark");
    if (theme === "clay" || theme === "clay-dark") root.classList.add("clay");
    var fontSize = read("boot-font-size");
    if (fontSize) root.style.fontSize = fontSize;
})();
