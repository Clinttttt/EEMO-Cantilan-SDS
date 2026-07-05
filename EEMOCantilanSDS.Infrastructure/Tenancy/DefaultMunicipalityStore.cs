namespace EEMOCantilanSDS.Infrastructure.Tenancy
{
    /// <summary>
    /// Process-wide holder for the default municipality id (Cantilan), populated once at startup from the
    /// seeded default municipality. Registered as a singleton. Thread-safe via a volatile read; the value
    /// is write-once (empty writes ignored), so concurrent readers always see a valid or empty id.
    /// The per-request <see cref="CurrentMunicipalityAccessor"/> falls back to this when the current
    /// request has no authenticated municipality (token-less flows, background jobs, startup).
    /// </summary>
    public sealed class DefaultMunicipalityStore
    {
        private volatile string _id = Guid.Empty.ToString();

        public Guid Default => Guid.TryParse(_id, out var g) ? g : Guid.Empty;

        public void Set(Guid id)
        {
            if (id != Guid.Empty)
                _id = id.ToString();
        }
    }
}
