// Admin sign-in helper that can also report a pending two-factor step.
// Always resolves with a JSON string so the caller can distinguish the three outcomes:
//   {"ok":true}                                     -> signed in, cookie set
//   {"mfaRequired":true,"challengeToken":"..."}      -> password OK, authenticator code still needed
//   {"error":"message"}                              -> failed
// Kept separate from loginWithCookies above because that one is shared with the payor portal and its
// null-or-error-string contract must not change.
window.loginWithMfa = async function (url, jsonData) {
    try {
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: jsonData,
            credentials: 'include'
        });

        let body = null;
        try { body = await response.json(); } catch { /* empty or non-JSON body */ }

        if (response.ok) {
            if (body && body.mfaRequired) {
                return JSON.stringify({ mfaRequired: true, challengeToken: body.challengeToken });
            }
            return JSON.stringify({ ok: true });
        }

        const message = (body && (body.error || body.message)) || 'Authentication failed.';
        return JSON.stringify({ error: message });
    } catch (error) {
        console.error('Login error:', error);
        return JSON.stringify({ error: 'Unable to connect. Please try again.' });
    }
};

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
