// Bound the history by actual data rows, including wrapped source/payer context.
// No row heights or financial facts are guessed here; the footer is outside this region.
// The register grows to fill the window (a tall window shows more rows instead of empty space below the card), never
// shows fewer than `visibleRows`, and never reserves more height than its rows need.
const observers = new WeakMap();
const PAGE_GAP = 24; // space kept between the card and the bottom of the window

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
        const horizontalScrollbar = region.offsetHeight - region.clientHeight;
        const minimum = rows[visibleRows - 1].getBoundingClientRect().bottom - top + horizontalScrollbar;
        const all = rows[rows.length - 1].getBoundingClientRect().bottom - top + horizontalScrollbar;

        // What sits below the region inside its card (the pager), measured rather than assumed.
        const regionBox = region.getBoundingClientRect();
        const card = region.closest('.v3-panel') ?? region.parentElement;
        const below = card ? Math.max(0, card.getBoundingClientRect().bottom - regionBox.bottom) : 0;
        const fill = window.innerHeight - (regionBox.top + window.scrollY) - below - PAGE_GAP;

        const height = Math.max(minimum, Math.min(fill, all));
        const value = height >= all ? 'none' : `${Math.ceil(height)}px`;
        if (region.style.maxHeight !== value) region.style.maxHeight = value;
    };
    const resize = new ResizeObserver(measure);
    resize.observe(table);
    const mutation = new MutationObserver(measure);
    mutation.observe(table, { childList: true, subtree: true, characterData: true });
    window.addEventListener('resize', measure);
    observers.set(region, { resize, mutation, measure });
    measure();
}

export function disconnect(region) {
    const observer = observers.get(region);
    if (!observer) return;
    observer.resize.disconnect();
    observer.mutation.disconnect();
    window.removeEventListener('resize', observer.measure);
    observers.delete(region);
}
