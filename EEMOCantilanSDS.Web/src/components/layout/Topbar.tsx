import { useAuth } from '@/context/AuthContext';

interface BreadcrumbItem {
  label: string;
  href?: string;
}

interface TopbarProps {
  title: string;
  breadcrumbs?: BreadcrumbItem[];
}

export const Topbar = ({ title, breadcrumbs }: TopbarProps) => {
  const { user, logout } = useAuth();

  const avatarLetter = user?.fullName?.[0]?.toUpperCase() ?? 'A';

  return (
    <header className="topbar">
      <div className="topbar-left" style={{ display: 'flex', flexDirection: 'column' }}>
        <div className="topbar-title">{title}</div>
        {breadcrumbs && breadcrumbs.length > 0 && (
          <div className="topbar-breadcrumb">
            {breadcrumbs.map((crumb, i) => (
              <span key={i} style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                {i > 0 && <span className="breadcrumb-sep">/</span>}
                {crumb.href ? (
                  <a href={crumb.href} className="breadcrumb-link">{crumb.label}</a>
                ) : (
                  <span className="breadcrumb-active">{crumb.label}</span>
                )}
              </span>
            ))}
          </div>
        )}
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

        <div className="topbar-admin" onClick={logout} title="Logout">
          <div className="admin-avatar">{avatarLetter}</div>
          <div className="admin-info">
            <div className="admin-name">{user?.fullName ?? 'Administrator'}</div>
            <div className="admin-role">EEMO Office</div>
          </div>
        </div>
      </div>
    </header>
  );
};
