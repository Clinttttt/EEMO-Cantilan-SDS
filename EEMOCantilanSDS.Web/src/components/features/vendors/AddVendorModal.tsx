import { useState, useEffect } from 'react';

export interface VendorModalForm {
  facilityCode: string;
  stallNo: string;
  actualOccupant: string;
  contractName: string;
  selectedSection: string;
  areaSqm: number;
  contractDate?: Date;
  contractYears: number;
  monthlyRate: number;
  actualMonthlyRental: number;
  areaLocation: string;
  feeTypes: string[];
}

export interface VendorFacilityOption {
  code: string;
  name: string;
}

interface AddVendorModalProps {
  show: boolean;
  isEditing: boolean;
  form: VendorModalForm;
  formError: string;
  fieldErrors?: Record<string, string>;
  facilityOptions: VendorFacilityOption[];
  facilityLocked: boolean;
  onSave: () => void;
  onCancel: () => void;
  onFacilityChanged: () => void;
  onSectionChanged: () => void;
  onToggleFee: (fee: string) => void;
  onFormChange: (form: VendorModalForm) => void;
}

export const AddVendorModal = ({
  show,
  isEditing,
  form,
  formError,
  fieldErrors = {},
  facilityOptions,
  facilityLocked,
  onSave,
  onCancel,
  onFacilityChanged,
  onSectionChanged,
  onToggleFee,
  onFormChange
}: AddVendorModalProps) => {
  const getFieldError = (fieldName: string) => fieldErrors[fieldName] || '';

  const updateForm = (updates: Partial<VendorModalForm>) => {
    onFormChange({ ...form, ...updates });
  };

  const wholeYearRental = form.monthlyRate * 12;
  const wholeYearActualRental = form.actualMonthlyRental > 0 ? form.actualMonthlyRental * 12 : wholeYearRental;

  if (!show) return null;

  return (
    <div className="eemo-modal-overlay" onClick={onCancel}>
      <div className="eemo-modal eemo-modal-wide" onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className="eemo-modal-header">
          <div>
            <div className="eemo-modal-title">{isEditing ? 'Edit Vendor / Stall' : 'Add New Vendor'}</div>
            <div className="eemo-modal-sub">{isEditing ? 'Update vendor and stall information.' : 'Register a new stall vendor in the system.'}</div>
          </div>
          <button className="eemo-modal-close" onClick={onCancel}>
            <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
          </button>
        </div>

        {/* Body */}
        <div className="eemo-modal-body">
          {/* §1 Leasee Information */}
          <div className="avm-section">
            <div className="avm-section-label">Leasee Information</div>
            <div className="avm-row-2">
              <div className="avm-group">
                <label className="avm-label">Actual Occupant (Leasee) <span className="avm-req">*</span></label>
                <input
                  className={`avm-input ${getFieldError('ActualOccupant') ? 'form-input-error' : ''}`}
                  type="text"
                  placeholder="Current stall occupant"
                  value={form.actualOccupant}
                  onChange={(e) => updateForm({ actualOccupant: e.target.value })}
                />
                {getFieldError('ActualOccupant') && <span className="form-error">{getFieldError('ActualOccupant')}</span>}
              </div>
              <div className="avm-group">
                <label className="avm-label">Name on Contract (Per Signed Contract)</label>
                <input
                  className={`avm-input ${getFieldError('NameOnContract') ? 'form-input-error' : ''}`}
                  type="text"
                  placeholder="Leave blank if no contract"
                  value={form.contractName}
                  onChange={(e) => updateForm({ contractName: e.target.value })}
                />
                {getFieldError('NameOnContract') && <span className="form-error">{getFieldError('NameOnContract')}</span>}
              </div>
            </div>
          </div>

          {/* §2 Space & Contract Details */}
          <div className="avm-section">
            <div className="avm-section-label">Space & Contract Details</div>

            <div className="avm-row-2">
              {/* Facility */}
              <div className="avm-group">
                <label className="avm-label">Facility <span className="avm-req">*</span></label>
                {facilityLocked ? (
                  <div className="avm-input avm-input-locked">
                    {facilityOptions[0]?.name || form.facilityCode}
                  </div>
                ) : (
                  <select
                    className="avm-input"
                    value={form.facilityCode}
                    onChange={(e) => {
                      updateForm({ facilityCode: e.target.value });
                      onFacilityChanged();
                    }}
                  >
                    <option value="">-- Select Facility --</option>
                    {facilityOptions.map(f => (
                      <option key={f.code} value={f.code}>{f.name}</option>
                    ))}
                  </select>
                )}
              </div>

              <div className="avm-group">
                <label className="avm-label">Stall / Space No. <span className="avm-req">*</span></label>
                <input
                  className={`avm-input ${getFieldError('StallNo') ? 'form-input-error' : ''}`}
                  type="text"
                  placeholder="e.g. 01, T4, B-02"
                  value={form.stallNo}
                  onChange={(e) => updateForm({ stallNo: e.target.value })}
                />
                {getFieldError('StallNo') && <span className="form-error">{getFieldError('StallNo')}</span>}
              </div>
            </div>

            {/* Section dropdown — NPM only */}
            {form.facilityCode === 'NPM' && (
              <div className="avm-row-2">
                <div className="avm-group">
                  <label className="avm-label">Section / Category <span className="avm-req">*</span></label>
                  <select
                    className={`avm-input ${getFieldError('Section') ? 'form-input-error' : ''}`}
                    value={form.selectedSection}
                    onChange={(e) => {
                      updateForm({ selectedSection: e.target.value });
                      onSectionChanged();
                    }}
                  >
                    <option value="">-- Select Section --</option>
                    <option value="Vegetable">Vegetable Area</option>
                    <option value="Fish">Fish Section</option>
                    <option value="Meat">Meat Section</option>
                  </select>
                  {getFieldError('Section') && <span className="form-error">{getFieldError('Section')}</span>}
                </div>
                <div className="avm-group" style={{ visibility: 'hidden' }}></div>
              </div>
            )}

            <div className="avm-row-2">
              <div className="avm-group">
                <label className="avm-label">Contract Effectivity Date</label>
                <input
                  className="avm-input"
                  type="date"
                  value={form.contractDate ? form.contractDate.toISOString().split('T')[0] : ''}
                  onChange={(e) => updateForm({ contractDate: e.target.value ? new Date(e.target.value) : undefined })}
                />
              </div>
              <div className="avm-group">
                <label className="avm-label">Contract Duration (No. of Years)</label>
                <select
                  className="avm-input"
                  value={form.contractYears}
                  onChange={(e) => updateForm({ contractYears: Number(e.target.value) })}
                >
                  <option value="0">No contract</option>
                  <option value="1">1 year</option>
                  <option value="2">2 years</option>
                  <option value="3">3 years</option>
                  <option value="5">5 years</option>
                </select>
              </div>
            </div>
          </div>

          {/* §3 Space Information */}
          <div className="avm-section">
            <div className="avm-section-label">Space Information</div>
            <div className="avm-row-2">
              <div className="avm-group">
                <label className="avm-label">Area (sq.m)</label>
                <input
                  className="avm-input"
                  type="number"
                  min="0"
                  placeholder="e.g. 4.8"
                  value={form.areaSqm || ''}
                  onChange={(e) => updateForm({ areaSqm: Number(e.target.value) })}
                />
              </div>
              <div className="avm-group" style={{ visibility: 'hidden' }}></div>
            </div>
          </div>

          {/* §4 Rental Rates */}
          <div className="avm-section">
            <div className="avm-section-label">
              {form.facilityCode === 'SLH' ? 'Fixed Charges' : 'Rental Rates'}
            </div>

            {form.facilityCode === 'SLH' ? (
              <div className="avm-info-box">
                <div className="avm-info-box-title"><strong>Per Head Charges:</strong></div>
                <div className="avm-info-box-grid">
                  <div>• Slaughter Fee</div><div>• Ante Mortem</div>
                  <div>• Post Mortem</div><div>• Table Charge</div>
                  <div>• Entrance Fee</div><div>• Livestock Fee</div>
                </div>
                <div className="avm-info-box-hint">These are fixed per-head charges for slaughterhouse</div>
              </div>
            ) : (
              <div className="avm-row-2">
                <div className="avm-group">
                  <label className="avm-label">Monthly Rental / Rate (₱) <span className="avm-req">*</span></label>
                  <input
                    className="avm-input"
                    type="number"
                    min="0"
                    placeholder="e.g. 900"
                    value={form.monthlyRate || ''}
                    onChange={(e) => updateForm({ monthlyRate: Number(e.target.value) })}
                  />
                  <div className="avm-hint-muted">Whole Year: ₱{wholeYearRental.toLocaleString()}</div>
                </div>
                <div className="avm-group">
                  <label className="avm-label">Actual Monthly Rental (₱)</label>
                  <input
                    className="avm-input"
                    type="number"
                    min="0"
                    placeholder="Leave blank if same as contract"
                    value={form.actualMonthlyRental || ''}
                    onChange={(e) => updateForm({ actualMonthlyRental: Number(e.target.value) })}
                  />
                  <div className="avm-hint-muted">Whole Year: ₱{wholeYearActualRental.toLocaleString()}</div>
                </div>
              </div>
            )}

            {form.facilityCode === 'NPM' && form.selectedSection === 'Vegetable' && (
              <div className="avm-fee-highlight">
                <div><strong>Daily Collection:</strong> ₱30/day (Vegetable vendors)</div>
              </div>
            )}
            {form.facilityCode === 'NPM' && form.selectedSection === 'Fish' && (
              <div className="avm-fee-highlight">
                <div><strong>Daily Collection:</strong> ₱30/day (Fish vendors)</div>
                <div><strong>Fish Fee:</strong> ₱1/kg (per kilo sold)</div>
              </div>
            )}
            {form.facilityCode === 'NPM' && form.selectedSection === 'Meat' && (
              <div className="avm-fee-highlight">
                <div><strong>Daily Collection:</strong> ₱30/day (Meat vendors)</div>
              </div>
            )}
          </div>

          {/* §5 Additional Information */}
          <div className="avm-section">
            <div className="avm-section-label">Additional Information</div>
            <div className="avm-row-2">
              <div className="avm-group">
                <label className="avm-label">Area Location / Note</label>
                <input
                  className="avm-input"
                  type="text"
                  placeholder="e.g. Extension, Corner"
                  value={form.areaLocation}
                  onChange={(e) => updateForm({ areaLocation: e.target.value })}
                />
              </div>
            </div>
          </div>

          {/* §6 Applicable Charges — hidden for SLH */}
          {form.facilityCode && form.facilityCode !== 'SLH' && (
            <div className="avm-section">
              <div className="avm-divider-header">
                <div className="avm-divider-line"></div>
                <span className="avm-divider-text">Applicable Charges</span>
                <div className="avm-divider-line"></div>
              </div>

              {/* Base Rental card */}
              <div className="avm-sublabel">Base Rental</div>
              <div className="avm-rental-card">
                <div className="avm-rental-info">
                  <div className="avm-rental-icon">
                    <svg viewBox="0 0 24 24">
                      <rect x="3" y="9" width="18" height="12" rx="2" />
                      <path d="M3 9l9-6 9 6" />
                      <line x1="9" y1="21" x2="9" y2="13" />
                      <line x1="15" y1="21" x2="15" y2="13" />
                    </svg>
                  </div>
                  <div>
                    <div className="avm-rental-title">Monthly Rental</div>
                    <div className="avm-rental-sub">
                      {form.facilityCode === 'NPM' ? 'New Public Market · ₱30/day base' : facilityOptions.find(f => f.code === form.facilityCode)?.name || form.facilityCode}
                    </div>
                  </div>
                </div>

                {/* ± Spinner */}
                <div className="avm-spinner">
                  <button
                    type="button"
                    className="avm-spin-btn avm-spin-left"
                    onClick={() => form.monthlyRate >= 100 && updateForm({ monthlyRate: form.monthlyRate - 100 })}
                  >
                    −
                  </button>
                  <div className="avm-spin-inner">
                    <span className="avm-spin-currency">₱</span>
                    <input
                      type="number"
                      min="0"
                      className="avm-spin-input"
                      value={form.monthlyRate || ''}
                      onChange={(e) => updateForm({ monthlyRate: Number(e.target.value) })}
                    />
                    <span className="avm-spin-freq">/mo</span>
                  </div>
                  <button
                    type="button"
                    className="avm-spin-btn avm-spin-right"
                    onClick={() => updateForm({ monthlyRate: form.monthlyRate + 100 })}
                  >
                    +
                  </button>
                </div>
              </div>

              {/* Whole-year hint */}
              <div className="avm-yearly-hint">
                <span className="avm-yearly-label">Whole-year equivalent</span>
                <strong className="avm-yearly-value">₱{wholeYearRental.toFixed(2)}</strong>
              </div>

              {/* Utility charges — NPM only */}
              {form.facilityCode === 'NPM' && form.selectedSection && (
                <>
                  <div className="avm-util-divider"></div>
                  <div className="avm-sublabel">Utility Charges</div>

                  {/* Electricity */}
                  <div
                    className={`avm-fee-toggle ${form.feeTypes.includes('Electricity') ? 'active' : ''}`}
                    onClick={() => onToggleFee('Electricity')}
                  >
                    <div className="avm-fee-icon">
                      <svg viewBox="0 0 24 24" style={{ stroke: '#d4920a' }}>
                        <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2" />
                      </svg>
                    </div>
                    <div className="avm-fee-text">
                      <div className="avm-fee-title">Electricity</div>
                      <div className="avm-fee-sub">Monthly billing — amount varies per reading</div>
                    </div>
                    <div className={`avm-fee-check ${form.feeTypes.includes('Electricity') ? 'checked' : ''}`}>
                      {form.feeTypes.includes('Electricity') && (
                        <svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12" /></svg>
                      )}
                    </div>
                  </div>

                  {/* Water */}
                  <div
                    className={`avm-fee-toggle avm-fee-toggle-water ${form.feeTypes.includes('Water') ? 'active' : ''}`}
                    onClick={() => onToggleFee('Water')}
                  >
                    <div className="avm-fee-icon">
                      <svg viewBox="0 0 24 24" style={{ stroke: '#1a7ab5' }}>
                        <path d="M12 2.69l5.66 5.66a8 8 0 1 1-11.31 0z" />
                      </svg>
                    </div>
                    <div className="avm-fee-text">
                      <div className="avm-fee-title">Water</div>
                      <div className="avm-fee-sub">Monthly billing — amount varies per reading</div>
                    </div>
                    <div className={`avm-fee-check ${form.feeTypes.includes('Water') ? 'checked' : ''}`}>
                      {form.feeTypes.includes('Water') && (
                        <svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12" /></svg>
                      )}
                    </div>
                  </div>

                  {/* Fish fee — Fish section only */}
                  {form.selectedSection === 'Fish' && (
                    <>
                      <div className="avm-util-divider"></div>
                      <div className="avm-sublabel">Section-Specific Fees</div>
                      <div className="avm-fish-fee">
                        <div className="avm-fish-icon">
                          <svg viewBox="0 0 24 24">
                            <path d="M2 12s4-6 10-6 10 6 10 6-4 6-10 6S2 12 2 12z" />
                            <circle cx="9" cy="12" r="1.5" />
                          </svg>
                        </div>
                        <div className="avm-fish-text">
                          <div className="avm-fish-title">Fish Fee</div>
                          <div className="avm-fish-sub">₱1.00 per kilo of fish sold — Fish Section only</div>
                        </div>
                        <span className="avm-fish-badge">✓ Always included</span>
                      </div>
                    </>
                  )}
                </>
              )}

              {form.facilityCode === 'NPM' && !form.selectedSection && (
                <div className="avm-section-note">Select a section above to configure utility charges.</div>
              )}
            </div>
          )}

          {/* Validation error */}
          {formError && (
            <div className="avm-error">
              <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" /><line x1="12" y1="8" x2="12" y2="12" /><line x1="12" y1="16" x2="12.01" y2="16" /></svg>
              {formError}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="eemo-modal-footer">
          <button className="btn-ghost" onClick={onCancel}>Cancel</button>
          <button className="btn-primary" onClick={onSave}>
            {isEditing ? 'Save Changes' : 'Add Vendor'}
          </button>
        </div>
      </div>
    </div>
  );
};
