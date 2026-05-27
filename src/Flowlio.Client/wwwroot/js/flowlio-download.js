// Triggers a browser download from a base64-encoded byte array generated server-side.
// Used by audit log CSV export and any other binary download that needs to go through
// the authenticated HttpClient rather than a direct <a href> navigation.
window.flowlioDownload = {
    fromBytes(filename, base64, mime) {
        const binary = atob(base64);
        const len = binary.length;
        const bytes = new Uint8Array(len);
        for (let i = 0; i < len; i++) bytes[i] = binary.charCodeAt(i);
        const blob = new Blob([bytes], { type: mime || "application/octet-stream" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
};
