import { useAuth } from '../../context/AuthContext';

interface HeaderProps {
  title: string;
  breadcrumbs?: string[];
}

export const Header = ({ title, breadcrumbs = ['EEMO Admin'] }: HeaderProps) => {
  const { user } = useAuth();

  return (
    <header className="topbar">
      <div>
        <div className="topbar-title">{title}</div>
        <div className="topbar-breadcrumb">
          {breadcrumbs.map((crumb, index) => (
            <span key={index}>
              {index > 0 && <span className="breadcrumb-sep">/</span>}
              <span className={index === breadcrumbs.length - 1 ? 'breadcrumb-active' : ''}>
                {crumb}
              </span>
            </span>
          ))}
        </div>
      </div>
      <div className="topbar-right">
        <div className="topbar-date">
          <svg viewBox="0 0 24 24">
            <rect x="3" y="4" width="18" height="18" rx="2" />
            <line x1="16" y1="2" x2="16" y2="6" />
            <line x1="8" y1="2" x2="8" y2="6" />
            <line x1="3" y1="10" x2="21" y2="10" />
          </svg>
          {new Date().toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' })}
        </div>
        <div className="topbar-admin">
          <div className="admin-avatar">
            {user?.fullName?.charAt(0).toUpperCase() || 'A'}
          </div>
          <div>
            <div className="admin-name">{user?.fullName || 'Administrator'}</div>
            <div className="admin-role">EEMO Office</div>
          </div>
        </div>
      </div>
    </header>
  );
};
