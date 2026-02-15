// When navigating with #items, ensure the items card is scrolled into view after reload
(function () {
    if (window.location.hash === '#items') {
        var el = document.getElementById('items');
        if (el && typeof el.scrollIntoView === 'function') {
            el.scrollIntoView({ behavior: 'auto', block: 'start' });
        }
    }
})();
