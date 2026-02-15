(function () {
    // Prevent browser's automatic scroll restoration
    if ('scrollRestoration' in history) {
        history.scrollRestoration = 'manual';
    }

    const scrollKey = "coaValidationsScrollY";
    const form = document.getElementById("correctedCoaForm");

    // Save scroll position when clicking Next/Previous (same-page links)
    document.addEventListener("click", function (e) {
        var anchor = e.target.closest && e.target.closest("a");
        if (!anchor || !anchor.href) return;
        try {
            var url = new URL(anchor.href, window.location.origin);
            if (url.pathname !== window.location.pathname) return;
            var scrollY = window.scrollY || window.pageYOffset || document.documentElement.scrollTop || 0;
            sessionStorage.setItem(scrollKey, String(scrollY));
        } catch (_) {
        }
    });

    // Save scroll position on button click (before form submit) - use capture phase
    if (form) {
        form.addEventListener("click", function (e) {
            // Only capture for submit buttons
            const btn = e.target.closest && e.target.closest("button[type='submit']");
            if (btn || e.target.type === "submit") {
                try {
                    const scrollY = window.scrollY || window.pageYOffset || document.documentElement.scrollTop || 0;
                    sessionStorage.setItem(scrollKey, String(scrollY));
                } catch (err) {
                }
            }
        }, true); // Capture phase to catch before default action

        // Also capture on form submit as backup
        form.addEventListener("submit", function () {
            try {
                const scrollY = window.scrollY || window.pageYOffset || document.documentElement.scrollTop || 0;
                sessionStorage.setItem(scrollKey, String(scrollY));
            } catch (err) {
            }
        });
    }

    // Restore scroll position after reload
    function restoreScroll() {
        try {
            const stored = sessionStorage.getItem(scrollKey);
            if (stored !== null) {
                const y = parseInt(stored, 10);
                if (!isNaN(y) && y >= 0) {
                    sessionStorage.removeItem(scrollKey);
                    requestAnimationFrame(function() {
                        window.scrollTo({ top: y, left: 0, behavior: "auto" });
                    });
                }
            }
        } catch (err) {
        }
    }

    // Try multiple times to handle different load scenarios
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', restoreScroll);
    } else {
        restoreScroll();
    }
    
    window.addEventListener('load', function() {
        setTimeout(restoreScroll, 50);
    });
    
    setTimeout(restoreScroll, 100);
    setTimeout(restoreScroll, 250);
    setTimeout(restoreScroll, 500);
})();
