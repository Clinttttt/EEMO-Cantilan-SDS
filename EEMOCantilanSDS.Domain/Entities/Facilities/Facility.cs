using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    public class Facility : AuditableEntity
    {
        public FacilityCode Code { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public string ShortName { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public bool IsActive { get; private set; } = true;

        public ICollection<Stall> Stalls { get; private set; } = new List<Stall>();
        public ICollection<CollectorFacilityAssignment> CollectorAssignments { get; private set; } = new List<CollectorFacilityAssignment>();
        private Facility() { }
        public static Facility Create(
           FacilityCode code,
           string name,
           string shortName,
           string? description = null)
        {
            return new Facility
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                ShortName = shortName,
                Description = description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };
        }

        public void Deactivate() => IsActive = false;
        public void Activate() => IsActive = true;
    }
}
