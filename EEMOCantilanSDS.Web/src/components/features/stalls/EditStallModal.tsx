import { useState } from 'react';
import { Modal } from '@/components/shared/Modal';
import { Button } from '@/components/shared/Button';
import { Input } from '@/components/shared/Input';
import type { StallDto, UpdateStallCommand } from '@/types/dto';

interface EditStallModalProps {
  stall: StallDto;
  onClose: () => void;
  onSubmit: (command: UpdateStallCommand) => Promise<void>;
  isLoading: boolean;
}

export const EditStallModal = ({ stall, onClose, onSubmit, isLoading }: EditStallModalProps) => {
  const [formData, setFormData] = useState({
    monthlyRate: stall.monthlyRate,
    areaSqm: stall.areaSqm || 0,
    areaLocation: stall.areaLocation || '',
    actualOccupant: stall.actualOccupant || '',
    nameOnContract: stall.nameOnContract || '',
    remarks: stall.remarks || '',
  });
  const [errors, setErrors] = useState<Record<string, string>>({});

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrors({});

    try {
      const command: UpdateStallCommand = {
        stallId: stall.id,
        monthlyRate: formData.monthlyRate,
        areaSqm: formData.areaSqm,
        areaLocation: formData.areaLocation,
        actualOccupant: formData.actualOccupant,
        nameOnContract: formData.nameOnContract,
        remarks: formData.remarks,
      };
      await onSubmit(command);
    } catch (error: any) {
      if (error.response?.data?.errors) {
        const backendErrors: Record<string, string> = {};
        Object.entries(error.response.data.errors).forEach(([key, value]) => {
          backendErrors[key] = (value as string[])[0];
        });
        setErrors(backendErrors);
      }
    }
  };

  return (
    <Modal 
      isOpen={true} 
      onClose={onClose} 
      title="Edit Stall Details"
    >
      <div style={{ marginBottom: '16px', paddingBottom: '12px', borderBottom: '1px solid var(--border)' }}>
        <span style={{ fontSize: '13px', color: 'var(--text-muted)' }}>
          Stall {stall.stallNo} · {stall.actualOccupant}
        </span>
      </div>

      <form id="edit-form" onSubmit={handleSubmit} className="edit-form-grid">
        <Input
          label="Actual Occupant"
          value={formData.actualOccupant}
          onChange={(e) => setFormData({ ...formData, actualOccupant: e.target.value })}
          error={errors.actualOccupant}
          required
        />

        <Input
          label="Name on Contract"
          value={formData.nameOnContract}
          onChange={(e) => setFormData({ ...formData, nameOnContract: e.target.value })}
          error={errors.nameOnContract}
        />

        <Input
          label="Area (sq.m)"
          type="number"
          value={formData.areaSqm}
          onChange={(e) => setFormData({ ...formData, areaSqm: Number(e.target.value) })}
          error={errors.areaSqm}
        />

        <Input
          label="Area Location / Note"
          value={formData.areaLocation}
          onChange={(e) => setFormData({ ...formData, areaLocation: e.target.value })}
          error={errors.areaLocation}
          placeholder="e.g. Corner slot, near entrance"
        />

        <Input
          label="Monthly Rental Rate (₱)"
          type="number"
          value={formData.monthlyRate}
          onChange={(e) => setFormData({ ...formData, monthlyRate: Number(e.target.value) })}
          error={errors.monthlyRate}
          required
        />

        <div style={{ gridColumn: '1 / -1' }}>
          <label className="form-label">Remarks</label>
          <textarea
            className="form-input form-textarea"
            value={formData.remarks}
            onChange={(e) => setFormData({ ...formData, remarks: e.target.value })}
            rows={3}
            placeholder="Any additional notes…"
          />
        </div>
      </form>

      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '16px' }}>
        <Button variant="ghost" onClick={onClose} disabled={isLoading}>
          Cancel
        </Button>
        <Button type="submit" form="edit-form" isLoading={isLoading}>
          Save Changes
        </Button>
      </div>
    </Modal>
  );
};
