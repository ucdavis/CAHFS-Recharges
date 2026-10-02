(function () {
    // Auto-submit batch select dropdown on change, preserving scroll position
    const scrollKey = "sendToAeScrollY";

    document.addEventListener("change", function (event) {
        if (!event.target || event.target.id !== "batchSelect") return;
        var form = document.getElementById("batchSelectForm");
        if (!form) return;
        try {
            sessionStorage.setItem(scrollKey, String(window.scrollY || window.pageYOffset || 0));
        } catch (_) {
        }
        form.submit();
    });

    try {
        const stored = sessionStorage.getItem(scrollKey);
        if (stored !== null) {
            sessionStorage.removeItem(scrollKey);
            const y = parseInt(stored, 10);
            if (!isNaN(y)) {
                window.scrollTo({ top: y, left: 0, behavior: "auto" });
            }
        }
    } catch (_) {
    }

    document.addEventListener("submit", function (event) {
        if (!event.target || event.target.id !== "sendToAeForm") return;
        var overlay = document.getElementById("sendToAeOverlay");
        var modalEl = document.getElementById("confirmSendModal");
        if (!overlay) return;
        try {
            if (modalEl && window.bootstrap && bootstrap.Modal) {
                const modalInstance = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
                modalInstance.hide();
            }
        } catch (e) {
        }

        overlay.classList.remove("d-none");
    });
})();
