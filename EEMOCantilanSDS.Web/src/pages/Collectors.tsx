import { useState, useMemo } from 'react';
import { Sidebar } from '@/components/layout/Sidebar';
import { Spinner } from '@/components/shared/Spinner';
import { useCollectors, useCollectorActivity } from '@/hooks/queries/useCollectors';
import { useCreateCollector } from '@/hooks/mutations/useCollectorMutations';
import { FacilityCode } from '@/types/enums';
import type { CollectorListDto, CreateCollectorCommand } from '@/types/dto';
import styles from './Collectors.module.css';

interface FacilityOption {
  code: FacilityCode;
  name: string;
}

const AVAILABLE_FACILITIES: FacilityOption[] = [
  { code: FacilityCode.NPM, name: 'New Public Market' },
  { code: FacilityCode.TCC, name: 'Tampak Commercial Center' },
  { code: FacilityCode.NCC, name: 'New Commercial Center' },
  { code: FacilityCode.BBQ, name: 'Barbecue Stand' },
  { code: FacilityCode.ICE, name: 'Iceplant' },
  { code: FacilityCode.SLH, name: 'Slaughterhouse' },
];

const STATUS_FILTERS = ['All', 'Active', 'Inactive'];

export const Collectors = () => {
  const [searchQuery, setSearchQuery] = useState('');
  const [activeFilter, setActiveFilter] = useState('All');
  const [showModal, setShowModal] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [showActivity, setShowActivity] = useState(false);
  const [activityCollectorId, setActivityCollectorId] = useState<string>('');
  const [formError, setFormError] = useState('');
  const [form, setForm] = useState<CreateCollectorCommand>({
    fullName: '',
    employeeId: '',
    contactNumber: '',
    email: '',
    username: '',
    password: '',
    assignedFacilities: [],
  });

  const { data: collectors, isLoading, error } = useCollectors();
  const { data: activityDetails } = useCollectorActivity(activityCollectorId);
  const createCollector = useCreateCollector();

  const filteredCollectors = useMemo(() => {
    if (!collectors) return [];
    return collectors.filter((c) => {
      const matchFilter =
        activeFilter === 'All' ||
        (activeFilter === 'Active' && c.isActive) ||
        (activeFilter === 'Inactive' && !c.isActive);
      const matchSearch =
        !searchQuery ||
        c.fullName.toLowerCase().includes(searchQuery.toLowerCase()) ||
        c.employeeId.toLowerCase().includes(searchQuery.toLowerCase()) ||
        c.assignedFacilities.some((f) => f.toString().toLowerCase().includes(searchQuery.toLowerCase()));
      return matchFilter && matchSearch;
    });
  }, [collectors, activeFilter, searchQuery]);

  const activeRate = collectors && collectors.length > 0
    ? (collectors.filter((c) => c.isActive).length * 100) / collectors.length
    : 0;

  const openCreate = () => {
    setIsEditing(false);
    setForm({
      fullName: '',
      employeeId: '',
      contactNumber: '',
      email: '',
      username: '',
      password: '',
      assignedFacilities: [],
    });
    setFormError('');
    setShowModal(true);
  };

  const openActivity = (c: CollectorListDto) => {
    setActivityCollectorId(c.id);
    setShowActivity(true);
  };

  const toggleFacility = (code: FacilityCode) => {
    setForm((prev) => ({
      ...prev,
      assignedFacilities: prev.assignedFacilities.includes(code)
        ? prev.assignedFacilities.filter((f) => f !== code)
        : [...prev.assignedFacilities, code],
    }));
  };

  const saveCollector = async () => {
    if (!form.fullName.trim()) {
      setFormError('Full name is required.');
      return;
    }
    if (!form.employeeId.trim()) {
      setFormError('Employee ID is required.');
      return;
    }
    if (!form.username.trim()) {
      setFormError('Username is required.');
      return;
    }
    if (!isEditing && !form.password.trim()) {
      setFormError('Password is required for new accounts.');
      return;
    }
    if (form.assignedFacilities.length === 0) {
      setFormError('Assign at least one facility.');
      return;
    }

    try {
      await createCollector.mutateAsync(form);
      setShowModal(false);
      setFormError('');
    } catch (error: any) {
      setFormError(error.response?.data?.error || 'Failed to create collector.');
    }
  };

  if (isLoading) return <Spinner />;
  if (error) return <div className="p-6 text-red">Error: {error.message}</div>;

  return (
    <div className="admin-layout">
      <Sidebar />

      <main className="admin-main">
        <header className="topbar">
          <div className="topbar-left">
            <div className="topbar-title">Collectors</div>
            <div className="topbar-breadcrumb">
              <span>EEMO Admin</span>
              <span className="breadcrumb-sep">/</span>
              <span className="breadcrumb-active">Collectors</span>
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
              <div className="admin-avatar">A</div>
              <div className="admin-info">
                <div className="admin-name">Administrator</div>
                <div className="admin-role">EEMO Office</div>
              </div>
            </div>
          </div>
        </header>

        <div className="content-area">
          <div className={styles.vsHero}>
            <div className={styles.vsHeroLeft}>
              <div className={styles.vsHeroIcon}>
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2" />
                  <circle cx="9" cy="7" r="4" />
                  <path d="M23 21v-2a4 4 0 00-3-3.87" />
                  <path d="M16 3.13a4 4 0 010 7.75" />
                </svg>
              </div>
              <div>
                <div className={styles.vsHeroEyebrow}>EEMO Admin · Collectors</div>
                <div className={styles.vsHeroTitle}>Revenue Collectors</div>
                <div className={styles.vsHeroSub}>
                  Collection Period: {new Date().toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
                </div>
              </div>
            </div>
            <div className={styles.vsHeroStats}>
              <div className={styles.vsHeroStat}>
                <div className={styles.vsHeroVal}>{collectors?.length ?? 0}</div>
                <div className={styles.vsHeroKey}>Total Collectors</div>
                <div className={styles.vsHeroSub2}>{collectors?.filter((c) => c.isActive).length ?? 0} active</div>
              </div>
              <div className={styles.vsHeroDivider}></div>
              <div className={styles.vsHeroStat}>
                <div className={styles.vsHeroVal}>
                  ₱{(collectors?.reduce((sum, c) => sum + c.collectedThisMonth, 0) ?? 0).toLocaleString()}
                </div>
                <div className={styles.vsHeroKey}>Total Collected</div>
                <div className={styles.vsHeroSub2}>
                  {new Date().toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
                </div>
              </div>
              <div className={styles.vsHeroDivider}></div>
              <div className={styles.vsHeroStat}>
                <div className={styles.vsHeroVal}>
                  {collectors?.reduce((sum, c) => sum + c.assignedFacilities.length, 0) ?? 0}
                </div>
                <div className={styles.vsHeroKey}>Facility Assignments</div>
                <div className={styles.vsHeroSub2}>Across all collectors</div>
              </div>
              <div className={styles.vsHeroDivider}></div>
              <div className={styles.vsHeroStat}>
                <div className={styles.vsHeroVal}>{collectors?.filter((c) => !c.isActive).length ?? 0}</div>
                <div className={styles.vsHeroKey}>Inactive Accounts</div>
                <div className={styles.vsHeroSub2}>Deactivated or suspended</div>
              </div>
            </div>
            <div className={styles.vsHeroProgress}>
              <div className={styles.vsHeroProgressFill} style={{ width: `${activeRate}%` }}></div>
            </div>
          </div>

          <div className={styles.pageToolbar}>
            <div className={styles.toolbarLeft}>
              <div className={styles.searchBox}>
                <svg viewBox="0 0 24 24">
                  <circle cx="11" cy="11" r="8" />
                  <line x1="21" y1="21" x2="16.65" y2="16.65" />
                </svg>
                <input
                  type="text"
                  placeholder="Search name, ID, facility…"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                />
                {searchQuery && (
                  <span className={styles.searchClear} onClick={() => setSearchQuery('')}>
                    ×
                  </span>
                )}
              </div>
              <div className={styles.filterTabs}>
                {STATUS_FILTERS.map((f) => (
                  <div
                    key={f}
                    className={`${styles.filterTab} ${activeFilter === f ? styles.active : ''}`}
                    onClick={() => setActiveFilter(f)}
                  >
                    {f}
                  </div>
                ))}
              </div>
            </div>
            <button className="btn-primary" onClick={openCreate}>
              <svg viewBox="0 0 24 24">
                <line x1="12" y1="5" x2="12" y2="19" />
                <line x1="5" y1="12" x2="19" y2="12" />
              </svg>
              Add Collector
            </button>
          </div>

          <div className="panel">
            <div className="panel-header">
              <div className="panel-title">Collector Accounts</div>
              <div className={styles.panelCount}>{filteredCollectors.length} records</div>
            </div>

            {filteredCollectors.length === 0 ? (
              <div className={styles.emptyState}>
                <svg viewBox="0 0 24 24">
                  <path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2" />
                  <circle cx="9" cy="7" r="4" />
                </svg>
                <div className={styles.emptyTitle}>No Collectors Found</div>
                <div className={styles.emptySub}>Try adjusting your search or filter.</div>
              </div>
            ) : (
              <div className={styles.tableWrap}>
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Collector</th>
                      <th>Employee ID</th>
                      <th>Assigned Facilities</th>
                      <th>Collected (This Month)</th>
                      <th>Transactions</th>
                      <th>Last Active</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredCollectors.map((c) => (
                      <tr key={c.id} className={!c.isActive ? styles.rowInactive : ''}>
                        <td>
                          <div className={styles.collectorCell}>
                            <div>
                              <div>{c.fullName}</div>
                              <div className={styles.collectorEmail}>{c.email}</div>
                            </div>
                          </div>
                        </td>
                        <td>
                          <span>{c.employeeId}</span>
                        </td>
                        <td>
                          <div className={styles.facilityTags}>
                            {c.assignedFacilities.map((f) => (
                              <span key={f}>{FacilityCode[f]}</span>
                            ))}
                          </div>
                        </td>
                        <td>
                          <span>₱{c.collectedThisMonth.toLocaleString()}</span>
                        </td>
                        <td>{c.transactions}</td>
                        <td>
                          {c.lastActiveAt
                            ? new Date(c.lastActiveAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
                            : 'Never'}
                        </td>
                        <td>
                          <div className={styles.actionBtns}>
                            <button className={styles.actionBtn} title="View Activity" onClick={() => openActivity(c)}>
                              <svg viewBox="0 0 24 24">
                                <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                                <circle cx="12" cy="12" r="3" />
                              </svg>
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      </main>

      {showModal && (
        <div className={styles.eemoModalOverlay} onClick={() => setShowModal(false)}>
          <div className={styles.eemoModal} onClick={(e) => e.stopPropagation()}>
            <div className={styles.eemoModalHeader}>
              <div className={styles.eemoModalTitle}>{isEditing ? 'Edit Collector' : 'Add New Collector'}</div>
              <button className={styles.eemoModalClose} onClick={() => setShowModal(false)}>
                <svg viewBox="0 0 24 24">
                  <line x1="18" y1="6" x2="6" y2="18" />
                  <line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            </div>

            <div className={styles.eemoModalBody}>
              <div className={styles.formRow2}>
                <div className={styles.formGroup}>
                  <label className={styles.formLabel}>
                    Full Name <span className={styles.req}>*</span>
                  </label>
                  <input
                    className={styles.formInput}
                    type="text"
                    placeholder="e.g. Juan dela Cruz"
                    value={form.fullName}
                    onChange={(e) => setForm({ ...form, fullName: e.target.value })}
                  />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.formLabel}>
                    Employee ID <span className={styles.req}>*</span>
                  </label>
                  <input
                    className={styles.formInput}
                    type="text"
                    placeholder="e.g. EEMO-2024-001"
                    value={form.employeeId}
                    onChange={(e) => setForm({ ...form, employeeId: e.target.value })}
                  />
                </div>
              </div>

              <div className={styles.formRow2}>
                <div className={styles.formGroup}>
                  <label className={styles.formLabel}>Contact Number</label>
                  <input
                    className={styles.formInput}
                    type="tel"
                    placeholder="+63 9xx xxx xxxx"
                    value={form.contactNumber}
                    onChange={(e) => setForm({ ...form, contactNumber: e.target.value })}
                  />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.formLabel}>Email Address</label>
                  <input
                    className={styles.formInput}
                    type="email"
                    placeholder="collector@cantilan.gov.ph"
                    value={form.email}
                    onChange={(e) => setForm({ ...form, email: e.target.value })}
                  />
                </div>
              </div>

              <div className={styles.formRow2}>
                <div className={styles.formGroup}>
                  <label className={styles.formLabel}>
                    Username <span className={styles.req}>*</span>
                  </label>
                  <input
                    className={styles.formInput}
                    type="text"
                    placeholder="Login username"
                    value={form.username}
                    onChange={(e) => setForm({ ...form, username: e.target.value })}
                  />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.formLabel}>
                    {isEditing ? 'New Password' : 'Password'} <span className={styles.req}>{isEditing ? '' : '*'}</span>
                  </label>
                  <input
                    className={styles.formInput}
                    type="password"
                    placeholder={isEditing ? 'Leave blank to keep current' : 'Set initial password'}
                    value={form.password}
                    onChange={(e) => setForm({ ...form, password: e.target.value })}
                  />
                </div>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.formLabel}>
                  Assigned Facilities <span className={styles.req}>*</span>
                </label>
                <div className={styles.facilityCheckboxes}>
                  {AVAILABLE_FACILITIES.map((fac) => {
                    const isChecked = form.assignedFacilities.includes(fac.code);
                    return (
                      <div
                        key={fac.code}
                        className={`${styles.facCheckbox} ${isChecked ? styles.checked : ''}`}
                        onClick={() => toggleFacility(fac.code)}
                      >
                        <div className={styles.facCheckBox}>
                          {isChecked && (
                            <svg viewBox="0 0 24 24">
                              <polyline points="20 6 9 17 4 12" />
                            </svg>
                          )}
                        </div>
                        <span className="code-badge">{FacilityCode[fac.code]}</span>
                        <span className={styles.facCheckName}>{fac.name}</span>
                      </div>
                    );
                  })}
                </div>
              </div>

              {formError && (
                <div className={styles.formError}>
                  <svg viewBox="0 0 24 24">
                    <circle cx="12" cy="12" r="10" />
                    <line x1="12" y1="8" x2="12" y2="12" />
                    <line x1="12" y1="16" x2="12.01" y2="16" />
                  </svg>
                  {formError}
                </div>
              )}
            </div>

            <div className={styles.eemoModalFooter}>
              <button className="btn-ghost" onClick={() => setShowModal(false)}>
                Cancel
              </button>
              <button className="btn-primary" onClick={saveCollector}>
                {isEditing ? 'Save Changes' : 'Create Account'}
              </button>
            </div>
          </div>
        </div>
      )}

      {showActivity && activityDetails && (
        <div className={styles.eemoModalOverlay} onClick={() => setShowActivity(false)}>
          <div className={`${styles.eemoModal} ${styles.eemoModalWide}`} onClick={(e) => e.stopPropagation()}>
            <div className={styles.eemoModalHeader}>
              <div>
                <div className={styles.eemoModalTitle}>{activityDetails.fullName}</div>
                <div className={styles.eemoModalSub}>
                  Activity — {new Date().toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
                </div>
              </div>
              <button className={styles.eemoModalClose} onClick={() => setShowActivity(false)}>
                <svg viewBox="0 0 24 24">
                  <line x1="18" y1="6" x2="6" y2="18" />
                  <line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            </div>
            <div className={styles.eemoModalBody}>
              <div className={styles.activityStats}>
                <div className={styles.actStat}>
                  <div className={styles.actStatVal}>₱{activityDetails.collectedThisMonth.toLocaleString()}</div>
                  <div className={styles.actStatKey}>Collected This Month</div>
                </div>
                <div className={styles.actStat}>
                  <div className={styles.actStatVal}>{activityDetails.transactions}</div>
                  <div className={styles.actStatKey}>Transactions</div>
                </div>
                <div className={styles.actStat}>
                  <div className={styles.actStatVal}>{activityDetails.facilitiesCount}</div>
                  <div className={styles.actStatKey}>Facilities</div>
                </div>
                <div className={styles.actStat}>
                  <div className={styles.actStatVal}>
                    {activityDetails.lastActiveAt
                      ? new Date(activityDetails.lastActiveAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
                      : 'Never'}
                  </div>
                  <div className={styles.actStatKey}>Last Active</div>
                </div>
              </div>

              <div className={styles.actSectionLabel}>Recent Transactions</div>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>OR #</th>
                    <th>Payor</th>
                    <th>Facility</th>
                    <th>Nature</th>
                    <th>Amount</th>
                    <th>Status</th>
                    <th>Date & Time</th>
                  </tr>
                </thead>
                <tbody>
                  {activityDetails.recentTransactions.map((tx, idx) => (
                    <tr key={idx}>
                      <td className={styles.mono}>#{tx.orNumber}</td>
                      <td>{tx.payorName}</td>
                      <td>
                        <span className="code-badge">{FacilityCode[tx.facility]}</span>
                      </td>
                      <td className={styles.tdNature}>{tx.nature}</td>
                      <td className={styles.amountVal}>₱{tx.amount.toLocaleString()}</td>
                      <td>
                        <span className={`${styles.statusPill} ${tx.status === 'Paid' ? styles.pillActive : styles.pillInactive}`}>
                          {tx.status}
                        </span>
                      </td>
                      <td className={styles.tdDate}>
                        {new Date(tx.transactionDate).toLocaleDateString('en-US', {
                          month: 'short',
                          day: 'numeric',
                          hour: 'numeric',
                          minute: '2-digit',
                          hour12: true,
                        })}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className={styles.eemoModalFooter}>
              <button className="btn-ghost" onClick={() => setShowActivity(false)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
