import { useState } from 'react';

interface VendorDetailPanelProps {
  show: boolean;
  vendor: {
    stallNo: string;
    actualOccupant: string;
    contractName?: string;
    facilityCode: string;
    section: string;
    isActive: boolean;
    monthlyRate: number;
    areaSqm?: number;
    contractDate?: Date;
    contractYears?: number;
    orNo?: string;
    feeTypes?: string[];
    isPaidThisMonth?: boolean;
    isPartialThisMonth?: boolean;
    dailyCollections?: Record<string, boolean>;
  };
  onClose: () => void;
  onHistory: () => void;
  onEdit: () => void;
}

export const VendorDetailPanel = ({ show, vendor, onClose, onHistory, onEdit }: VendorDetailPanelProps) => {
  const [calendarMonth, setCalendarMonth] = useState(new Date());

  if (!show) return null;

  const getContractExpiry = () => {
    if (!vendor.contractDate || !vendor.contractYears) return null;
    const expiry = new Date(vendor.contractDate);
    expiry.setFullYear(expiry.getFullYear() + vendor.contractYears);
    return expiry;
  };

  const contractExpiry = getContractExpiry();
  const currentMonth = new Date();
  const monthStr = currentMonth.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });

  const hasVegDaily = vendor.facilityCode === 'NPM' && vendor.section.toLowerCase().includes('vegetable');
  const hasFishSection = vendor.facilityCode === 'NPM' && vendor.section.toLowerCase().includes('fish');
  const hasMeatSection = vendor.facilityCode === 'NPM' && vendor.section.toLowerCase().includes('meat');
  const hasFishPerKg = vendor.feeTypes?.some(f => f.toLowerCase().includes('fish') && f.toLowerCase().includes('kg'));

  // Calendar calculations
  const showDailyCalendar = vendor.facilityCode === 'NPM' && (hasVegDaily || hasFishSection || hasMeatSection);
  const calFirst = new Date(calendarMonth.getFullYear(), calendarMonth.getMonth(), 1);
  const calDays = new Date(calendarMonth.getFullYear(), calendarMonth.getMonth() + 1, 0).getDate();
  const calOffset = calFirst.getDay();
  const calMStr = calendarMonth.toISOString().split('T')[0].substring(0, 7);
  const calToday = new Date();
  calToday.setHours(0, 0, 0, 0);
  const isCurMo = calendarMonth.getFullYear() === calToday.getFullYear() && calendarMonth.getMonth() === calToday.getMonth();
  const maxDay = isCurMo ? calToday.getDate() : calDays;

  let calCollected = 0;
  for (let ci = 1; ci <= maxDay; ci++) {
    const ck = `${calMStr}-${ci.toString().padStart(2, '0')}`;
    if (vendor.dailyCollections?.[ck]) calCollected++;
  }
  const calMissed = maxDay - calCollected;
  const calTotal = calCollected * 30;

  const canGoNext = calendarMonth < new Date(calToday.getFullYear(), calToday.getMonth(), 1);

  const prevMonth = () => {
    setCalendarMonth(new Date(calendarMonth.getFullYear(), calendarMonth.getMonth() - 1, 1));
  };

  const nextMonth = () => {
    if (canGoNext) {
      setCalendarMonth(new Date(calendarMonth.getFullYear(), calendarMonth.getMonth() + 1, 1));
    }
  };

  // Build calendar days array
  const calendarDays = [];
  for (let i = 0; i < calOffset; i++) {
    calendarDays.push(null);
  }
  for (let day = 1; day <= calDays; day++) {
    calendarDays.push(day);
  }

  return (
    <div className="eemo-modal-overlay" onClick={onClose}>
      <div className="eemo-modal eemo-modal-wide detail-modal" onClick={(e) => e.stopPropagation()}>
        
        {/* ═══ HEADER ═══ */}
        <div className="detail-header">
          <div className="detail-header-left">
            <div className="detail-avatar">{vendor.actualOccupant[0]?.toUpperCase() || '?'}</div>
            <div>
              <div className="detail-name">{vendor.actualOccupant}</div>
              <div className="detail-meta">
                <span>Stall {vendor.stallNo}</span>
                <span className="detail-sep">·</span>
                <span>{vendor.facilityCode}</span>
                <span className="detail-sep">·</span>
                <span>{vendor.section}</span>
                <span className="detail-sep">·</span>
                <span className={`status-pill ${vendor.isActive ? 'pill-active' : 'pill-inactive'}`}>
                  {vendor.isActive ? 'Active' : 'Closed'}
                </span>
              </div>
            </div>
          </div>
          <button className="eemo-modal-close" onClick={onClose}>
            <svg viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
          </button>
        </div>

        <div className="eemo-modal-body detail-body">
          
          {/* ═══ TOP STATS ROW ═══ */}
          <div className="detail-stats-row">
            <div className="detail-stat-card">
              <div className="detail-stat-icon ds-icon-gold">
                <svg viewBox="0 0 24 24"><line x1="12" y1="1" x2="12" y2="23" /><path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6" /></svg>
              </div>
              <div>
                <div className="detail-stat-val">₱{vendor.monthlyRate.toLocaleString()}</div>
                <div className="detail-stat-key">Monthly Rental</div>
              </div>
            </div>

            <div className="detail-stat-card">
              <div className="detail-stat-icon ds-icon-navy">
                <svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="18" rx="2" /><line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" /></svg>
              </div>
              <div>
                <div className="detail-stat-val">
                  {vendor.contractDate ? new Date(vendor.contractDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : 'No Contract'}
                </div>
                <div className="detail-stat-key">Contract Date</div>
              </div>
            </div>

            <div className="detail-stat-card">
              <div className="detail-stat-icon ds-icon-gold">
                <svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12" /></svg>
              </div>
              <div>
                <div className="detail-stat-val">
                  <span className={`status-pill ${vendor.isPaidThisMonth ? 'pill-paid' : vendor.isPartialThisMonth ? 'pill-partial' : 'pill-unpaid'}`} style={{ fontSize: '11px', padding: '4px 12px' }}>
                    {vendor.isPaidThisMonth ? 'Rental Paid' : vendor.isPartialThisMonth ? 'Partial' : 'Unpaid'}
                  </span>
                </div>
                <div className="detail-stat-key">{monthStr.toUpperCase()}</div>
              </div>
            </div>

            <div className="detail-stat-card">
              <div className="detail-stat-icon ds-icon-gold">
                <svg viewBox="0 0 24 24"><path d="M20 7H4a2 2 0 00-2 2v6a2 2 0 002 2h16a2 2 0 002-2V9a2 2 0 00-2-2z" /></svg>
              </div>
              <div>
                <div className="detail-stat-val">{vendor.areaSqm || '—'} sq.m</div>
                <div className="detail-stat-key">Stall Area</div>
              </div>
            </div>
          </div>

          {/* ═══ TWO-COLUMN INFO LAYOUT ═══ */}
          <div className="detail-two-col">
            
            {/* LEFT: Vendor Info */}
            <div className="detail-section">
              <div className="detail-section-title">
                <svg viewBox="0 0 24 24"><path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2" /><circle cx="12" cy="7" r="4" /></svg>
                Vendor Information
              </div>
              <div className="detail-info-grid">
                <div className="detail-info-row">
                  <span className="detail-info-key">Actual Occupant</span>
                  <span className="detail-info-val">{vendor.actualOccupant}</span>
                </div>
                <div className="detail-info-row">
                  <span className="detail-info-key">Name on Contract</span>
                  <span className="detail-info-val">
                    {!vendor.contractName || vendor.contractName === 'No contract' ? (
                      <span className="no-contract">No contract</span>
                    ) : (
                      vendor.contractName
                    )}
                  </span>
                </div>
                <div className="detail-info-row">
                  <span className="detail-info-key">Facility</span>
                  <span className="detail-info-val">
                    {vendor.facilityCode === 'NPM' ? 'New Public Market' : vendor.facilityCode}
                  </span>
                </div>
                <div className="detail-info-row">
                  <span className="detail-info-key">Section / Area</span>
                  <span className="detail-info-val"><span className="section-tag">{vendor.section}</span></span>
                </div>
                {contractExpiry && (
                  <div className="detail-info-row">
                    <span className="detail-info-key">Contract Duration</span>
                    <span className="detail-info-val">
                      {vendor.contractYears} year{vendor.contractYears !== 1 ? 's' : ''} (expires {contractExpiry.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })})
                    </span>
                  </div>
                )}
              </div>
            </div>

            {/* RIGHT: Fee Structure */}
            <div className="detail-section">
              <div className="detail-section-title">
                <svg viewBox="0 0 24 24"><line x1="12" y1="1" x2="12" y2="23" /><path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6" /></svg>
                Fee Structure
              </div>

              {hasVegDaily && (
                <div className="fee-breakdown-item fee-item-daily">
                  <div className="fee-item-left">
                    <div className="fee-item-icon fee-icon-navy">
                      <svg viewBox="0 0 24 24"><path d="M12 2a10 10 0 110 20A10 10 0 0112 2z" /><path d="M12 8v8M8 12h8" /></svg>
                    </div>
                    <div>
                      <div className="fee-item-name">Daily Collection</div>
                      <div className="fee-item-sub">Vegetable vendor · Collected daily</div>
                    </div>
                  </div>
                  <div className="fee-item-amount">₱30<span className="fee-freq">/day</span></div>
                </div>
              )}

              {hasFishSection && (
                <div className="fee-breakdown-item fee-item-daily">
                  <div className="fee-item-left">
                    <div className="fee-item-icon fee-icon-navy">
                      <svg viewBox="0 0 24 24"><path d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /><path d="M9 9h.01M15 9h.01M9.5 15a3.5 3.5 0 005 0" /></svg>
                    </div>
                    <div>
                      <div className="fee-item-name">Daily Collection</div>
                      <div className="fee-item-sub">Fish vendor · Collected daily</div>
                    </div>
                  </div>
                  <div className="fee-item-amount">₱30<span className="fee-freq">/day</span></div>
                </div>
              )}

              {hasMeatSection && (
                <div className="fee-breakdown-item fee-item-daily">
                  <div className="fee-item-left">
                    <div className="fee-item-icon fee-icon-navy">
                      <svg viewBox="0 0 24 24"><path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z" /></svg>
                    </div>
                    <div>
                      <div className="fee-item-name">Daily Collection</div>
                      <div className="fee-item-sub">Meat vendor · Collected daily</div>
                    </div>
                  </div>
                  <div className="fee-item-amount">₱30<span className="fee-freq">/day</span></div>
                </div>
              )}

              {hasFishPerKg && (
                <div className="fee-breakdown-item fee-item-perkg">
                  <div className="fee-item-left">
                    <div className="fee-item-icon fee-icon-gold">
                      <svg viewBox="0 0 24 24"><path d="M3 6h18M3 12h18M3 18h18" /></svg>
                    </div>
                    <div>
                      <div className="fee-item-name">Fish Fee (by weight)</div>
                      <div className="fee-item-sub">All fish types · Per kilo sold</div>
                    </div>
                  </div>
                  <div className="fee-item-amount">₱1<span className="fee-freq">/kg</span></div>
                </div>
              )}

              {vendor.feeTypes?.filter(f => !f.toLowerCase().includes('kg')).map((fee, idx) => {
                const isElec = fee.toLowerCase().includes('electricity');
                const isWater = fee.toLowerCase().includes('water');
                return (
                  <div key={idx} className="fee-breakdown-item fee-item-utility">
                    <div className="fee-item-left">
                      <div className="fee-item-icon fee-icon-utility">
                        {isElec ? (
                          <svg viewBox="0 0 24 24"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2" /></svg>
                        ) : isWater ? (
                          <svg viewBox="0 0 24 24"><path d="M12 2.69l5.66 5.66a8 8 0 11-11.31 0z" /></svg>
                        ) : (
                          <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" /><line x1="12" y1="8" x2="12" y2="16" /></svg>
                        )}
                      </div>
                      <div>
                        <div className="fee-item-name">{fee}</div>
                        <div className="fee-item-sub">Utility · Billed monthly</div>
                      </div>
                    </div>
                    <div className="fee-item-amount fee-amount-variable">Variable</div>
                  </div>
                );
              })}

              {!vendor.feeTypes?.length && !hasVegDaily && !hasFishSection && !hasMeatSection && (
                <div className="fee-none">No additional fees for this stall.</div>
              )}
            </div>
          </div>

          {/* ═══ DAILY COLLECTION CALENDAR ═══ */}
          {showDailyCalendar && (
            <div className="detail-section">
              <div className="detail-section-title">
                <svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="18" rx="2" /><line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" /></svg>
                Daily Collection — ₱30/day{hasFishSection ? ' + Fish Fee (₱1/kg)' : ''}
              </div>

              {/* Calendar Navigation */}
              <div className="cal-nav">
                <button className="cal-nav-btn" onClick={prevMonth}>
                  <svg viewBox="0 0 24 24"><polyline points="15 18 9 12 15 6" /></svg>
                </button>
                <span className="cal-month-label">{calendarMonth.toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}</span>
                <button className="cal-nav-btn" onClick={nextMonth} disabled={!canGoNext}>
                  <svg viewBox="0 0 24 24"><polyline points="9 18 15 12 9 6" /></svg>
                </button>
              </div>

              {/* Calendar Grid */}
              <div className="cal-grid-wrap">
                {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map(day => (
                  <div key={day} className="cal-day-hdr">{day}</div>
                ))}

                {calendarDays.map((day, idx) => {
                  if (day === null) {
                    return <div key={`empty-${idx}`} className="cal-cell cal-empty"></div>;
                  }

                  const dayDate = new Date(calendarMonth.getFullYear(), calendarMonth.getMonth(), day);
                  dayDate.setHours(0, 0, 0, 0);
                  const dayKey = dayDate.toISOString().split('T')[0];
                  const isFuture = dayDate > calToday;
                  const isToday = dayDate.getTime() === calToday.getTime();
                  const isMarked = vendor.dailyCollections?.[dayKey] || false;
                  const cellClass = isFuture ? 'cal-future' : isMarked ? 'cal-paid' : 'cal-miss';

                  return (
                    <div key={day} className={`cal-cell ${cellClass} ${isToday ? 'cal-today' : ''}`}>
                      <div className="cal-cell-top">
                        <span className="cal-num">{day}</span>
                        {!isFuture && (
                          <span className={`cal-checkmark ${isMarked ? 'cal-check-yes' : 'cal-check-no'}`}>
                            {isMarked ? '✓' : '✕'}
                          </span>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>

              {/* Calendar Stats */}
              <div className="cal-footer-stats">
                <div className="cal-sum-item">
                  <span className="cal-sum-val cal-sum-green">{calCollected}</span>
                  <span className="cal-sum-key">Days Collected</span>
                </div>
                <div className="cal-sum-item">
                  <span className="cal-sum-val cal-sum-red">{calMissed}</span>
                  <span className="cal-sum-key">Days Missed</span>
                </div>
                <div className="cal-sum-item">
                  <span className="cal-sum-val">₱{calTotal.toLocaleString()}</span>
                  <span className="cal-sum-key">Daily Collected {calendarMonth.toLocaleDateString('en-US', { month: 'short', year: 'numeric' }).toUpperCase()}</span>
                </div>
                <div className="cal-sum-item" style={{ background: 'rgba(200, 168, 75, 0.07)', borderColor: '#c8a84b' }}>
                  <span className="cal-sum-val" style={{ color: '#c8a84b' }}>₱{calTotal.toLocaleString()}</span>
                  <span className="cal-sum-key">Total Fee ({calendarMonth.toLocaleDateString('en-US', { month: 'short', year: 'numeric' }).toUpperCase()})</span>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* ═══ FOOTER ═══ */}
        <div className="eemo-modal-footer">
          <button className="btn-ghost" onClick={onClose}>Close</button>
          <button className="btn-outline modal-icon-button" onClick={onHistory}>
            History
          </button>
          <button className="btn-primary" onClick={onEdit}>
            <svg viewBox="0 0 24 24"><path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" /><path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" /></svg>
            Edit Vendor
          </button>
        </div>
      </div>
    </div>
  );
};
