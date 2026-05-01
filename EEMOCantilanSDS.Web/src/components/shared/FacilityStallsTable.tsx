import { useNavigate } from 'react-router-dom';

interface BaseStall {
  stallNo: string;
  actualOccupant: string;
  contractName?: string;
  areaSqm?: number;
  contractDate?: Date | string;
  monthlyRate: number;
  orNumber?: string;
  isActive?: boolean;
  isPaid?: boolean;
  isPartial?: boolean;
}

interface FacilityStallsTableProps<T extends BaseStall> {
  facility: string;
  filteredStalls: T[];
  onPaymentClick: (stall: T) => void;
  onHistoryClick: (stall: T) => void;
  onEditClick?: (stall: T) => void;
}

export const FacilityStallsTable = <T extends BaseStall>({
  facility,
  filteredStalls,
  onPaymentClick,
  onHistoryClick,
  onEditClick,
}: FacilityStallsTableProps<T>) => {
  const navigate = useNavigate();

  const getContractDate = (stall: T): string => {
    if (!stall.contractDate) return '—';
    const date = typeof stall.contractDate === 'string' ? new Date(stall.contractDate) : stall.contractDate;
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  };

  const getRowClass = (stall: T): string => {
    if (stall.isActive === false) return 'row-inactive';
    if (stall.isPaid) return 'row-paid';
    if (stall.isPartial) return 'row-partial';
    return 'row-unpaid';
  };

  const navigateToProfile = (stall: T) => {
    const facilityId = facility.toLowerCase();
    navigate(`/profile/${facilityId}/${stall.stallNo}`);
  };

  return (
    <div className="panel">
      {filteredStalls.length === 0 ? (
        <div className="empty-state">
          <svg viewBox="0 0 24 24">
            <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
          </svg>
          <div className="empty-title">No Stalls Found</div>
          <div className="empty-sub">Try adjusting the filters or search.</div>
        </div>
      ) : (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>Stall No.</th>
                <th>Actual Occupant</th>
                <th>Name on Contract</th>
                <th>Area (sqm)</th>
                <th>Contract Date</th>
                <th>Monthly Rent</th>
                <th>OR No.</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredStalls.map((stall, index) => (
                <tr key={index} className={`${getRowClass(stall)} data-row`}>
                  <td>{stall.stallNo}</td>
                  <td>{stall.actualOccupant}</td>
                  <td>
                    {!stall.contractName || stall.contractName.startsWith('No contract') ? (
                      <span>No contract</span>
                    ) : (
                      stall.contractName
                    )}
                  </td>
                  <td>{stall.areaSqm || '—'}</td>
                  <td>{getContractDate(stall)}</td>
                  <td>₱{stall.monthlyRate.toLocaleString()}</td>
                  <td>
                    <span className="mono">{stall.orNumber || '—'}</span>
                  </td>
                  <td>
                    <div className="action-btns">
                      <button
                        className="action-btn"
                        title="View / Record Payment"
                        onClick={() => onPaymentClick(stall)}
                      >
                        <svg viewBox="0 0 24 24">
                          <line x1="12" y1="1" x2="12" y2="23" />
                          <path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6" />
                        </svg>
                      </button>
                      <button
                        className="action-btn"
                        title="Payment History"
                        onClick={() => onHistoryClick(stall)}
                      >
                        <svg viewBox="0 0 24 24">
                          <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
                          <polyline points="14 2 14 8 20 8" />
                          <line x1="16" y1="13" x2="8" y2="13" />
                          <line x1="16" y1="17" x2="8" y2="17" />
                        </svg>
                      </button>
                      <button
                        className="action-btn"
                        title="View Profile"
                        onClick={() => navigateToProfile(stall)}
                      >
                        <svg viewBox="0 0 24 24">
                          <path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2" />
                          <circle cx="12" cy="7" r="4" />
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
  );
};
