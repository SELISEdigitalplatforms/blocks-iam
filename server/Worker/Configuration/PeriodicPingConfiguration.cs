namespace Worker.Configuration
{
    public class PeriodicPingConfiguration
    {
        public bool Enabled { get; set; }
        public string PingUrl { get; set; } = string.Empty;
        public int PingIntervalSeconds { get; set; }
    }
}
