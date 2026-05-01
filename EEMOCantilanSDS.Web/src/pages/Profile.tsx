import { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { stallService } from '@/api/services/stallService';
import { paymentService } from '@/api/services/paymentService';
import { Sidebar } from '@/components/layout/Sidebar';
import { Header } from '@/components/layout/Header';
import type { StallDto, UpdateStallCommand, RecordPaymentCommand } from '@/types/dto';
import { FacilityCode, PaymentStatus } from '@/types/enums';
import { formatCurrency, formatDate } from '@/utils/formatters';
import styles from './Profile.module.css';

export const Profile = () => {
  const { facilityId, stallNo } = useParams<{ facilityId: string; stallNo: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [showEditModal, setShowEditModal] = useState(false);
  const [showPayModal, setShowPayModal] = useState(false);
  const [showConfirmModal, setShowConfirmModal] = useState(false);
  const [paymentStatus, setPaymentStatus] = useState<PaymentStatus>(PaymentStatus.Paid);
  const [partialAmount, setPartialAmount] = useState('');
  const [remarks, setRemarks] = useState('');
  const [showValidation, setShowValidation] = useState(false);

  // Edit form state
  const [editForm, setEditForm] = useState({
    monthlyRate: 0,
    areaSqm: 0,
    areaLocation: '',
    actualOccupant: '',
    nameOnContract: '',
    remarks: '',
  });

  const facilityCode = FacilityCode[facilityId?.toUpperCase() as keyof typeof FacilityCode] || FacilityCode.NPM;

  const { data: stall, isLoading, error } = useQuery({
    queryKey: ['stalls', facilityCode, stallNo],
    queryFn: async () => {
      const stalls = await stallService.getStallsByFacility(facilityCode as FacilityCode);
      const found = stalls.find((s) => s.stallNo === stallNo);
      if (!found) throw new Error(`Stall ${stallNo} not found`);
      return found;
    },
    enabled: !!facilityCode && !!stallNo,
  });

  const { data: paymentHistory = [] } = useQuery({
    queryKey: ['payments', stall?.id],
    queryFn: async () => {
      try {
        return await paymentService.getPaymentHistory(stall!.id);
      } catch (error: any) {
        if (error.response?.status === 404) {
          return [];
        }
        throw error;
      }
    },
    enabled: !!stall?.id,
    retry: (failureCount, error: any) => {
      if (error?.response?.status === 404) return false;
      return failureCount < 3;
    },
  });

  const currentMonth = new Date();
  const { data: currentPayment } = useQuery({
    queryKey: ['payment', stall?.id, currentMonth.getFullYear(), currentMonth.getMonth() + 1],
    queryFn: async () => {
      try {
        return await paymentService.getPaymentRecord(stall!.id, currentMonth.getFullYear(), currentMonth.getMonth() + 1);
      } catch (error: any) {
        if (error.response?.status === 404) {
          return null;
        }
        throw error;
      }
    },
    enabled: !!stall?.id,
    retry: (failureCount, error: any) => {
      if (error?.response?.status === 404) return false;
      return failureCount < 3;
    },
  });

  const updateStall = useMutation({
    mutationFn: (cmd: UpdateStallCommand) => stallService.updateStall(stall!.id, cmd),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['stalls'] });
      setShowEditModal(false);
    },
  });

  const recordPayment = useMutation({
    mutationFn: (cmd: RecordPaymentCommand) => paymentService.recordPayment(cmd),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['payments'] });
      queryClient.invalidateQueries({ queryKey: ['stalls'] });
      setShowPayModal(false);
      setShowConfirmModal(false);
      setPartialAmount('');
      setRemarks('');
      setPaymentStatus(PaymentStatus.Paid);
    },
  });

  const handleEditSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!stall) return;

    const command: UpdateStallCommand = {
      stallId: stall.id,
      monthlyRate: editForm.monthlyRate,
      areaSqm: editForm.areaSqm || undefined,
      areaLocation: editForm.areaLocation || undefined,
      actualOccupant: editForm.actualOccupant || undefined,
      nameOnContract: editForm.nameOnContract || undefined,
      remarks: editForm.remarks || undefined,
    };

    await updateStall.mutateAsync(command);
  };

  const handlePaymentContinue = () => {
    setShowValidation(true);

    if (paymentStatus === PaymentStatus.Partial && (!partialAmount || parseFloat(partialAmount) <= 0)) {
      return;
    }

    setShowPayModal(false);
    setShowConfirmModal(true);
    setShowValidation(false);
  };

  const handlePaymentConfirm = async () => {
    if (!stall) return;

    const currentMonth = new Date();
    const command: RecordPaymentCommand = {
      stallId: stall.id,
      year: currentMonth.getFullYear(),
      month: currentMonth.getMonth() + 1,
      status: paymentStatus,
      partialAmount: paymentStatus === PaymentStatus.Partial ? parseFloat(partialAmount) : undefined,
      remarks: remarks || undefined,
    };

    await recordPayment.mutateAsync(command);
  };

  if (isLoading) {
    return (
      <div className="admin-layout">
        <Sidebar />
        <main className="admin-main">
          <div className="flex items-center justify-center p-12">
            <div className="animate-spin rounded-full h-12 w-12 border-4 border-gold border-t-transparent" />
          </div>
        </main>
      </div>
    );
  }

  if (error || !stall) {
    return (
      <div className="admin-layout">
        <Sidebar />
        <main className="admin-main">
          <div className="p-6">
            <div className="bg-red-bg text-red p-4 rounded">
              <h3 className="font-bold mb-2">Error loading stall profile</h3>
              <p>{error instanceof Error ? error.message : 'Stall not found'}</p>
              <button onClick={() => navigate(-1)} className="mt-4 px-4 py-2 bg-red text-white rounded hover:bg-red/90">
                Go Back
              </button>
            </div>
          </div>
        </main>
      </div>
    );
  }

  const isPaid = currentPayment?.status === PaymentStatus.Paid;
  const isPartial = currentPayment?.status === PaymentStatus.Partial;
  const isUnpaid = !currentPayment || currentPayment?.status === PaymentStatus.Unpaid;

  const now = new Date();

  // Initialize edit form when modal opens
  const handleEditModalOpen = () => {
    if (stall) {
      setEditForm({
        monthlyRate: stall.monthlyRate,
        areaSqm: stall.areaSqm || 0,
        areaLocation: stall.areaLocation || '',
        actualOccupant: stall.actualOccupant || '',
        nameOnContract: stall.nameOnContract || '',
        remarks: stall.remarks || '',
      });
    }
    setShowEditModal(true);
  };

  return (
    <div className="admin-layout">
      <Sidebar />

      <main className="admin-main">
        <Header 
          title="Stall Profile" 
          breadcrumbs={['EEMO Admin', getFacilityName(facilityId)]} 
        />

        <div className={styles.profContent}>
          {/* Hero Section */}
          <div className={styles.profHero}>
            <div className={styles.profHeroBadge}>
              <svg viewBox="0 0 24 24"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z" /><polyline points="9 22 9 12 15 12 15 22" /></svg>
            </div>
            <div className={styles.profHeroIdentity}>
              <div className={styles.profHeroStallNo}>STALL {stall.stallNo}</div>
              <div className={styles.profHeroName}>{stall.actualOccupant}</div>
              <div className={styles.profHeroMeta}>
                <span className={styles.profHeroChip}>
                  <svg viewBox="0 0 24 24"><path d="M21 10c0 7-9 13-9 13S3 17 3 10a9 9 0 0118 0z" /><circle cx="12" cy="10" r="3" /></svg>
                  {getFacilityName(facilityId)} · {stall.section}
                </span>
                <span className={`${styles.profStatusPill} ${isPaid ? styles.pillPaid : isPartial ? styles.pillPartial : styles.pillUnpaid}`}>
                  {isPaid ? 'Paid' : isPartial ? 'Partial' : 'Unpaid'}
                </span>
                <span className={`${styles.profStatusPill} ${stall.isActive ? styles.pillActive : styles.pillInactive}`}>
                  {stall.isActive ? 'Active' : 'Closed'}
                </span>
              </div>
            </div>
            <div className={styles.profHeroActions}>
              <button onClick={handleEditModalOpen} className={styles.btnOutline}>
                <svg viewBox="0 0 24 24"><path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" /><path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" /></svg>
                Edit Details
              </button>
              <button onClick={() => setShowPayModal(true)} className={styles.btnPrimary}>
                <svg viewBox="0 0 24 24"><line x1="12" y1="1" x2="12" y2="23" /><path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6" /></svg>
                Record Payment
              </button>
            </div>
          </div>

          {/* 2-Column Grid */}
          <div className={styles.profGrid}>
            {/* LEFT COLUMN */}
            <div className={styles.profCol}>
              {/* Stall Information */}
              <div className={styles.profCard}>
                <div className={styles.profCardHeader}>
                  <div className={styles.profCardIcon}>
                    <svg viewBox="0 0 24 24"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z" /></svg>
                  </div>
                  <h3 className={styles.profCardTitle}>Stall Information</h3>
                </div>
                <div className={styles.profFieldList}>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Facility</span>
                    <span className={styles.profFieldVal}>{getFacilityName(facilityId)}</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Stall / Space No.</span>
                    <span className={styles.profFieldVal}>{stall.stallNo}</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Section / Area</span>
                    <span className={styles.profFieldVal}>{stall.section || '—'}</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Area (sq.m)</span>
                    <span className={styles.profFieldVal}>{stall.areaSqm} sqm</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Area Location / Note</span>
                    <span className={styles.profFieldVal}>{stall.areaLocation || '—'}</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Stall Status</span>
                    <span className={`${styles.profStatusPill} ${stall.isActive ? styles.pillActive : styles.pillInactive}`}>
                      {stall.isActive ? 'Active' : 'Closed'}
                    </span>
                  </div>
                </div>
              </div>

              {/* Occupant & Contract */}
              <div className={styles.profCard}>
                <div className={styles.profCardHeader}>
                  <div className={styles.profCardIcon}>
                    <svg viewBox="0 0 24 24"><path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2" /><circle cx="12" cy="7" r="4" /></svg>
                  </div>
                  <h3 className={styles.profCardTitle}>Occupant & Contract</h3>
                </div>
                <div className={styles.profFieldList}>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Actual Occupant</span>
                    <span className={styles.profFieldVal}>{stall.actualOccupant}</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Name on Contract</span>
                    <span className={styles.profFieldVal}>{stall.nameOnContract || '—'}</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Contract Effectivity Date</span>
                    <span className={styles.profFieldVal}>{stall.contractDate ? formatDate(stall.contractDate) : '—'}</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Contract Duration</span>
                    <span className={styles.profFieldVal}>3 years</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Contract Expiry</span>
                    <span className={styles.profFieldVal}>
                      {stall.contractDate ? formatDate(new Date(new Date(stall.contractDate).getTime() + 3 * 365 * 24 * 60 * 60 * 1000)) : '—'}
                    </span>
                  </div>
                </div>
              </div>

              {/* Fees & Utilities */}
              <div className={styles.profCard}>
                <div className={styles.profCardHeader}>
                  <div className={styles.profCardIcon}>
                    <svg viewBox="0 0 24 24"><line x1="12" y1="1" x2="12" y2="23" /><path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6" /></svg>
                  </div>
                  <h3 className={styles.profCardTitle}>Fees & Utilities</h3>
                </div>
                <div className={styles.profFieldList}>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Monthly Rental Rate</span>
                    <span className={`${styles.profFieldVal} ${styles.gold}`}>{formatCurrency(stall.monthlyRate)}/month</span>
                  </div>
                </div>
              </div>
            </div>

            {/* RIGHT COLUMN */}
            <div className={styles.profCol}>
              {/* Payment Status */}
              <div className={styles.profCard}>
                <div className={styles.profCardHeader}>
                  <div className={styles.profCardIcon}>
                    <svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="18" rx="2" /><line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" /></svg>
                  </div>
                  <h3 className={styles.profCardTitle}>{now.toLocaleDateString('en-US', { month: 'long', year: 'numeric' })} — Payment Status</h3>
                </div>

                <div className={`${styles.profMonthStatus} ${isPaid ? styles.monthPaid : isPartial ? styles.monthPartial : styles.monthUnpaid}`}>
                  <div className={styles.profMonthIcon}>
                    {isPaid ? (
                      <svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12" /></svg>
                    ) : isPartial ? (
                      <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="none" stroke="currentColor" strokeWidth="2" /><path d="M12 2 A 10 10 0 0 1 12 22 Z" fill="currentColor" /></svg>
                    ) : (
                      <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                    )}
                  </div>
                  <div>
                    <div className={styles.profMonthLabel}>{isPaid ? 'Fully Paid' : isPartial ? 'Partially Paid' : 'Unpaid'}</div>
                    <div className={styles.profMonthSub}>
                      {isPaid
                        ? `${formatCurrency(stall.monthlyRate)} settled`
                        : isPartial
                          ? `${formatCurrency(currentPayment?.amountPaid || 0)} paid · ${formatCurrency(stall.monthlyRate - (currentPayment?.amountPaid || 0))} remaining`
                          : `${formatCurrency(stall.monthlyRate)} outstanding`}
                    </div>
                  </div>
                </div>

                <div className={styles.profFieldList}>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>OR Number</span>
                    <span className={`${styles.profFieldVal} ${styles.mono}`}>{currentPayment?.orNumber || '— Not issued'}</span>
                  </div>
                  <div className={styles.profField}>
                    <span className={styles.profFieldLabel}>Assigned Collector</span>
                    <span className={styles.profFieldVal}>
                      <svg viewBox="0 0 24 24" style={{width:'13px',height:'13px',stroke:'var(--text-muted)',fill:'none',strokeWidth:2,verticalAlign:'middle',marginRight:'4px'}}><path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2" /><circle cx="12" cy="7" r="4" /></svg>
                      {stall.collectorName || '—'}
                    </span>
                  </div>
                </div>
              </div>

              {/* 12-Month Payment History */}
              <div className={styles.profCard}>
                <div className={styles.profCardHeader}>
                  <div className={styles.profCardIcon}>
                    <svg viewBox="0 0 24 24"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12" /></svg>
                  </div>
                  <h3 className={styles.profCardTitle}>12-Month Payment History</h3>
                </div>

                <div className={styles.profHistGrid}>
                  {Array.from({ length: 12 }).map((_, i) => {
                    const month = new Date();
                    month.setMonth(month.getMonth() - (11 - i));
                    const yearMonth = `${month.getFullYear()}-${String(month.getMonth() + 1).padStart(2, '0')}`;
                    const payment = paymentHistory?.find((p) => `${p.billingYear}-${String(p.billingMonth).padStart(2, '0')}` === yearMonth);
                    const status = payment?.status;
                    const isCurrent = i === 11;

                    return (
                      <div key={`${yearMonth}-${i}`} className={`${styles.profHistMonth} ${status === PaymentStatus.Paid ? styles.histPaid : status === PaymentStatus.Partial ? styles.histPartial : styles.histUnpaid} ${isCurrent ? styles.histCurrent : ''}`}>
                        <div className={styles.profHistMonthName}>{month.toLocaleDateString('en-US', { month: 'short' }).toUpperCase()}</div>
                        <div className={styles.profHistMonthYear}>{month.toLocaleDateString('en-US', { year: '2-digit' })}</div>
                        <div className={styles.profHistIcon}>
                          {status === PaymentStatus.Paid ? '✓' : status === PaymentStatus.Partial ? '◐' : '✗'}
                        </div>
                      </div>
                    );
                  })}
                </div>

                <div className={styles.profHistSummary}>
                  <div className={styles.profHistStat}>
                    <div className={`${styles.profHistStatVal} ${styles.green}`}>0</div>
                    <div className={styles.profHistStatKey}>Months Paid</div>
                  </div>
                  <div className={`${styles.profHistStat} ${styles.statWarn}`}>
                    <div className={`${styles.profHistStatVal} ${styles.red}`}>1</div>
                    <div className={styles.profHistStatKey}>Months Unpaid</div>
                  </div>
                  <div className={styles.profHistStat}>
                    <div className={styles.profHistStatVal}>PO</div>
                    <div className={styles.profHistStatKey}>Total Collected</div>
                  </div>
                  <div className={`${styles.profHistStat} ${styles.statWarn}`}>
                    <div className={`${styles.profHistStatVal} ${styles.red}`}>{formatCurrency(stall.monthlyRate)}</div>
                    <div className={styles.profHistStatKey}>Balance Due</div>
                  </div>
                </div>
              </div>

              {/* Remarks */}
              <div className={styles.profCard}>
                <div className={styles.profCardHeader}>
                  <div className={styles.profCardIcon}>
                    <svg viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" /><polyline points="14 2 14 8 20 8" /><line x1="16" y1="13" x2="8" y2="13" /><line x1="16" y1="17" x2="8" y2="17" /></svg>
                  </div>
                  <h3 className={styles.profCardTitle}>Remarks / Notes</h3>
                </div>
                <div className={styles.profNotesBody}>
                  {stall.remarks ? stall.remarks : <span className={styles.profNotesEmpty}>No remarks on file.</span>}
                </div>
              </div>
            </div>
          </div>
        </div>
      </main>

      {/* Edit Modal */}
      {showEditModal && (
        <div className="eemo-modal-overlay" onClick={() => setShowEditModal(false)}>
          <div className="eemo-modal eemo-modal-wide" onClick={(e) => e.stopPropagation()}>
            <div className="eemo-modal-header">
              <div>
                <div className="eemo-modal-title">Edit Stall Details</div>
                <div className="eemo-modal-sub">Stall {stall.stallNo} · {stall.actualOccupant}</div>
              </div>
              <button className="eemo-modal-close" onClick={() => setShowEditModal(false)}>
                <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
              </button>
            </div>
            <form onSubmit={handleEditSubmit}>
              <div className="eemo-modal-body">
                <div className={styles.editFormGrid}>
                  <div className="form-group">
                    <label className="form-label">Actual Occupant <span className="req">*</span></label>
                    <input
                      className="form-input"
                      type="text"
                      value={editForm.actualOccupant}
                      onChange={(e) => setEditForm({ ...editForm, actualOccupant: e.target.value })}
                      required
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Name on Contract</label>
                    <input
                      className="form-input"
                      type="text"
                      value={editForm.nameOnContract}
                      onChange={(e) => setEditForm({ ...editForm, nameOnContract: e.target.value })}
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Monthly Rental Rate (₱) <span className="req">*</span></label>
                    <input
                      className="form-input"
                      type="number"
                      value={editForm.monthlyRate}
                      onChange={(e) => setEditForm({ ...editForm, monthlyRate: parseFloat(e.target.value) })}
                      required
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Area (sq.m)</label>
                    <input
                      className="form-input"
                      type="number"
                      value={editForm.areaSqm}
                      onChange={(e) => setEditForm({ ...editForm, areaSqm: parseFloat(e.target.value) })}
                    />
                  </div>
                  <div className="form-group form-group-full">
                    <label className="form-label">Area Location / Note</label>
                    <input
                      className="form-input"
                      type="text"
                      value={editForm.areaLocation}
                      onChange={(e) => setEditForm({ ...editForm, areaLocation: e.target.value })}
                      placeholder="e.g. Corner slot, near entrance"
                    />
                  </div>
                  <div className="form-group form-group-full">
                    <label className="form-label">Remarks</label>
                    <textarea
                      className={`form-input ${styles.formTextarea}`}
                      value={editForm.remarks}
                      onChange={(e) => setEditForm({ ...editForm, remarks: e.target.value })}
                      rows={3}
                      placeholder="Any additional notes…"
                    />
                  </div>
                </div>
              </div>
              <div className="eemo-modal-footer">
                <button type="button" className="btn-ghost" onClick={() => setShowEditModal(false)} disabled={updateStall.isPending}>
                  Cancel
                </button>
                <button type="submit" className={`btn-primary ${updateStall.isPending ? styles.btnLoading : ''}`} disabled={updateStall.isPending}>
                  {updateStall.isPending ? (
                    <>
                      <span className={styles.spinner}></span>
                      <span>Saving...</span>
                    </>
                  ) : (
                    <span>Save Changes</span>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Payment Modal */}
      {showPayModal && (
        <div className="eemo-modal-overlay" onClick={() => setShowPayModal(false)}>
          <div className="eemo-modal" onClick={(e) => e.stopPropagation()}>
            <div className="eemo-modal-header">
              <div>
                <div className="eemo-modal-title">Record Payment</div>
                <div className="eemo-modal-sub">
                  Stall {stall.stallNo} · {stall.actualOccupant} · {now.toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
                </div>
              </div>
              <button className="eemo-modal-close" onClick={() => setShowPayModal(false)}>
                <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
              </button>
            </div>
            <div className="eemo-modal-body">
              <div className="form-group">
                <label className="form-label">Payment Status <span className="req">*</span></label>
                <div className={styles.payOptions}>
                  <div
                    className={`${styles.payOption} ${paymentStatus === PaymentStatus.Paid ? styles.payOptPaid : ''}`}
                    onClick={() => setPaymentStatus(PaymentStatus.Paid)}
                  >
                    <svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12" /></svg>
                    <span>Paid</span>
                  </div>
                  <div
                    className={`${styles.payOption} ${paymentStatus === PaymentStatus.Partial ? styles.payOptPartial : ''}`}
                    onClick={() => setPaymentStatus(PaymentStatus.Partial)}
                  >
                    <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="none" stroke="currentColor" strokeWidth="2" /><path d="M12 2 A 10 10 0 0 1 12 22 Z" fill="currentColor" /></svg>
                    <span>Partial</span>
                  </div>
                  <div
                    className={`${styles.payOption} ${paymentStatus === PaymentStatus.Unpaid ? styles.payOptUnpaid : ''}`}
                    onClick={() => setPaymentStatus(PaymentStatus.Unpaid)}
                  >
                    <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                    <span>Unpaid</span>
                  </div>
                </div>
              </div>

              {paymentStatus === PaymentStatus.Partial && (
                <div className={styles.partialAmountSection}>
                  <label className={styles.partialLabel}>
                    Partial Amount Paid <span className={styles.requiredStar}>*</span>
                  </label>
                  <div className={styles.partialInputWrap}>
                    <span className={styles.currencySymbol}>₱</span>
                    <input
                      type="number"
                      min="0"
                      max={stall.monthlyRate}
                      step="0.01"
                      placeholder="0.00"
                      value={partialAmount}
                      onChange={(e) => setPartialAmount(e.target.value)}
                      className={`${styles.partialInput} ${showValidation && (!partialAmount || parseFloat(partialAmount) <= 0) ? styles.inputError : ''}`}
                    />
                  </div>
                  {showValidation && (!partialAmount || parseFloat(partialAmount) <= 0) && (
                    <span className={styles.inputErrorMsg}>Partial amount must be greater than ₱0</span>
                  )}
                  {partialAmount && parseFloat(partialAmount) > 0 && parseFloat(partialAmount) < stall.monthlyRate && (
                    <div className={styles.partialHint}>
                      Balance remaining: {formatCurrency(stall.monthlyRate - parseFloat(partialAmount))}
                    </div>
                  )}
                </div>
              )}

              <div className="form-group">
                <label className="form-label">Remarks (optional)</label>
                <textarea
                  className={`form-input ${styles.formTextarea}`}
                  value={remarks}
                  onChange={(e) => setRemarks(e.target.value)}
                  rows={2}
                  placeholder="e.g. Late payment..."
                />
              </div>

              <div className={styles.payFeeBlock}>
                <div className={styles.payFeeHeader}>
                  <span className={styles.payFeeLabel}>Total Bill</span>
                  <span className={styles.payFeeRate}>{formatCurrency(stall.monthlyRate)} / month</span>
                </div>
                {paymentStatus === PaymentStatus.Paid && (
                  <div className={styles.payRecordRow}>
                    <span className={styles.payRecordKey}>Amount Paid</span>
                    <span className={`${styles.payRecordVal} ${styles.green}`}>{formatCurrency(stall.monthlyRate)}</span>
                  </div>
                )}
                {paymentStatus === PaymentStatus.Partial && partialAmount && (
                  <>
                    <div className={styles.payRecordRow}>
                      <span className={styles.payRecordKey}>Partial Amount Paid</span>
                      <span className={`${styles.payRecordVal} ${styles.green}`}>{formatCurrency(parseFloat(partialAmount))}</span>
                    </div>
                    <div className={styles.payRecordRow}>
                      <span className={styles.payRecordKey}>Balance Remaining</span>
                      <span className={`${styles.payRecordVal} ${styles.red}`}>{formatCurrency(stall.monthlyRate - parseFloat(partialAmount))}</span>
                    </div>
                  </>
                )}
                {paymentStatus === PaymentStatus.Unpaid && (
                  <div className={styles.payRecordRow}>
                    <span className={styles.payRecordKey}>Amount Due</span>
                    <span className={`${styles.payRecordVal} ${styles.red}`}>{formatCurrency(stall.monthlyRate)}</span>
                  </div>
                )}
              </div>
            </div>
            <div className="eemo-modal-footer">
              <button className="btn-ghost" onClick={() => setShowPayModal(false)}>
                Cancel
              </button>
              <button className="btn-primary" onClick={handlePaymentContinue}>
                Continue
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Payment Confirmation Modal */}
      {showConfirmModal && (
        <div className="eemo-modal-overlay" onClick={() => setShowConfirmModal(false)}>
          <div className="eemo-modal payment-confirm-modal" onClick={(e) => e.stopPropagation()}>
            <div className="eemo-modal-header">
              <div>
                <div className="eemo-modal-title">Confirm Payment</div>
                <div className="eemo-modal-sub">Please review the payment details before saving</div>
              </div>
              <button className="eemo-modal-close" onClick={() => setShowConfirmModal(false)}>
                <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
              </button>
            </div>
            <div className="eemo-modal-body">
              <div className="confirm-section">
                <div className="confirm-label">Stall Information</div>
                <div className="confirm-info-grid">
                  <div className="confirm-info-item">
                    <span className="confirm-info-key">Stall No.</span>
                    <span className="confirm-info-val">{stall.stallNo}</span>
                  </div>
                  <div className="confirm-info-item">
                    <span className="confirm-info-key">Occupant</span>
                    <span className="confirm-info-val">{stall.actualOccupant}</span>
                  </div>
                  <div className="confirm-info-item">
                    <span className="confirm-info-key">Billing Period</span>
                    <span className="confirm-info-val">{now.toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}</span>
                  </div>
                </div>
              </div>

              <div className="confirm-section">
                <div className="confirm-label">Payment Details</div>
                <div className={`confirm-payment-card ${paymentStatus === PaymentStatus.Paid ? 'status-paid' : paymentStatus === PaymentStatus.Partial ? 'status-partial' : 'status-unpaid'}`}>
                  <div className="confirm-payment-row">
                    <span className="confirm-payment-label">Status</span>
                    <span className={`confirm-payment-status ${paymentStatus === PaymentStatus.Paid ? 'status-paid' : paymentStatus === PaymentStatus.Partial ? 'status-partial' : 'status-unpaid'}`}>
                      {paymentStatus === PaymentStatus.Paid ? 'Paid in Full' : paymentStatus === PaymentStatus.Partial ? 'Partial Payment' : 'Unpaid'}
                    </span>
                  </div>
                  <div className="confirm-payment-row">
                    <span className="confirm-payment-label">Total Bill</span>
                    <span className="confirm-payment-value">{formatCurrency(stall.monthlyRate)}</span>
                  </div>
                  {paymentStatus === PaymentStatus.Partial && partialAmount && (
                    <>
                      <div className="confirm-payment-row highlight">
                        <span className="confirm-payment-label">Amount Paid</span>
                        <span className="confirm-payment-value">{formatCurrency(parseFloat(partialAmount))}</span>
                      </div>
                      <div className="confirm-payment-row">
                        <span className="confirm-payment-label">Balance Remaining</span>
                        <span className="confirm-payment-value text-red">{formatCurrency(stall.monthlyRate - parseFloat(partialAmount))}</span>
                      </div>
                    </>
                  )}
                </div>
              </div>

              {paymentStatus === PaymentStatus.Unpaid && (
                <div className="confirm-warning">
                  <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" /><line x1="12" y1="8" x2="12" y2="12" /><line x1="12" y1="16" x2="12.01" y2="16" /></svg>
                  <span>This will mark the payment as unpaid. No OR number will be recorded.</span>
                </div>
              )}
            </div>
            <div className="eemo-modal-footer">
              <button className="btn-ghost" onClick={() => setShowConfirmModal(false)}>
                Cancel
              </button>
              <button
                className={`btn-primary ${recordPayment.isPending ? styles.btnLoading : ''}`}
                onClick={handlePaymentConfirm}
                disabled={recordPayment.isPending}
              >
                {recordPayment.isPending ? (
                  <>
                    <span className={styles.spinner}></span>
                    <span>Saving...</span>
                  </>
                ) : (
                  <span>
                    {paymentStatus === PaymentStatus.Paid ? 'Confirm Payment' : paymentStatus === PaymentStatus.Partial ? 'Confirm Partial Payment' : 'Mark as Unpaid'}
                  </span>
                )}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

function getFacilityName(code?: string): string {
  switch (code?.toUpperCase()) {
    case 'NPM': return 'New Public Market';
    case 'TCC': return 'Tampak Commercial Center';
    case 'NCC': return 'New Commercial Center';
    case 'BBQ': return 'Barbecue Stand';
    case 'ICE': return 'Iceplant';
    case 'SLH': return 'Slaughterhouse';
    default: return 'Unknown Facility';
  }
}
