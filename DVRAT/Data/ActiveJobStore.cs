using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DVRAT
{
    /// <summary>Job IDs the player marked as active on the tablet. Saved per savegame.</summary>
    public static class ActiveJobStore
    {
        private const string Key = "activeJobs";

        private static readonly HashSet<string> Ids = new HashSet<string>();

        public static bool IsActive(string jobId) => jobId != null && Ids.Contains(jobId);

        public static void Add(string jobId)
        {
            if (!string.IsNullOrEmpty(jobId))
                Ids.Add(jobId);
        }

        public static void Toggle(string jobId)
        {
            if (jobId == null) return;
            if (!Ids.Remove(jobId))
                Ids.Add(jobId);
        }

        /// <summary>Drops marks of jobs that are gone. Pass taken jobs and jobs still on offer, as offered jobs can be marked too.</summary>
        public static void KeepOnly(ICollection<string> currentJobIds)
        {
            Ids.RemoveWhere(id => !currentJobIds.Contains(id));
        }

        public static void WriteTo(JObject data)
        {
            data[Key] = new JArray(Ids.OrderBy(id => id));
        }

        public static void ReadFrom(JObject data)
        {
            Ids.Clear();
            if (data?[Key] is JArray ids)
                Ids.UnionWith(ids.Select(id => (string)id).Where(id => !string.IsNullOrEmpty(id)));
        }
    }
}
