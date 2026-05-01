import { SelectHTMLAttributes } from 'react';

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string;
  options: { value: string | number; label: string }[];
  error?: string;
}

export const Select = ({ label, options, error, className = '', ...props }: SelectProps) => {
  return (
    <div className="space-y-1">
      <label className="block text-sm font-medium text-navy">
        {label}
        {props.required && <span className="text-red ml-1">*</span>}
      </label>
      <select
        className={`w-full px-4 py-2 border rounded focus:outline-none focus:ring-2 focus:ring-gold ${
          error ? 'border-red' : 'border-border'
        } ${className}`}
        {...props}
      >
        {options.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
      {error && <p className="text-sm text-red">{error}</p>}
    </div>
  );
};
