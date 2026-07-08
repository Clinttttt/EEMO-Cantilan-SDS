namespace EEMOCantilanSDS.Infrastructure.Services
{
    /// <summary>SMTP configuration (bound from the "Email" config section / env vars).</summary>
    public class EmailOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = "StallTrack";
        public bool UseStartTls { get; set; } = true;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromEmail);
    }
}
