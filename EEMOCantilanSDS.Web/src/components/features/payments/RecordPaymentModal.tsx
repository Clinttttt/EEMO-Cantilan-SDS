import { Modal } from '@/components/shared/Modal';
import { Button } from '@/components/shared/Button';
import type { StallDto } from '@/types/dto';
import { formatCurrency } from '@/utils/formatters';
import './RecordPaymentModal.css';

interface RecordPaymentModalProps {
  stall: StallDto;
  paymentStatus: 'Paid' | 'Partial' | 'Unpaid';
  partialAmount: number;
  remarks: string;
  showValidation: boolean;
  onStatusChange: (status: 'Paid' | 'Partial' | 'Unpaid') => void;
  onPartialAmountChange: (amount: number) => void;
  onRemarksChange: (remarks: string) => void;
  onClose: () => void;
  onContinue: () => void;
}

export const RecordPaymentModal = ({
  stall,
  paymentStatus,
  partialAmount,
  remarks,
  showValidation,
  onStatusChange,
  onPartialAmountChange,
  onRemarksChange,
  onClose,
  onContinue,
}: RecordPaymentModalProps) => {
  return (
    <Modal 
      isOpen={true} 
      onClose={onClose} 
      title="Record Payment"
    >
      <div style={{ marginBottom: '16px', paddingBottom: '12px', borderBottom: '1px solid var(--border)' }}>
        <span style={{ fontSize: '13px', color: 'var(--text-muted)' }}>
          Stall {stall.stallNo} · {stall.actualOccupant} · {new Date().toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
        </span>
      </div>

      <div className="form-group">
        <label className="form-label">Payment Status *</label>
        <div className="pay-options">
          <div
            className={`pay-option ${paymentStatus === 'Paid' ? 'pay-opt-paid' : ''}`}
            onClick={() => onStatusChange('Paid')}
          >
            <svg viewBox="0 0 24 24">
              <polyline points="20 6 9 17 4 12" />
            </svg>
            <span>Paid</span>
          </div>
          <div
            className={`pay-option ${paymentStatus === 'Partial' ? 'pay-opt-partial' : ''}`}
            onClick={() => onStatusChange('Partial')}
          >
            <svg viewBox="0 0 24 24">
              <circle cx="12" cy="12" r="10" fill="none" stroke="currentColor" strokeWidth="2" />
              <path d="M12 2 A 10 10 0 0 1 12 22 Z" fill="currentColor" />
            </svg>
            <span>Partial</span>
          </div>
          <div
            className={`pay-option ${paymentStatus === 'Unpaid' ? 'pay-opt-unpaid' : ''}`}
            onClick={() => onStatusChange('Unpaid')}
          >
            <svg viewBox="0 0 24 24">
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
            <span>Unpaid</span>
          </div>
        </div>
      </div>

      {paymentStatus === 'Partial' && (
        <div className="partial-amount-section">
          <label className="partial-label">Partial Amount Paid *</label>
          <div className="partial-input-wrap">
            <span className="currency-symbol">₱</span>
            <input
              type="number"
              min="0"
              max={stall.monthlyRate}
              step="0.01"
              value={partialAmount}
              onChange={(e) => onPartialAmountChange(Number(e.target.value))}
              placeholder="0.00"
              className={`partial-input ${showValidation && partialAmount <= 0 ? 'input-error' : ''}`}
            />
          </div>
          {showValidation && partialAmount <= 0 && <span className="input-error-msg">Partial amount must be greater than ₱0</span>}
          {partialAmount > 0 && partialAmount < stall.monthlyRate && (
            <div className="partial-hint">Balance remaining: {formatCurrency(stall.monthlyRate - partialAmount)}</div>
          )}
        </div>
      )}

      <div className="form-group">
        <label className="form-label">Remarks (optional)</label>
        <textarea
          className="form-input form-textarea"
          value={remarks}
          onChange={(e) => onRemarksChange(e.target.value)}
          rows={2}
          placeholder="e.g. Late payment..."
        />
      </div>

      <div className="pay-fee-block">
        <div className="pay-fee-header">
          <span className="pay-fee-label">Total Bill</span>
          <span className="pay-fee-rate">{formatCurrency(stall.monthlyRate)} / month</span>
        </div>
        {paymentStatus === 'Paid' && (
          <div className="pay-record-row">
            <span className="pay-record-key">Amount Paid</span>
            <span className="pay-record-val green">{formatCurrency(stall.monthlyRate)}</span>
          </div>
        )}
        {paymentStatus === 'Partial' && (
          <>
            <div className="pay-record-row">
              <span className="pay-record-key">Partial Amount Paid</span>
              <span className="pay-record-val green">{formatCurrency(partialAmount)}</span>
            </div>
            <div className="pay-record-row">
              <span className="pay-record-key">Balance Remaining</span>
              <span className="pay-record-val red">{formatCurrency(stall.monthlyRate - partialAmount)}</span>
            </div>
          </>
        )}
        {paymentStatus === 'Unpaid' && (
          <div className="pay-record-row">
            <span className="pay-record-key">Amount Due</span>
            <span className="pay-record-val red">{formatCurrency(stall.monthlyRate)}</span>
          </div>
        )}
      </div>

      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '16px' }}>
        <Button variant="ghost" onClick={onClose}>
          Cancel
        </Button>
        <Button variant="primary" onClick={onContinue}>
          Continue
        </Button>
      </div>
    </Modal>
  );
};
