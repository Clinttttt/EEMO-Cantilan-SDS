window.loginWithCookies = async function (url, jsonData) {
    try {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: jsonData,
            credentials: 'include'
        });
        
        return response.ok;
    } catch (error) {
        console.error('Login error:', error);
        return false;
    }
};
