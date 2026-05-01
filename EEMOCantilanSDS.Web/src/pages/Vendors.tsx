import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { Sidebar } from '../components/layout/Sidebar';
import { PaymentHistoryModal } from '../components/features/vendors/PaymentHistoryModal';
import { AddVendorModal, VendorModalForm, VendorFacilityOption } from '../components/features/vendors/AddVendorModal';
import { VendorDetailPanel } from '../components/features/vendors/VendorDetailPanel';

// Hardcoded data matching Blazor
interface VendorEntry {
  id: number;
  facilityCode: string;
  stallNo: string;
  actualOccupant: string;
  contractName: string;
  section: string;
  orNo?: string;
  areaSqm: number;
  contractDate?: Date;
  contractYears: number;
  monthlyRate: number;
  actualMonthlyRental: number;
  areaLocation: string;
  feeTypes: string[];
  isActive: boolean;
  isPaidThisMonth: boolean;
  isPartialThisMonth: boolean;
  partialAmount: number;
  paymentHistory: Record<string, boolean>;
  dailyCollections: Record<string, boolean>;
  dailyFishKilos: Record<string, number>;
}

interface FacilityTab {
  code: string;
  name: string;
  shortName: string;
  unpaidCount: number;
}

const facilityTabs: FacilityTab[] = [
  { code: 'NPM', name: 'New Public Market', shortName: 'Public Market', unpaidCount: 20 },
  { code: 'TCC', name: 'Tampak Commercial Center', shortName: 'Tampak CC', unpaidCount: 8 },
  { code: 'NCC', name: 'New Commercial Center', shortName: 'New CC', unpaidCount: 5 },
  { code: 'BBQ', name: 'Barbecue Stand', shortName: 'BBQ Stand', unpaidCount: 1 },
  { code: 'ICE', name: 'Iceplant', shortName: 'Iceplant', unpaidCount: 2 },
  { code: 'SLH', name: 'Slaughterhouse', shortName: 'Slaughterhouse', unpaidCount: 6 },
];

const makeHistory = (paidCount: number): Record<string, boolean> => {
  const dict: Record<string, boolean> = {};
  for (let i = 11; i >= 0; i--) {
    const date = new Date();
    date.setMonth(date.getMonth() - i);
    const key = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
    dict[key] = (11 - i) < paidCount;
  }
  return dict;
};

const vendors: VendorEntry[] = [
  { id: 1, facilityCode: 'NPM', stallNo: '01', actualOccupant: 'Trugillo, Elpedia C.', contractName: 'Nila F. Andoy', section: 'Vegetable Area', areaSqm: 4.8, contractDate: new Date(2023, 5, 7), contractYears: 3, monthlyRate: 900, actualMonthlyRental: 0, orNo: 'OR-2024-001', feeTypes: ['Electricity', 'Water'], isActive: true, isPaidThisMonth: true, isPartialThisMonth: false, partialAmount: 0, paymentHistory: makeHistory(10), dailyCollections: {}, dailyFishKilos: {}, areaLocation: '' },
  { id: 2, facilityCode: 'NPM', stallNo: '02', actualOccupant: 'Bohol, Marilyn C.', contractName: 'Marilyn C. Bohol', section: 'Vegetable Area', areaSqm: 4.8, contractDate: new Date(2023, 5, 7), contractYears: 3, monthlyRate: 900, actualMonthlyRental: 0, orNo: 'OR-2024-002', feeTypes: ['Electricity', 'Water'], isActive: true, isPaidThisMonth: true, isPartialThisMonth: false, partialAmount: 0, paymentHistory: makeHistory(12), dailyCollections: {}, dailyFishKilos: {}, areaLocation: '' },
  { id: 20, facilityCode: 'TCC', stallNo: '31', actualOccupant: 'Pude, Myra', contractName: 'Myra Pude', section: 'TCC', areaSqm: 10.5, contractDate: new Date(2023, 5, 7), contractYears: 3, monthlyRate: 2400, actualMonthlyRental: 0, orNo: 'OR-2024-020', feeTypes: [], isActive: true, isPaidThisMonth: true, isPartialThisMonth: false, partialAmount: 0, paymentHistory: makeHistory(12), dailyCollections: {}, dailyFishKilos: {}, areaLocation: '' },
];

export const Vendors = () => {
  const navigate = useNavigate();
  const [activeFacility, setActiveFacility] = useState('NPM');
  const [activeStatus, setActiveStatus] = useState('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [facDropdownOpen, setFacDropdownOpen] = useState(false);
  const [showDetail, setShowDetail] = useState(false);
  const [detailVendor, setDetailVendor] = useState<VendorEntry | null>(null);
  const [calendarMonth, setCalendarMonth] = useState(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  const [monthlyCalYear, setMonthlyCalYear] = useState(new Date().getFullYear());
  const [showHistory, setShowHistory] = useState(false);
  const [historyVendor, setHistoryVendor] = useState<VendorEntry | null>(null);
  const [showModal, setShowModal] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [form, setForm] = useState<VendorModalForm>({
    facilityCode: '',
    stallNo: '',
    actualOccupant: '',
    contractName: '',
    selectedSection: '',
    areaSqm: 0,
    contractDate: undefined,
    contractYears: 3,
    monthlyRate: 0,
    actualMonthlyRental: 0,
    areaLocation: '',
    feeTypes: []
  });
  const [formError, setFormError] = useState('');
  const [showConfirm, setShowConfirm] = useState(false);
  const [confirmTarget, setConfirmTarget] = useState<VendorEntry | null>(null);

  const statusFilters = ['All', 'Paid', 'Partial', 'Unpaid', 'Closed'];
  const activeFacilityTab = facilityTabs.find(f => f.code === activeFacility)!;

  const filteredVendors = useMemo(() => {
    return vendors.filter(v => {
      if (v.facilityCode !== activeFacility) return false;
      const matchStatus = activeStatus === 'Paid' ? v.isPaidThisMonth && v.isActive :
        activeStatus === 'Partial' ? v.isPartialThisMonth && v.isActive :
        activeStatus === 'Unpaid' ? !v.isPaidThisMonth && !v.isPartialThisMonth && v.isActive :
        activeStatus === 'Closed' ? !v.isActive : true;
      const matchSearch = !searchQuery ||
        v.actualOccupant.toLowerCase().includes(searchQuery.toLowerCase()) ||
        v.stallNo.toLowerCase().includes(searchQuery.toLowerCase()) ||
        v.contractName.toLowerCase().includes(searchQuery.toLowerCase());
      return matchStatus && matchSearch;
    }).sort((a, b) => a.stallNo.localeCompare(b.stallNo));
  }, [activeFacility, activeStatus, searchQuery]);

  const allVendors = vendors;
  const activeVendors = allVendors.filter(v => v.isActive);
  const paidThisMonth = allVendors.filter(v => v.isActive && v.isPaidThisMonth).length;
  const unpaidThisMonth = allVendors.filter(v => v.isActive && !v.isPaidThisMonth).length;
  const monthlyTarget = activeVendors.reduce((sum, v) => sum + v.monthlyRate, 0);
  const overallRate = activeVendors.length > 0 ? (paidThisMonth * 100.0 / activeVendors.length) : 0;

  const openDetail = (v: VendorEntry) => {
    setDetailVendor(v);
    setShowDetail(true);
    setCalendarMonth(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  };

  const openHistory = (v: VendorEntry) => {
    setHistoryVendor(v);
    setShowHistory(true);
  };

  const openCreate = () => {
    setIsEditing(false);
    setForm({
      facilityCode: activeFacility,
      stallNo: '',
      actualOccupant: '',
      contractName: '',
      selectedSection: '',
      areaSqm: 0,
      contractDate: new Date(2023, 5, 7),
      contractYears: 3,
      monthlyRate: 0,
      actualMonthlyRental: 0,
      areaLocation: '',
      feeTypes: []
    });
    setFormError('');
    autoPopulateFees();
    setShowModal(true);
  };

  const openEdit = (v: VendorEntry) => {
    setIsEditing(true);
    let selectedSection = '';
    if (v.facilityCode === 'NPM' && v.section) {
      if (v.section.includes('Vegetable')) selectedSection = 'Vegetable';
      else if (v.section.includes('Fish')) selectedSection = 'Fish';
      else if (v.section.includes('Meat')) selectedSection = 'Meat';
    }
    setForm({
      facilityCode: v.facilityCode,
      stallNo: v.stallNo,
      actualOccupant: v.actualOccupant,
      contractName: v.contractName,
      selectedSection,
      areaSqm: v.areaSqm,
      contractDate: v.contractDate,
      contractYears: v.contractYears,
      monthlyRate: v.monthlyRate,
      actualMonthlyRental: v.actualMonthlyRental,
      areaLocation: v.areaLocation,
      feeTypes: [...v.feeTypes]
    });
    setFormError('');
    setShowModal(true);
  };

  const autoPopulateFees = () => {
    const newFeeTypes: string[] = [];
    if (form.facilityCode === 'NPM') {
      newFeeTypes.push('Electricity', 'Water');
      if (form.selectedSection === 'Fish') newFeeTypes.push('Fish ₱1/kg');
      if (form.monthlyRate <= 0) setForm(prev => ({ ...prev, monthlyRate: 900 }));
    }
    setForm(prev => ({ ...prev, feeTypes: newFeeTypes }));
  };

  const onFacilityChanged = () => {
    setForm(prev => ({ ...prev, selectedSection: '', feeTypes: [] }));
    autoPopulateFees();
  };

  const onSectionChanged = () => {
    setForm(prev => ({ ...prev, feeTypes: [] }));
    autoPopulateFees();
  };

  const toggleFee = (fee: string) => {
    setForm(prev => ({
      ...prev,
      feeTypes: prev.feeTypes.includes(fee)
        ? prev.feeTypes.filter(f => f !== fee)
        : [...prev.feeTypes, fee]
    }));
  };

  const saveVendor = () => {
    // Validation
    if (!form.facilityCode) { setFormError('Please select a facility.'); return; }
    if (!form.stallNo) { setFormError('Stall number is required.'); return; }
    if (!form.actualOccupant) { setFormError('Actual occupant name is required.'); return; }
    if (form.facilityCode === 'NPM' && !form.selectedSection) { setFormError('Please select a section for NPM.'); return; }
    if (form.monthlyRate <= 0) { setFormError('Monthly rate must be greater than 0.'); return; }

    // In real app, call API here
    console.log('Saving vendor:', form);
    setShowModal(false);
  };

  const facilityTabsAsOptions = (): VendorFacilityOption[] => {
    return facilityTabs.map(f => ({ code: f.code, name: f.name }));
  };

  const viewProfile = (v: VendorEntry) => {
    navigate(`/profile/${v.facilityCode}/${v.stallNo}`);
  };

  const toggleVendor = (v: VendorEntry) => {
    setConfirmTarget(v);
    setShowConfirm(true);
  };

  const confirmToggle = () => {
    if (!confirmTarget) return;
    // In real app, call API to toggle status
    console.log('Toggle status for:', confirmTarget.actualOccupant);
    setShowConfirm(false);
    setConfirmTarget(null);
  };

  return (
    <div className="admin-layout">
      <Sidebar />

      <main className="admin-main">
        <header className="topbar">
          <div className="topbar-left">
            <div className="topbar-title">Vendors & Stalls</div>
            <div className="topbar-breadcrumb">
              <span>EEMO Admin</span>
              <span className="breadcrumb-sep">/</span>
              <span className="breadcrumb-active">Vendors & Stalls</span>
            </div>
          </div>
          <div className="topbar-right">
            <div className="topbar-date">
              <svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="18" rx="2" /><line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" /></svg>
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
          {/* Hero Banner */}
          <div className="vs-hero">
            <div className="vs-hero-left">
              <div className="vs-hero-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
                  <circle cx="9" cy="7" r="4" />
                  <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
                  <path d="M16 3.13a4 4 0 0 1 0 7.75" />
                </svg>
              </div>
              <div>
                <div className="vs-hero-eyebrow">Vendors & Stalls</div>
                <div className="vs-hero-title">Vendor Registry</div>
                <div className="vs-hero-sub">Municipality of Cantilan, SDS</div>
              </div>
            </div>
            <div className="vs-hero-stats">
              <div className="vs-hero-stat">
                <div className="vs-hero-val">{allVendors.length}</div>
                <div className="vs-hero-key">Total Vendors</div>
                <div className="vs-hero-sub2">{activeVendors.length} active · {allVendors.length - activeVendors.length} closed</div>
              </div>
              <div className="vs-hero-divider"></div>
              <div className="vs-hero-stat">
                <div className="vs-hero-val">{paidThisMonth}</div>
                <div className="vs-hero-key">Paid This Month</div>
                <div className="vs-hero-sub2">out of {activeVendors.length} active vendors</div>
              </div>
              <div className="vs-hero-divider"></div>
              <div className="vs-hero-stat">
                <div className="vs-hero-val">{unpaidThisMonth}</div>
                <div className="vs-hero-key">Unpaid</div>
                <div className="vs-hero-sub2">{monthlyTarget.toLocaleString()} outstanding</div>
              </div>
              <div className="vs-hero-divider"></div>
              <div className="vs-hero-stat">
                <div className="vs-hero-val">{monthlyTarget.toLocaleString()}</div>
                <div className="vs-hero-key">Monthly Target</div>
                <div className="vs-hero-sub2">{new Date().toLocaleDateString('en-US', { month: 'long', year: 'numeric' })} · All Facilities</div>
              </div>
            </div>
            <div className="vs-hero-progress">
              <div className="vs-hero-progress-fill" style={{ width: `${overallRate}%` }}></div>
            </div>
          </div>

          {/* Toolbar */}
          <div className="vs-toolbar-unified">
            <div className="facility-selector">
              <div className={`fac-trigger ${facDropdownOpen ? 'open' : ''}`} onClick={() => setFacDropdownOpen(!facDropdownOpen)}>
                <div className="fac-trigger-body">
                  <span className="fac-trigger-label">{activeFacilityTab.shortName}</span>
                  <span className="fac-trigger-sub">{activeFacilityTab.name}</span>
                </div>
                {activeFacilityTab.unpaidCount > 0 && (
                  <span className="fac-trigger-badge">{activeFacilityTab.unpaidCount} unpaid</span>
                )}
                <svg className="fac-trigger-chevron" viewBox="0 0 24 24"><polyline points="6 9 12 15 18 9" /></svg>
              </div>

              {facDropdownOpen && (
                <>
                  <div className="fac-backdrop" onClick={() => setFacDropdownOpen(false)}></div>
                  <div className="fac-dropdown">
                    <div className="fac-dd-header">Facility</div>
                    {facilityTabs.map(f => (
                      <div key={f.code} className={`fac-dd-item ${activeFacility === f.code ? 'active' : ''}`} onClick={() => { setActiveFacility(f.code); setFacDropdownOpen(false); }}>
                        <div className="fac-dd-body"><span className="fac-dd-name">{f.name}</span></div>
                        {f.unpaidCount > 0 && <span className="fac-dd-badge">{f.unpaidCount}</span>}
                        {activeFacility === f.code && <svg className="fac-dd-check" viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12" /></svg>}
                      </div>
                    ))}
                  </div>
                </>
              )}
            </div>

            <div className="filter-tabs-inline">
              {statusFilters.map(f => (
                <div key={f} className={`filter-tab ${activeStatus === f ? 'active' : ''}`} onClick={() => setActiveStatus(f)}>{f}</div>
              ))}
            </div>

            <div className="search-box">
              <svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" /></svg>
              <input type="text" placeholder="Search stall no. or name…" value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)} />
              {searchQuery && <span className="search-clear" onClick={() => setSearchQuery('')}>×</span>}
            </div>

            <div className="vs-actions-compact">
              <span className="vs-count-label">{filteredVendors.length} stalls</span>
              <button className="btn-primary" onClick={openCreate}>
                <svg viewBox="0 0 24 24"><line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" /></svg>
                Add Vendor
              </button>
            </div>
          </div>

          {/* Table */}
          <div className="panel">
            {filteredVendors.length === 0 ? (
              <div className="empty-state">
                <svg viewBox="0 0 24 24"><path d="M20 7H4a2 2 0 00-2 2v6a2 2 0 002 2h16a2 2 0 002-2V9a2 2 0 00-2-2z" /></svg>
                <div className="empty-title">No Vendors Found</div>
                <div className="empty-sub">Try adjusting the filters or search.</div>
              </div>
            ) : (
              <div className="table-wrap">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Stall No.</th>
                      <th>Actual Occupant</th>
                      <th>OR No.</th>
                      <th>Section / Area</th>
                      <th>Monthly Rate</th>
                      <th>Status</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredVendors.map(v => (
                      <tr key={v.id} className={`${!v.isActive ? 'row-inactive' : ''} vendor-row`} onClick={() => openDetail(v)}>
                        <td>
                          <div className="stall-num-cell">
                            <span>{v.stallNo}</span>
                          </div>
                        </td>
                        <td>
                          <div className="occupant-cell">
                            <div>
                              <div>{v.actualOccupant}</div>
                              {v.contractName && v.contractName !== 'No contract' && v.contractName !== v.actualOccupant && (
                                <div className="occupant-contract">Contract: {v.contractName}</div>
                              )}
                            </div>
                          </div>
                        </td>
                        <td><span>{v.orNo || '—'}</span></td>
                        <td><span>{v.section}</span></td>
                        <td><span>{v.monthlyRate.toLocaleString()}</span></td>
                        <td onClick={(e) => e.stopPropagation()}>
                          <span className={`status-pill ${v.isActive ? 'pill-active' : 'pill-inactive'}`}>
                            {v.isActive ? 'Active' : 'Closed'}
                          </span>
                        </td>
                        <td onClick={(e) => e.stopPropagation()}>
                          <div className="action-btns">
                            <button className="action-btn" title="View Details" onClick={() => openDetail(v)}>
                              <svg viewBox="0 0 24 24"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></svg>
                            </button>
                            <button className="action-btn" title="View Profile" onClick={() => viewProfile(v)}>
                              <svg viewBox="0 0 24 24"><path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2" /><circle cx="12" cy="7" r="4" /></svg>
                            </button>
                            <button className="action-btn" title="Edit" onClick={() => openEdit(v)}>
                              <svg viewBox="0 0 24 24"><path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" /><path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" /></svg>
                            </button>
                            <button className="action-btn" title="Payment History" onClick={() => openHistory(v)}>
                              <svg viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" /><polyline points="14 2 14 8 20 8" /><line x1="16" y1="13" x2="8" y2="13" /><line x1="16" y1="17" x2="8" y2="17" /></svg>
                            </button>
                            <button
                              className={`action-btn ${v.isActive ? 'action-danger' : 'action-success'}`}
                              title={v.isActive ? 'Close Stall' : 'Reopen Stall'}
                              onClick={() => toggleVendor(v)}
                            >
                              {v.isActive ? (
                                <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" /><line x1="15" y1="9" x2="9" y2="15" /><line x1="9" y1="9" x2="15" y2="15" /></svg>
                              ) : (
                                <svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12" /></svg>
                              )}
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

      {/* Detail Panel */}
      <VendorDetailPanel
        show={showDetail}
        vendor={detailVendor ? {
          ...detailVendor,
          isPaidThisMonth: detailVendor.isPaidThisMonth,
          dailyCollections: detailVendor.dailyCollections,
        } : {
          stallNo: '',
          actualOccupant: '',
          facilityCode: '',
          section: '',
          isActive: false,
          monthlyRate: 0,
        }}
        onClose={() => setShowDetail(false)}
        onHistory={() => {
          setShowDetail(false);
          if (detailVendor) openHistory(detailVendor);
        }}
        onEdit={() => {
          setShowDetail(false);
          if (detailVendor) openEdit(detailVendor);
        }}
      />

      {/* Payment History Modal */}
      {showHistory && historyVendor && (
        <PaymentHistoryModal
          show={showHistory}
          stall={{
            stallNo: historyVendor.stallNo,
            actualOccupant: historyVendor.actualOccupant,
            contractName: historyVendor.contractName,
            monthlyRate: historyVendor.monthlyRate,
            partialAmount: historyVendor.partialAmount,
            contractDate: historyVendor.contractDate,
            paymentHistory: historyVendor.paymentHistory,
            transactions: []
          }}
          facilityCode={historyVendor.facilityCode}
          facilityName={facilityTabs.find(f => f.code === historyVendor.facilityCode)?.name || ''}
          onClose={() => setShowHistory(false)}
        />
      )}

      {/* Add/Edit Vendor Modal */}
      <AddVendorModal
        show={showModal}
        isEditing={isEditing}
        form={form}
        formError={formError}
        facilityOptions={facilityTabsAsOptions()}
        facilityLocked={false}
        onSave={saveVendor}
        onCancel={() => setShowModal(false)}
        onFacilityChanged={onFacilityChanged}
        onSectionChanged={onSectionChanged}
        onToggleFee={toggleFee}
        onFormChange={setForm}
      />

      {/* Confirm Close/Reopen Stall Modal */}
      {showConfirm && confirmTarget && (
        <div className="eemo-modal-overlay" onClick={() => setShowConfirm(false)}>
          <div className="eemo-modal eemo-modal-sm" onClick={(e) => e.stopPropagation()}>
            <div className="eemo-modal-header">
              <div className="eemo-modal-title">{confirmTarget.isActive ? 'Close Stall?' : 'Reopen Stall?'}</div>
            </div>
            <div className="eemo-modal-body">
              <p className="confirm-text">
                {confirmTarget.isActive
                  ? `Mark stall ${confirmTarget.stallNo} (${confirmTarget.actualOccupant}) as Closed? It will be hidden from collection lists.`
                  : `Reopen stall ${confirmTarget.stallNo}? It will be re-added to the active collection list.`}
              </p>
            </div>
            <div className="eemo-modal-footer">
              <button className="btn-ghost" onClick={() => setShowConfirm(false)}>Cancel</button>
              <button className={confirmTarget.isActive ? 'btn-danger' : 'btn-primary'} onClick={confirmToggle}>
                {confirmTarget.isActive ? 'Yes, Close' : 'Yes, Reopen'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
