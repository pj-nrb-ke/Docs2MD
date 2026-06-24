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

// Reads text from the system clipboard (requires HTTPS or localhost)
window.readClipboardText = async function () {
    return await navigator.clipboard.readText();
};

// Writes HTML + plain-text to clipboard so email clients (Outlook, eM Client) paste with formatting
window.copyHtmlToClipboard = async function (html, plain) {
    const item = new ClipboardItem({
        'text/html':  new Blob([html],   { type: 'text/html' }),
        'text/plain': new Blob([plain || ''], { type: 'text/plain' })
    });
    await navigator.clipboard.write([item]);
};

// Intercepts copy events on the preview element so selected text carries HTML formatting.
// Guards with a data attribute so the listener is only added once per element instance.
window.enhancePreviewCopy = function (element) {
    if (!element || element.dataset.copyEnhanced) return;
    element.dataset.copyEnhanced = '1';
    element.addEventListener('copy', function (e) {
        const sel = window.getSelection();
        if (!sel || sel.isCollapsed) return;
        const range = sel.getRangeAt(0);
        const div = document.createElement('div');
        div.appendChild(range.cloneContents());
        e.clipboardData.setData('text/html', div.innerHTML);
        e.clipboardData.setData('text/plain', sel.toString());
        e.preventDefault();
    });
};

// Copies the current text selection from the preview as rich HTML or plain text.
// Returns true if there was a selection, false if not (so Blazor can show a hint).
window.copySelectedToClipboard = async function (rich) {
    const sel = window.getSelection();
    if (!sel || sel.isCollapsed) return false;
    const range = sel.getRangeAt(0);
    const wrapper = document.createElement('div');
    wrapper.appendChild(range.cloneContents());
    const html = wrapper.innerHTML;
    const text = sel.toString();
    if (rich) {
        await navigator.clipboard.write([new ClipboardItem({
            'text/html':  new Blob([html], { type: 'text/html' }),
            'text/plain': new Blob([text], { type: 'text/plain' })
        })]);
    } else {
        await navigator.clipboard.writeText(text);
    }
    return true;
};

// Attaches a right-click context menu to the preview element.
// Guards with a data attribute so setup only runs once per element instance.
window.setupPreviewContextMenu = function (element) {
    if (!element || element.dataset.ctxMenuSetup) return;
    element.dataset.ctxMenuSetup = '1';

    const menu = document.createElement('div');
    menu.style.cssText = [
        'position:fixed', 'display:none', 'z-index:9999',
        'background:#fff', 'border:1px solid #dce3ef', 'border-radius:6px',
        'box-shadow:0 4px 16px rgba(27,53,96,0.15)',
        'font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif',
        'font-size:0.875rem', 'min-width:190px', 'padding:4px 0', 'user-select:none'
    ].join(';');
    document.body.appendChild(menu);

    function addItem(label, fn) {
        const item = document.createElement('div');
        item.textContent = label;
        item.style.cssText = 'padding:7px 16px;cursor:pointer;color:#1b3560;';
        item.addEventListener('mouseenter', () => item.style.background = '#f4f7fb');
        item.addEventListener('mouseleave', () => item.style.background = '');
        item.addEventListener('mousedown',  e  => { e.preventDefault(); hideMenu(); fn(); });
        menu.appendChild(item);
    }

    function hideMenu() { menu.style.display = 'none'; }

    addItem('📋  Copy as Rich Text',  () => window.copySelectedToClipboard(true));
    addItem('📄  Copy as Plain Text', () => window.copySelectedToClipboard(false));

    document.addEventListener('mousedown', e => { if (!menu.contains(e.target)) hideMenu(); });
    document.addEventListener('keydown',   e => { if (e.key === 'Escape') hideMenu(); });
    document.addEventListener('scroll',    hideMenu, true);

    element.addEventListener('contextmenu', e => {
        e.preventDefault();
        menu.style.display = 'block';
        let x = e.clientX, y = e.clientY;
        if (x + menu.offsetWidth  > window.innerWidth)  x = window.innerWidth  - menu.offsetWidth  - 8;
        if (y + menu.offsetHeight > window.innerHeight) y = window.innerHeight - menu.offsetHeight - 8;
        menu.style.left = x + 'px';
        menu.style.top  = y + 'px';
    });
};

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
