import { ReactNode } from 'react';
import { Sidebar } from './Sidebar';

interface AdminLayoutProps {
  children: ReactNode;
}

export const AdminLayout = ({ children }: AdminLayoutProps) => {
  return (
    <div className="admin-layout">
      <Sidebar />
      <main className="admin-main">
        {children}
      </main>
    </div>
  );
};
