# EEMO Cantilan — HttpOnly Cookie Auth Flow (Refactored)

## Overview
Tokens are now stored in **HttpOnly, Secure, SameSite=Strict cookies** on the server. React never touches tokens.

---

## Backend Changes

### 1. Program.cs
- Added CORS policy `AllowReactLocal` with `AllowCredentials()`
- Added `CookiePolicy` middleware with `HttpOnly = Always`, `Secure = Always`, `SameSite = Strict`
- Middleware order: CookiePolicy → CORS → Auth

### 2. AuthenticationExtensions.cs
- JWT now reads from `accessToken` cookie instead of Authorization header
- `OnMessageReceived` event extracts token from `context.Request.Cookies`

### 3. TokenService.cs
- `SetAuthCookies()` — Sets both access (15min) and refresh (7 days) cookies
- `ClearAuthCookies()` — Deletes both cookies
- `GetRefreshTokenFromCookie()` — Reads refresh token from request
- Access token expiry changed to 15 minutes (was 7 days)

### 4. ITokenService.cs
- Added three new methods for cookie management

### 5. AdminAuthController.cs
- **Login** — Sets cookies, returns only user info (no tokens in body)
- **Refresh** — Reads refresh token from cookie, sets new cookies, returns success message
- **Logout** — Clears cookies, requires `[Authorize]`

---

## Frontend Changes

### 1. cookieService.ts
- Simplified to just `clearTokens()` placeholder
- All cookie logic moved to backend

### 2. client.ts (Axios)
- Removed request interceptor (no token to add)
- Removed token storage logic
- Kept response interceptor for 401 → refresh flow
- `withCredentials: true` automatically sends/receives cookies

### 3. AuthContext.tsx
- Removed all token storage logic
- `login()` just calls API and updates user state
- `logout()` calls API (which clears cookies server-side)
- `initAuth()` calls `getCurrentUser()` to check session

### 4. dto.ts
- `LoginResponse` now extends `AdminUserDto` (no tokens)

---

## Auth Flow

### Login
```
1. User submits username/password
2. React calls POST /api/AdminAuth/login
3. Backend validates, generates tokens
4. Backend sets HttpOnly cookies (accessToken, refreshToken)
5. Backend returns user info (no tokens)
6. React stores user in state
7. Browser automatically includes cookies in future requests
```

### API Request
```
1. React calls any protected endpoint
2. Axios automatically includes cookies (withCredentials: true)
3. Backend reads accessToken from cookie
4. JWT middleware validates token
5. Request proceeds
```

### Token Refresh (Auto)
```
1. Access token expires (15 min)
2. Next API request gets 401
3. Axios interceptor calls POST /api/AdminAuth/refresh-token
4. Backend reads refreshToken from cookie
5. Backend validates, generates new tokens
6. Backend sets new cookies (rotates refresh token)
7. Axios retries original request with new token
```

### Logout
```
1. User clicks logout
2. React calls POST /api/AdminAuth/logout
3. Backend clears both cookies
4. React clears user state
5. Redirect to login
```

---

## Security Benefits

✅ **Tokens never in JavaScript** — Can't be stolen by XSS  
✅ **HttpOnly flag** — Can't be accessed by JavaScript  
✅ **Secure flag** — Only sent over HTTPS  
✅ **SameSite=Strict** — Prevents CSRF attacks  
✅ **Automatic rotation** — Refresh token rotated on each refresh  
✅ **Short-lived access token** — 15 minutes  
✅ **Long-lived refresh token** — 7 days, stored securely  

---

## Testing Checklist

- [ ] Login works, cookies set in browser DevTools
- [ ] Protected endpoints work (token read from cookie)
- [ ] Token refresh works on 401
- [ ] Logout clears cookies
- [ ] CORS works (React can call API)
- [ ] withCredentials sends cookies automatically
- [ ] Refresh token rotates on each refresh
- [ ] Access token expires after 15 minutes

---

## Environment Setup

### Backend (appsettings.Development.json)
```json
{
  "Jwt": {
    "Key": "EEMO-Cantilan-SDS-SuperSecretKey@2025!MunicipalityOfCantilan#Surigao",
    "Issuer": "EEMOCantilanSDS",
    "Audience": "EEMOCantilanSDS.Client"
  }
}
```

### Frontend (.env)
```
VITE_API_BASE_URL=https://localhost:7097/api
```

---

## Notes

- Cookies are **domain-scoped** — only sent to `localhost:7097`
- React frontend at `localhost:5173` can access via CORS + credentials
- In production, update CORS policy to your domain
- Refresh token is stored in DB, validated on each refresh
- Access token is stateless (JWT), no DB lookup needed

---

## Troubleshooting

**Cookies not being set?**
- Check CORS policy includes `AllowCredentials()`
- Check `withCredentials: true` in axios
- Check browser DevTools → Application → Cookies

**401 on protected endpoints?**
- Check `accessToken` cookie exists
- Check JWT middleware reads from cookies
- Check token not expired

**CORS errors?**
- Check CORS policy includes React origin
- Check `AllowCredentials()` is set
- Check `withCredentials: true` in axios

**Refresh not working?**
- Check `refreshToken` cookie exists
- Check refresh endpoint reads from cookie
- Check refresh token not expired in DB
