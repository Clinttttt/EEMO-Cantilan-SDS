using EEMOCantilanSDS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    public class Contract : AuditableEntity
    {
        public Guid StallId { get; private set; }
        public string? ORNumber { get; private set; }
        public string ActualOccupant { get; private set; } = string.Empty;
        public string? NameOnContract { get; private set; }
        public DateOnly EffectivityDate { get; private set; }
        public int DurationYears { get; private set; }
        public decimal MonthlyRentalRate { get; private set; }
        public decimal? ActualMonthlyRental { get; private set; }
        public bool IsActive { get; private set; } = true;
        public string? Remarks { get; private set; }
        public Stall? Stall { get; private set; }
        private Contract() { }

        public DateOnly ExpiryDate => EffectivityDate.AddYears(DurationYears);
        public decimal WholeYearRental => MonthlyRentalRate * 12;
        public bool IsExpired => DateOnly.FromDateTime(DateTime.UtcNow) > ExpiryDate;
        public bool IsExpiringSoon => !IsExpired &&
                                         ExpiryDate <= DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
        public static Contract Create(Guid stallId,string actualOccupant,
            string? nameOnContract,
            DateOnly effectivityDate,
            int durationYears,
            decimal monthlyRate,
            decimal? actualMonthlyRental = null,
            string? remarks = null,
            string createdBy = "System")
        {
            return new Contract
            {
                Id = Guid.NewGuid(),
                StallId = stallId,
                ActualOccupant = actualOccupant,
                NameOnContract = nameOnContract,
                EffectivityDate = effectivityDate,
                DurationYears = durationYears,
                MonthlyRentalRate = monthlyRate,
                ActualMonthlyRental = actualMonthlyRental,
                Remarks = remarks,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
        public void UpdateOccupant(string actualOccupant, string? nameOnContract, string updatedBy)
        {
            ActualOccupant = actualOccupant;
            NameOnContract = nameOnContract;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void UpdateRemarks(string? remarks, string updatedBy)
        {
            Remarks = remarks;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void Terminate(string updatedBy)
        {
            IsActive = false;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
