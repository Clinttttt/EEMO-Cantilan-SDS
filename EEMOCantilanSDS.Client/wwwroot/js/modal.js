window.scrollModalToTop = function () {
    const modalBody = document.querySelector('.eemo-modal-body');
    if (modalBody) {
        modalBody.scrollTo({
            top: 0,
            behavior: 'smooth'
        });
    }
};
