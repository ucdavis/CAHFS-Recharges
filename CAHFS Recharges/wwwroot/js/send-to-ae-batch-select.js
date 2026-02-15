(function () {
    // Auto-submit batch select dropdown on change, preserving scroll position
    const form = document.getElementById("batchSelectForm");
    const sel = document.getElementById("batchSelect");
    const scrollKey = "sendToAeScrollY";

    if (form && sel) {
        sel.addEventListener("change", function () {
            try {
                sessionStorage.setItem(scrollKey, String(window.scrollY || window.pageYOffset || 0));
            } catch (_) {
            }
            form.submit();
        });
    }

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

    // When the user confirms Send to AE in the modal, show a full-screen
    // "sending" overlay so they know something is happening.
    const sendForm = document.getElementById("sendToAeForm");
    const overlay = document.getElementById("sendToAeOverlay");
    const modalEl = document.getElementById("confirmSendModal");

    if (sendForm && overlay) {
        sendForm.addEventListener("submit", function () {
            try {
                if (modalEl && window.bootstrap && bootstrap.Modal) {
                    const modalInstance = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
                    modalInstance.hide();
                }
            } catch (e) {
            }

            overlay.classList.remove("d-none");
        });
    }
})();
