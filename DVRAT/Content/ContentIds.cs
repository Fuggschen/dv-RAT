namespace DVRAT
{
    public static class ContentIds
    {
        public const string Info = "info";
        public const string Stations = "stations";
        public const string Cargo = "cargo";
        public const string RollingStock = "rollingstock";

        public static string Station(string yardId) => "station:" + yardId;
        public static string CargoType(string id) => "cargo:" + id;
        public static string CarType(string id) => "car:" + id;
    }
}
