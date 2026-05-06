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

    const titleInput = form.querySelector('[name="Input.Title"]');
    const countrySelect = form.querySelector('[name="Input.Country"]');
    const announcementIdInput = form.querySelector('[name="Input.Id"]');
    const antiForgeryInput = form.querySelector('input[name="__RequestVerificationToken"]');
    const announcementId = announcementIdInput ? announcementIdInput.value : '';
    const antiForgeryToken = antiForgeryInput ? antiForgeryInput.value : '';
    const leaveLink = document.querySelector('a[data-leave-edit]');

    var maxRows = 200;
    var pendingRequests = 0;
    var hasUnsavedChanges = false;

    const DND_TYPE = 'application/x-announcement-collage';

    function cloneRow() {
        return tpl.content.firstElementChild.cloneNode(true);
    }

    function setPreview(row, url, isVideo) {
        const el = row.querySelector('[data-dropzone] .preview');
        if (!url) {
            el.innerHTML = '';
            row.dataset.previewUrl = '';
            return;
        }
        row.dataset.previewUrl = url;
        if (isVideo || /\.(mp4|webm|mov)(\?|$)/i.test(url))
            el.innerHTML = '<video src="' + url.replace(/"/g, '&quot;') + '" controls style="max-width:100%;max-height:180px"></video>';
        else
            el.innerHTML = '<img src="' + url.replace(/"/g, '&quot;') + '" alt="" style="max-width:100%;max-height:180px" />';
    }

    function setRowStatus(row, text, isError) {
        var indicator = row.querySelector('.row-sync-indicator');
        var state = 'idle';
        if (isError) {
            state = 'error';
        } else if (/Завантаження|Збереження/.test(text || '')) {
            state = 'saving';
        } else if (/Збережено|Видалено/.test(text || '')) {
            state = 'saved';
        } else if (/Не збережено/.test(text || '')) {
            state = 'dirty';
        }

        if (indicator) {
            indicator.classList.remove('text-muted', 'text-success', 'text-danger');
            if (state === 'saving') {
                indicator.textContent = '⏳';
                indicator.classList.add('text-muted');
                indicator.title = 'Зберігаємо...';
            } else if (state === 'saved') {
                indicator.textContent = '✓';
                indicator.classList.add('text-success');
                indicator.title = 'Синхронізовано';
            } else if (state === 'error') {
                indicator.textContent = '⚠';
                indicator.classList.add('text-danger');
                indicator.title = 'Помилка збереження';
            } else {
                indicator.textContent = '○';
                indicator.classList.add('text-muted');
                indicator.title = 'Не збережено';
            }
        }

        var status = row.querySelector('.row-save-status');
        if (!status) {
            status = document.createElement('div');
            status.className = 'row-save-status small mt-1';
            row.querySelector('.card-body').appendChild(status);
        }
        status.textContent = text || '';
        status.classList.toggle('text-danger', !!isError);
        status.classList.toggle('text-muted', !isError);
    }

    function getRowOrder(row) {
        return Math.max(0, Array.prototype.indexOf.call(container.children, row));
    }

    function appendAntiForgery(formData) {
        if (antiForgeryToken)
            formData.append('__RequestVerificationToken', antiForgeryToken);
    }

    async function postForm(handler, formData) {
        appendAntiForgery(formData);
        pendingRequests++;
        try {
            const response = await fetch(window.location.pathname + '?handler=' + encodeURIComponent(handler) + '&id=' + encodeURIComponent(announcementId), {
                method: 'POST',
                body: formData,
                credentials: 'same-origin'
            });
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return await response.json();
        } finally {
            pendingRequests = Math.max(0, pendingRequests - 1);
        }
    }

    async function uploadRowMedia(row, file) {
        const cap1 = row.querySelector('.cap1');
        const cap2 = row.querySelector('.cap2');
        const idInput = row.querySelector('[data-field="id"]');
        const keepInput = row.querySelector('[data-field="keep"]');
        const fileInput = row.querySelector('.file-input');

        setRowStatus(row, 'Завантаження медіа...', false);
        row.dataset.uploading = '1';
        hasUnsavedChanges = true;
        try {
            const data = new FormData();
            data.append('mediaFile', file);
            data.append('caption1', cap1.value || '');
            data.append('caption2', cap2.value || '');
            data.append('sortOrder', String(getRowOrder(row)));
            const res = await postForm('AddCollage', data);
            if (!res || !res.success || !res.collage || !res.collage.id || !res.collage.keep)
                throw new Error((res && res.error) ? res.error : 'Не вдалося зберегти медіа.');

            idInput.value = String(res.collage.id);
            keepInput.value = String(res.collage.keep);
            fileInput.value = '';
            // Keep current local preview (blob URL) to avoid visual flicker.
            // Persisted URL from DB (keepInput) will be used on next page open.
            setRowStatus(row, 'Збережено', false);
            hasUnsavedChanges = false;
        } catch (err) {
            setRowStatus(row, err && err.message ? err.message : 'Помилка збереження медіа', true);
        } finally {
            row.dataset.uploading = '0';
            scheduleReorderSave();
        }
    }

    async function saveRowMeta(row) {
        const idInput = row.querySelector('[data-field="id"]');
        if (!idInput.value)
            return;
        const cap1 = row.querySelector('.cap1');
        const cap2 = row.querySelector('.cap2');
        const data = new FormData();
        data.append('collageId', idInput.value);
        data.append('caption1', cap1.value || '');
        data.append('caption2', cap2.value || '');
        data.append('sortOrder', String(getRowOrder(row)));
        setRowStatus(row, 'Збереження...', false);
        hasUnsavedChanges = true;
        const res = await postForm('UpdateCollage', data);
        if (!res || !res.success)
            throw new Error((res && res.error) ? res.error : 'Не вдалося оновити підписи.');
        setRowStatus(row, 'Збережено', false);
        hasUnsavedChanges = false;
    }

    async function deleteRowPersisted(row) {
        const idInput = row.querySelector('[data-field="id"]');
        if (!idInput.value)
            return;
        const data = new FormData();
        data.append('collageId', idInput.value);
        const res = await postForm('DeleteCollage', data);
        if (!res || !res.success)
            throw new Error((res && res.error) ? res.error : 'Не вдалося видалити рядок.');
    }

    let announcementSaveTimer = null;
    async function saveAnnouncementCore() {
        if (!titleInput || !countrySelect)
            return;
        const data = new FormData();
        data.append('title', titleInput.value || '');
        data.append('country', countrySelect.value || '');
        hasUnsavedChanges = true;
        await postForm('UpdateAnnouncement', data);
        hasUnsavedChanges = false;
    }
    function scheduleAnnouncementSave() {
        if (announcementSaveTimer) clearTimeout(announcementSaveTimer);
        announcementSaveTimer = setTimeout(function () {
            announcementSaveTimer = null;
            saveAnnouncementCore().catch(function () { });
        }, 600);
    }

    let reorderTimer = null;
    function scheduleReorderSave() {
        if (reorderTimer) clearTimeout(reorderTimer);
        reorderTimer = setTimeout(function () {
            reorderTimer = null;
            saveReorder().catch(function () { });
        }, 400);
    }
    async function saveReorder() {
        const items = Array.prototype.slice.call(container.querySelectorAll('.collage-row'))
            .map(function (row, index) {
                const idInput = row.querySelector('[data-field="id"]');
                const id = parseInt(idInput.value || '', 10);
                return isNaN(id) ? null : { id: id, sortOrder: index };
            })
            .filter(function (x) { return !!x; });
        if (!items.length)
            return;
        const response = await fetch(window.location.pathname + '?handler=ReorderCollages&id=' + encodeURIComponent(announcementId), {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': antiForgeryToken
            },
            credentials: 'same-origin',
            body: JSON.stringify({ items: items })
        });
        if (!response.ok)
            throw new Error('HTTP ' + response.status);
        hasUnsavedChanges = false;
    }

    function debounceRowSave(row) {
        if (row.__saveTimer) clearTimeout(row.__saveTimer);
        row.__saveTimer = setTimeout(function () {
            row.__saveTimer = null;
            saveRowMeta(row).catch(function (err) {
                setRowStatus(row, err && err.message ? err.message : 'Помилка оновлення підписів', true);
            });
        }, 700);
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
            if (fileInput.files && fileInput.files[0]) {
                const f = fileInput.files[0];
                const u = URL.createObjectURL(f);
                keepInput.value = '';
                idInput.value = '';
                setPreview(row, u, f.type && f.type.indexOf('video') === 0);
                uploadRowMedia(row, f);
            } else {
                setPreview(row, keepInput.value, false);
            }
            syncClearVisibility();
        });

        btnClear.addEventListener('click', async function () {
            fileInput.value = '';
            if (idInput.value) {
                try {
                    await deleteRowPersisted(row);
                    idInput.value = '';
                    keepInput.value = '';
                    setPreview(row, '');
                    setRowStatus(row, 'Видалено', false);
                } catch (err) {
                    setRowStatus(row, err && err.message ? err.message : 'Помилка видалення', true);
                }
            } else {
                keepInput.value = '';
                setPreview(row, '');
            }
            syncClearVisibility();
        });

        btnRemove.addEventListener('click', async function () {
            if (idInput.value) {
                try {
                    await deleteRowPersisted(row);
                } catch (err) {
                    setRowStatus(row, err && err.message ? err.message : 'Помилка видалення', true);
                    return;
                }
            }
            row.remove();
            scheduleReorderSave();
        });

        function moveRow(delta) {
            const i = Array.prototype.indexOf.call(container.children, row);
            if (i < 0) return;
            const j = i + delta;
            if (j < 0 || j >= container.children.length) return;
            const ref = delta > 0 ? container.children[j].nextSibling : container.children[j];
            container.insertBefore(row, ref);
            scheduleReorderSave();
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
            setRowStatus(row, 'Збережено', false);
        } else {
            syncClearVisibility();
            setRowStatus(row, 'Не збережено', false);
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
            scheduleReorderSave();
        });

        row.querySelectorAll('.cap1, .cap2').forEach(function (ta) {
            ta.addEventListener('input', function () {
                hasUnsavedChanges = true;
                setRowStatus(row, 'Не збережено', false);
                debounceRowSave(row);
            });
            ta.addEventListener('blur', function () {
                saveRowMeta(row).catch(function (err) {
                    setRowStatus(row, err && err.message ? err.message : 'Помилка оновлення підписів', true);
                });
            });
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
            setRowStatus(row, 'Збережено', false);
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

    if (titleInput) {
        titleInput.addEventListener('blur', function () {
            saveAnnouncementCore().catch(function () { });
        });
        titleInput.addEventListener('input', function () {
            hasUnsavedChanges = true;
            scheduleAnnouncementSave();
        });
    }
    if (countrySelect) {
        countrySelect.addEventListener('change', function () {
            hasUnsavedChanges = true;
            saveAnnouncementCore().catch(function () { });
        });
    }

    function hasPendingDebounceTimers() {
        if (announcementSaveTimer || reorderTimer)
            return true;
        return Array.prototype.some.call(container.querySelectorAll('.collage-row'), function (row) {
            return !!row.__saveTimer;
        });
    }

    function hasActiveUploadRows() {
        return Array.prototype.some.call(container.querySelectorAll('.collage-row'), function (row) {
            return row.dataset.uploading === '1';
        });
    }

    function shouldWarnBeforeLeave() {
        return hasUnsavedChanges || pendingRequests > 0 || hasPendingDebounceTimers() || hasActiveUploadRows();
    }

    const leaveConfirmText = 'Не всі дані можуть бути збережені. Вийти з редагування негайно?';

    if (leaveLink) {
        leaveLink.addEventListener('click', function (e) {
            if (!shouldWarnBeforeLeave())
                return;
            if (window.confirm(leaveConfirmText))
                return;
            e.preventDefault();
        });
    }

    window.addEventListener('beforeunload', function (e) {
        if (!shouldWarnBeforeLeave())
            return;
        e.preventDefault();
        e.returnValue = '';
    });

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
