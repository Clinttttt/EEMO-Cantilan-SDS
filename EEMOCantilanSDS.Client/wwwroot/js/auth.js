// Returns null on success, or an error message string on failure (for display in the UI).
window.loginWithCookies = async function (url, jsonData) {
    try {
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: jsonData,
            credentials: 'include'
        });

        if (response.ok) return null;

        try {
            const body = await response.json();
            if (body && body.error) return body.error;
        } catch { /* body not JSON */ }

        return 'Authentication failed.';
    } catch (error) {
        console.error('Login error:', error);
        return 'Unable to connect. Please try again.';
    }
};
