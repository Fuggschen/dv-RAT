namespace DVRAT
{
    public enum IntegrationState
    {
        Inactive,
        Active,

        /// <summary>Loaded, but its data could not be read, e.g. after an update.</summary>
        Failed,
    }

    /// <summary>Another mod whose data the tablet shows.</summary>
    public class IntegrationStatus
    {
        public string Name { get; set; }
        public IntegrationState State { get; set; }
    }
}
