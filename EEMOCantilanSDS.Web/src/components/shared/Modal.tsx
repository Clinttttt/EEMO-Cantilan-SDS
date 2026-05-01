interface ModalProps {
  isOpen?: boolean;
  onClose: () => void;
  title: string;
  children: React.ReactNode;
}

export const Modal = ({ isOpen = true, onClose, title, children }: ModalProps) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="absolute inset-0 bg-navy/50" onClick={onClose} />
      <div className="relative bg-bg-card rounded-lg shadow-xl max-w-2xl w-full mx-4 max-h-[90vh] overflow-auto">
        <div className="flex items-center justify-between p-6 border-b border-border">
          <h3 className="text-xl font-semibold text-navy">{title}</h3>
          <button onClick={onClose} className="text-text-muted hover:text-navy transition-colors">
            ✕
          </button>
        </div>
        <div className="p-6">{children}</div>
      </div>
    </div>
  );
};
