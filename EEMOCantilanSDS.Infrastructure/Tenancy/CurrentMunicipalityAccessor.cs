using EEMOCantilanSDS.Application.Common.Tenancy;

namespace EEMOCantilanSDS.Infrastructure.Tenancy
{
    /// <summary>
    /// Process-wide holder for the current (default) municipality id. Registered as a singleton and
    /// populated once at startup from the seeded default municipality. Thread-safe via a volatile read;
    /// the value is write-once (empty writes ignored), so concurrent readers always see a valid or empty id.
    /// </summary>
    public sealed class CurrentMunicipalityAccessor : ICurrentMunicipalityAccessor
    {
        private volatile string _id = Guid.Empty.ToString();

        public Guid MunicipalityId => Guid.TryParse(_id, out var g) ? g : Guid.Empty;

        public void Set(Guid municipalityId)
        {
            if (municipalityId != Guid.Empty)
                _id = municipalityId.ToString();
        }
    }
}
