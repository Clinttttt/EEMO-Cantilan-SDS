import { ReactNode } from 'react';

type KpiIconVariant = 'blue' | 'red' | 'gold' | 'green';

interface KpiCardProps {
  label: string;
  value: string | number;
  sub?: string;
  icon?: ReactNode;
  iconVariant?: KpiIconVariant;
}

export const KpiCard = ({ label, value, sub, icon, iconVariant = 'blue' }: KpiCardProps) => {
  return (
    <div className="kpi-card">
      {icon && (
        <div className={`kpi-icon kpi-icon-${iconVariant}`}>
          {icon}
        </div>
      )}
      <div>
        <div className="kpi-value">{value}</div>
        <div className="kpi-label">{label}</div>
        {sub && <div className="kpi-sub">{sub}</div>}
      </div>
    </div>
  );
};
