namespace EEMOCantilanSDS.Domain.Constants
{
    /// <summary>
    /// Identity of the operating office shown on the portal, reports and receipts.
    /// Single source today; replaced by per-LGU configuration in the CARCANMADCARLAN release.
    /// </summary>
    public static class OfficeProfile
    {
        public const string Office = "Economic Enterprise & Management Office";
        public const string Municipality = "Cantilan";
        public const string Province = "Surigao del Sur";
        public const string SystemName = "StallTrack — Revenue Collection System";
        public const string ReceiptsIssuedBy = "EEMO · Municipality of Cantilan";
    }

    /// <summary>Static application identity.</summary>
    public static class AppInfo
    {
        public const string Name = "StallTrack";
        public const string Version = "1.0.0";
    }
}
