// Progressive enhancement for GET filter panels.
// Apply, Reset, and Clear stay normal links and forms when this script does not run.
(function () {
    var request = null;
    var requestId = 0;

    function announce(message) {
        var live = document.getElementById("caei-filter-status");
        if (!live) {
            live = document.createElement("div");
            live.id = "caei-filter-status";
            live.className = "visually-hidden";
            live.setAttribute("aria-live", "polite");
            document.body.appendChild(live);
        }
        live.textContent = message;
    }

    function buildUrl(form) {
        var url = new URL(form.getAttribute("action") || window.location.pathname, window.location.href);
        var params = new URLSearchParams();
        new FormData(form).forEach(function (value, key) {
            params.append(key, value);
        });
        url.search = params.toString();
        url.hash = "";
        return url.toString();
    }

    function setBusy(form, busy) {
        if (!form || !form.isConnected) return;
        var button = form.querySelector('button[type="submit"], input[type="submit"]');
        if (button) {
            if (busy) {
                if (!button.hasAttribute("data-caei-label")) {
                    button.setAttribute("data-caei-label", button.innerHTML);
                }
                button.textContent = "Applying…";
                button.disabled = true;
            } else if (button.hasAttribute("data-caei-label")) {
                button.innerHTML = button.getAttribute("data-caei-label");
                button.removeAttribute("data-caei-label");
                button.disabled = false;
            }
        }
        form.removeAttribute("aria-busy");
        if (busy) form.setAttribute("aria-busy", "true");
    }

    function syncRegions(doc) {
        var seen = {};
        doc.querySelectorAll("[data-caei-region]").forEach(function (fresh) {
            var key = fresh.getAttribute("data-caei-region");
            seen[key] = true;
            var current = document.querySelector('[data-caei-region="' + CSS.escape(key) + '"]');
            var imported = document.importNode(fresh, true);
            if (current) {
                current.replaceWith(imported);
                return;
            }
            var main = document.getElementById("main-content");
            if (main) main.insertBefore(imported, main.firstChild);
        });
        document.querySelectorAll("[data-caei-region]").forEach(function (node) {
            if (!seen[node.getAttribute("data-caei-region")]) node.remove();
        });
    }

    function syncForms(doc) {
        document.querySelectorAll("form[data-caei-filter]").forEach(function (form) {
            var label = form.getAttribute("aria-label");
            if (!label) return;
            var fresh = doc.querySelector('form[data-caei-filter][aria-label="' + CSS.escape(label) + '"]');
            if (!fresh) return;
            fresh.querySelectorAll("input, select, textarea").forEach(function (field) {
                if (!field.name || field.type === "submit" || field.type === "button" || field.type === "hidden") return;
                var current = form.querySelector('[name="' + CSS.escape(field.name) + '"]');
                if (!current) return;
                if (current.type === "checkbox" || current.type === "radio") current.checked = field.checked;
                else current.value = field.value;
            });
            form.querySelectorAll("[data-caei-live]").forEach(function (node) {
                var key = node.getAttribute("data-caei-live");
                var updated = fresh.querySelector('[data-caei-live="' + CSS.escape(key) + '"]');
                if (updated && node.tagName === "A") node.setAttribute("href", updated.getAttribute("href"));
            });
        });
    }

    function load(url, form, push) {
        var id = ++requestId;
        if (request) request.abort();
        request = new AbortController();
        setBusy(form, true);
        document.querySelectorAll("[data-caei-region]").forEach(function (node) {
            node.setAttribute("aria-busy", "true");
        });
        announce("Updating results");

        fetch(url, {
            method: "GET",
            credentials: "same-origin",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            signal: request.signal
        }).then(function (response) {
            if (!response.ok) throw new Error("Request failed");
            return response.text();
        }).then(function (html) {
            var doc = new DOMParser().parseFromString(html, "text/html");
            if (!doc.querySelector("form[data-caei-filter]") && !doc.querySelector("[data-caei-region]")) {
                window.location.assign(url);
                return;
            }
            syncRegions(doc);
            syncForms(doc);
            var title = doc.querySelector("title");
            if (title && title.textContent) document.title = title.textContent;
            if (push !== false && window.location.href !== url) {
                history.pushState({ caeiFilter: 1 }, "", url);
            }
            scrollToHash(url);
            announce("Results updated");
            document.dispatchEvent(new Event("caei-region-updated"));
        }).catch(function (error) {
            if (error && error.name === "AbortError") return;
            window.location.assign(url);
        }).finally(function () {
            if (id !== requestId) return;
            setBusy(form, false);
            document.querySelectorAll("[data-caei-region]").forEach(function (node) {
                node.removeAttribute("aria-busy");
            });
        });
    }

    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!form || !form.matches || !form.matches("form[data-caei-filter]")) return;
        if ((form.getAttribute("method") || "get").toLowerCase() !== "get") return;
        event.preventDefault();
        load(buildUrl(form), form, true);
    });

    function scrollToHash(url) {
        var hash = "";
        try {
            hash = new URL(url, window.location.href).hash;
        } catch (e) {
            return;
        }
        if (!hash || hash.length < 2) return;
        var el = document.getElementById(decodeURIComponent(hash.slice(1)));
        if (el && el.scrollIntoView) el.scrollIntoView({ behavior: "auto", block: "start" });
    }

    function isSamePageViewLink(link) {
        if (!link || !link.href) return false;
        if (link.hasAttribute("data-caei-filter-nav")) return true;
        if (!link.closest("[data-caei-region]")) return false;
        if (!document.querySelector("form[data-caei-filter]")) return false;
        var url;
        try {
            url = new URL(link.href, window.location.href);
        } catch (e) {
            return false;
        }
        if (url.origin !== window.location.origin) return false;
        if (url.pathname !== window.location.pathname) return false;
        if (url.searchParams.has("handler")) return false;
        return true;
    }

    document.addEventListener("click", function (event) {
        var link = event.target.closest ? event.target.closest("a") : null;
        if (!isSamePageViewLink(link)) return;
        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) return;
        event.preventDefault();
        var form = link.closest("form[data-caei-filter]") || null;
        load(link.href, form, true);
    });

    window.addEventListener("popstate", function () {
        if (!document.querySelector("form[data-caei-filter]")) return;
        load(window.location.href, document.querySelector("form[data-caei-filter]"), false);
    });
})();
