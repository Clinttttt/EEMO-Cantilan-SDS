import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { cookieService } from '@/utils/cookieService';
import { authService } from '@/api/services/authService';
import type { AdminUserDto } from '@/types/dto';

interface AuthContextType {
  user: AdminUserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const [user, setUser] = useState<AdminUserDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const initAuth = async () => {
      try {
        // Try to fetch current user - if cookies are valid, this will work
        const userData = await authService.getCurrentUser();
        setUser(userData);
      } catch {
        // No valid session
        setUser(null);
      } finally {
        setIsLoading(false);
      }
    };

    initAuth();
  }, []);

  const login = async (username: string, password: string) => {
    const response = await authService.login({ username, password });
    // Cookies are set server-side, just update user state
    setUser(response);
  };

  const logout = async () => {
    await authService.logout();
    // Cookies are cleared server-side
    cookieService.clearTokens();
    setUser(null);
  };

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: !!user, isLoading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider');
  return context;
};
