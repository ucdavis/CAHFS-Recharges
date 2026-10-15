// Global client-side behaviors for CAEI.
// - Auto-dismiss flash alerts after a short delay, while still allowing manual close.

(function () {
    // Auto-dismiss Bootstrap alerts after 8 seconds
    document.addEventListener("DOMContentLoaded", function () {
        var alerts = document.querySelectorAll(".alert-dismissible");
        if (!alerts || alerts.length === 0) return;

        alerts.forEach(function (el) {
            // Skip alerts that opt out via data attribute if needed in future
            if (el.hasAttribute("data-no-auto-dismiss")) return;

            setTimeout(function () {
                try {
                    // Bootstrap 5 Alert API
                    if (window.bootstrap && bootstrap.Alert) {
                        var alertInstance = bootstrap.Alert.getOrCreateInstance(el);
                        alertInstance.close();
                    } else {
                        el.classList.remove("show");
                        el.classList.add("fade");
                    }
                } catch (e) {
                }
            }, 8000); 
        });
    });
})();

// Moves the top progress bar and marks the submit button while a form post is in flight.
(function () {
    var bar = document.getElementById("global-progress-bar");
    if (!bar) return;

    function setProgress(percent) {
        bar.style.width = percent + "%";
        bar.setAttribute("aria-valuenow", String(percent));
    }

    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!form || form.nodeName !== "FORM" || e.defaultPrevented) return;
        if (form.getAttribute("target") && form.getAttribute("target") !== "_self") return;

        var button = e.submitter;
        if (button && button.classList) {
            button.classList.add("is-loading");
            button.setAttribute("aria-busy", "true");
        }

        window.setTimeout(function () {
            if (e.defaultPrevented) {
                if (button && button.classList) {
                    button.classList.remove("is-loading");
                    button.removeAttribute("aria-busy");
                }
                return;
            }

            var wait = form.querySelector(".caei-form-wait");
            if (wait) wait.hidden = false;

            if ((form.getAttribute("method") || "get").toLowerCase() === "get") {
                var table = document.querySelector(".caei-data-table");
                var body = table && !form.contains(table) ? table.querySelector("tbody") : null;
                if (body) {
                    var colCount = table.querySelectorAll("thead th").length || 5;
                    var skeletonHtml = "";
                    for (var r = 0; r < 4; r++) {
                        skeletonHtml += '<tr class="caei-skeleton-row" aria-hidden="true">';
                        for (var c = 0; c < colCount; c++)
                            skeletonHtml += '<td><div class="caei-skeleton-line"></div></td>';
                        skeletonHtml += "</tr>";
                    }
                    body.innerHTML = skeletonHtml;
                    table.setAttribute("aria-busy", "true");
                }
            }

            bar.classList.add("is-active");
            bar.setAttribute("aria-hidden", "false");
            setProgress(30);
            window.setTimeout(function () { setProgress(70); }, 1000);
            window.setTimeout(function () { setProgress(90); }, 2000);
        }, 0);
    });
})();
