import { useMemo } from 'react';

interface PaymentTransaction {
  periodKey: string;
  orNumber: string;
  paidDate?: Date;
  collector: string;
  amount: number;
}

interface VendorStall {
  stallNo: string;
  actualOccupant: string;
  contractName?: string;
  monthlyRate: number;
  partialAmount?: number;
  contractDate?: Date;
  paymentHistory: Record<string, boolean>;
  transactions?: PaymentTransaction[];
}

interface PaymentHistoryModalProps {
  show: boolean;
  stall: VendorStall | null;
  facilityCode: string;
  facilityName: string;
  onClose: () => void;
}

export const PaymentHistoryModal = ({ show, stall, facilityCode, facilityName, onClose }: PaymentHistoryModalProps) => {
  const historyData = useMemo(() => {
    if (!stall) return null;

    const history = stall.paymentHistory || {};
    const rate = stall.monthlyRate || 0;
    const contractDate = stall.contractDate;

    // Calculate experienced months only (from contract start to now)
    const experiencedMonths: Date[] = [];
    for (let i = 11; i >= 0; i--) {
      const month = new Date();
      month.setMonth(month.getMonth() - i);
      if (contractDate) {
        const contractStart = new Date(contractDate.getFullYear(), contractDate.getMonth(), 1);
        if (month >= contractStart) {
          experiencedMonths.push(month);
        }
      } else {
        experiencedMonths.push(month);
      }
    }

    const monthsPaid = experiencedMonths.filter(m => {
      const key = `${m.getFullYear()}-${String(m.getMonth() + 1).padStart(2, '0')}`;
      return history[key];
    }).length;

    const monthsUnpaid = experiencedMonths.length - monthsPaid;
    const collected = rate * monthsPaid;
    const balance = rate * monthsUnpaid;
    const compliancePct = experiencedMonths.length > 0 ? Math.round((monthsPaid / experiencedMonths.length) * 100) : 0;

    const statusLabel = monthsUnpaid === 0 ? 'Consistent Payer' :
      monthsUnpaid >= 3 ? 'Delinquent' : 'With Arrears';
    const statusClass = monthsUnpaid === 0 ? 'ph-badge-consistent' :
      monthsUnpaid >= 3 ? 'ph-badge-delinquent' : 'ph-badge-arrears';

    return {
      history,
      rate,
      experiencedMonths,
      monthsPaid,
      monthsUnpaid,
      collected,
      balance,
      compliancePct,
      statusLabel,
      statusClass,
      contractDate
    };
  }, [stall]);

  if (!show || !stall || !historyData) return null;

  const { history, rate, experiencedMonths, monthsPaid, monthsUnpaid, collected, balance, compliancePct, statusLabel, statusClass, contractDate } = historyData;

  return (
    <div className="eemo-modal-overlay" onClick={onClose}>
      <div className="ph-modal" onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className="ph-header">
          <div className="ph-header-glow"></div>

          <div className="ph-header-left">
            <div className="ph-header-icon">
              <svg viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" /><polyline points="14 2 14 8 20 8" /><line x1="16" y1="13" x2="8" y2="13" /><line x1="16" y1="17" x2="8" y2="17" /></svg>
            </div>
            <div className="ph-header-text">
              <div className="ph-header-eyebrow">
                {facilityCode}
                <span className="ph-dot">·</span>
                Stall {stall.stallNo}
                <span className="ph-dot">·</span>
                12-Month Payment Record
              </div>
              <div className="ph-header-name">{stall.actualOccupant}</div>
              {stall.contractName && stall.contractName !== stall.actualOccupant && (
                <div className="ph-header-contract">Contract: {stall.contractName}</div>
              )}
            </div>
          </div>

          <div className="ph-header-right">
            <div className="ph-header-stats">
              <div className="ph-hstat">
                <div className="ph-hstat-val">₱{rate.toLocaleString()}</div>
                <div className="ph-hstat-key">Monthly Rate</div>
              </div>
              <div className="ph-hstat-div"></div>
              <div className="ph-hstat">
                <div className="ph-hstat-val">{monthsPaid}<span className="ph-hstat-of">/{experiencedMonths.length}</span></div>
                <div className="ph-hstat-key">Months Paid</div>
              </div>
              <div className="ph-hstat-div"></div>
              <div className="ph-hstat">
                <div className={`ph-hstat-val ${monthsUnpaid > 0 ? 'ph-hstat-red' : ''}`}>{monthsUnpaid}<span className="ph-hstat-of">/{experiencedMonths.length}</span></div>
                <div className="ph-hstat-key">Unpaid</div>
              </div>
              <div className="ph-hstat-div"></div>
              <div className="ph-hstat">
                <div className="ph-hstat-val">₱{collected.toLocaleString()}</div>
                <div className="ph-hstat-key">Collected</div>
              </div>
              <div className="ph-hstat-div"></div>
              <div className="ph-hstat">
                <div className={`ph-hstat-val ${balance > 0 ? 'ph-hstat-red' : ''}`}>₱{balance.toLocaleString()}</div>
                <div className="ph-hstat-key">Balance Due</div>
              </div>
              <div className="ph-hstat-div"></div>
              <div className="ph-hstat">
                <div className="ph-hstat-val">₱{(stall.partialAmount || 0).toLocaleString()}</div>
                <div className="ph-hstat-key">Current Partial</div>
              </div>
            </div>
            <div className="ph-header-right-actions">
              <span className={`ph-status-badge ${statusClass}`}>{statusLabel}</span>
              <button className="ph-close-btn" onClick={onClose}>
                <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
              </button>
            </div>
          </div>

          <div className="ph-header-progress">
            <div className="ph-header-progress-fill" style={{ width: `${compliancePct}%` }}></div>
          </div>
        </div>

        {/* Compliance Row */}
        <div className="ph-compliance-row">
          <div className="ph-compliance-label">
            <svg viewBox="0 0 24 24"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12" /></svg>
            Collection Compliance
          </div>
          <div className="ph-compliance-track">
            <div className="ph-compliance-fill" style={{ width: `${compliancePct}%` }}></div>
          </div>
          <div className={`ph-compliance-pct ${compliancePct === 100 ? 'pct-full' : compliancePct < 50 ? 'pct-low' : ''}`}>
            {compliancePct}%
          </div>
        </div>

        {/* Ledger */}
        <div className="ph-body">
          <div className="ph-ledger-header">
            <div>Period</div>
            <div>Status</div>
            <div>OR Number</div>
            <div>Payment Date</div>
            <div>Collector</div>
            <div>Amount</div>
            <div>Balance</div>
          </div>

          <div className="ph-ledger-body">
            {(() => {
              let runningBalance = 0;
              return Array.from({ length: 12 }, (_, i) => {
                const month = new Date();
                month.setMonth(month.getMonth() - (11 - i));
                const key = `${month.getFullYear()}-${String(month.getMonth() + 1).padStart(2, '0')}`;
                const isBeforeContract = contractDate && month < new Date(contractDate.getFullYear(), contractDate.getMonth(), 1);
                const isPaid = history[key];
                const tx = stall.transactions?.find(t => t.periodKey === key);
                const isCurr = month.getMonth() === new Date().getMonth() && month.getFullYear() === new Date().getFullYear();

                if (!isPaid && !isBeforeContract) runningBalance += rate;

                return (
                  <div key={key} className={`ph-ledger-row ${isBeforeContract ? 'ph-row-before-contract' : isPaid ? 'ph-row-paid' : 'ph-row-unpaid'} ${isCurr ? 'ph-row-current' : ''}`}>
                    {/* Period */}
                    <div className="ph-cell-period">
                      <div className={`ph-period-dot ${isBeforeContract ? 'ph-dot-gray' : isPaid ? 'ph-dot-paid' : 'ph-dot-unpaid'}`}></div>
                      <div>
                        <div className="ph-period-month">{month.toLocaleDateString('en-US', { month: 'long' })}</div>
                        <div className="ph-period-year">{month.getFullYear()}</div>
                      </div>
                      {isCurr && <span className="ph-current-tag">Current</span>}
                    </div>

                    {/* Status */}
                    <div>
                      {isBeforeContract ? (
                        <span className="ph-pay-pill ph-pill-gray">
                          <svg viewBox="0 0 24 24"><line x1="5" y1="12" x2="19" y2="12" /></svg>
                          N/A
                        </span>
                      ) : (
                        <span className={`ph-pay-pill ${isPaid ? 'ph-pill-paid' : 'ph-pill-unpaid'}`}>
                          {isPaid ? (
                            <><svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12" /></svg>Paid</>
                          ) : (
                            <><svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>Unpaid</>
                          )}
                        </span>
                      )}
                    </div>

                    {/* OR Number */}
                    <div>
                      {isBeforeContract ? (
                        <span className="ph-cell-empty">—</span>
                      ) : isPaid && tx?.orNumber ? (
                        <span className="ph-or-num">{tx.orNumber}</span>
                      ) : isPaid ? (
                        <span className="ph-or-num ph-or-auto">OR-{month.getFullYear()}{String(month.getMonth() + 1).padStart(2, '0')}-{stall.stallNo}</span>
                      ) : (
                        <span className="ph-cell-empty">—</span>
                      )}
                    </div>

                    {/* Payment Date */}
                    <div>
                      {isBeforeContract ? (
                        <span className="ph-cell-empty">—</span>
                      ) : isPaid && tx?.paidDate ? (
                        <span className="ph-pay-date">{new Date(tx.paidDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}</span>
                      ) : isPaid ? (
                        <span className="ph-pay-date">{month.toLocaleDateString('en-US', { month: 'short', year: 'numeric' })}</span>
                      ) : (
                        <span className="ph-cell-empty">—</span>
                      )}
                    </div>

                    {/* Collector */}
                    <div>
                      {isBeforeContract ? (
                        <span className="ph-cell-empty">—</span>
                      ) : isPaid && tx?.collector ? (
                        <div className="ph-collector-cell">
                          <div className="ph-collector-avatar">{tx.collector[0]?.toUpperCase()}</div>
                          <span className="ph-collector-name">{tx.collector}</span>
                        </div>
                      ) : (
                        <span className="ph-cell-empty">—</span>
                      )}
                    </div>

                    {/* Amount */}
                    <div>
                      {isBeforeContract ? (
                        <span className="ph-cell-empty">—</span>
                      ) : isPaid ? (
                        <span className="ph-amount-paid">₱{rate.toLocaleString()}</span>
                      ) : (
                        <span className="ph-amount-unpaid">₱{rate.toLocaleString()}</span>
                      )}
                    </div>

                    {/* Running Balance */}
                    <div>
                      {isBeforeContract ? (
                        <span className="ph-cell-empty">—</span>
                      ) : runningBalance > 0 ? (
                        <span className="ph-balance-due">₱{runningBalance.toLocaleString()}</span>
                      ) : (
                        <span className="ph-balance-zero">₱0</span>
                      )}
                    </div>
                  </div>
                );
              });
            })()}
          </div>
        </div>

        {/* Footer */}
        <div className="ph-footer">
          <div className="ph-footer-meta">
            <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" /><line x1="12" y1="8" x2="12" y2="12" /><line x1="12" y1="16" x2="12.01" y2="16" /></svg>
            Records cover the last 12 rolling months &nbsp;·&nbsp;
            {new Date(new Date().setMonth(new Date().getMonth() - 11)).toLocaleDateString('en-US', { month: 'short', year: 'numeric' })} – {new Date().toLocaleDateString('en-US', { month: 'short', year: 'numeric' })}
          </div>
          <div className="ph-footer-actions">
            {balance > 0 && (
              <div className="ph-balance-alert">
                <svg viewBox="0 0 24 24"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z" /><line x1="12" y1="9" x2="12" y2="13" /><line x1="12" y1="17" x2="12.01" y2="17" /></svg>
                Outstanding: ₱{balance.toLocaleString()} · {monthsUnpaid} month{monthsUnpaid !== 1 ? 's' : ''} unpaid
              </div>
            )}
            <button className="btn-ghost" onClick={onClose}>Close</button>
          </div>
        </div>
      </div>
    </div>
  );
};
