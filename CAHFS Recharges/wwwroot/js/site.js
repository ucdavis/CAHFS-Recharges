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
