window.__valyanERP__ = window.__valyanERP__ || {};
window.__valyanERP__.sendSessionInvalidate = function() {
    try {
        fetch('/api/sessions/invalidate', { method: 'POST', credentials: 'same-origin' });
    } catch (e) { console.warn('Failed to send session invalidate', e); }
};

window.__valyanERP__.sendHeartbeat = function() {
    try {
        fetch('/api/sessions/heartbeat', { method: 'POST', credentials: 'same-origin' });
    } catch (e) { console.warn('Failed to send heartbeat', e); }
};

// Start heartbeat every 20 seconds
setInterval(function () { window.__valyanERP__.sendHeartbeat(); }, 20 * 1000);

window.addEventListener('beforeunload', function () {
    window.__valyanERP__.sendSessionInvalidate();
});