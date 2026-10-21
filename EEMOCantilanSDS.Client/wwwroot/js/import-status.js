// Import review presentation only. Native auto popovers provide top-layer placement, Escape and outside dismissal.
const instances = new WeakMap();

export function attach(trigger, popup) {
    if (!trigger || !popup || !trigger.isConnected || !popup.isConnected) return;
    detach(trigger);
    let pinned = false, overPopup = false, timer;
    const open = () => popup.isConnected && popup.matches(':popover-open');
    const sync = () => {
        trigger.setAttribute('aria-expanded', String(open()));
        if (!open()) pinned = false;
    };
    const hide = () => { clearTimeout(timer); if (open()) popup.hidePopover(); sync(); };
    const position = () => {
        if (!open()) return;
        const anchor = trigger.getBoundingClientRect();
        const region = trigger.closest('.imp-table-wrap')?.getBoundingClientRect();
        if (region && (anchor.right <= region.left || anchor.left >= region.right || anchor.bottom <= region.top || anchor.top >= region.bottom)) { hide(); return; }
        const bounds = popup.getBoundingClientRect();
        const gap = 8;
        const left = Math.max(gap, Math.min(anchor.right - bounds.width, innerWidth - bounds.width - gap));
        const below = anchor.bottom + gap;
        const top = Math.max(gap, Math.min(below + bounds.height <= innerHeight - gap ? below : anchor.top - bounds.height - gap, innerHeight - bounds.height - gap));
        popup.style.left = `${left}px`;
        popup.style.top = `${top}px`;
    };
    const show = () => { clearTimeout(timer); if (!popup.isConnected) return; if (!open()) popup.showPopover(); sync(); position(); };
    const leave = () => { clearTimeout(timer); timer = setTimeout(() => { if (!pinned && !overPopup && document.activeElement !== trigger) hide(); }, 150); };
    const enter = event => { if (event.pointerType !== 'touch') show(); };
    const click = event => { event.preventDefault(); if (pinned) hide(); else { pinned = true; show(); } };
    const popupEnter = () => { overPopup = true; clearTimeout(timer); };
    const popupLeave = () => { overPopup = false; leave(); };
    const toggle = () => { sync(); if (open()) position(); };
    const listeners = [[trigger,'pointerenter',enter],[trigger,'pointerleave',leave],[trigger,'focus',show],[trigger,'blur',leave],
        [trigger,'click',click],[popup,'pointerenter',popupEnter],[popup,'pointerleave',popupLeave],[popup,'toggle',toggle]];
    for (const [target,event,handler] of listeners) target.addEventListener(event,handler);
    window.addEventListener('scroll',position,true);
    window.addEventListener('resize',position);
    instances.set(trigger, { refresh: () => { sync(); position(); }, dispose: () => {
        hide();
        for (const [target,event,handler] of listeners) target.removeEventListener(event,handler);
        window.removeEventListener('scroll',position,true);
        window.removeEventListener('resize',position);
    }});
}

export function refresh(trigger, popup) { if (instances.has(trigger)) instances.get(trigger).refresh(); else attach(trigger, popup); }
export function detach(trigger) { instances.get(trigger)?.dispose(); instances.delete(trigger); }
// An asynchronous render/navigation can remove a focus target before interop reaches the browser.
export function focusConnected(element, selector) {
    const editor = document.querySelector('.shi .shi-editor');
    if (selector === '.shi-editor') { editor?.focus({ preventScroll: true }); return; }
    // A delayed close callback must not pull focus behind a newly opened editor.
    if (editor) return;
    const target = element?.isConnected ? element : document.querySelector(`.shi ${selector}`);
    target?.focus({ preventScroll: true });
}
