// Keyboard shortcuts for the filter panel.
// "/" focuses the search field. Esc clicks Reset.
(function () {
    function panel() {
        return document.querySelector("#main-content .caei-control-panel");
    }

    function isTyping(target) {
        if (!target || !target.closest) return false;
        var field = target.closest("input, textarea, select, [contenteditable='true']");
        if (!field) return false;
        if (field.disabled || field.readOnly) return false;
        var type = (field.getAttribute("type") || "text").toLowerCase();
        return type !== "button" && type !== "submit" && type !== "reset" && type !== "checkbox" && type !== "radio";
    }

    function dialogOpen() {
        if (document.querySelector(".modal.show, .dropdown-menu.show")) return true;
        var nodes = document.querySelectorAll("[role='dialog'], .caei-send-overlay");
        var i, node, style;
        for (i = 0; i < nodes.length; i++) {
            node = nodes[i];
            if (node.classList.contains("d-none") || node.hidden) continue;
            style = window.getComputedStyle(node);
            if (style.display === "none" || style.visibility === "hidden") continue;
            return true;
        }
        return false;
    }

    function searchInput(root) {
        var typed = root.querySelector('input[type="search"]');
        if (typed && !typed.disabled) return typed;
        var inputs = root.querySelectorAll("input");
        var i, input, type, hint, fallback;
        for (i = 0; i < inputs.length; i++) {
            input = inputs[i];
            if (input.disabled || input.readOnly) continue;
            type = (input.getAttribute("type") || "text").toLowerCase();
            if (type !== "text" && type !== "search") continue;
            hint = ((input.getAttribute("placeholder") || "") + " " + (input.getAttribute("aria-label") || "") + " " + (input.name || "") + " " + (input.id || "")).toLowerCase();
            if (hint.indexOf("search") !== -1) return input;
            if (!fallback) fallback = input;
        }
        return fallback || null;
    }

    function resetControl(root) {
        var controls = root.querySelectorAll("a, button");
        var i, text;
        for (i = 0; i < controls.length; i++) {
            if (controls[i].disabled || controls[i].getAttribute("aria-disabled") === "true") continue;
            text = (controls[i].textContent || "").replace(/\s+/g, " ").trim().toLowerCase();
            if (text === "reset") return controls[i];
        }
        return null;
    }

    document.addEventListener("keydown", function (event) {
        if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.repeat) return;
        var root = panel();
        if (!root) return;

        if (event.key === "/") {
            if (isTyping(event.target)) return;
            var search = searchInput(root);
            if (!search) return;
            event.preventDefault();
            search.focus();
            if (search.select) search.select();
            return;
        }

        if (event.key === "Escape") {
            if (dialogOpen()) return;
            var reset = resetControl(root);
            if (!reset) return;
            event.preventDefault();
            reset.click();
        }
    });
})();
