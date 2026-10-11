const reviews = new WeakMap();
export function bind(root) {
    const dialog = root.querySelector('[role="dialog"]');
    if (!dialog) return;
    const previous = document.activeElement;
    const controls = () => [...dialog.querySelectorAll('button:not([disabled]),a[href],input,select,summary,[tabindex="0"]')]
        .filter(el => el.getClientRects().length > 0);
    const trap = event => {
        if (event.key !== 'Tab') return;
        const items = controls();
        if (!items.length) return;
        const first = items[0], last = items[items.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    dialog.addEventListener('keydown', trap);
    controls()[0]?.focus();
    reviews.set(root, {dialog, trap, previous});
}
export function release(root) {
    const state = reviews.get(root);
    if (!state) return;
    state.dialog.removeEventListener('keydown', state.trap);
    if (state.previous?.isConnected) state.previous.focus();
    reviews.delete(root);
}
