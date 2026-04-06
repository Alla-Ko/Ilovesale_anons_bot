(function () {
    function hasType(dt, name) {
        return dt && dt.types && Array.prototype.indexOf.call(dt.types, name) >= 0;
    }

    const container = document.getElementById('collage-rows');
    const tpl = document.getElementById('tpl-collage-row');
    const form = document.getElementById('announcement-form');
    const btnAdd = document.getElementById('btn-add-rows');
    const addCount = document.getElementById('add-row-count');
    const bulkInput = document.getElementById('bulk-media-input');
    const btnBulk = document.getElementById('btn-bulk-media');
    if (!container || !tpl || !form) return;

    var maxRows = 60;

    const DND_TYPE = 'application/x-announcement-collage';

    function cloneRow() {
        return tpl.content.firstElementChild.cloneNode(true);
    }

    function setPreview(row, url, isVideo) {
        const el = row.querySelector('[data-dropzone] .preview');
        if (!url) {
            el.innerHTML = '';
            return;
        }
        if (isVideo || /\.(mp4|webm|mov)(\?|$)/i.test(url))
            el.innerHTML = '<video src="' + url.replace(/"/g, '&quot;') + '" controls style="max-width:100%;max-height:180px"></video>';
        else
            el.innerHTML = '<img src="' + url.replace(/"/g, '&quot;') + '" alt="" style="max-width:100%;max-height:180px" />';
    }

    function wireRow(row) {
        const drop = row.querySelector('[data-dropzone]');
        const fileInput = row.querySelector('.file-input');
        const keepInput = row.querySelector('[data-field="keep"]');
        const idInput = row.querySelector('[data-field="id"]');
        const btnClear = row.querySelector('.btn-clear-media');
        const btnRemove = row.querySelector('.btn-remove-row');
        const btnUp = row.querySelector('.btn-move-up');
        const btnDown = row.querySelector('.btn-move-down');
        const handle = row.querySelector('.drag-handle');

        function syncClearVisibility() {
            const hasKeep = keepInput.value && keepInput.value.length > 0;
            const hasFile = fileInput.files && fileInput.files.length > 0;
            btnClear.hidden = !(hasKeep || hasFile);
        }

        fileInput.addEventListener('change', function () {
            keepInput.value = '';
            if (fileInput.files && fileInput.files[0]) {
                const f = fileInput.files[0];
                const u = URL.createObjectURL(f);
                setPreview(row, u, f.type && f.type.indexOf('video') === 0);
            } else {
                setPreview(row, keepInput.value, false);
            }
            syncClearVisibility();
        });

        btnClear.addEventListener('click', function () {
            fileInput.value = '';
            keepInput.value = '';
            setPreview(row, '');
            syncClearVisibility();
        });

        btnRemove.addEventListener('click', function () {
            row.remove();
        });

        function moveRow(delta) {
            const i = Array.prototype.indexOf.call(container.children, row);
            if (i < 0) return;
            const j = i + delta;
            if (j < 0 || j >= container.children.length) return;
            const ref = delta > 0 ? container.children[j].nextSibling : container.children[j];
            container.insertBefore(row, ref);
        }

        if (btnUp) btnUp.addEventListener('click', function () { moveRow(-1); });
        if (btnDown) btnDown.addEventListener('click', function () { moveRow(1); });

        ['dragenter', 'dragover'].forEach(function (ev) {
            drop.addEventListener(ev, function (e) {
                e.preventDefault();
                e.stopPropagation();
            });
        });
        drop.addEventListener('drop', function (e) {
            e.preventDefault();
            e.stopPropagation();
            if (!e.dataTransfer || !e.dataTransfer.files || !e.dataTransfer.files[0]) return;
            fileInput.files = e.dataTransfer.files;
            fileInput.dispatchEvent(new Event('change'));
        });

        drop.addEventListener('paste', function (e) {
            const items = e.clipboardData && e.clipboardData.items;
            if (!items) return;
            for (let i = 0; i < items.length; i++) {
                if (items[i].kind === 'file' && items[i].type.indexOf('image') === 0) {
                    const f = items[i].getAsFile();
                    if (f) {
                        const dt = new DataTransfer();
                        dt.items.add(f);
                        fileInput.files = dt.files;
                        fileInput.dispatchEvent(new Event('change'));
                    }
                    e.preventDefault();
                    break;
                }
            }
        });

        row.querySelectorAll('.btn-paste-cap').forEach(function (btn) {
            btn.addEventListener('click', async function () {
                const which = btn.getAttribute('data-cap');
                const ta = which === '1' ? row.querySelector('.cap1') : row.querySelector('.cap2');
                try {
                    const text = await navigator.clipboard.readText();
                    ta.value = (ta.value ? ta.value + '\n' : '') + text;
                } catch (err) {
                    alert('Немає доступу до буфера. Вставте вручну (Ctrl+V) у полі підпису.');
                }
            });
        });

        if (keepInput.value) {
            var ku = keepInput.value;
            var isVid = /\.(mp4|webm|mov)(\?|$)/i.test(ku) || ku.toLowerCase().indexOf('video') >= 0;
            setPreview(row, ku, isVid);
            syncClearVisibility();
        } else {
            syncClearVisibility();
        }

        if (handle) {
            handle.setAttribute('draggable', 'true');
            handle.addEventListener('dragstart', function (e) {
                const idx = Array.prototype.indexOf.call(container.children, row);
                e.dataTransfer.setData(DND_TYPE, String(idx));
                e.dataTransfer.effectAllowed = 'move';
                row.classList.add('opacity-50');
            });
            handle.addEventListener('dragend', function () {
                row.classList.remove('opacity-50');
            });
        }

        row.addEventListener('dragover', function (e) {
            if (hasType(e.dataTransfer, 'Files')) return;
            if (!hasType(e.dataTransfer, DND_TYPE)) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
        });

        row.addEventListener('drop', function (e) {
            if (hasType(e.dataTransfer, 'Files')) return;
            const fromStr = e.dataTransfer.getData(DND_TYPE);
            if (!fromStr) return;
            e.preventDefault();
            const from = parseInt(fromStr, 10);
            const to = Array.prototype.indexOf.call(container.children, row);
            if (isNaN(from) || from === to) return;
            const moved = container.children[from];
            if (!moved) return;
            if (from < to)
                container.insertBefore(moved, row.nextSibling);
            else
                container.insertBefore(moved, row);
        });
    }

    function addEmptyRows(n) {
        var current = container.querySelectorAll('.collage-row').length;
        for (let i = 0; i < n; i++) {
            if (current + i >= maxRows) break;
            const row = cloneRow();
            container.appendChild(row);
            wireRow(row);
        }
    }

    function addRowWithFile(file) {
        var current = container.querySelectorAll('.collage-row').length;
        if (current >= maxRows) return;
        const row = cloneRow();
        container.appendChild(row);
        wireRow(row);
        const fileInput = row.querySelector('.file-input');
        const keepInput = row.querySelector('[data-field="keep"]');
        keepInput.value = '';
        const dt = new DataTransfer();
        dt.items.add(file);
        fileInput.files = dt.files;
        fileInput.dispatchEvent(new Event('change'));
    }

    if (window.__initialCollages && window.__initialCollages.length) {
        window.__initialCollages.forEach(function (c) {
            const row = cloneRow();
            const idInput = row.querySelector('[data-field="id"]');
            const keepInput = row.querySelector('[data-field="keep"]');
            if (c.id != null && c.id !== '') idInput.value = String(c.id);
            if (c.keep) keepInput.value = c.keep;
            row.querySelector('.cap1').value = c.cap1 || '';
            row.querySelector('.cap2').value = c.cap2 || '';
            container.appendChild(row);
            wireRow(row);
        });
    }

    btnAdd.addEventListener('click', function () {
        const n = parseInt(addCount.value, 10) || 1;
        addEmptyRows(Math.min(maxRows, Math.max(1, n)));
    });

    if (btnBulk && bulkInput) {
        btnBulk.addEventListener('click', function () {
            bulkInput.value = '';
            bulkInput.click();
        });
        bulkInput.addEventListener('change', function () {
            if (!bulkInput.files || !bulkInput.files.length) return;
            var files = Array.prototype.slice.call(bulkInput.files);
            files.forEach(function (f) {
                addRowWithFile(f);
            });
            bulkInput.value = '';
        });
    }

    form.addEventListener('submit', function () {
        const rows = container.querySelectorAll('.collage-row');
        rows.forEach(function (row, i) {
            const idInput = row.querySelector('[data-field="id"]');
            const keepInput = row.querySelector('[data-field="keep"]');
            const fileInput = row.querySelector('.file-input');
            const cap1 = row.querySelector('.cap1');
            const cap2 = row.querySelector('.cap2');

            idInput.removeAttribute('name');
            if (idInput.value)
                idInput.name = 'Input.Collages[' + i + '].Id';

            keepInput.name = 'Input.Collages[' + i + '].KeepMediaUrl';

            fileInput.removeAttribute('name');
            if (fileInput.files && fileInput.files.length > 0)
                fileInput.name = 'Input.Collages[' + i + '].MediaFile';

            cap1.name = 'Input.Collages[' + i + '].Caption1';
            cap2.name = 'Input.Collages[' + i + '].Caption2';
        });
    });
})();
