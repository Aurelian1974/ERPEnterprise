// audit-modal.js - Bootstrap modal helper for Blazor Server

window.auditModal = {
    show: function (modalId) {
        const modalElement = document.getElementById(modalId);
        if (modalElement) {
            const modal = new bootstrap.Modal(modalElement);
            modal.show();
            console.log('✅ Modal opened:', modalId);
        } else {
            console.error('❌ Modal not found:', modalId);
        }
    },

    hide: function (modalId) {
        const modalElement = document.getElementById(modalId);
        if (modalElement) {
            const modal = bootstrap.Modal.getInstance(modalElement);
            if (modal) {
                modal.hide();
                console.log('✅ Modal closed:', modalId);
            }
        }
    }
};
