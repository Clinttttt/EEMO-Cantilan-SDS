using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Slaughterhouse
{
    public class SlaughterTransaction : AuditableEntity
    {
        public Guid FacilityId { get; private set; }
        public Guid? CollectorId { get; private set; }
        public string OwnerName { get; private set; } = string.Empty;
        public AnimalType AnimalType { get; private set; }
        public string? CustomAnimalType { get; private set; }  // For custom animals (not Hog/Carabao/Cow)
        public int NumberOfHeads { get; private set; }
        public decimal RatePerHead { get; private set; }
        public decimal TotalAmount => RatePerHead * NumberOfHeads;
        public string? ORNumber { get; private set; }
        public DateOnly TransactionDate { get; private set; }

        // Fee breakdown (stored for audit transparency)
        public decimal SlaughterFee { get; private set; }
        public decimal? SlaughterPermit { get; private set; }  // Carabao/Cow only
        public decimal AntemortemFee { get; private set; }
        public decimal? PostmortemFee { get; private set; }  // Carabao/Cow only
        public decimal TableCharge { get; private set; }
        public decimal? EntranceFee { get; private set; }  // Hog only
        public decimal? LivestockFee { get; private set; }  // Carabao/Cow only

        public Facility? Facility { get; private set; }

        private SlaughterTransaction() { }

        public static SlaughterTransaction CreateCustomAnimal(
            Guid facilityId,
            Guid collectorId,
            string ownerName,
            string customAnimalType,
            int heads,
            decimal customRate,
            string orNumber,
            DateOnly transactionDate,
            string createdBy = "System")
        {
            return new SlaughterTransaction
            {
                Id = Guid.NewGuid(),
                FacilityId = facilityId,
                CollectorId = collectorId,
                OwnerName = ownerName,
                AnimalType = AnimalType.Other,
                CustomAnimalType = customAnimalType,
                NumberOfHeads = heads,
                RatePerHead = customRate,
                ORNumber = orNumber,
                TransactionDate = transactionDate,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        public static SlaughterTransaction CreateHog(
         Guid facilityId,
         Guid collectorId,
         string ownerName,
         int heads,
         string orNumber,
         DateOnly transactionDate,
         string createdBy = "System")
        {
            return new SlaughterTransaction
            {
                Id = Guid.NewGuid(),
                FacilityId = facilityId,
                CollectorId = collectorId,
                OwnerName = ownerName,
                AnimalType = AnimalType.Hog,
                NumberOfHeads = heads,
                RatePerHead = FeeRates.SlhHogTotalPerHead,
                SlaughterFee = FeeRates.SlhHogSlaughterFee,
                AntemortemFee = FeeRates.SlhHogAntemortem,
                TableCharge = FeeRates.SlhHogTableCharge,
                EntranceFee = FeeRates.SlhHogEntranceFee,
                ORNumber = orNumber,
                TransactionDate = transactionDate,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
        public static SlaughterTransaction CreateLargeAnimal(
          Guid facilityId,
          Guid collectorId,
          string ownerName,
          AnimalType animalType,
          int heads,
          string orNumber,
          DateOnly transactionDate,
          string createdBy = "System")
        {
            if (animalType == AnimalType.Hog)
                throw new ArgumentException("Use CreateHog() for hog transactions.", nameof(animalType));

            return new SlaughterTransaction
            {
                Id = Guid.NewGuid(),
                FacilityId = facilityId,
                CollectorId = collectorId,
                OwnerName = ownerName,
                AnimalType = animalType,
                NumberOfHeads = heads,
                RatePerHead = FeeRates.SlhLargeTotalPerHead,
                SlaughterFee = FeeRates.SlhLargeSlaughterFee,
                SlaughterPermit = FeeRates.SlhLargePermit,
                AntemortemFee = FeeRates.SlhLargeAntemortem,
                PostmortemFee = FeeRates.SlhLargePostmortem,
                TableCharge = FeeRates.SlhLargeTableCharge,
                LivestockFee = FeeRates.SlhLargeLivestockFee,
                ORNumber = orNumber,
                TransactionDate = transactionDate,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
    }
}
