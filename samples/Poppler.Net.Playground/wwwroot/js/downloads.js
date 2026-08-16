const popplerNetObjectUrls = new Set();

function releasePopplerNetObjectUrl(url) {
    if (!url || !popplerNetObjectUrls.delete(url)) {
        return;
    }
    URL.revokeObjectURL(url);
}

window.popplerNetDownloads = {
    async save(fileName, contentType, streamReference) {
        const buffer = await streamReference.arrayBuffer();
        const blob = new Blob([buffer], {
            type: contentType || "application/octet-stream"
        });
        const url = URL.createObjectURL(blob);
        popplerNetObjectUrls.add(url);
        const link = document.createElement("a");
        try {
            link.href = url;
            link.download = fileName;
            link.style.display = "none";
            document.body.appendChild(link);
            link.click();
        } finally {
            link.remove();
            // Safari may not have consumed the object URL when click() returns.
            setTimeout(() => releasePopplerNetObjectUrl(url), 1000);
        }
    },

    async preview(contentType, streamReference, previousUrl) {
        const buffer = await streamReference.arrayBuffer();
        const blob = new Blob([buffer], {
            type: contentType || "application/octet-stream"
        });
        const url = URL.createObjectURL(blob);
        popplerNetObjectUrls.add(url);
        releasePopplerNetObjectUrl(previousUrl);
        return url;
    },

    release(url) {
        releasePopplerNetObjectUrl(url);
    },

    releaseAll() {
        for (const url of [...popplerNetObjectUrls]) {
            releasePopplerNetObjectUrl(url);
        }
    }
};

window.addEventListener("pagehide", () => window.popplerNetDownloads.releaseAll());
