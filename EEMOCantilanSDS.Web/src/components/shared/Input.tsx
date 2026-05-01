import { InputHTMLAttributes } from 'react';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
}

export const Input = ({ label, error, className = '', ...props }: InputProps) => {
  return (
    <div className="space-y-1">
      <label className="block text-sm font-medium text-navy">
        {label}
        {props.required && <span className="text-red ml-1">*</span>}
      </label>
      <input
        className={`w-full px-4 py-2 border rounded focus:outline-none focus:ring-2 focus:ring-gold ${
          error ? 'border-red' : 'border-border'
        } ${className}`}
        {...props}
      />
      {error && <p className="text-sm text-red">{error}</p>}
    </div>
  );
};
