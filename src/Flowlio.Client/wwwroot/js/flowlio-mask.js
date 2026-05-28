// Numeric amount masking (thousands separator, comma decimals, optional sign) for
// money inputs, backed by IMask. Used by the <MoneyField> Blazor component, which
// owns the element ref and receives the parsed numeric value via a .NET callback.
window.flowlioMask = {
    attach(el, dotNetRef, opts) {
        if (!el || typeof IMask === "undefined") return false;
        const mask = IMask(el, {
            mask: Number,
            scale: 2,
            thousandsSeparator: " ",
            radix: ",",
            mapToRadix: [".", ","],
            signed: !opts || opts.signed !== false,
            normalizeZeros: true,
            padFractionalZeros: false,
        });
        el._imask = mask;
        mask.on("accept", () => {
            const raw = mask.value;
            const value = raw === "" || raw === "-" ? null : mask.typedValue;
            dotNetRef.invokeMethodAsync("OnMaskValue", value);
        });
        return true;
    },
    // Push a value from .NET into the masked input without firing the accept callback.
    set(el, value) {
        if (!el || !el._imask) return;
        el._imask.typedValue = value === null || value === undefined ? "" : Number(value);
    },
    dispose(el) {
        if (el && el._imask) {
            el._imask.destroy();
            el._imask = null;
        }
    },
};
