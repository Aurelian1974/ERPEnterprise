/**
 * ValyanERP Authentication JavaScript Module
 * Handles API calls to auth controller for login/logout operations
 */
window.ValyanAuth = {
    /**
     * Login via API Controller
     * @param {string} email - User email
     * @param {string} password - User password
     * @param {string} entityId - Selected entity ID (optional)
     * @param {string} entityType - Selected entity type: LOCATION, WORKPLACE, COMPANY, GROUP (optional)
     * @returns {Promise<{success: boolean, message?: string, redirectUrl?: string}>}
     */
    login: async function(email, password, entityId, entityType) {
        try {
            const response = await fetch('/api/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ 
                    email, 
                    password,
                    entityId: entityId || null,
                    entityType: entityType || null
                }),
                credentials: 'include' // Important: include cookies in request
            });

            const data = await response.json();
            
            return {
                success: data.success || false,
                message: data.message || null,
                redirectUrl: data.redirectUrl || '/'
            };
        } catch (error) {
            console.error('Login error:', error);
            return {
                success: false,
                message: 'Eroare de conexiune. Verificați rețeaua.',
                redirectUrl: null
            };
        }
    },

    /**
     * Logout - invalidate session
     * @returns {Promise<boolean>}
     */
    logout: async function() {
        try {
            await fetch('/api/sessions/invalidate', {
                method: 'POST',
                credentials: 'include'
            });
            return true;
        } catch (error) {
            console.error('Logout error:', error);
            return false;
        }
    }
};

/**
 * Get cookie value by name - used by Blazor to read working context
 * @param {string} name - Cookie name
 * @returns {string|null} - Cookie value or null if not found
 */
window.getCookie = function(name) {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) {
        const cookieValue = parts.pop().split(';').shift();
        // Decode URL-encoded value (e.g., %3A becomes :)
        return decodeURIComponent(cookieValue);
    }
    return null;
};