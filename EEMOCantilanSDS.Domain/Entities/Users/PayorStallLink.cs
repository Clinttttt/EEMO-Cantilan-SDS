using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    /// <summary>
    /// Links a <see cref="PayorUser"/> to a <see cref="Stall"/> they may view and pay for. A payor can
    /// hold several stalls, so this is a many-to-one (payor → stalls) join created during activation.
    /// </summary>
    public class PayorStallLink : BaseEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public Guid PayorUserId { get; private set; }
        public Guid StallId { get; private set; }

        public PayorUser? Payor { get; private set; }
        public Stall? Stall { get; private set; }

        private PayorStallLink() { }

        public static PayorStallLink Create(Guid payorUserId, Guid stallId)
        {
            return new PayorStallLink
            {
                Id = Guid.NewGuid(),
                PayorUserId = payorUserId,
                StallId = stallId
            };
        }
    }
}
