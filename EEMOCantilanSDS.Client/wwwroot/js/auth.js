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

        // A message the office can act on, and never one that says which half was wrong.
        //
        // The API answers a bad username and a bad password identically - both 401, both with no body - and that is
        // deliberate: a screen that distinguished them would confirm to a stranger that a username exists. 403 is
        // treated the same way here for the same reason; it can mean the account belongs to another LGU, which is
        // equally not worth confirming. So both read alike, and only the office's OWN server trouble reads differently,
        // because that is something the reader can wait out rather than correct.
        //
        // A message the server DID send is always preferred: a locked account or a password that must be changed is
        // specific, actionable and not a secret.
        const sent = body && (body.error || body.message);
        const message = sent
            || (response.status === 401 || response.status === 403
                ? 'Those sign-in details do not match an account for this office. Check them and try again.'
                : 'Sign-in could not be completed. Please try again in a moment.');
        return JSON.stringify({ error: message });
    } catch (error) {
        console.error('Login error:', error);
        return JSON.stringify({ error: 'Unable to reach the office\'s server. Check the connection and try again.' });
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

        // Same rule as the shim above: a bad username and a bad password read alike, so the screen confirms nothing.
        return response.status === 401 || response.status === 403
            ? 'Those sign-in details do not match an account for this office. Check them and try again.'
            : 'Sign-in could not be completed. Please try again in a moment.';
    } catch (error) {
        console.error('Login error:', error);
        return 'Unable to reach the office\'s server. Check the connection and try again.';
    }
};
