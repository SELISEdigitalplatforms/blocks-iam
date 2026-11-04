(function () {
  try {
    var stored = localStorage.getItem("theme") || "system";
    var prefersDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
    var isDark = stored === "dark" || (stored === "system" && prefersDark);
    if (isDark) document.documentElement.classList.add("dark");
  } catch (_) {}
})();
