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
    /**
     * Print an official report sheet: A4 portrait, with a real page margin top and bottom.
     *
     * Why this exists rather than a stylesheet rule. Two attempts in CSS did nothing, and the reason is the one this file already
     * records: an @page written in a component's scoped stylesheet does not reliably reach the print engine. A NAMED page fares no
     * better - print.css assigns `page: report-landscape` to .print-report-sheet and the Status Report still printed portrait,
     * which is the proof that `page:` is not being honoured at all. So a named page with a margin was inert.
     *
     * The margin has to be on the PAGE, not on a block. A block's padding applies at its own top and bottom, once - never at a
     * page break - so the sheet's 12mm gave the first page a margin and left every page after it starting at y=0, with the seal on
     * page one and a section heading on page two hard against the paper edge.
     *
     * Injected here and removed afterwards, exactly as `landscape` does, so the margin is scoped in TIME rather than by selector:
     * only the print this button starts gets it, and the roster, the utility statements and the receipts keep the margin-free box
     * they were tuned for.
     *
     * ONE CONSEQUENCE: a page with a top margin gives the browser somewhere to draw its automatic date, URL and page numbers.
     * Untick "Headers and footers" in the print dialog if they appear. The alternative is figures against the paper edge.
     */
    reportDocument: function () {
        const STYLE_ID = 'stalltrack-report-document-print';
        document.getElementById(STYLE_ID)?.remove();

        const style = document.createElement('style');
        style.id = STYLE_ID;
        style.media = 'print';
        style.textContent =
            // A paper size must be named alongside the orientation, for the same reason the landscape helper names one.
            '@page { size: A4 portrait; margin: 12mm 0; }' +
            'html, body { margin: 0 !important; }' +
            // Left and right stay on the sheet: only the vertical margin has to repeat per page.
            '.print-report-sheet { padding: 0 12mm !important; }';
        document.head.appendChild(style);

        try {
            window.print();
        } finally {
            // afterprint does not fire in every browser/dialog path, so clean up on a short timer as well.
            const cleanup = () => document.getElementById(STYLE_ID)?.remove();
            window.addEventListener('afterprint', cleanup, { once: true });
            window.setTimeout(cleanup, 60000);
        }
    },

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
