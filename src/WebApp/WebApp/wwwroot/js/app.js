// Modal helper functions for Bootstrap modals
window.indexToolsSession = {
    read: () => sessionStorage.getItem('indextools.operator.v1'),
    write: value => sessionStorage.setItem('indextools.operator.v1', value),
    clear: () => sessionStorage.removeItem('indextools.operator.v1')
};
window.modalHelpers = {
    showModal: function (modalId) {
        const modalElement = document.getElementById(modalId);
        if (modalElement) {
            const modal = new bootstrap.Modal(modalElement);
            modal.show();
        }
    },
    
    hideModal: function (modalId) {
        const modalElement = document.getElementById(modalId);
        if (modalElement) {
            const modal = bootstrap.Modal.getInstance(modalElement);
            if (modal) {
                modal.hide();
            }
        }
    }
};

// Test function to verify Bootstrap is loaded
window.testBootstrap = function () {
    console.log('Bootstrap available:', typeof bootstrap !== 'undefined');
    console.log('Bootstrap Modal available:', typeof bootstrap.Modal !== 'undefined');
    return typeof bootstrap !== 'undefined';
};
