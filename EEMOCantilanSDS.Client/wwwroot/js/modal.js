window.scrollModalToTop = function () {
    const modalBody = document.querySelector('.eemo-modal-body');
    if (modalBody) {
        modalBody.scrollTo({
            top: 0,
            behavior: 'smooth'
        });
    }
};


// Opens the native date picker for a date input (Chrome/Edge ignore clicks on opacity:0 inputs).
window.openDatePicker = function (el) {
    if (!el) return;
    try { el.showPicker(); }
    catch { el.focus(); el.click(); }
};
