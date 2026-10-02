// Replace the browser's validation bubble with the gold inline error.
(function () {
    function icon() {
        var svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("xmlns", "http://www.w3.org/2000/svg");
        svg.setAttribute("width", "16");
        svg.setAttribute("height", "16");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("fill", "none");
        svg.setAttribute("stroke", "currentColor");
        svg.setAttribute("stroke-width", "2");
        svg.setAttribute("aria-hidden", "true");
        var circle = document.createElementNS("http://www.w3.org/2000/svg", "circle");
        circle.setAttribute("cx", "12");
        circle.setAttribute("cy", "12");
        circle.setAttribute("r", "10");
        var line1 = document.createElementNS("http://www.w3.org/2000/svg", "line");
        line1.setAttribute("x1", "12");
        line1.setAttribute("y1", "8");
        line1.setAttribute("x2", "12");
        line1.setAttribute("y2", "12");
        var line2 = document.createElementNS("http://www.w3.org/2000/svg", "line");
        line2.setAttribute("x1", "12");
        line2.setAttribute("y1", "16");
        line2.setAttribute("x2", "12.01");
        line2.setAttribute("y2", "16");
        svg.appendChild(circle);
        svg.appendChild(line1);
        svg.appendChild(line2);
        return svg;
    }

    function hostFor(field) {
        return field.closest(".caei-field") || field.parentElement;
    }

    function clearError(field) {
        if (!field || !field.classList) return;
        field.classList.remove("caei-field-invalid");
        field.removeAttribute("aria-invalid");
        var host = hostFor(field);
        if (!host) return;
        var error = host.querySelector(".caei-float-error[data-caei-client]");
        if (error) error.remove();
    }

    function showError(field, message) {
        var host = field.parentElement;
        if (!host) return;
        host.classList.add("caei-field");
        field.classList.add("caei-field-invalid");
        field.setAttribute("aria-invalid", "true");
        var error = host.querySelector(".caei-float-error[data-caei-client]");
        if (!error) {
            error = document.createElement("div");
            error.className = "caei-float-error";
            error.setAttribute("data-caei-client", "true");
            error.setAttribute("role", "alert");
            host.appendChild(error);
        }
        error.replaceChildren(icon(), document.createTextNode(message));
    }

    document.addEventListener("invalid", function (event) {
        var field = event.target;
        if (!field || !field.matches || !field.matches("input, select, textarea")) return;
        event.preventDefault();
        showError(field, field.validationMessage || "Check this value.");
    }, true);

    document.addEventListener("input", function (event) {
        clearError(event.target);
    });

    document.addEventListener("change", function (event) {
        var field = event.target;
        if (field && field.validity && field.validity.valid) clearError(field);
    });
})();
