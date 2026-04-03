using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    public class CollectorFacilityAssignment : BaseEntity
    {
        public Guid CollectorId { get; private set; }
        public Guid FacilityId { get; private set; }
        public FacilityCode FacilityCode { get; private set; }
        public DateTime AssignedAt { get; private set; }

        // Navigation
        public CollectorUser? Collector { get; private set; }
        public Facility? Facility { get; private set; }

        private CollectorFacilityAssignment() { }

        public static CollectorFacilityAssignment Create(
            Guid collectorId,
            Guid facilityId,
            FacilityCode facilityCode)
        {
            return new CollectorFacilityAssignment
            {
                Id = Guid.NewGuid(),
                CollectorId = collectorId,
                FacilityId = facilityId,
                FacilityCode = facilityCode,
                AssignedAt = DateTime.UtcNow
            };
        }
    }
}
