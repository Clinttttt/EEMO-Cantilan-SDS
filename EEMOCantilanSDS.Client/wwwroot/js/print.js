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
     * Print an official report sheet: A4 portrait, with no page margin at all.
     *
     * THE MARGIN IS NOUGHT ON PURPOSE, and this is the whole reason. Chromium draws its own furniture - the date, the document
     * title, the page URL and "1/2" - inside the page's MARGIN box, and there is no CSS that turns it off. Give the page a 12mm
     * margin for whitespace and you buy that furniture with it; an office had "StallTrack - NPM Reports" and a localhost-looking
     * URL printed on a document carrying the municipal seal. With no margin there is nowhere to draw it.
     *
     * So the whitespace comes from inside the document instead: the sheet's own padding for the first page, and PADDING on each
     * section block for the pages after it. Padding, not margin - a margin is dropped at a page break, a padding is part of the
     * box and survives, so a section that begins a page still opens clear of the paper edge. Sections are kept whole by the
     * stylesheet, so a continued page always begins with one.
     *
     * Injected here rather than written in CSS because an @page in a scoped stylesheet does not reach the print engine, and a
     * named page is not honoured at all - print.css assigns a landscape named page to .print-report-sheet and these documents
     * still print portrait, which is the proof.
     */
    reportDocument: function () {
        const STYLE_ID = 'stalltrack-report-document-print';
        document.getElementById(STYLE_ID)?.remove();

        const style = document.createElement('style');
        style.id = STYLE_ID;
        style.media = 'print';
        style.textContent =
            // A paper size must be named alongside the orientation, for the same reason the landscape helper names one.
            '@page { size: A4 portrait; margin: 0; }' +
            'html, body { margin: 0 !important; }' +
            '.print-report-sheet { padding: 12mm !important; }';
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
