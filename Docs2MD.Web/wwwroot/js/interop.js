// Triggers a browser download from base64 data
window.downloadBase64 = function (filename, mimeType, base64) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const blob = new Blob([bytes], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// Sets the srcdoc of an iframe element
window.setIframeSrcdoc = function (iframe, html) {
    if (iframe) iframe.srcdoc = html;
};

// Triggers browser print dialog (for PDF export)
window.printPage = function () { window.print(); };

// Wires up native drag-and-drop on the drop zone so OS file drops work.
// Finds the hidden <input type="file"> inside inputContainerId and
// programmatically assigns the dropped file to it, then fires a change event
// so Blazor's InputFile component picks it up.
window.setupDropZone = function (dropZoneId, inputContainerId) {
    const dropZone = document.getElementById(dropZoneId);
    const inputEl  = document.querySelector('#' + inputContainerId + ' input[type=file]');
    if (!dropZone || !inputEl) return;

    dropZone.addEventListener('dragover', e => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.add('dragging');
    });

    dropZone.addEventListener('dragleave', e => {
        e.preventDefault();
        dropZone.classList.remove('dragging');
    });

    dropZone.addEventListener('drop', e => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.remove('dragging');

        const files = e.dataTransfer?.files;
        if (!files || files.length === 0) return;

        // Transfer dropped file into the hidden <input> so Blazor sees it
        const dt = new DataTransfer();
        dt.items.add(files[0]);
        inputEl.files = dt.files;
        inputEl.dispatchEvent(new Event('change', { bubbles: true }));
    });
};
