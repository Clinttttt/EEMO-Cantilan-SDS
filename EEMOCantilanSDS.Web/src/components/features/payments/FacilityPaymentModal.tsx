import { useState, useEffect } from 'react';

interface FacilityPaymentModalProps {
  show: boolean;
  facility: string;
  stall: {
    stallNo: string;
    actualOccupant: string;
    monthlyRate: number;
    isPaid: boolean;
    isPartial: boolean;
    partialAmount: number;
    orNumber?: string;
    elecAmount?: number;
    waterAmount?: number;
    selectedSection?: string;
    totalPaid: number;
    balanceDue: number;
  };
  onClose: () => void;
  onSave: (data: PaymentSubmitData) => Promise<void>;
}

export interface PaymentSubmitData {
  orNumber: string;
  remarks?: string;
  partialAmount?: number;
  savedAt: Date;
}

export const FacilityPaymentModal = ({ show, facility, stall, onClose, onSave }: FacilityPaymentModalProps) => {
  const [orNumber, setOrNumber] = useState('');
  const [remarks, setRemarks] = useState('');
  const [partialAmountInput, setPartialAmountInput] = useState(0);
  const [showValidation, setShowValidation] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [isEditingOrNumber, setIsEditingOrNumber] = useState(false);
  const [showConfirmation, setShowConfirmation] = useState(false);

  useEffect(() => {
    if (!show) {
      setOrNumber('');
      setRemarks('');
      setPartialAmountInput(0);
      setShowValidation(false);
      setIsSaving(false);
      setIsEditingOrNumber(false);
      setShowConfirmation(false);
    } else if (show && stall) {
      setPartialAmountInput(stall.partialAmount || 0);
    }
  }, [show, stall]);

  const getStatusText = () => {
    if (stall.isPaid) return 'Paid';
    if (stall.isPartial) return `Partial — ₱${stall.partialAmount.toLocaleString()}`;
    return 'Unpaid';
  };

  const startEditOrNumber = () => {
    setOrNumber(stall.orNumber || '');
    setIsEditingOrNumber(true);
    setShowValidation(false);
  };

  const cancelEditOrNumber = () => {
    setOrNumber('');
    setIsEditingOrNumber(false);
    setShowValidation(false);
  };

  const requestSaveOrNumber = () => {
    setShowValidation(true);

    if (!orNumber.trim()) return;
    if (stall.isPartial && partialAmountInput <= 0) return;

    setShowConfirmation(true);
  };

  const cancelConfirmation = () => {
    setShowConfirmation(false);
  };

  const confirmSaveOrNumber = async () => {
    setShowConfirmation(false);
    setIsSaving(true);

    try {
      await onSave({
        orNumber: orNumber.trim(),
        remarks: remarks || undefined,
        partialAmount: stall.isPartial ? partialAmountInput : undefined,
        savedAt: new Date(),
      });
    } finally {
      setIsSaving(false);
    }
  };

  if (!show || !stall) return null;

  return (
    <>
      <div className="eemo-modal-overlay" onClick={onClose}>
        <div className="eemo-modal" onClick={(e) => e.stopPropagation()}>
          {/* Header */}
          <div className="eemo-modal-header">
            <div>
              <div className="eemo-modal-title">Payment Record</div>
              <div className="eemo-modal-sub">
                Stall {stall.stallNo} · {stall.actualOccupant} · {new Date().toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
              </div>
            </div>
            <button className="eemo-modal-close" onClick={onClose}>
              <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
            </button>
          </div>

          <div className="eemo-modal-body">
            {/* Status Badge */}
            <div className={`status-badge ${stall.isPaid ? 'paid' : stall.isPartial ? 'partial' : 'unpaid'}`}>
              Status: {getStatusText()}
            </div>

            {/* Partial Amount Input */}
            {stall.isPartial && (
              <div className="partial-amount-section">
                <label className="partial-label">
                  Partial Amount Paid <span className="required-star">*</span>
                </label>
                <div className="partial-input-wrap">
                  <span className="currency-symbol">₱</span>
                  <input
                    type="number"
                    min="0"
                    max={stall.monthlyRate}
                    step="0.01"
                    placeholder="0.00"
                    value={partialAmountInput || ''}
                    onChange={(e) => setPartialAmountInput(Number(e.target.value))}
                    className={`pay-input partial-input ${showValidation && partialAmountInput <= 0 ? 'input-error' : ''}`}
                  />
                </div>
                {showValidation && partialAmountInput <= 0 && (
                  <span className="input-error-msg">Partial amount must be greater than ₱0</span>
                )}
                {partialAmountInput > 0 && partialAmountInput < stall.monthlyRate && (
                  <div className="partial-hint">
                    Balance remaining: ₱{(stall.monthlyRate - partialAmountInput).toFixed(2)}
                  </div>
                )}
              </div>
            )}

            {/* Unpaid Notice */}
            {!stall.isPaid && !stall.isPartial && (
              <div className="unpaid-notice">
                Waiting for collector to record payment on mobile.
                OR Number can be encoded once payment is collected.
              </div>
            )}

            {/* Payment Details */}
            <div className="financial-breakdown">
              <div className="financial-breakdown-title">Payment Details</div>

              {/* OR Number Row */}
              {(stall.isPaid || stall.isPartial) && (
                <>
                  {stall.orNumber && !isEditingOrNumber ? (
                    <div className="breakdown-item">
                      <span>OR Number</span>
                      <div className="or-encoded-row">
                        <span className="or-number-display">{stall.orNumber}</span>
                        <button className="btn-edit-or" onClick={startEditOrNumber} title="Edit OR Number">
                          <svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" strokeWidth="2">
                            <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                            <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                          </svg>
                          Edit
                        </button>
                      </div>
                    </div>
                  ) : (
                    <>
                      <div className="breakdown-item breakdown-or-input">
                        <span>
                          OR Number <span className="required-star">*</span>
                          {isEditingOrNumber && <span className="editing-tag">editing</span>}
                        </span>
                        <div className="or-input-col">
                          <input
                            type="text"
                            placeholder="e.g. OR-2026-001"
                            value={orNumber}
                            onChange={(e) => setOrNumber(e.target.value)}
                            className={`pay-input pay-input-inline ${showValidation && !orNumber.trim() ? 'input-error' : ''}`}
                          />
                          {showValidation && !orNumber.trim() && (
                            <span className="input-error-msg">OR Number is required</span>
                          )}
                        </div>
                      </div>
                      <div className="breakdown-item breakdown-or-input">
                        <span style={{ color: 'var(--text-muted)', fontSize: '12px' }}>
                          Remarks <span className="optional-tag">(optional)</span>
                        </span>
                        <input
                          type="text"
                          placeholder="e.g. Late payment..."
                          value={remarks}
                          onChange={(e) => setRemarks(e.target.value)}
                          className="pay-input pay-input-inline"
                        />
                      </div>
                    </>
                  )}
                </>
              )}

              {/* Monthly Rental */}
              <div className="breakdown-item breakdown-section-header">
                <span>Monthly Rental</span>
                <span>₱{stall.monthlyRate.toLocaleString()}</span>
              </div>

              {stall.isPartial && (
                <>
                  <div className="breakdown-item">
                    <span>Amount Paid</span>
                    <span style={{ color: 'var(--green)' }}>₱{stall.partialAmount.toLocaleString()}</span>
                  </div>
                  <div className="breakdown-item">
                    <span>Balance Remaining</span>
                    <span style={{ color: '#b87333' }}>₱{(stall.monthlyRate - stall.partialAmount).toLocaleString()}</span>
                  </div>
                </>
              )}

              {/* NPM Utilities */}
              {facility === 'NPM' && (
                <>
                  {(stall.elecAmount || 0) > 0 && (
                    <div className="breakdown-item">
                      <span>Electricity</span>
                      <span>₱{stall.elecAmount!.toLocaleString()}</span>
                    </div>
                  )}
                  {(stall.waterAmount || 0) > 0 && (
                    <div className="breakdown-item">
                      <span>Water</span>
                      <span>₱{stall.waterAmount!.toLocaleString()}</span>
                    </div>
                  )}
                  {stall.selectedSection === 'Fish' && (
                    <div className="breakdown-item">
                      <span>Fish Fee (₱1/kg)</span>
                      <span>Variable</span>
                    </div>
                  )}
                </>
              )}

              {/* Totals */}
              <div className="breakdown-item breakdown-total">
                <span>Total Paid</span>
                <span>₱{stall.totalPaid.toLocaleString()}</span>
              </div>

              <div className="breakdown-item balance-due">
                <span>Balance Due</span>
                <span>₱{stall.balanceDue.toLocaleString()}</span>
              </div>
            </div>
          </div>

          {/* Footer */}
          <div className="eemo-modal-footer">
            <button className="btn-ghost" onClick={onClose}>Close</button>

            {(stall.isPaid || stall.isPartial) && !stall.orNumber && (
              <button
                className={`btn-primary ${isSaving ? 'btn-loading' : ''}`}
                onClick={requestSaveOrNumber}
                disabled={isSaving}
              >
                {isSaving ? (
                  <>
                    <span className="spinner"></span>
                    <span>Saving...</span>
                  </>
                ) : (
                  <span>Save OR Number</span>
                )}
              </button>
            )}

            {isEditingOrNumber && (
              <>
                <button className="btn-ghost" onClick={cancelEditOrNumber}>Cancel</button>
                <button
                  className={`btn-primary ${isSaving ? 'btn-loading' : ''}`}
                  onClick={requestSaveOrNumber}
                  disabled={isSaving}
                >
                  {isSaving ? (
                    <>
                      <span className="spinner"></span>
                      <span>Updating...</span>
                    </>
                  ) : (
                    <span>Update OR Number</span>
                  )}
                </button>
              </>
            )}
          </div>
        </div>
      </div>

      {/* Confirmation Modal */}
      {showConfirmation && (
        <div className="eemo-modal-overlay confirm-overlay" onClick={cancelConfirmation}>
          <div className="eemo-modal confirm-modal" onClick={(e) => e.stopPropagation()}>
            <div className="eemo-modal-header">
              <div className="eemo-modal-title">Confirm OR Number</div>
            </div>
            <div className="eemo-modal-body">
              <p>You are about to save the following OR Number:</p>
              <div className="confirm-or-display">{orNumber}</div>
              {stall.isPartial && (
                <p style={{ marginTop: '1rem' }}>
                  Partial amount: <strong>₱{partialAmountInput.toFixed(2)}</strong>
                </p>
              )}
              <p style={{ marginTop: '1rem', color: 'var(--text-muted)', fontSize: '14px' }}>
                This action cannot be undone. Please verify the OR Number is correct.
              </p>
            </div>
            <div className="eemo-modal-footer">
              <button className="btn-ghost" onClick={cancelConfirmation}>Cancel</button>
              <button className="btn-primary" onClick={confirmSaveOrNumber}>Confirm & Save</button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};
