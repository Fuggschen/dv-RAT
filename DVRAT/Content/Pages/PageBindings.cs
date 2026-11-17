using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DVRAT
{
    /// <summary>Values page JSON binds to, e.g. <c>{train.speedKmh}</c>. The names are public API for page authors. No Unity types, so tests can check the shipped pages.</summary>
    public static class PageBindings
    {
        public static Dictionary<string, object> Build(TrainData train, RouteData route, JobData jobs, WorldData world,
            IDictionary<string, string> pageVars, Func<string, string> stationName, IDictionary<string, string> settings = null)
        {
            return new Dictionary<string, object>
            {
                ["train"] = Train(train, jobs),
                ["loco"] = Loco(train),
                ["solar"] = Solar(train),
                ["electric"] = Electric(train?.Electric),
                ["damage"] = Damage(train?.Locomotive?.Damage),
                ["integrations"] = Integrations(world),
                ["route"] = Route(route, jobs, world, stationName, TrainLimitLength(train, settings)),
                ["jobs"] = Jobs(jobs),
                ["world"] = World(world),
                ["weather"] = Weather(world),
                ["seasons"] = Seasons(world),
                ["station"] = Station(world, stationName),
                ["page"] = pageVars.ToDictionary(v => v.Key, v => (object)v.Value),
                ["settings"] = (settings ?? TabletSession.DefaultSettings.ToDictionary(e => e.Key, e => e.Value))
                    .ToDictionary(v => v.Key, v => (object)v.Value),
            };
        }

        /// <summary>See <see cref="RouteProfile.LimitUnderTrain"/>. 0 when "showTrainClear" is off.</summary>
        private static float TrainLimitLength(TrainData train, IDictionary<string, string> settings)
        {
            if (settings != null && settings.TryGetValue("showTrainClear", out string show) && show == "false") return 0f;
            return train?.TotalLength ?? 0f;
        }

        /// <summary>The root plus <c>item</c>.</summary>
        public static Dictionary<string, object> WithItem(Dictionary<string, object> root, object item)
        {
            return new Dictionary<string, object>(root) { ["item"] = item };
        }

        private static Dictionary<string, object> Train(TrainData train, JobData jobs)
        {
            bool onTrain = train != null && train.Cars.Count > 0;
            bool moving = onTrain && train.Braking != null && train.Braking.SpeedMs >= 0.1f;
            var trainJob = TrainJob(jobs);
            int otherJobs = (jobs?.CoupledJobs.Count ?? 0) - 1;
            return new Dictionary<string, object>
            {
                ["loaded"] = train != null,
                ["number"] = trainJob == null ? "--" : otherJobs > 0 ? $"{trainJob.JobId} +{otherJobs}" : trainJob.JobId,
                ["onTrain"] = onTrain,
                ["derailed"] = onTrain && train.Cars.Any(c => c.IsDerailed),
                ["moving"] = moving,
                ["speedKmh"] = train?.SpeedKmh ?? 0f,
                ["massT"] = train?.TotalMass ?? 0f,
                ["lengthM"] = train?.TotalLength ?? 0f,
                ["wagonCount"] = train?.WagonCount ?? 0,
                ["brakeDistance"] = moving ? Format.Distance(train.Braking.CurrentBrakingDistance) : "--",
                ["emergencyDistance"] = moving ? Format.Distance(train.Braking.EmergencyBrakingDistance) : "--",
                ["decelerationMs2"] = train?.Braking?.DecelerationRate ?? 0f,
                ["maxDecelerationMs2"] = train?.Braking?.MaxDecelerationRate ?? 0f,
                ["brakeEffectivenessPct"] = (train?.Braking?.BrakeEffectiveness ?? 0f) * 100f,
                ["wagons"] = (train?.Cars ?? new List<CarInfo>()).Where(c => !c.IsLocomotive).Select(Wagon).ToList<object>(),
                ["altitudeM"] = train?.AltitudeM ?? 0f,
                ["gradePercent"] = train?.PitchGradePercent ?? 0f,
                ["realisticBrakes"] = train?.RealisticBrakes ?? false,
                ["cars"] = CarRows(train?.Cars, false),
                ["groups"] = CarRows(train?.Cars, true),
            };
        }

        /// <summary>Loco end first. Grouped mode merges neighbouring cars of one job bound for the same next track, as dv-hud does. Rows have the same fields either way.</summary>
        public static List<object> CarRows(IReadOnlyList<CarInfo> cars, bool grouped)
        {
            var rows = new List<object>();
            if (cars == null) return rows;
            int start = 0;
            for (int i = 1; i <= cars.Count; i++)
            {
                bool sameGroup = grouped && i < cars.Count && cars[i].NextDestination != null
                    && cars[i].NextDestination == cars[i - 1].NextDestination && cars[i].JobId == cars[i - 1].JobId;
                if (sameGroup) continue;
                rows.Add(CarRow(cars.Skip(start).Take(i - start).ToList(), start));
                start = i;
            }
            return rows;
        }

        private static object CarRow(List<CarInfo> cars, int start)
        {
            var first = cars[0];
            int count = cars.Count;
            bool oneType = cars.All(c => c.CarType == first.CarType);
            string name = first.CarName ?? first.CarType ?? "";
            name = oneType ? (count > 1 ? $"{count}x {name}" : name) : $"{count} cars";
            var cargos = cars.Select(c => string.IsNullOrEmpty(c.LoadedCargo) ? "empty" : c.LoadedCargo).Distinct().ToList();
            var couplers = cars.Where(c => c.CouplerForceKn.HasValue).Select(c => c.CouplerForceKn.Value).ToList();
            int handbrakes = cars.Count(c => c.HandbrakeApplied);
            return new Dictionary<string, object>
            {
                ["pos"] = count > 1 ? $"{start + 1}-{start + count}" : $"{start + 1}",
                ["count"] = count,
                ["id"] = first.CarId ?? "",
                ["type"] = oneType ? first.CarType ?? "" : "",
                ["name"] = name,
                ["isLoco"] = cars.Any(c => c.IsLocomotive),
                ["massT"] = cars.Sum(c => c.Mass),
                ["cargo"] = cargos.Count == 1 ? cargos[0] : "mixed",
                ["state"] = cars.Any(c => c.IsDerailed) ? "DERAILED" : cars.Any(c => c.DamagePercent > 50f) ? "damaged" : "",
                ["hasJob"] = first.JobId != null,
                ["job"] = first.JobId ?? "",
                ["jobTaken"] = first.JobTaken,
                ["destination"] = first.NextDestination ?? "",
                ["atDestination"] = first.NextDestination != null && cars.All(c => c.AtDestination),
                ["derailPct"] = cars.Max(c => c.DerailPercent),
                ["hasCoupler"] = couplers.Count > 0,
                ["couplerKn"] = couplers.Count > 0 ? couplers.Max() : 0f,
                ["pipeBar"] = cars.Min(c => c.BrakePipePressure),
                ["reservoirBar"] = cars.Min(c => c.ReservoirPressure),
                ["cylinderBar"] = cars.Max(c => c.BrakeCylinderPressure),
                ["controlBar"] = cars.Min(c => c.ControlReservoirPressure),
                ["valve"] = Same(cars.Select(c => c.BrakeValve ?? "")),
                ["brakePct"] = cars.Max(c => c.BrakingFactor) * 100f,
                ["brakeTempC"] = cars.Max(c => c.BrakeTemperature),
                ["handbrakes"] = handbrakes,
                ["handbrake"] = handbrakes == 0 ? "" : count > 1 ? $"{handbrakes} on" : "on",
            };
        }

        private static object Wagon(CarInfo car) => new Dictionary<string, object>
        {
            ["id"] = car.CarId ?? "",
            ["type"] = car.CarType ?? "",
            ["name"] = car.CarName ?? car.CarType ?? "",
            ["massT"] = car.Mass,
            ["lengthM"] = car.Length,
            ["cargo"] = string.IsNullOrEmpty(car.LoadedCargo) ? "empty" : car.LoadedCargo,
            ["cargoMassT"] = car.CargoAmount,
            ["derailed"] = car.IsDerailed,
            ["damaged"] = car.DamagePercent > 50f,
            ["state"] = car.IsDerailed ? "DERAILED" : car.DamagePercent > 50f ? "damaged" : "",
        };

        /// <summary>"mixed" when the values differ.</summary>
        private static string Same(IEnumerable<string> values)
        {
            var distinct = values.Distinct().ToList();
            return distinct.Count == 1 ? distinct[0] : "mixed";
        }

        private static Dictionary<string, object> Integrations(WorldData world)
        {
            var all = world?.Integrations ?? new List<IntegrationStatus>();
            return new Dictionary<string, object>
            {
                ["anyFailed"] = all.Any(i => i.State == IntegrationState.Failed),
                ["list"] = all.Select(i => (object)new Dictionary<string, object>
                {
                    ["name"] = i.Name ?? "",
                    ["active"] = i.State == IntegrationState.Active,
                    ["failed"] = i.State == IntegrationState.Failed,
                    ["state"] = i.State.ToString(),
                }).ToList(),
            };
        }

        private static Dictionary<string, object> Electric(ElectricInfo electric)
        {
            var pantographs = electric?.Pantographs ?? new List<PantographState>();
            var raised = pantographs.Where(p => p.Raised > 0.95f).ToList();
            var battery = electric?.Battery;
            return new Dictionary<string, object>
            {
                ["present"] = electric != null,
                ["source"] = electric?.Source ?? "",
                ["car"] = electric?.CarId ?? "",
                ["voltageV"] = electric?.LineVoltage ?? 0f,
                ["voltage"] = $"{electric?.LineVoltage ?? 0f:F0} V",
                ["hasNominal"] = electric?.NominalVoltage != null,
                ["nominal"] = electric?.NominalVoltage is float nominal ? $"{nominal:F0} V" : "",
                ["currentA"] = electric?.LineCurrent ?? 0f,
                ["current"] = $"{electric?.LineCurrent ?? 0f:F0} A",
                ["powerKw"] = electric?.PowerKw ?? 0f,
                ["power"] = Format.Power((electric?.PowerKw ?? 0f) * 1000f),
                // Below a few amps it is auxiliaries or noise.
                ["regenerating"] = (electric?.LineCurrent ?? 0f) < -5f,
                ["pantographsReported"] = electric?.PantographsReported ?? false,
                ["raisedCount"] = raised.Count,
                // Raised, but no head touches a wire.
                ["offWire"] = raised.Count > 0 && !raised.Any(p => p.InContact),
                ["pantographs"] = pantographs.Select(p => (object)new Dictionary<string, object>
                {
                    ["name"] = p.Name ?? "",
                    ["raisedPct"] = p.Raised * 100f,
                    ["raised"] = Format.Percent(p.Raised),
                    ["contact"] = p.InContact,
                    ["wire"] = p.InContact ? $"{p.WireVoltage:F0} V" : "no contact",
                }).ToList(),
                ["hasMotorTemperature"] = electric?.MotorTemperature != null,
                ["motorTemperatureC"] = electric?.MotorTemperature ?? 0f,
                ["motorTemperature"] = Format.Temperature(electric?.MotorTemperature ?? 0f),
                ["hasWorkingMotors"] = electric?.WorkingMotors != null,
                ["workingMotors"] = electric?.WorkingMotors ?? 0,
                ["hasEnergy"] = electric?.EnergyUnits != null,
                ["energyKwh"] = electric?.EnergyKwh ?? 0f,
                ["energy"] = electric?.EnergyKwh is float kwh ? Format.Energy(kwh * 1000f) : "--",
                ["hasBattery"] = battery != null,
                ["onBattery"] = battery?.Active ?? false,
                ["batteryWanted"] = battery?.Wanted ?? false,
                ["batteryVoltageV"] = battery?.Voltage ?? 0f,
                ["batteryVoltage"] = $"{battery?.Voltage ?? 0f:F0} V",
                ["batteryCurrentA"] = battery?.Current ?? 0f,
                ["batteryCurrent"] = $"{battery?.Current ?? 0f:F0} A",
                ["batteryPowerKw"] = battery?.PowerKw ?? 0f,
                ["batteryPower"] = Format.Power((battery?.PowerKw ?? 0f) * 1000f),
                ["batteryLevelPct"] = (battery?.Charge ?? 0f) * 100f,
                ["batteryLevel"] = Format.Percent(battery?.Charge ?? 0f),
            };
        }

        private static Dictionary<string, object> Damage(DamageInfo damage)
        {
            var fuses = damage?.Fuses ?? new List<FuseState>();
            return new Dictionary<string, object>
            {
                ["present"] = damage != null,
                ["parts"] = (damage?.Parts ?? new List<DamagePart>()).Select(p => (object)new Dictionary<string, object>
                {
                    ["name"] = p.Name ?? "",
                    ["healthPct"] = p.Health * 100f,
                    ["health"] = Format.Percent(p.Health),
                }).ToList(),
                ["blownCount"] = fuses.Count(f => !f.On),
                ["fuses"] = fuses.Select(f => (object)new Dictionary<string, object>
                {
                    ["id"] = f.Id ?? "",
                    ["on"] = f.On,
                    ["state"] = f.On ? "on" : "off",
                }).ToList(),
                ["hasMotors"] = damage?.WorkingMotors != null,
                ["workingMotors"] = damage?.WorkingMotors ?? 0,
                ["totalMotors"] = damage?.TotalMotors ?? 0,
                ["motors"] = damage?.WorkingMotors is int working ? $"{working} of {damage.TotalMotors}" : "",
                ["motorsLost"] = damage?.WorkingMotors < damage?.TotalMotors,
            };
        }

        private static Dictionary<string, object> Solar(TrainData train)
        {
            // No cable links cars, so each is its own network and there is no train total.
            var cars = train?.Power ?? new List<CarPower>();
            return new Dictionary<string, object>
            {
                ["installed"] = train?.SolarGadgets ?? false,
                ["present"] = cars.Count > 0,
                ["cars"] = cars.Select(c => (object)new Dictionary<string, object>
                {
                    ["id"] = c.CarId ?? "",
                    ["hasBattery"] = c.Charge.HasValue,
                    ["chargePct"] = c.Charge ?? 0f,
                    ["charge"] = c.Charge.HasValue ? $"{c.Charge.Value:F0} %" : "--",
                    ["capacityWh"] = c.Capacity,
                    ["capacity"] = Format.Energy(c.Capacity),
                    ["supplyW"] = c.Supply,
                    ["supply"] = Format.Power(c.Supply),
                    ["demandW"] = c.Demand,
                    ["demand"] = Format.Power(c.Demand),
                    ["ratePctH"] = c.RatePctPerHour,
                    ["rate"] = PowerRate(c),
                }).ToList(),
                ["batteries"] = cars.SelectMany(c => c.Batteries.Select(b => (object)new Dictionary<string, object>
                {
                    ["car"] = c.CarId ?? "",
                    ["name"] = b.Name ?? "",
                    ["soldered"] = b.Soldered,
                    ["chargePct"] = b.Charge,
                    ["charge"] = b.Soldered ? $"{b.Charge:F0} %" : "Not soldered",
                    ["capacityWh"] = b.Capacity,
                    ["capacity"] = Format.Energy(b.Capacity),
                })).ToList(),
                ["consumers"] = cars.SelectMany(c => c.Consumers.Select(k => (object)new Dictionary<string, object>
                {
                    ["car"] = c.CarId ?? "",
                    ["name"] = k.Name ?? "",
                    ["drawW"] = k.Draw,
                    ["draw"] = Format.Power(k.Draw),
                })).ToList(),
            };
        }

        /// <summary>E.g. "-4.2 %/h, empty in 11 h 5 min", in game hours. "Not charging" means a surplus no battery can take.</summary>
        internal static string PowerRate(CarPower power)
        {
            if (!power.Charge.HasValue) return "No battery";
            float? hours = power.HoursLeft;
            if (!hours.HasValue)
            {
                if (power.Supply > power.Demand) return power.Charge.Value >= 99.9f ? "Full" : "Not charging";
                if (power.Supply < power.Demand) return power.Stored <= 0.001f ? "Empty" : "Not draining";
                return "Steady";
            }
            string time = Format.Duration(TimeSpan.FromHours(hours.Value));
            return power.Net > 0f
                ? $"+{power.RatePctPerHour.ToString("F1", CultureInfo.InvariantCulture)} %/h, full in {time}"
                : $"{power.RatePctPerHour.ToString("F1", CultureInfo.InvariantCulture)} %/h, empty in {time}";
        }

        private static Dictionary<string, object> Loco(TrainData train)
        {
            var loco = train?.Locomotive;
            return new Dictionary<string, object>
            {
                ["present"] = loco != null,
                ["type"] = loco?.CarType ?? "",
                ["name"] = loco?.CarName ?? loco?.CarType ?? "",
                ["hasEngine"] = loco?.HasEngine ?? false,
                ["engineOn"] = loco?.EngineRunning ?? false,
                ["engine"] = loco == null || !loco.HasEngine ? "" : loco.EngineRunning ? "running" : "stopped",
                ["rpm"] = loco?.EngineRPM ?? 0f,
                ["hasFee"] = loco?.Fee != null,
                ["feeUsd"] = loco?.Fee ?? 0f,
                ["fee"] = "$" + (loco?.Fee ?? 0f).ToString("N0", CultureInfo.InvariantCulture),
                ["feeIsService"] = loco?.FeeIsService ?? false,
                ["hasTmTemperature"] = loco?.TmTemperature != null,
                ["tmTemperatureC"] = loco?.TmTemperature ?? 0f,
                ["tmTemperature"] = Format.Temperature(loco?.TmTemperature ?? 0f),
                // Game default for traction motors.
                ["tmOverheatC"] = loco?.TmOverheatTemperature ?? 120f,
                ["hasTemperature"] = loco?.HasTemperature ?? false,
                ["temperatureC"] = loco?.Temperature ?? 0f,
                ["hasBoiler"] = loco?.HasBoiler ?? false,
                ["boilerBar"] = loco?.BoilerPressure ?? 0f,
                ["boilerWaterPct"] = (loco?.BoilerWaterLevel ?? 0f) * 100f,
                ["hasFirebox"] = loco?.HasFirebox ?? false,
                ["fireOn"] = loco?.FireOn ?? false,
                ["fireboxC"] = loco?.FireboxTemperature ?? 0f,
                ["throttle"] = loco == null ? "" : Format.Percent(loco.Throttle),
                ["reverser"] = loco == null ? "" : loco.Reverser > 0.1f ? "forward" : loco.Reverser < -0.1f ? "reverse" : "neutral",
                ["trainBrake"] = loco == null ? "" : Format.Percent(loco.TrainBrakePosition),
                ["independentBrake"] = loco == null ? "" : Format.Percent(loco.IndependentBrakePosition),
                ["handbrake"] = loco == null ? "" : loco.HandbrakePosition > 0.1f ? "applied" : "released",
                ["hasTraction"] = loco?.HasTraction ?? false,
                ["tractiveEffortKn"] = loco?.TractiveEffortKn ?? 0f,
                ["powerKw"] = loco?.PowerKw ?? 0f,
                ["adhesionKn"] = loco?.AdhesionLimitKn ?? 0f,
                ["slipPct"] = (loco?.Wheelslip ?? 0f) * 100f,
                ["slidePct"] = (loco?.WheelSlide ?? 0f) * 100f,
                ["hasCutoff"] = loco?.HasCutoff ?? false,
                ["cutoffPct"] = (loco?.Cutoff ?? 0f) * 100f,
                ["cutoff"] = loco != null && loco.HasCutoff ? $"{loco.Cutoff * 100f:F0} %" : "--",
                ["brakePipeBar"] = loco?.BrakePipePressure ?? 0f,
                ["mainReservoirBar"] = loco?.MainReservoirPressure ?? 0f,
                ["brakeCylinderBar"] = loco?.BrakeCylinderPressure ?? 0f,
                ["hasDriverValve"] = loco?.DriverValve != null,
                ["driverValve"] = loco?.DriverValve ?? "",
                ["equalizing"] = loco?.EqualizingReservoirPressure is float er ? $"{er:F2} bar" : "--",
                ["fuel"] = Resource(loco, "fuel"),
                ["oil"] = Resource(loco, "oil"),
                ["sand"] = Resource(loco, "sand"),
                ["water"] = Resource(loco, "water"),
                ["coal"] = Resource(loco, "coal"),
                ["charge"] = Resource(loco, "charge"),
                ["ports"] = (loco?.Ports ?? new List<SimPort>()).Select(p => (object)new Dictionary<string, object>
                {
                    ["id"] = p.Id,
                    ["type"] = p.Type,
                    ["value"] = p.Value,
                }).ToList(),
                ["port"] = PortTree(loco?.Ports),
            };
        }

        /// <summary>Every resource name exists, so pages can ask <c>loco.coal.has</c> on any loco.</summary>
        private static object Resource(LocoInfo loco, string name)
        {
            ResourceLevel level = null;
            loco?.Resources.TryGetValue(name, out level);
            float perMinute = level?.PerMinute ?? 0f;
            bool draining = perMinute > 0f;
            return new Dictionary<string, object>
            {
                ["has"] = level != null,
                ["pct"] = (level?.Normalized ?? 0f) * 100f,
                ["amount"] = level?.Amount ?? 0f,
                ["capacity"] = level?.Capacity ?? 0f,
                ["draining"] = draining,
                ["perMin"] = perMinute,
                ["pctPerMin"] = level != null && level.Capacity > 0f ? perMinute / level.Capacity * 100f : 0f,
                ["timeLeft"] = draining ? Format.Duration(level.Amount / perMinute * 60f) : "--",
            };
        }

        /// <summary>Splits port IDs at dots: <c>loco.port.engine.RPM</c> reads port "engine.RPM".</summary>
        private static Dictionary<string, object> PortTree(List<SimPort> ports)
        {
            var tree = new Dictionary<string, object>();
            foreach (var port in ports ?? new List<SimPort>())
            {
                var parts = port.Id.Split('.');
                var node = tree;
                for (int i = 0; i < parts.Length - 1 && node != null; i++)
                {
                    if (!node.TryGetValue(parts[i], out object child))
                        node[parts[i]] = child = new Dictionary<string, object>();
                    node = child as Dictionary<string, object>;
                }
                // On a name clash with another port's ID prefix, the first wins.
                if (node != null && !node.ContainsKey(parts[parts.Length - 1]))
                    node[parts[parts.Length - 1]] = port.Value;
            }
            return tree;
        }

        /// <summary>The job the train runs as: a coupled job, active ones first. Null when no job's cars are coupled.</summary>
        public static JobInfo TrainJob(JobData jobs) =>
            jobs?.CoupledJobs.OrderByDescending(j => j.IsActive).FirstOrDefault();

        /// <summary>Game time of arrival when driving at the limits. Null without a game clock.</summary>
        public static DateTime? Arrival(RouteData route, WorldData world, float distance)
        {
            if (world?.GameTime == null || route == null) return null;
            double seconds = RouteProfile.TravelSeconds(route.SpeedSections, distance) * world.TimeScale;
            return world.GameTime.Value.AddSeconds(seconds);
        }

        /// <summary>Seconds late at the job's destination, negative when early. Null without a time limit or when the destination is outside the scanned route.</summary>
        public static double? Delay(RouteData route, JobInfo job)
        {
            if (route == null || job?.TimeLeft == null || string.IsNullOrEmpty(job.DestinationStation)) return null;
            var arrival = route.UpcomingEvents.FirstOrDefault(e =>
                e.EventType == TrackEventType.Station && e.StationId == job.DestinationStation);
            if (arrival == null) return null;
            return RouteProfile.TravelSeconds(route.SpeedSections, arrival.DistanceMeters) - job.TimeLeft.Value.TotalSeconds;
        }

        private static Dictionary<string, object> Route(RouteData route, JobData jobs, WorldData world, Func<string, string> stationName,
            float trainLength)
        {
            bool onTrack = route != null && !string.IsNullOrEmpty(route.CurrentTrackId);
            float trackLimit = onTrack ? route.SpeedLimitKmh : 0f;
            // A raised limit applies only once the whole train is past.
            float underTrain = onTrack && trainLength > 0f
                ? RouteProfile.LimitUnderTrain(route.SpeedSectionsBehind.Concat(route.SpeedSections), 0f, trainLength) : 0f;
            var damage = onTrack ? SpeedRestrictions.AtCar(route.Restrictions) : null;
            // On closed track the limit is 0, which sections cannot tell from unknown.
            bool closed = damage?.LimitKmh == 0;
            float limit = closed ? 0f : underTrain > 0f ? underTrain : trackLimit;
            var trainJob = TrainJob(jobs);
            double? delay = onTrack ? Delay(route, trainJob) : null;
            string next = onTrack ? route.NextStationName ?? "" : "";
            return new Dictionary<string, object>
            {
                ["loaded"] = route != null,
                ["onTrack"] = onTrack,
                ["track"] = onTrack ? route.CurrentTrackName : "",
                ["direction"] = onTrack ? route.Direction : "",
                ["limitKmh"] = limit,
                // Yard tracks and tracks under 100 m have no signs, so the limit is unknown.
                ["limit"] = closed ? "0 km/h" : limit > 0 ? $"{limit:F0} km/h" : "--",
                ["trackLimitKmh"] = trackLimit,
                ["trackLimit"] = closed ? "0 km/h" : trackLimit > 0 ? $"{trackLimit:F0} km/h" : "--",
                ["trackDamaged"] = damage != null,
                ["trackClosed"] = closed,
                ["trackDamagePct"] = damage?.DamagePercent ?? 0,
                ["grade"] = onTrack ? Format.Grade(route.CurrentGradePercent) : "",
                ["hasPlan"] = !string.IsNullOrEmpty(route?.PlannedDestination),
                ["destination"] = string.IsNullOrEmpty(route?.PlannedDestination) ? "--" : stationName(route.PlannedDestination),
                ["hasNextStation"] = next != "",
                ["nextStationId"] = next,
                ["nextStation"] = next != "" ? stationName(next) : "",
                ["nextStationDistance"] = next != "" ? Format.Distance(route.DistanceToNextStation) : "",
                ["next"] = next != "" ? $"{stationName(next)} {Format.Distance(route.DistanceToNextStation)}" : "--",
                ["events"] = onTrack
                    ? route.UpcomingEvents.Take(40).Select(e => Event(e, route, world, stationName)).ToList<object>()
                    : new List<object>(),
                ["scannedM"] = onTrack ? route.ScannedMeters : 0f,
                ["hasDelay"] = delay.HasValue,
                ["delayS"] = delay ?? 0d,
                ["late"] = delay > 0,
                ["delay"] = delay.HasValue ? DelayText(delay.Value) : "--",
                ["speedSections"] = Sections(onTrack ? route.SpeedSections : null, "limitKmh"),
                ["gradeSections"] = Sections(onTrack ? route.GradeSections : null, "percent"),
            };
        }

        private static List<object> Sections(List<RouteSection> sections, string valueName) =>
            (sections ?? new List<RouteSection>()).Select(s => (object)new Dictionary<string, object>
            {
                ["start"] = Format.Distance(s.Start),
                ["end"] = Format.Distance(s.End),
                ["startM"] = s.Start,
                ["endM"] = s.End,
                ["lengthM"] = s.Length,
                [valueName] = s.Value,
            }).ToList();

        /// <summary>E.g. "+3 min" late or "-2 min" early, rounded to whole minutes like the real device.</summary>
        public static string DelayText(double seconds)
        {
            int minutes = (int)Math.Round(seconds / 60);
            return minutes > 0 ? $"+{minutes} min" : minutes < 0 ? $"{minutes} min" : "on time";
        }

        private static object Event(TrackEvent ev, RouteData route, WorldData world, Func<string, string> stationName) => new Dictionary<string, object>
        {
            ["arrival"] = ev.EventType == TrackEventType.Station
                ? Arrival(route, world, ev.DistanceMeters)?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "" : "",
            ["track"] = ev.Track ?? "",
            ["aspect"] = ev.Aspect ?? "",
            ["restrictive"] = ev.Restrictive,
            ["distance"] = Format.Distance(ev.DistanceMeters),
            ["distanceM"] = ev.DistanceMeters,
            ["kind"] = EventKind(ev.EventType),
            ["name"] = ev.EventType == TrackEventType.Station ? stationName(ev.StationId) : ev.Name ?? "",
            ["stationId"] = ev.StationId ?? "",
            ["warning"] = ev.EventType == TrackEventType.EndOfTrack || ev.EventType == TrackEventType.TrackDamage,
            ["damagePct"] = ev.Switch?.DamagePercent ?? 0,
            ["locked"] = ev.Switch?.Locked ?? false,
            ["trailing"] = ev.Trailing,
        };

        private static string EventKind(TrackEventType type)
        {
            switch (type)
            {
                case TrackEventType.Station: return "Station";
                case TrackEventType.Junction: return "Switch";
                case TrackEventType.Track: return "Track";
                case TrackEventType.SpeedLimitChange: return "Speed";
                case TrackEventType.GradeChange: return "Grade";
                case TrackEventType.EndOfTrack: return "End";
                case TrackEventType.NeutralBegin: return "Neutral";
                case TrackEventType.NeutralEnd: return "NeutralEnd";
                case TrackEventType.WireBegin: return "WireBegin";
                case TrackEventType.WireEnd: return "WireEnd";
                case TrackEventType.TrackDamage: return "Damage";
                case TrackEventType.TrackDamageEnd: return "DamageEnd";
                default: return type.ToString();
            }
        }

        private static Dictionary<string, object> Jobs(JobData jobs)
        {
            return new Dictionary<string, object>
            {
                ["loaded"] = jobs != null,
                ["acceptedCount"] = jobs?.AcceptedJobs.Count ?? 0,
                ["activeCount"] = jobs?.ActiveJobs.Count ?? 0,
                ["coupledCount"] = jobs?.CoupledJobs.Count ?? 0,
                ["cargoT"] = jobs?.TotalJobMass ?? 0f,
                ["accepted"] = (jobs?.AcceptedJobs ?? new List<JobInfo>()).Select(Job).ToList<object>(),
                ["active"] = (jobs?.ActiveJobs ?? new List<JobInfo>()).Select(Job).ToList<object>(),
                ["coupled"] = (jobs?.CoupledJobs ?? new List<JobInfo>()).Select(Job).ToList<object>(),
                ["cars"] = (jobs?.AcceptedJobs ?? new List<JobInfo>())
                    .SelectMany(job => job.Cars.Select(car => JobCar(job, car))).ToList<object>(),
            };
        }

        private static Dictionary<string, object> Weather(WorldData world)
        {
            var slots = world?.Forecast ?? new List<ForecastSlot>();
            int now = ForecastSlot.IndexAt(slots, world?.GameTime);
            return new Dictionary<string, object>
            {
                ["available"] = slots.Count > 0,
                ["date"] = slots.Count > 0 ? slots[0].Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                ["icon"] = now >= 0 ? slots[now].Icon : "",
                ["now"] = now >= 0 ? ForecastSlot.Describe(slots[now].Icon) : "",
                ["forecast"] = slots.Select((s, i) => (object)new Dictionary<string, object>
                {
                    ["from"] = s.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                    ["until"] = s.End.ToString("HH:mm", CultureInfo.InvariantCulture),
                    ["icon"] = s.Icon ?? "",
                    ["text"] = ForecastSlot.Describe(s.Icon),
                    ["now"] = i == now,
                    ["past"] = now >= 0 && i < now,
                    ["cloudPct"] = s.CloudPct,
                    ["rainPct"] = s.RainPct,
                    ["fogPct"] = s.FogPct,
                    ["hasTemp"] = s.TemperatureC.HasValue,
                    ["temp"] = s.TemperatureC.HasValue ? Format.Temperature(s.TemperatureC.Value) : "",
                }).ToList(),
            };
        }

        private static Dictionary<string, object> Seasons(WorldData world)
        {
            var seasons = world?.Seasons;
            return new Dictionary<string, object>
            {
                ["installed"] = world?.SeasonsInstalled ?? false,
                ["ready"] = seasons != null,
                ["season"] = seasons?.Season ?? "",
                ["next"] = seasons?.NextSeason ?? "",
                ["changing"] = seasons != null && seasons.Transition > 0f,
                ["transitionPct"] = (seasons?.Transition ?? 0f) * 100f,
                ["snowPct"] = (seasons?.Snow ?? 0f) * 100f,
                ["tempC"] = seasons?.TemperatureC ?? 0f,
                ["temp"] = seasons != null ? Format.Temperature(seasons.TemperatureC) : "",
                ["blizzard"] = seasons?.Blizzard ?? "",
            };
        }

        private static Dictionary<string, object> World(WorldData world)
        {
            var time = world?.GameTime;
            return new Dictionary<string, object>
            {
                ["loaded"] = world != null,
                ["hasTime"] = time.HasValue,
                ["time"] = time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "",
                ["date"] = time?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                ["hour"] = time.HasValue ? time.Value.Hour + time.Value.Minute / 60f : 0f,
                ["day"] = world?.IsDay ?? false,
                ["raining"] = (world?.Rain ?? 0f) > 0f,
                ["rainPct"] = (world?.Rain ?? 0f) * 100f,
                ["wetnessPct"] = (world?.Wetness ?? 0f) * 100f,
                ["realTime"] = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
                ["money"] = world?.Money ?? 0d,
                ["feesTotal"] = (world?.Fees ?? new List<Fee>()).Sum(f => f.Price),
                ["fees"] = (world?.Fees ?? new List<Fee>()).Select(f => (object)new Dictionary<string, object>
                {
                    ["id"] = f.Id ?? "",
                    ["kind"] = f.Kind ?? "",
                    ["price"] = f.Price,
                    ["payable"] = f.Payable,
                }).ToList(),
                ["owned"] = (world?.OwnedCars ?? new List<OwnedCar>()).Select(c => (object)new Dictionary<string, object>
                {
                    ["id"] = c.Id ?? "",
                    ["type"] = c.Type ?? "",
                    ["name"] = c.Name ?? c.Type ?? "",
                    ["servicePrice"] = c.ServicePrice,
                    ["destroyed"] = c.Destroyed,
                    ["track"] = c.Track ?? "",
                    ["distance"] = c.Destroyed ? "" : Format.Distance(c.Distance),
                    ["distanceM"] = c.Distance,
                }).ToList(),
                ["licenses"] = (world?.Licenses ?? new List<License>()).Select(l => (object)new Dictionary<string, object>
                {
                    ["name"] = l.Name ?? "",
                    ["kind"] = l.Kind ?? "",
                }).ToList(),
            };
        }

        private static Dictionary<string, object> Station(WorldData world, Func<string, string> stationName)
        {
            string id = world?.NearestStationId ?? "";
            return new Dictionary<string, object>
            {
                ["known"] = id != "",
                ["id"] = id,
                ["name"] = id != "" ? stationName(id) : "",
                ["distance"] = id != "" ? Format.Distance(world.NearestStationDistance) : "",
                ["distanceM"] = world?.NearestStationDistance ?? 0f,
                ["here"] = world?.AtStation ?? false,
                ["picked"] = world?.StationPicked ?? false,
                ["jobsKnown"] = world?.JobsKnown ?? false,
                ["all"] = (world?.Stations ?? new List<StationEntry>())
                    .Select(s => new { s.Id, s.Distance, Name = stationName(s.Id) })
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(s => (object)new Dictionary<string, object>
                    {
                        ["id"] = s.Id,
                        ["name"] = s.Name,
                        ["distance"] = Format.Distance(s.Distance),
                        ["distanceM"] = s.Distance,
                    }).ToList(),
                ["jobs"] = (world?.StationJobs ?? new List<JobInfo>()).Select(Job).ToList<object>(),
                ["jobTypes"] = JobTypes(world?.StationJobs),
                ["destinations"] = Destinations(world?.StationJobs, stationName),
                ["tracks"] = (world?.StationTracks ?? new List<TrackSpace>()).Select(t => (object)new Dictionary<string, object>
                {
                    ["id"] = t.Id ?? "",
                    ["lengthM"] = t.Length,
                    ["freeM"] = t.Free,
                    ["usedPct"] = t.Length > 0f ? (1f - t.Free / t.Length) * 100f : 0f,
                }).ToList(),
                ["switchesKnown"] = world?.SwitchesKnown ?? false,
                ["damagedSwitches"] = (world?.StationSwitches ?? new List<SwitchState>()).Count(s => s.DamagePercent > 0),
                ["switches"] = (world?.StationSwitches ?? new List<SwitchState>()).Select(s => (object)new Dictionary<string, object>
                {
                    ["id"] = s.Id ?? "",
                    ["damagePct"] = s.DamagePercent,
                    ["damaged"] = s.DamagePercent > 0,
                    ["repairing"] = s.Repairing,
                    ["locked"] = s.Locked,
                    ["state"] = s.Repairing ? "under repair" : s.Locked ? "locked" : "",
                }).ToList(),
                ["trackDamageKnown"] = world?.TrackDamageKnown ?? false,
                ["trackDamage"] = (world?.StationTrackDamage ?? new List<DamagedSection>()).Select(d => (object)new Dictionary<string, object>
                {
                    ["track"] = d.Track ?? "",
                    ["fromM"] = d.From,
                    ["toM"] = d.To,
                    ["range"] = $"{d.From:F0}-{d.To:F0} m",
                    ["damagePct"] = d.DamagePercent,
                    ["hasLimit"] = d.LimitKmh.HasValue,
                    ["limitKmh"] = d.LimitKmh ?? 0,
                    ["closed"] = d.LimitKmh == 0,
                    ["limit"] = d.LimitKmh == null ? "--" : d.LimitKmh == 0 ? "closed" : $"{d.LimitKmh} km/h",
                }).ToList(),
            };
        }

        private static List<object> JobTypes(List<JobInfo> jobs) =>
            (jobs ?? new List<JobInfo>())
                .GroupBy(j => j.JobTypeShort ?? "")
                .Where(g => g.Key != "")
                .OrderBy(g => g.Key)
                .Select(g => (object)new Dictionary<string, object> { ["id"] = g.Key, ["name"] = g.First().JobTypeDisplay ?? g.Key })
                .ToList();

        private static List<object> Destinations(List<JobInfo> jobs, Func<string, string> stationName) =>
            (jobs ?? new List<JobInfo>())
                .Select(j => j.DestinationStation)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .OrderBy(id => id)
                .Select(id => (object)new Dictionary<string, object> { ["id"] = id, ["name"] = stationName(id) })
                .ToList();

        private static object JobCar(JobInfo job, JobCar car)
        {
            var next = car.Legs.FirstOrDefault();
            return new Dictionary<string, object>
            {
                ["jobId"] = job.JobId ?? "",
                ["jobActive"] = job.IsActive,
                ["id"] = car.Id ?? "",
                ["type"] = car.CarType ?? "",
                ["name"] = car.CarName ?? car.CarType ?? "",
                ["track"] = car.Track ?? "",
                ["lengthM"] = car.Length,
                ["coupled"] = car.IsCoupled,
                ["action"] = next == null ? "done" : next.Kind == JobLegKind.Transport ? "move" : next.Kind == JobLegKind.Load ? "load" : "unload",
                ["from"] = next?.FromTrack ?? "",
                ["to"] = next?.ToTrack ?? "",
                ["legs"] = string.Join("\n", car.Legs.Select(Leg)),
            };
        }

        private static string Leg(JobLeg leg)
        {
            switch (leg.Kind)
            {
                case JobLegKind.Load: return $"load at {leg.ToTrack}";
                case JobLegKind.Unload: return $"unload at {leg.ToTrack}";
                default: return $"{leg.FromTrack} to {leg.ToTrack}";
            }
        }

        private static string TimeLeft(TimeSpan left) =>
            left < TimeSpan.Zero ? "-" + Format.Duration(left.Negate()) : Format.Duration(left);

        private static object Job(JobInfo job) => new Dictionary<string, object>
        {
            ["id"] = job.JobId ?? "",
            ["type"] = job.JobTypeDisplay ?? "",
            ["typeShort"] = job.JobTypeShort ?? "",
            ["licenses"] = string.Join(" ", job.Licenses),
            ["licenseIds"] = job.Licenses.ToList<object>(),
            ["missingLicenses"] = job.MissingLicenses.ToList<object>(),
            ["from"] = job.OriginDisplay ?? "",
            ["to"] = job.DestinationDisplay ?? "",
            ["fromId"] = job.OriginStation ?? "",
            ["toId"] = job.DestinationStation ?? "",
            ["cargo"] = job.CargoType ?? "",
            ["cargoT"] = job.CargoAmount,
            ["wagons"] = job.WagonCount,
            ["massT"] = job.TotalMass,
            ["pay"] = job.Payment,
            ["timeLimit"] = job.TimeLimit.HasValue ? Format.Duration(job.TimeLimit.Value) : "",
            ["lengthM"] = job.TotalLength,
            ["bonus"] = job.Bonus,
            ["payWithBonus"] = job.Payment + job.Bonus,
            // Pay with bonus per minute of the time limit.
            ["payPerMin"] = job.TimeLimit.HasValue && job.TimeLimit.Value.TotalMinutes > 0
                ? (job.Payment + job.Bonus) / job.TimeLimit.Value.TotalMinutes : 0d,
            ["timed"] = job.TimeLimit.HasValue,
            ["timeLeft"] = job.TimeLeft.HasValue ? TimeLeft(job.TimeLeft.Value) : "",
            ["timeLeftS"] = job.TimeLeft?.TotalSeconds ?? 0d,
            ["late"] = job.TimeLeft.HasValue && job.TimeLeft.Value < TimeSpan.Zero,
            ["fromTrack"] = job.StartTrackId ?? "",
            ["toTrack"] = job.DestinationTrackId ?? "",
            ["coupled"] = job.IsCoupled,
            ["active"] = job.IsActive,
            ["steps"] = string.Join("\n", job.Steps),
        };
    }
}
