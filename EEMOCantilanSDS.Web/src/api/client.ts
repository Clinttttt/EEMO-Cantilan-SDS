import axios from 'axios';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'https://localhost:7097/api';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  withCredentials: true, // Automatically send/receive cookies
});

// Response interceptor: Handle 401 and refresh token
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    // If 401 and not already retried, attempt refresh
    if (error.response?.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true;

      try {
        // Call refresh endpoint - cookies are sent automatically via withCredentials
        await axios.post(`${API_BASE_URL}/AdminAuth/refresh-token`,
          {},
          { withCredentials: true }
        );
        
        // Retry original request with new token (in cookie)
        return apiClient(originalRequest);
      } catch (refreshError) {
        // Refresh failed - redirect to login only if not already there
        if (!window.location.pathname.includes('/login') && !window.location.pathname.includes('/account-setup')) {
          window.location.href = '/login';
        }
        return Promise.reject(refreshError);
      }
    }

    return Promise.reject(error);
  }
);
