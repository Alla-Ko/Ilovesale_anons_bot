(function () {
  const nodes = document.querySelectorAll('.caption-status[data-announcement-id]');
  if (!nodes.length) return;

  const ids = [...nodes].map((n) => n.dataset.announcementId);
  const params = new URLSearchParams();
  params.set('handler', 'CaptionStatus');
  ids.forEach((id) => params.append('id', id));

  fetch(`${window.location.pathname}?${params}`, {
    headers: { Accept: 'application/json' },
    credentials: 'same-origin'
  })
    .then((r) => (r.ok ? r.json() : Promise.reject(new Error('status'))))
    .then((data) => {
      nodes.forEach((node) => {
        const status = data[node.dataset.announcementId];
        node.classList.remove('caption-status-loading');
        if (status === 'partial') {
          node.textContent = '✓';
          node.classList.add('caption-status-partial');
          node.title = 'Є колажі з валютним підписом без гривневого';
        } else if (status === 'complete') {
          node.textContent = '✓✓';
          node.classList.add('caption-status-complete');
          node.title = 'У всіх колажів з валютним підписом є гривневий';
        } else {
          node.remove();
        }
      });
    })
    .catch(() => {
      nodes.forEach((node) => node.remove());
    });
})();
