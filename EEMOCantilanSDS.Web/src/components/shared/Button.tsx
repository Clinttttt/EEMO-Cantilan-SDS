import { ButtonHTMLAttributes, ReactNode } from 'react';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'ghost' | 'outline' | 'danger';
  children: ReactNode;
  isLoading?: boolean;
}

export const Button = ({
  variant = 'primary',
  children,
  isLoading,
  disabled,
  className = '',
  ...props
}: ButtonProps) => {
  const baseStyles = 'px-4 py-2 rounded font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed';
  
  const variantStyles = {
    primary: 'bg-navy text-gold-light hover:opacity-90 border-0',
    ghost: 'bg-bg-card border border-border text-text-muted hover:bg-bg',
    outline: 'bg-bg-card border border-border text-text-subtle hover:border-gold hover:text-gold',
    danger: 'bg-red text-white hover:opacity-90 border-0',
  };

  return (
    <button
      className={`${baseStyles} ${variantStyles[variant]} ${className}`}
      disabled={disabled || isLoading}
      {...props}
    >
      {isLoading ? 'Loading...' : children}
    </button>
  );
};
