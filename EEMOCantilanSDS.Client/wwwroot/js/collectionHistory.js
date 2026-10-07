// Bound the history by actual data rows, including wrapped source/payer context.
// No row heights or financial facts are guessed here; the footer is outside this region.
const observers = new WeakMap();

export function observe(region, visibleRows) {
    disconnect(region);
    const table = region.querySelector('table');
    if (!table) return;

    const measure = () => {
        const rows = table.querySelectorAll('tbody tr:not(:has(.v3-empty))');
        if (rows.length <= visibleRows) {
            region.style.maxHeight = 'none';
            return;
        }
        const top = table.getBoundingClientRect().top;
        const bottom = rows[visibleRows - 1].getBoundingClientRect().bottom;
        const horizontalScrollbar = region.offsetHeight - region.clientHeight;
        const height = `${Math.ceil(bottom - top + horizontalScrollbar)}px`;
        if (region.style.maxHeight !== height) region.style.maxHeight = height;
    };
    const resize = new ResizeObserver(measure);
    resize.observe(table);
    const mutation = new MutationObserver(measure);
    mutation.observe(table, { childList: true, subtree: true, characterData: true });
    observers.set(region, { resize, mutation });
    measure();
}

export function disconnect(region) {
    const observer = observers.get(region);
    if (!observer) return;
    observer.resize.disconnect();
    observer.mutation.disconnect();
    observers.delete(region);
}
