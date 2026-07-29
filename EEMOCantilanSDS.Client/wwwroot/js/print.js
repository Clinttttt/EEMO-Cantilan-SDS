// Printing helpers.
//
// Why this exists: `@page { size: landscape }` written in a component's scoped stylesheet does not reliably
// reach the print engine — the office's roster kept coming out portrait with its ten columns squeezed. Adding
// the rule to a global sheet is not an option either, because @page cannot be conditioned on a page or class.
// Injecting it immediately before printing and removing it afterwards is deterministic and affects only the
// document being printed.
//
// The page margin is deliberately zero. Chromium draws its own header and footer — the print date, the
// document title and the page URL — inside the page margin, and there is no CSS switch for them; with no
// margin there is nowhere to draw them, so an official sheet leaves the printer carrying only the office's
// own letterhead. The paper margin is supplied as padding on the body instead.
window.stalltrackPrint = {
    // Print the current page in landscape. Safe to call repeatedly: the injected rule is removed each time,
    // so a later portrait print (another page) is unaffected.
    landscape: function () {
        const STYLE_ID = 'stalltrack-landscape-print';
        document.getElementById(STYLE_ID)?.remove();

        const style = document.createElement('style');
        style.id = STYLE_ID;
        style.media = 'print';
        style.textContent =
            // A paper size must be named alongside the orientation: `size: landscape` on its own is treated as
            // a hint and the dialog still opened Portrait, so the ten-column roster came out over two sheets.
            '@page { size: A4 landscape; margin: 0; }' +
            'html, body { margin: 0 !important; }' +
            'body { padding: 10mm 12mm !important; }';
        document.head.appendChild(style);

        try {
            window.print();
        } finally {
            // afterprint does not fire in every browser/dialog path, so clean up on a short timer as well.
            const cleanup = () => document.getElementById(STYLE_ID)?.remove();
            window.addEventListener('afterprint', cleanup, { once: true });
            window.setTimeout(cleanup, 60000);
        }
    }
};
