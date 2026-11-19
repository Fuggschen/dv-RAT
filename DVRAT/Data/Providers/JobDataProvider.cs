using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;

namespace DVRAT
{
    /// <summary>Reads the game's jobs into a <see cref="JobData"/> snapshot.</summary>
    public class JobDataProvider : IDisposable
    {
        /// <param name="trainCar">Any car of the tablet's train. Jobs with cars in it count as coupled.</param>
        public JobData Refresh(TrainCar trainCar)
        {
            var data = new JobData();

            try
            {
                var jobsManager = JobsManager.Instance;
                if (jobsManager == null) return data;

                var currentJobs = jobsManager.currentJobs;
                if (currentJobs == null) return data;

                var trainset = trainCar?.trainset;
                var coupledCarIds = new HashSet<string>();

                if (trainset != null)
                {
                    foreach (var car in trainset.cars)
                    {
                        if (car != null)
                            coupledCarIds.Add(car.ID);
                    }
                }

                float totalJobMass = 0f;
                float totalJobLength = 0f;

                var offered = StationController.allStations
                    .SelectMany(s => s?.logicStation?.availableJobs ?? new List<Job>())
                    .Where(j => j != null).ToList();
                var known = new HashSet<string>(currentJobs.Where(j => j != null).Concat(offered).Select(j => j.ID));
                // Right after loading a save the lists can still be empty, so only prune once they are not.
                if (known.Count > 0)
                    ActiveJobStore.KeepOnly(known);

                foreach (var job in currentJobs)
                {
                    if (job == null) continue;

                    var jobInfo = BuildJobInfo(job);
                    foreach (var car in jobInfo.Cars)
                        car.IsCoupled = coupledCarIds.Contains(car.Id);
                    jobInfo.IsCoupled = jobInfo.Cars.Any(c => c.IsCoupled);
                    jobInfo.IsActive = ActiveJobStore.IsActive(jobInfo.JobId);

                    data.AcceptedJobs.Add(jobInfo);
                    if (jobInfo.IsActive)
                        data.ActiveJobs.Add(jobInfo);

                    if (jobInfo.IsCoupled)
                    {
                        data.CoupledJobs.Add(jobInfo);
                        totalJobMass += jobInfo.TotalMass;
                        totalJobLength += jobInfo.TotalLength;
                    }
                }

                foreach (var job in offered.Where(j => ActiveJobStore.IsActive(j.ID)))
                {
                    var jobInfo = BuildJobInfo(job);
                    foreach (var car in jobInfo.Cars)
                        car.IsCoupled = coupledCarIds.Contains(car.Id);
                    jobInfo.IsCoupled = jobInfo.Cars.Any(c => c.IsCoupled);
                    jobInfo.IsActive = true;
                    data.PlannedJobs.Add(jobInfo);
                }

                data.TotalJobMass = totalJobMass;
                data.TotalJobLength = totalJobLength;
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Error in JobDataProvider.Refresh: {ex.Message}");
            }

            return data;
        }

        /// <summary>Everything about a job except the flags that depend on the player's train.</summary>
        internal static JobInfo BuildJobInfo(Job job)
        {
            var info = new JobInfo();

            try
            {
                info.JobId = job.ID;
                info.JobType = job.jobType.ToString();
                info.JobTypeDisplay = GetJobTypeDisplay(job);
                info.JobTypeShort = GetJobTypeShort(job) ?? info.JobTypeDisplay;
                ReadLicenses(job, info);

                if (job.chainData != null)
                {
                    info.OriginStation = job.chainData.chainOriginYardId;
                    info.DestinationStation = job.chainData.chainDestinationYardId;
                    info.OriginDisplay = GetStationDisplayName(info.OriginStation);
                    info.DestinationDisplay = GetStationDisplayName(info.DestinationStation);
                }

                info.State = JobState.Active;

                // Only the innermost tasks list cars, and a car appears in several of them.
                var seenCars = new HashSet<Car>();
                var carInfos = new Dictionary<Car, JobCar>();
                if (job.tasks != null)
                {
                    foreach (var (task, taskData) in LeafTasks(job.tasks))
                    {
                        var taskCars = taskData.cars;
                        if (taskData.state != TaskState.Done)
                            AddLegs(info, carInfos, task, taskData);
                        CargoType cargoType = taskData.cargoTypePerCar != null && taskData.cargoTypePerCar.Count > 0
                            ? taskData.cargoTypePerCar[0]
                            : CargoType.None;
                        // Cargo amounts are units. massPerUnit gives kg.
                        float cargoTons = cargoType != CargoType.None
                            ? cargoType.ToV2().massPerUnit * taskData.totalCargoAmount / 1000f
                            : 0f;

                        foreach (var logicCar in taskCars)
                        {
                            if (logicCar == null || !seenCars.Add(logicCar)) continue;
                            info.WagonCount++;

                            if (TrainCarRegistry.Instance.logicCarToTrainCar.TryGetValue(logicCar, out var trainCar))
                            {
                                info.WagonIds.Add(trainCar.ID);

                                // Game masses are in kg.
                                if (trainCar.massController != null)
                                    info.TotalMass += trainCar.massController.TotalMass / 1000f;
                                else
                                    info.TotalMass += (trainCar.carLivery?.parentType?.mass ?? 0f) / 1000f;

                                info.TotalLength += trainCar.InterCouplerDistance;
                            }
                            else
                            {
                                // No car in the world, e.g. suspended by Persistent Jobs. The logic car still knows type, length and load.
                                info.WagonIds.Add(logicCar.ID);
                                info.TotalMass += (logicCar.carType?.parentType?.mass ?? 0f) / 1000f;
                                var loaded = logicCar.CurrentCargoTypeInCar;
                                if (loaded != CargoType.None)
                                    info.TotalMass += loaded.ToV2().massPerUnit * logicCar.LoadedCargoAmount / 1000f;
                                info.TotalLength += logicCar.length;
                            }
                        }

                        if (info.CargoType == null && cargoType != DV.ThingTypes.CargoType.None)
                        {
                            info.CargoType = cargoType.ToString();
                            info.CargoAmount = cargoTons;
                        }
                    }
                }

                // Without the bonus, which the game adds only while a taken job is in time.
                info.Payment = job.GetBasePaymentForTheJob();

                // Seconds. Untimed jobs have a limit of 0.
                if (job.TimeLimit > 0f)
                {
                    info.TimeLimit = TimeSpan.FromSeconds(job.TimeLimit);
                    info.Bonus = job.GetPotentialBonusPaymentForTheJob();
                    info.TimeLeft = TimeSpan.FromSeconds(job.TimeLimit - job.GetTimeOnJob());
                }
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"Error building job info: {ex.Message}");
            }

            return info;
        }

        /// <summary>Adds a step line for the job and a leg per car.</summary>
        private static void AddLegs(JobInfo info, Dictionary<Car, JobCar> carInfos, Task source, TaskData task)
        {
            string from = TrackName(task.startTrack);
            string to = TrackName(task.destinationTrack);
            int count = task.cars.Count;
            string cars = count == 1 ? "1 car" : $"{count} cars";

            JobLegKind kind;
            bool passengers = false;
            if (source is WarehouseTask warehouse)
            {
                // Passenger Jobs boarding tasks have their own task types. A rural stop is on a main line track
                // whose ID means nothing to the player, so use the stop's machine ID.
                passengers = source.InstanceTaskType != TaskType.Warehouse;
                if (passengers && task.type != TaskType.Warehouse && warehouse.warehouseMachine != null)
                    to = warehouse.warehouseMachine.ID;
                bool loading = warehouse.warehouseTaskType == WarehouseTaskType.Loading;
                kind = loading ? JobLegKind.Load : JobLegKind.Unload;
                info.Steps.Add($"{(loading ? "Load" : "Unload")} {cars} at {to}");
                // A passenger job has only stops, from its first to its last.
                if (passengers)
                {
                    info.StartTrackId = info.StartTrackId ?? to;
                    info.DestinationTrackId = to;
                }
            }
            else
            {
                kind = JobLegKind.Transport;
                info.Steps.Add($"{cars} {from} to {to}");
                info.StartTrackId = info.StartTrackId ?? from;
                info.DestinationTrackId = to;
            }

            foreach (var logicCar in task.cars)
            {
                if (logicCar == null) continue;
                if (!carInfos.TryGetValue(logicCar, out var car))
                {
                    TrainCarRegistry.Instance.logicCarToTrainCar.TryGetValue(logicCar, out var trainCar);
                    car = new JobCar
                    {
                        Id = trainCar != null ? trainCar.ID : logicCar.ID,
                        CarType = (trainCar != null ? trainCar.carLivery : logicCar.carType)?.id,
                        CarName = GameContent.LiveryName(trainCar != null ? trainCar.carLivery : logicCar.carType),
                        Track = TrackName(TrackNames.CurrentTrack(logicCar)),
                        Length = logicCar.length,
                    };
                    carInfos[logicCar] = car;
                    info.Cars.Add(car);
                }
                car.Legs.Add(new JobLeg { Kind = kind, FromTrack = from, ToTrack = to, Passengers = passengers });
            }
        }

        private static string TrackName(Track track) => track?.ID.FullDisplayID;

        /// <summary>The innermost tasks that list cars, in job order.</summary>
        private static IEnumerable<(Task task, TaskData data)> LeafTasks(IEnumerable<Task> tasks)
        {
            foreach (var task in tasks)
            {
                var data = task?.GetTaskData();
                if (data == null) continue;
                if (data.nestedTasks != null && data.nestedTasks.Count > 0)
                {
                    foreach (var nested in LeafTasks(data.nestedTasks))
                        yield return nested;
                }
                else if (data.cars != null && data.cars.Count > 0)
                {
                    yield return (task, data);
                }
            }
        }

        private static string GetJobTypeShort(Job job)
        {
            switch (job.jobType)
            {
                case JobType.Transport: return "FH";
                case JobType.EmptyHaul: return "LH";
                case JobType.ShuntingLoad: return "SL";
                case JobType.ShuntingUnload: return "SU";
                case PassengerExpress: return "PE";
                case PassengerRegional: return "PR";
                default: return null;
            }
        }

        /// <summary>Short license names, as the career manager abbreviates them.</summary>
        private static readonly Dictionary<JobLicenses, string> LicenseNames = new Dictionary<JobLicenses, string>
        {
            [JobLicenses.FreightHaul] = "FH",
            [JobLicenses.LogisticalHaul] = "LH",
            [JobLicenses.Shunting] = "SH",
            [JobLicenses.Hazmat1] = "HZ1",
            [JobLicenses.Hazmat2] = "HZ2",
            [JobLicenses.Hazmat3] = "HZ3",
            [JobLicenses.Military1] = "MIL1",
            [JobLicenses.Military2] = "MIL2",
            [JobLicenses.Military3] = "MIL3",
            [JobLicenses.TrainLength1] = "TL1",
            [JobLicenses.TrainLength2] = "TL2",
            [JobLicenses.Fragile] = "FR",
        };

        /// <summary>Each license with the higher tiers that need it first.</summary>
        private static readonly Dictionary<JobLicenses, JobLicenses> HigherTiers = new Dictionary<JobLicenses, JobLicenses>
        {
            [JobLicenses.Hazmat1] = JobLicenses.Hazmat2 | JobLicenses.Hazmat3,
            [JobLicenses.Hazmat2] = JobLicenses.Hazmat3,
            [JobLicenses.Military1] = JobLicenses.Military2 | JobLicenses.Military3,
            [JobLicenses.Military2] = JobLicenses.Military3,
            [JobLicenses.TrainLength1] = JobLicenses.TrainLength2,
        };

        /// <summary>Short names of mod licenses, by license ID. These are Passenger Jobs.</summary>
        private static readonly Dictionary<string, string> ModLicenseNames = new Dictionary<string, string>
        {
            ["Passengers1"] = "PAX1",
            ["Passengers2"] = "PAX2",
        };

        /// <summary>Walks the game's license list rather than the JobLicenses flags, so licenses that mods add count too.</summary>
        private static void ReadLicenses(Job job, JobInfo info)
        {
            var manager = SingletonBehaviour<LicenseManager>.Instance;
            var known = LicenseNames.Keys.ToList();
            var required = DV.Globals.G.Types.jobLicenses
                .Where(l => l != null && l.v1 != 0 && (job.requiredLicenses & l.v1) != 0)
                // Game licenses in career manager order, then mod licenses.
                .OrderBy(l => known.IndexOf(l.v1) is int i && i >= 0 ? i : known.Count)
                .ToList();
            foreach (var license in required)
            {
                // Only the highest tier of a family is listed.
                if (HigherTiers.TryGetValue(license.v1, out var higher) && (job.requiredLicenses & higher) != 0) continue;
                string name = LicenseNames.TryGetValue(license.v1, out string shortName) ? shortName
                    : ModLicenseNames.TryGetValue(license.id, out shortName) ? shortName : license.id;
                // The job type already shows these, but they can still be missing.
                if ((license.v1 & JobTypeLicenses) == 0)
                    info.Licenses.Add(name);
                if (manager != null && !manager.IsJobLicenseAcquired(license))
                    info.MissingLicenses.Add(name);
            }
        }

        private const JobLicenses JobTypeLicenses = JobLicenses.FreightHaul | JobLicenses.LogisticalHaul | JobLicenses.Shunting;

        /// <summary>Job types Passenger Jobs adds to the game's enum.</summary>
        private const JobType PassengerExpress = (JobType)101, PassengerRegional = (JobType)102;

        private static string GetJobTypeDisplay(Job job)
        {
            try
            {
                if (job.jobType == PassengerExpress) return "Express Passenger";
                if (job.jobType == PassengerRegional) return "Regional Passenger";
                string type = job.jobType.ToString();
                switch (type)
                {
                    case "Transport": return "Freight Haul";
                    case "ShuntingLoad": return "Shunting (Load)";
                    case "ShuntingUnload": return "Shunting (Unload)";
                    case "EmptyHaul": return "Empty Haul";
                    default: return type;
                }
            }
            catch
            {
                return "Job";
            }
        }

        private static string GetStationDisplayName(string yardId)
        {
            return yardId;
        }

        public void Dispose()
        {
        }
    }
}