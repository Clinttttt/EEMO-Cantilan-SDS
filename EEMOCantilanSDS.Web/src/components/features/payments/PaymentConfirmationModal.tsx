import { Modal } from '@/components/shared/Modal';
import { Button } from '@/components/shared/Button';
import { formatCurrency } from '@/utils/formatters';
import styles from './PaymentConfirmationModal.module.css';

interface PaymentConfirmationModalProps {
  show: boolean;
  status: 'Paid' | 'Partial' | 'Unpaid';
  stallNo: string;
  occupant: string;
  billingPeriod: string;
  totalBill: number;
  partialAmount: number;
  orNumber: string;
  onConfirm: () => Promise<void>;
  onCancel: () => void;
  isLoading?: boolean;
}

export const PaymentConfirmationModal = ({
  show,
  status,
  stallNo,
  occupant,
  billingPeriod,
  totalBill,
  partialAmount,
  orNumber,
  onConfirm,
  onCancel,
  isLoading,
}: PaymentConfirmationModalProps) => {
  const getStatusClass = () => {
    switch (status) {
      case 'Paid': return styles.statusPaid;
      case 'Partial': return styles.statusPartial;
      case 'Unpaid': return styles.statusUnpaid;
      default: return '';
    }
  };

  const getStatusText = () => {
    switch (status) {
      case 'Paid': return 'Paid in Full';
      case 'Partial': return 'Partial Payment';
      case 'Unpaid': return 'Unpaid';
      default: return status;
    }
  };

  const getButtonClass = () => {
    switch (status) {
      case 'Paid': return styles.btnSuccess;
      case 'Partial': return styles.btnWarning;
      case 'Unpaid': return 'btn-danger';
      default: return '';
    }
  };

  const getButtonText = () => {
    switch (status) {
      case 'Paid': return 'Confirm Payment';
      case 'Partial': return 'Confirm Partial Payment';
      case 'Unpaid': return 'Mark as Unpaid';
      default: return 'Confirm';
    }
  };

  if (!show) return null;

  return (
    <Modal isOpen={show} onClose={onCancel} title="Confirm Payment">
      <div style={{ marginBottom: '16px', paddingBottom: '12px', borderBottom: '1px solid var(--border)' }}>
        <span style={{ fontSize: '13px', color: 'var(--text-muted)' }}>
          Please review the payment details before saving
        </span>
      </div>

      {/* Stall Info */}
      <div className={styles.confirmSection}>
        <div className={styles.confirmLabel}>Stall Information</div>
        <div className={styles.confirmInfoGrid}>
          <div className={styles.confirmInfoItem}>
            <span className={styles.confirmInfoKey}>Stall No.</span>
            <span className={styles.confirmInfoVal}>{stallNo}</span>
          </div>
          <div className={styles.confirmInfoItem}>
            <span className={styles.confirmInfoKey}>Occupant</span>
            <span className={styles.confirmInfoVal}>{occupant}</span>
          </div>
          <div className={styles.confirmInfoItem}>
            <span className={styles.confirmInfoKey}>Billing Period</span>
            <span className={styles.confirmInfoVal}>{billingPeriod}</span>
          </div>
        </div>
      </div>

      {/* Payment Details */}
      <div className={styles.confirmSection}>
        <div className={styles.confirmLabel}>Payment Details</div>
        <div className={`${styles.confirmPaymentCard} ${getStatusClass()}`}>
          <div className={styles.confirmPaymentRow}>
            <span className={styles.confirmPaymentLabel}>Status</span>
            <span className={`${styles.confirmPaymentStatus} ${getStatusClass()}`}>
              {getStatusText()}
            </span>
          </div>
          <div className={styles.confirmPaymentRow}>
            <span className={styles.confirmPaymentLabel}>Total Bill</span>
            <span className={styles.confirmPaymentValue}>{formatCurrency(totalBill)}</span>
          </div>
          {status === 'Partial' && (
            <>
              <div className={`${styles.confirmPaymentRow} ${styles.highlight}`}>
                <span className={styles.confirmPaymentLabel}>Amount Paid</span>
                <span className={styles.confirmPaymentValue}>{formatCurrency(partialAmount)}</span>
              </div>
              <div className={styles.confirmPaymentRow}>
                <span className={styles.confirmPaymentLabel}>Balance Remaining</span>
                <span className={`${styles.confirmPaymentValue} ${styles.textRed}`}>
                  {formatCurrency(totalBill - partialAmount)}
                </span>
              </div>
            </>
          )}
          {orNumber && (
            <div className={styles.confirmPaymentRow}>
              <span className={styles.confirmPaymentLabel}>OR Number</span>
              <span className={`${styles.confirmPaymentValue} ${styles.mono}`}>{orNumber}</span>
            </div>
          )}
        </div>
      </div>

      {/* Warning for Unpaid */}
      {status === 'Unpaid' && (
        <div className={styles.confirmWarning}>
          <svg viewBox="0 0 24 24">
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="8" x2="12" y2="12" />
            <line x1="12" y1="16" x2="12.01" y2="16" />
          </svg>
          <span>This will mark the payment as unpaid. No OR number will be recorded.</span>
        </div>
      )}

      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '16px' }}>
        <Button variant="ghost" onClick={onCancel} disabled={isLoading}>
          Cancel
        </Button>
        <button
          onClick={onConfirm}
          disabled={isLoading}
          className={`px-4 py-2 rounded font-medium transition-colors ${getButtonClass()}`}
        >
          {isLoading ? 'Processing...' : getButtonText()}
        </button>
      </div>
    </Modal>
  );
};
