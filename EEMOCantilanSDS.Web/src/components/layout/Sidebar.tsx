import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import styles from './Sidebar.module.css';

interface FacilityItem {
  code: string;
  name: string;
  shortName: string;
  iconSvg: string;
  unpaidCount: number;
}

const facilities: FacilityItem[] = [
  {
    code: 'NPM',
    name: 'New Public Market',
    shortName: 'Public Market',
    iconSvg: '<svg viewBox="0 0 24 24"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>',
    unpaidCount: 20,
  },
  {
    code: 'TCC',
    name: 'Tampak Commercial Center',
    shortName: 'Tampak Comm.',
    iconSvg: '<svg viewBox="0 0 24 24"><rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 7V5a2 2 0 00-2-2h-4a2 2 0 00-2 2v2"/></svg>',
    unpaidCount: 8,
  },
  {
    code: 'NCC',
    name: 'New Commercial Center',
    shortName: 'New Comm. Center',
    iconSvg: '<svg viewBox="0 0 24 24"><path d="M3 3h18v18H3zM3 9h18M3 15h18M9 3v18M15 3v18"/></svg>',
    unpaidCount: 5,
  },
  {
    code: 'BBQ',
    name: 'Barbecue Stand',
    shortName: 'BBQ Stand',
    iconSvg: '<svg viewBox="0 0 24 24"><path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/><line x1="4" y1="22" x2="4" y2="15"/></svg>',
    unpaidCount: 1,
  },
  {
    code: 'ICE',
    name: 'Iceplant',
    shortName: 'Iceplant',
    iconSvg: '<svg viewBox="0 0 24 24"><line x1="12" y1="2" x2="12" y2="22"/><path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/></svg>',
    unpaidCount: 2,
  },
  {
    code: 'SLH',
    name: 'Slaughterhouse',
    shortName: 'Slaughterhouse',
    iconSvg: '<svg viewBox="0 0 24 24"><path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/></svg>',
    unpaidCount: 6,
  },
];

export const Sidebar = () => {
  const [collapsed, setCollapsed] = useState(false);
  const location = useLocation();

  const isActive = (path: string) => location.pathname === path;

  return (
    <aside className={`${styles.sidebar} ${collapsed ? styles.collapsed : ''}`}>
      <div className={styles.sidebarBrand}>
        <div className={styles.sidebarSeal}>
          <img src="/images/eemo-logov2.png" alt="EEMO" />
        </div>
        <div className={styles.sidebarBrandText}>
          <div className={styles.sidebarBrandName}>EEMO</div>
          <div className={styles.sidebarBrandSub}>Admin Portal</div>
        </div>
      </div>

      <div className={styles.sidebarDivider}></div>

      <nav className={styles.sidebarNav}>
        <div className={styles.navGroupLabel}>Main</div>

        <Link to="/dashboard" className={`${styles.navItem} ${isActive('/menu') ? styles.active : ''}`}>
          <svg viewBox="0 0 24 24">
            <rect x="3" y="3" width="7" height="7" />
            <rect x="14" y="3" width="7" height="7" />
            <rect x="14" y="14" width="7" height="7" />
            <rect x="3" y="14" width="7" height="7" />
          </svg>
          <span>Dashboard</span>
        </Link>

        <Link to="/collectors" className={`${styles.navItem} ${isActive('/collectors') ? styles.active : ''}`}>
          <svg viewBox="0 0 24 24">
            <path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2" />
            <circle cx="9" cy="7" r="4" />
            <path d="M23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75" />
          </svg>
          <span>Collectors</span>
        </Link>

        <Link to="/vendors" className={`${styles.navItem} ${isActive('/vendors') ? styles.active : ''}`}>
          <svg viewBox="0 0 24 24">
            <path d="M20 7H4a2 2 0 00-2 2v6a2 2 0 002 2h16a2 2 0 002-2V9a2 2 0 00-2-2z" />
            <path d="M16 21V5a2 2 0 00-2-2h-4a2 2 0 00-2 2v16" />
          </svg>
          <span>Vendors &amp; Stalls</span>
        </Link>

        <div className={styles.navGroupLabel} style={{ marginTop: '1rem' }}>
          Facilities
        </div>

        {facilities.map((fac) => (
          <Link
            key={fac.code}
            to={`/facility/${fac.code.toLowerCase()}`}
            className={`${styles.navItem} ${isActive(`/facility/${fac.code.toLowerCase()}`) ? styles.active : ''}`}
          >
            <span dangerouslySetInnerHTML={{ __html: fac.iconSvg }} />
            <span>{fac.shortName}</span>
            {fac.unpaidCount > 0 && <span className={styles.navBadge}>{fac.unpaidCount}</span>}
          </Link>
        ))}

        <div className={styles.navGroupLabel} style={{ marginTop: '1rem' }}>
          Reports
        </div>

        <Link to="/reports" className={`${styles.navItem} ${isActive('/reports') ? styles.active : ''}`}>
          <svg viewBox="0 0 24 24">
            <line x1="18" y1="20" x2="18" y2="10" />
            <line x1="12" y1="20" x2="12" y2="4" />
            <line x1="6" y1="20" x2="6" y2="14" />
          </svg>
          <span>Financial Reports</span>
        </Link>

        <Link to="/audit" className={`${styles.navItem} ${isActive('/audit') ? styles.active : ''}`}>
          <svg viewBox="0 0 24 24">
            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
            <polyline points="14 2 14 8 20 8" />
          </svg>
          <span>Audit Trail</span>
        </Link>

        <Link to="/export" className={`${styles.navItem} ${isActive('/export') ? styles.active : ''}`}>
          <svg viewBox="0 0 24 24">
            <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" />
            <polyline points="7 10 12 15 17 10" />
            <line x1="12" y1="15" x2="12" y2="3" />
          </svg>
          <span>Export Data</span>
        </Link>

        <div className={styles.navGroupLabel} style={{ marginTop: '1rem' }}>
          System
        </div>

        <Link to="/settings" className={`${styles.navItem} ${isActive('/settings') ? styles.active : ''}`}>
          <svg viewBox="0 0 24 24">
            <circle cx="12" cy="12" r="3" />
            <path d="M19.07 4.93l-1.41 1.41M19.07 19.07l-1.41-1.41M4.93 19.07l1.41-1.41M4.93 4.93l1.41 1.41M21 12h-2M5 12H3M12 21v-2M12 5V3" />
          </svg>
          <span>Settings</span>
        </Link>
      </nav>

      <button className={styles.sidebarToggle} onClick={() => setCollapsed(!collapsed)}>
        <svg viewBox="0 0 24 24">
          <polyline points={collapsed ? '9 6 15 12 9 18' : '15 18 9 12 15 6'} />
        </svg>
      </button>
    </aside>
  );
};
