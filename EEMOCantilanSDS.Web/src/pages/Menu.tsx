import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Sidebar } from '@/components/layout/Sidebar';
import styles from './Menu.module.css';


// Mock data interfaces (replace with actual DTOs later)
interface FacilityItem {
  code: string;
  name: string;
  shortName: string;
  iconSvg: string;
  collected: number;
  pending: number;
  totalVendors: number;
  unpaidCount: number;
}

interface TxRecord {
  orNumber: string;
  payorName: string;
  facilityCode: string;
  nature: string;
  amount: number;
  isPaid: boolean;
  isPartial: boolean;
  partialAmount: number;
  collectorName: string;
  collectedAt: Date;
}

interface DelinquentItem {
  name: string;
  stallNo: string;
  facilityCode: string;
  monthsUnpaid: number;
  balance: number;
}

export const Menu = () => {
  // Mock data (replace with TanStack Query hooks later)
  const facilities: FacilityItem[] = [
    {
      code: 'NPM',
      name: 'New Public Market',
      shortName: 'Public Market',
      iconSvg: '<svg viewBox="0 0 24 24"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>',
      collected: 62400,
      pending: 18000,
      totalVendors: 100,
      unpaidCount: 20,
    },
    {
      code: 'TCC',
      name: 'Tampak Commercial Center',
      shortName: 'Tampak Comm.',
      iconSvg: '<svg viewBox="0 0 24 24"><rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 7V5a2 2 0 00-2-2h-4a2 2 0 00-2 2v2"/></svg>',
      collected: 48600,
      pending: 12000,
      totalVendors: 43,
      unpaidCount: 8,
    },
    {
      code: 'NCC',
      name: 'New Commercial Center',
      shortName: 'New Comm. Center',
      iconSvg: '<svg viewBox="0 0 24 24"><path d="M3 3h18v18H3zM3 9h18M3 15h18M9 3v18M15 3v18"/></svg>',
      collected: 56520,
      pending: 8400,
      totalVendors: 28,
      unpaidCount: 5,
    },
    {
      code: 'BBQ',
      name: 'Barbecue Stand',
      shortName: 'BBQ Stand',
      iconSvg: '<svg viewBox="0 0 24 24"><path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/><line x1="4" y1="22" x2="4" y2="15"/></svg>',
      collected: 38400,
      pending: 9600,
      totalVendors: 5,
      unpaidCount: 1,
    },
    {
      code: 'ICE',
      name: 'Iceplant',
      shortName: 'Iceplant',
      iconSvg: '<svg viewBox="0 0 24 24"><line x1="12" y1="2" x2="12" y2="22"/><path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/></svg>',
      collected: 24000,
      pending: 12000,
      totalVendors: 4,
      unpaidCount: 2,
    },
    {
      code: 'SLH',
      name: 'Slaughterhouse',
      shortName: 'Slaughterhouse',
      iconSvg: '<svg viewBox="0 0 24 24"><path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/></svg>',
      collected: 12250,
      pending: 3750,
      totalVendors: 30,
      unpaidCount: 6,
    },
  ];

  const recentTransactions: TxRecord[] = [
    { orNumber: '2315701', payorName: 'Dela Cruz, Maria S.', facilityCode: 'NPM', nature: 'Stall Rental', amount: 900, isPaid: true, isPartial: false, partialAmount: 0, collectorName: 'Clint', collectedAt: new Date(new Date().setHours(10, 14)) },
    { orNumber: '2315702', payorName: 'Guerta, Alvin', facilityCode: 'TCC', nature: 'Monthly Rental', amount: 2400, isPaid: true, isPartial: false, partialAmount: 0, collectorName: 'Clint', collectedAt: new Date(new Date().setHours(10, 32)) },
    { orNumber: '2315703', payorName: 'Bebero, Lucrecia R.', facilityCode: 'NCC', nature: 'Monthly Rental', amount: 1200, isPaid: true, isPartial: false, partialAmount: 0, collectorName: 'Ruel', collectedAt: new Date(new Date().setHours(10, 55)) },
    { orNumber: '2315704', payorName: 'Mendaña, Recto', facilityCode: 'ICE', nature: 'Space Rental', amount: 1000, isPaid: false, isPartial: false, partialAmount: 0, collectorName: 'Ruel', collectedAt: new Date(new Date().setHours(11, 8)) },
    { orNumber: '2315705', payorName: 'Ruaza, Joy', facilityCode: 'BBQ', nature: 'Space Rental', amount: 1600, isPaid: true, isPartial: false, partialAmount: 0, collectorName: 'Clint', collectedAt: new Date(new Date().setHours(11, 20)) },
    { orNumber: '2315706', payorName: 'Trugillo, Elpedia C.', facilityCode: 'NPM', nature: 'Stall Rental', amount: 900, isPaid: false, isPartial: true, partialAmount: 450, collectorName: 'Clint', collectedAt: new Date(new Date().setHours(11, 40)) },
    { orNumber: '2315707', payorName: 'Hog Slaughter', facilityCode: 'SLH', nature: 'Slaughter Fee', amount: 250, isPaid: true, isPartial: false, partialAmount: 0, collectorName: 'Marco', collectedAt: new Date(new Date().setHours(7, 30)) },
    { orNumber: '2315708', payorName: 'Carabao Slaughter', facilityCode: 'SLH', nature: 'Slaughter Fee', amount: 365, isPaid: true, isPartial: false, partialAmount: 0, collectorName: 'Marco', collectedAt: new Date(new Date().setHours(8, 15)) },
  ];

  const delinquentVendors: DelinquentItem[] = [
    { name: 'Salnoden Datumanong', stallNo: '39', facilityCode: 'TCC', monthsUnpaid: 5, balance: 12000 },
    { name: 'Mangubat, Jose P.', stallNo: '09', facilityCode: 'NPM', monthsUnpaid: 4, balance: 3600 },
    { name: 'Orpina, Rebecca S.', stallNo: 'ICE-2', facilityCode: 'ICE', monthsUnpaid: 3, balance: 3000 },
    { name: 'Datumanong, Jessie', stallNo: '40', facilityCode: 'TCC', monthsUnpaid: 3, balance: 7200 },
    { name: 'Garcia, Flordeliza', stallNo: '06', facilityCode: 'NPM', monthsUnpaid: 2, balance: 1800 },
  ];

  // Computed values
  const totalCollected = useMemo(() => facilities.reduce((sum, f) => sum + f.collected, 0), [facilities]);
  const totalPending = useMemo(() => facilities.reduce((sum, f) => sum + f.pending, 0), [facilities]);
  const unpaidCount = useMemo(() => facilities.reduce((sum, f) => sum + f.unpaidCount, 0), [facilities]);
  const totalCollectors = 4;
  const activeFacilitiesCount = useMemo(() => facilities.filter(f => f.collected > 0).length, [facilities]);
  const paidCount = useMemo(() => facilities.reduce((sum, f) => sum + (f.totalVendors - f.unpaidCount), 0), [facilities]);
  const totalVendors = useMemo(() => facilities.reduce((sum, f) => sum + f.totalVendors, 0), [facilities]);
  const collectionRate = useMemo(() => totalVendors === 0 ? 0 : Math.round((paidCount / totalVendors) * 100), [paidCount, totalVendors]);

  const getCollectionRate = (facility: FacilityItem) => {
    return facility.totalVendors === 0 ? 0 : Math.round(((facility.totalVendors - facility.unpaidCount) / facility.totalVendors) * 100);
  };

  const currentDate = new Date();
  const currentMonth = currentDate.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
  const currentFullDate = currentDate.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' });

  return (
    <div className="admin-layout">
      <Sidebar />
      
      <main className="admin-main">
        <header className="topbar">
          <div className="topbar-left">
            <div className="topbar-title">Dashboard</div>
            <div className="topbar-breadcrumb">
              <span>EEMO Admin</span>
              <span className="breadcrumb-sep">/</span>
              <span className="breadcrumb-active">Overview</span>
            </div>
          </div>
          <div className="topbar-right">
            <div className="topbar-date">
              <svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="18" rx="2" /><line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" /></svg>
              {currentFullDate}
            </div>
            <div className="topbar-period">
              <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" /><polyline points="12 6 12 12 16 14" /></svg>
              Collection Period: <strong>{currentMonth}</strong>
            </div>
            <div className="topbar-admin">
              <div className="admin-avatar">A</div>
              <div className="admin-info">
                <div className="admin-name">Administrator</div>
                <div className="admin-role">EEMO Office</div>
              </div>
            </div>
          </div>
        </header>

        <div className="content-area">
          {/* Dashboard Hero Banner */}
          <div className={styles.vsHero}>
            <div className={styles.vsHeroLeft}>
              <div className={styles.vsHeroIcon}>
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <rect x="3" y="3" width="7" height="7" />
                  <rect x="14" y="3" width="7" height="7" />
                  <rect x="3" y="14" width="7" height="7" />
                  <rect x="14" y="14" width="7" height="7" />
                </svg>
              </div>
              <div>
                <div className={styles.vsHeroEyebrow}>EEMO Admin · Overview</div>
                <div className={styles.vsHeroTitle}>Dashboard</div>
                <div className={styles.vsHeroSub}>Collection Period: {currentMonth}</div>
              </div>
            </div>
            <div className={styles.vsHeroStats}>
              <div className={styles.vsHeroStat}>
                <div className={styles.vsHeroVal}>₱{totalCollected.toLocaleString()}</div>
                <div className={styles.vsHeroKey}>Total Collected</div>
                <div className={styles.vsHeroSub2}>{currentMonth}</div>
              </div>
              <div className={styles.vsHeroDivider}></div>
              <div className={styles.vsHeroStat}>
                <div className={styles.vsHeroVal}>₱{totalPending.toLocaleString()}</div>
                <div className={styles.vsHeroKey}>Pending / Unpaid</div>
                <div className={styles.vsHeroSub2}>{unpaidCount} vendors</div>
              </div>
              <div className={styles.vsHeroDivider}></div>
              <div className={styles.vsHeroStat}>
                <div className={styles.vsHeroVal}>{paidCount}</div>
                <div className={styles.vsHeroKey}>Paid Transactions</div>
                <div className={styles.vsHeroSub2}>{activeFacilitiesCount} facilities covered</div>
              </div>
              <div className={styles.vsHeroDivider}></div>
              <div className={styles.vsHeroStat}>
                <div className={styles.vsHeroVal}>{collectionRate}%</div>
                <div className={styles.vsHeroKey}>Collection Rate</div>
                <div className={styles.vsHeroSub2}>{totalCollectors} active collectors</div>
              </div>
            </div>
            <div className={styles.vsHeroProgress}>
              <div className={styles.vsHeroProgressFill} style={{ width: `${collectionRate}%` }}></div>
            </div>
          </div>

          {/* Facility Cards */}
          <div className="section-header">
            <div className="section-title-block">
              <div className="section-eyebrow">Facilities</div>
              <div className="section-heading">Revenue by Facility — {currentMonth}</div>
            </div>
            <Link to="/reports" className="btn-outline">View Full Report</Link>
          </div>

          <div className={styles.facilityGrid}>
            {facilities.map((fac) => (
              <div key={fac.code} className={`${styles.facilityCard} ${fac.unpaidCount > 0 ? styles.hasAlert : ''}`}>
                <div className={styles.facilityCardHeader}>
                  <div className={styles.facilityCardIcon} dangerouslySetInnerHTML={{ __html: fac.iconSvg }} />
                  <div className={styles.facilityCardMeta}>
                    <div className={styles.facilityCardCode}>{fac.code}</div>
                    <div className={styles.facilityCardName}>{fac.name}</div>
                  </div>
                  {fac.unpaidCount > 0 && (
                    <div className={styles.facilityAlertDot} title="Has unpaid vendors"></div>
                  )}
                </div>

                <div className={styles.facilityCardStats}>
                  <div className={styles.facStat}>
                    <div className={styles.facStatVal}>₱{fac.collected.toLocaleString()}</div>
                    <div className={styles.facStatKey}>Collected</div>
                  </div>
                  <div className={styles.facStat}>
                    <div className={`${styles.facStatVal} ${styles.unpaidVal}`}>{fac.unpaidCount}</div>
                    <div className={styles.facStatKey}>Unpaid</div>
                  </div>
                  <div className={styles.facStat}>
                    <div className={styles.facStatVal}>{fac.totalVendors}</div>
                    <div className={styles.facStatKey}>Vendors</div>
                  </div>
                </div>

                <div className={styles.facilityProgressWrap}>
                  <div className={styles.facilityProgressBar}>
                    <div className={styles.facilityProgressFill} style={{ width: `${getCollectionRate(fac)}%` }}></div>
                  </div>
                  <span className={styles.facilityProgressPct}>{getCollectionRate(fac)}%</span>
                </div>

                <Link to={`/facility/${fac.code.toLowerCase()}`} className={styles.facilityCardBtn}>
                  Manage Stalls
                  <svg viewBox="0 0 24 24"><polyline points="9 6 15 12 9 18" /></svg>
                </Link>
              </div>
            ))}
          </div>

          {/* Bottom Row */}
          <div className={styles.bottomRow}>
            <div className="panel">
              <div className="panel-header">
                <div className="panel-title">Recent Transactions</div>
                <Link to="/audit" className="panel-link">View All</Link>
              </div>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>OR #</th>
                    <th>Payor</th>
                    <th>Facility</th>
                    <th>Nature</th>
                    <th>Amount</th>
                    <th>Collector</th>
                    <th>Time</th>
                  </tr>
                </thead>
                <tbody>
                  {recentTransactions.map((tx) => (
                    <tr key={tx.orNumber}>
                      <td>#{tx.orNumber}</td>
                      <td>{tx.payorName}</td>
                      <td><span>{tx.facilityCode}</span></td>
                      <td>{tx.nature}</td>
                      <td>₱{tx.amount.toLocaleString()}</td>
                      <td>{tx.collectorName}</td>
                      <td>{tx.collectedAt.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="panel panel-narrow">
              <div className="panel-header">
                <div className="panel-title">⚠ Overdue Vendors</div>
                <Link to="/vendors?filter=delinquent" className="panel-link">View All</Link>
              </div>
              <div className={styles.delinquentList}>
                {delinquentVendors.map((v, idx) => (
                  <div key={idx} className={styles.delinquentItem}>
                    <div className={styles.delinquentInfo}>
                      <div className={styles.delinquentName}>{v.name}</div>
                      <div className={styles.delinquentMeta}>
                        <span className="code-badge">{v.facilityCode}</span>
                        Stall {v.stallNo}
                      </div>
                    </div>
                    <div className={styles.delinquentRight}>
                      <div className={styles.delinquentAmount}>₱{v.balance.toLocaleString()}</div>
                      <div className={`${styles.delinquentMonths} ${v.monthsUnpaid >= 3 ? 'text-red' : 'text-gold'}`}>
                        {v.monthsUnpaid} mo. unpaid
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </main>
    </div>
  );
};
