import { Link } from 'react-router-dom';

interface ActionBarProps {
  facility?: string;
}

export const ActionBar = ({ facility }: ActionBarProps) => {
  const stallHoldersLink = `/list-stall-holders?facility=${facility}`;
  const vendorsLink = facility ? `/vendors?facility=${facility}` : '/vendors?facility=npm';

  return (
    <div style={{ display: 'flex', gap: '8px' }}>
      <Link to={stallHoldersLink} className="btn-outline">
        <svg
          viewBox="0 0 24 24"
          style={{
            width: '13px',
            height: '13px',
            stroke: 'currentColor',
            fill: 'none',
            strokeWidth: '2',
            strokeLinecap: 'round',
            strokeLinejoin: 'round',
            verticalAlign: 'middle',
            marginRight: '4px',
          }}
        >
          <line x1="8" y1="6" x2="21" y2="6" />
          <line x1="8" y1="12" x2="21" y2="12" />
          <line x1="8" y1="18" x2="21" y2="18" />
          <circle cx="3" cy="6" r="1" />
          <circle cx="3" cy="12" r="1" />
          <circle cx="3" cy="18" r="1" />
        </svg>
        Stall Holders List
      </Link>
      <Link to={vendorsLink} className="btn-outline">
        <svg
          viewBox="0 0 24 24"
          style={{
            width: '13px',
            height: '13px',
            stroke: 'currentColor',
            fill: 'none',
            strokeWidth: '2',
            strokeLinecap: 'round',
            strokeLinejoin: 'round',
            verticalAlign: 'middle',
            marginRight: '4px',
          }}
        >
          <path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" />
          <path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" />
        </svg>
        Manage Vendors
      </Link>
    </div>
  );
};
