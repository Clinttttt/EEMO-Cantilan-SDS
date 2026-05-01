import { ReactNode } from 'react';
import styles from './Toolbar.module.css';

export interface ToolbarAction {
  label: string;
  icon?: string;
  className?: string;
  title?: string;
  onClick: () => void;
  isChild?: boolean;
  childHtml?: string;
}

interface ToolbarProps {
  searchQuery?: string;
  onSearch?: (query: string) => void;
  activeFilter?: string;
  onFilterChanged?: (filter: string) => void;
  filters?: string[];
  actions?: ToolbarAction[];
  showSearch?: boolean;
  searchPlaceholder?: string;
  middleContent?: ReactNode;
}

export const Toolbar = ({
  searchQuery = '',
  onSearch,
  activeFilter = 'All',
  onFilterChanged,
  filters = [],
  actions = [],
  showSearch = true,
  searchPlaceholder = 'Search stall no. or occupant…',
  middleContent,
}: ToolbarProps) => {
  const handleSearchInput = (e: React.ChangeEvent<HTMLInputElement>) => {
    onSearch?.(e.target.value);
  };

  const handleSearchClear = () => {
    onSearch?.('');
  };

  const handleFilterChange = (filter: string) => {
    onFilterChanged?.(filter);
  };

  return (
    <div className={styles.toolbarUnified}>
      {/* LEFT: Search Box */}
      {showSearch && (
        <div className={styles.searchBox}>
          <svg viewBox="0 0 24 24" className={styles.searchIcon}>
            <circle cx="11" cy="11" r="8" />
            <line x1="21" y1="21" x2="16.65" y2="16.65" />
          </svg>
          <input
            type="text"
            className={styles.searchInput}
            placeholder={searchPlaceholder}
            value={searchQuery}
            onChange={handleSearchInput}
          />
          {searchQuery && (
            <span className={styles.searchClear} onClick={handleSearchClear}>
              ×
            </span>
          )}
        </div>
      )}

      {/* CENTER-LEFT: Filter Tabs */}
      {filters.length > 0 && (
        <div className={styles.filterTabsInline}>
          {filters.map((filter) => (
            <div
              key={filter}
              className={`${styles.filterTab} ${activeFilter === filter ? styles.active : ''}`}
              onClick={() => handleFilterChange(filter)}
            >
              {filter}
            </div>
          ))}
        </div>
      )}

      {/* CENTER: Middle Content */}
      {middleContent && <div className={styles.toolbarMiddle}>{middleContent}</div>}

      {/* RIGHT: Actions & Buttons */}
      <div className={styles.toolbarActions}>
        {actions.map((action, index) => {
          if (action.isChild && action.childHtml) {
            return <span key={index} dangerouslySetInnerHTML={{ __html: action.childHtml }} />;
          }

          return (
            <button
              key={index}
              className={`${styles.btn} ${action.className || styles.btnOutline}`}
              onClick={action.onClick}
              title={action.title}
            >
              {action.icon && (
                <span className={styles.btnIcon} dangerouslySetInnerHTML={{ __html: action.icon }} />
              )}
              {action.label}
            </button>
          );
        })}
      </div>
    </div>
  );
};
