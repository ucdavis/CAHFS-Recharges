(function () {
    // Scroll expanded row into view after reload (avoid jump to top)
    var hash = window.location.hash;
    if (hash && hash.indexOf('batch-') === 1) {
        var id = hash.slice(1);
        var el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: 'auto', block: 'nearest' });
    }

    // Copy Debug Summary / Download Payload button handler
    document.querySelectorAll('.copy-debug-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var button = this;
            var debugText = button.getAttribute('data-debug');
            navigator.clipboard.writeText(debugText).then(function () {
                var originalHtml = button.innerHTML;
                button.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="me-2"><polyline points="20 6 9 17 4 12"/></svg> Copied!';
                button.classList.add('btn-success');
                button.classList.remove('btn-outline-secondary');
                setTimeout(function () {
                    button.innerHTML = originalHtml;
                    button.classList.remove('btn-success');
                    button.classList.add('btn-outline-secondary');
                }, 1500);
            }).catch(function (err) {
                alert('Failed to copy: ' + err);
            });
        });
    });
})();
