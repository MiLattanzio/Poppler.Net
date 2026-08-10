window.popplerNetDownloads = {
    async save(fileName, contentType, streamReference) {
        const buffer = await streamReference.arrayBuffer();
        const blob = new Blob([buffer], {
            type: contentType || "application/octet-stream"
        });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        link.style.display = "none";
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    }
};
