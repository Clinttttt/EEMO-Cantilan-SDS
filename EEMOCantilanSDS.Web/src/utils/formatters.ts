// Utility functions for formatting

export const formatCurrency = (amount: number): string => {
  return `₱${amount.toLocaleString('en-PH', { 
    minimumFractionDigits: 2, 
    maximumFractionDigits: 2 
  })}`;
};

export const formatDate = (date: string | Date): string => {
  return new Date(date).toLocaleDateString('en-PH', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  });
};

export const formatShortDate = (date: string | Date): string => {
  return new Date(date).toLocaleDateString('en-PH', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  });
};

export const formatMonthYear = (year: number, month: number): string => {
  const date = new Date(year, month - 1);
  return date.toLocaleDateString('en-PH', {
    year: 'numeric',
    month: 'long',
  });
};
