/**
 * ValyanERP Authentication JavaScript Module
 * Handles API calls to auth controller for login/logout operations
 */
window.ValyanAuth = {
    /**
     * Login via API Controller
     * @param {string} email - User email
     * @param {string} password - User password
     * @returns {Promise<{success: boolean, message?: string, redirectUrl?: string}>}
     */
    login: async function(email, password) {
        try {
            const response = await fetch('/api/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ email, password }),
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
