// Compact / comfortable row padding for .caei-data-table.
(function () {
    var key = "caei-table-density";

    function mode() {
        return document.documentElement.getAttribute("data-caei-density") === "compact" ? "compact" : "comfortable";
    }

    function stored() {
        try {
            return localStorage.getItem(key) === "compact" ? "compact" : "comfortable";
        } catch (e) {
            return "comfortable";
        }
    }

    function apply(next) {
        document.documentElement.setAttribute("data-caei-density", next);
        document.documentElement.style.setProperty("--table-click-padding", next === "compact" ? "0.25rem" : "0.75rem");
        var buttons = document.querySelectorAll("[data-caei-density-toggle]");
        var i;
        for (i = 0; i < buttons.length; i++) {
            buttons[i].setAttribute("aria-pressed", next === "compact" ? "true" : "false");
        }
        try {
            localStorage.setItem(key, next);
        } catch (e) { }
    }

    document.addEventListener("click", function (event) {
        var button = event.target.closest ? event.target.closest("[data-caei-density-toggle]") : null;
        if (!button) return;
        event.preventDefault();
        apply(mode() === "compact" ? "comfortable" : "compact");
    });

    document.addEventListener("caei-region-updated", function () {
        apply(mode());
    });

    apply(stored());
    document.addEventListener("DOMContentLoaded", function () {
        apply(mode());
    });
})();
