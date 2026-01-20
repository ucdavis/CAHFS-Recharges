(function () {
    const form = document.getElementById("batchSelectForm");
    const sel = document.getElementById("batchSelect");
    if (!form || !sel) return;

    sel.addEventListener("change", function () {
        form.submit();
    });
})();
