using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.Simulation.Cars;
using DV.Utils;
using DV.Wheels;
using LocoSim.Definitions;
using LocoSim.Implementations;
using LocoSim.Resources;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Reads the player's train into a <see cref="TrainData"/> snapshot.</summary>
    public class TrainDataProvider : IDisposable
    {
        private readonly ResourceRates _resourceRates = new ResourceRates();

        /// <param name="playerCar">See <see cref="RATTabletController.TrainCar"/>.</param>
        public TrainData Refresh(TrainCar playerCar)
        {
            var data = new TrainData();

            try
            {
                if (playerCar == null) return data;

                var trainset = playerCar.trainset;
                if (trainset == null) return data;

                // Like dv-hud: the end with a loco first.
                var cars = trainset.cars.ToList();
                if (cars.Count > 1 && !cars[0].IsLoco && cars[cars.Count - 1].IsLoco)
                    cars.Reverse();
                float totalMass = 0f;
                float totalLength = 0f;

                // With several locos, show the tablet's own car or the closest loco.
                int own = cars.IndexOf(playerCar);
                int bestDistance = int.MaxValue;
                for (int i = 0; i < cars.Count; i++)
                {
                    var car = cars[i];
                    if (car == null) continue;

                    var carInfo = BuildCarInfo(car);
                    if (i > 0 && cars[i - 1] != null)
                        carInfo.CouplerForceKn = CouplerForceKn(cars[i - 1], car);
                    data.Cars.Add(carInfo);
                    totalMass += carInfo.Mass;
                    totalLength += carInfo.Length;

                    int distance = own >= 0 ? Math.Abs(i - own) : i;
                    if (carInfo.IsLocomotive && distance < bestDistance)
                    {
                        data.Locomotive = (LocoInfo)carInfo;
                        bestDistance = distance;
                    }
                }

                // Scaled game time, as the simulation drains nothing while paused.
                _resourceRates.Update(data.Locomotive?.CarId,
                    data.Locomotive?.Resources ?? new Dictionary<string, ResourceLevel>(), Time.time);

                data.RealisticBrakes = data.Cars.Any(c => c.BrakeValve != null);
                data.SolarGadgets = SolarGadgetsPower.Active;
                if (data.SolarGadgets)
                    data.Power = cars.Select(SolarGadgetsPower.Read).Where(p => p != null).ToList();
                data.TotalMass = totalMass;
                data.TotalLength = totalLength;
                data.WagonCount = data.Cars.Count(c => !c.IsLocomotive);

                float speedMs = Mathf.Abs(playerCar.GetForwardSpeed());
                data.SpeedKmh = speedMs * 3.6f;
                // dv-hud's sea level. Positions move with the floating origin.
                data.AltitudeM = (playerCar.transform.position - DV.OriginShift.OriginShift.currentMove).y - 110f;
                float pitch = Mathf.Asin(Mathf.Clamp(playerCar.transform.forward.y, -1f, 1f));
                data.PitchGradePercent = Mathf.Tan(pitch) * 100f * (TrackNames.MovesForward(playerCar) ? 1f : -1f);
                foreach (var loco in data.Cars.OfType<LocoInfo>().Where(l => l.HasTraction))
                    loco.PowerKw = loco.TractiveEffortKn * speedMs;

                data.Braking = CalculateBrakingInfo(data, speedMs);
                data.Electric = CatenaryMods.Read(data.Locomotive, data.Cars, cars);
                if (data.Locomotive != null)
                {
                    var locoCar = cars.FirstOrDefault(c => c != null && c.ID == data.Locomotive.CarId);
                    ReadFee(locoCar, data.Locomotive);
                    data.Locomotive.OdometerKm = Odometer.KmOf(locoCar);
                    data.Locomotive.Firing = FiringData.FromReadings(RealisticFiring.Read(locoCar));
                }
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Error in TrainDataProvider.Refresh: {ex.Message}");
            }

            return data;
        }

        private CarInfo BuildCarInfo(TrainCar car)
        {
            bool isLoco = car.IsLoco;

            CarInfo info;
            if (isLoco)
            {
                info = BuildLocoInfo(car);
            }
            else
            {
                info = new CarInfo();
            }

            info.CarId = car.ID;
            info.CarType = GetCarTypeName(car);
            info.CarName = GameContent.LiveryName(car.carLivery) ?? info.CarType;
            info.IsLocomotive = isLoco;
            info.Mass = GetCarMass(car);
            info.Length = GetCarLength(car);
            info.LoadedCargo = GetCargoType(car);
            info.CargoAmount = (car.massController?.CargoMass ?? 0f) / 1000f;
            info.IsDerailed = car.derailed;
            info.DamagePercent = GetDamagePercent(car);
            info.CurrentTrackId = GetTrackId(car);
            ReadBrakes(car, info);
            ReadJob(car, info);
            var stress = car.stress;
            float threshold = Globals.G.GameParams.DerailBuildUpThreshold;
            if (stress != null && threshold > 0f)
                info.DerailPercent = stress.derailBuildUp / threshold * 100f;

            return info;
        }

        private static void ReadBrakes(TrainCar car, CarInfo info)
        {
            var brake = car.brakeSystem;
            if (brake == null) return;
            const float atmosphere = DV.Simulation.Brake.BrakeSystemConsts.ATMOSPHERE_PRESSURE;
            info.BrakePipePressure = Mathf.Max(0f, brake.brakePipePressure - atmosphere);
            info.ReservoirPressure = Mathf.Max(0f, (car.IsLoco ? brake.mainReservoirPressure : brake.auxReservoirPressure) - atmosphere);
            info.BrakeCylinderPressure = Mathf.Max(0f, brake.brakeCylinderPressure - atmosphere);
            info.ControlReservoirPressure = Mathf.Max(0f, brake.controlReservoirPressure - atmosphere);
            info.BrakingFactor = brake.brakingFactor;
            info.BrakeTemperature = brake.heatController?.temperature ?? 0f;
            info.HandbrakeApplied = brake.hasHandbrake && brake.handbrakePosition > 0.1f;

            var valve = RealisticBrakes.Read(car);
            if (valve == null) return;
            info.BrakeValve = valve.Text ?? "";
            if (valve.ControlBar.HasValue)
                info.ControlReservoirPressure = valve.ControlBar.Value;
            if (info is LocoInfo loco)
            {
                loco.EqualizingReservoirPressure = valve.EqualizingBar;
                loco.DriverValve = valve.DriverValve;
            }
        }

        /// <summary>Covers a job that is taken or only offered.</summary>
        private static void ReadJob(TrainCar car, CarInfo info)
        {
            var job = car.logicCar != null ? JobsManager.Instance?.GetJobOfCar(car.logicCar, false) : null;
            if (job == null) return;
            info.JobId = job.ID;
            info.JobTaken = job.State == DV.ThingTypes.JobState.InProgress;
            var destination = job.tasks.Select(task => NextDestination(task, car.logicCar)).FirstOrDefault(t => t != null);
            info.NextDestination = destination?.ID.FullDisplayID;
            info.AtDestination = destination != null && car.logicCar.CurrentTrack == destination;
        }

        private static Track NextDestination(Task task, Car car)
        {
            var data = task.GetTaskData();
            if (data.type == TaskType.Transport || data.type == TaskType.Warehouse)
                return data.state == TaskState.InProgress && data.cars.Contains(car) ? data.destinationTrack : null;
            return data.nestedTasks?.Select(t => NextDestination(t, car)).FirstOrDefault(t => t != null);
        }

        /// <summary>Pull or push between two neighbouring cars, from whichever car created the joint. Null when no joint couples them.</summary>
        private static float? CouplerForceKn(TrainCar a, TrainCar b)
        {
            foreach (var (car, other) in new[] { (a, b), (b, a) })
                foreach (var coupler in new[] { car.frontCoupler, car.rearCoupler })
                    if (coupler != null && coupler.rigidCJ != null && coupler.coupledTo?.train == other)
                        return coupler.rigidCJ.currentForce.magnitude / 1000f;
            return null;
        }

        private LocoInfo BuildLocoInfo(TrainCar car)
        {
            var loco = new LocoInfo();

            try
            {
                var brake = car.brakeSystem;
                if (brake != null)
                {
                    loco.TrainBrakePosition = brake.trainBrakePosition;
                    loco.IndependentBrakePosition = brake.independentBrakePosition;
                    loco.HandbrakePosition = brake.handbrakePosition;
                    // The game keeps absolute pressures. Gauges read above the atmosphere.
                    const float atmosphere = DV.Simulation.Brake.BrakeSystemConsts.ATMOSPHERE_PRESSURE;
                    loco.MainReservoirPressure = Mathf.Max(0f, brake.mainReservoirPressure - atmosphere);
                }

                var simController = car.SimController;
                if (simController != null)
                {
                    var controls = simController.controlsOverrider;
                    if (controls != null)
                    {
                        loco.Throttle = controls.Throttle?.Value ?? 0f;
                        loco.Reverser = TrackNames.ReverserDirection(car);
                        loco.HandbrakePosition = controls.Handbrake?.Value ?? 0f;
                    }

                    ReadSimulation(simController, loco);
                }
                ReadTraction(car, loco);
                loco.Damage = LocoDamage.Read(car, simController);
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"Error building loco info: {ex.Message}");
            }

            return loco;
        }

        /// <summary>The loco's debt as the career manager lists it. Mod charges, like electricity, add to it.</summary>
        private static void ReadFee(TrainCar car, LocoInfo loco)
        {
            if (car == null) return;
            try
            {
                // In multiplayer only the host has the debts. A client gets them from the host.
                var fee = SharedFees.ForLoco(car);
                if (fee == null) return;
                loco.FeeIsService = fee.IsService;
                loco.Fee = fee.Price;
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"Reading the fee of {car.ID} failed: {ex.Message}");
            }
        }

        private static void ReadTraction(TrainCar car, LocoInfo loco)
        {
            var wheels = car.GetComponentInChildren<WheelslipController>(true);
            if (wheels == null) return;
            loco.HasTraction = true;
            loco.TractiveEffortKn = (wheels.DrivingForce != null ? wheels.DrivingForce.generatedForce : 0f) / 1000f;
            loco.AdhesionLimitKn = wheels.TotalForceLimit / 1000f;
            loco.Wheelslip = wheels.wheelslip;
            loco.WheelSlide = car.adhesionController?.wheelSlide ?? 0f;
        }

        /// <summary>Resources come from the resource containers. Everything else is found by port name, because component IDs differ per loco.</summary>
        private static void ReadSimulation(SimController simController, LocoInfo loco)
        {
            foreach (var container in simController.resourceContainerController?.resourceContainers ?? new List<ResourceContainer>())
            {
                loco.Resources[ResourceName(container.resourceType)] = new ResourceLevel
                {
                    Amount = container.amountReadOut.Value,
                    Capacity = container.capacity,
                    Normalized = container.normalizedReadOutPort.Value,
                };
            }

            var ports = simController.simFlow?.AllPorts;
            if (ports == null) return;
            foreach (var port in ports)
                loco.Ports.Add(new SimPort { Id = port.id, Type = port.valueType.ToString(), Value = port.Value });

            var byId = loco.Ports.ToDictionary(p => p.Id, p => p.Value);
            // Component that owns the port, e.g. "engine" for "engine.ENGINE_ON".
            string Owner(string portName) =>
                loco.Ports.Select(p => p.Id).FirstOrDefault(id => id.EndsWith("." + portName, StringComparison.Ordinal))?.Split('.')[0];

            string engine = Owner("ENGINE_ON");
            if (engine != null)
            {
                loco.HasEngine = true;
                loco.EngineRunning = byId[engine + ".ENGINE_ON"] > 0.5f;
                byId.TryGetValue(engine + ".RPM", out float rpm);
                loco.EngineRPM = rpm;
            }

            string boiler = Owner("WATER_LEVEL_NORMALIZED");
            if (boiler != null)
            {
                loco.HasBoiler = true;
                loco.BoilerWaterLevel = byId[boiler + ".WATER_LEVEL_NORMALIZED"];
                byId.TryGetValue(boiler + ".PRESSURE", out float pressure);
                loco.BoilerPressure = pressure;
            }

            string firebox = Owner("FIRE_ON");
            if (firebox != null)
            {
                loco.HasFirebox = true;
                loco.FireOn = byId[firebox + ".FIRE_ON"] > 0.5f;
                byId.TryGetValue(firebox + ".TEMPERATURE", out float fireTemperature);
                loco.FireboxTemperature = fireTemperature;
            }

            // Steam valve cutoff, set by the reverser.
            var cutoff = loco.Ports.FirstOrDefault(p => p.Id.EndsWith(".CUTOFF", StringComparison.Ordinal) || p.Id.EndsWith(".CUT_OFF", StringComparison.Ordinal));
            if (cutoff != null)
            {
                loco.HasCutoff = true;
                loco.Cutoff = Mathf.Abs(cutoff.Value);
            }

            // The traction motor set reads its temperature through a port reference. The connection names the port.
            string tmTemperature = simController.connectionsDefinition?.portReferenceConnections?
                .FirstOrDefault(c => c?.portReferenceId != null && c.portReferenceId.EndsWith(".TM_TEMPERATURE", StringComparison.Ordinal))?.portId;
            if (tmTemperature != null && byId.TryGetValue(tmTemperature, out float tmValue))
            {
                loco.TmTemperature = tmValue;
                loco.TmOverheatTemperature = simController.connectionsDefinition.executionOrder?
                    .OfType<TractionMotorSetDefinition>().FirstOrDefault()?.overheatingTemperatureThreshold;
            }

            // Diesel coolant or oil: the first temperature outside boiler, firebox and traction motors.
            var temperature = loco.Ports.FirstOrDefault(p => p.Id.EndsWith(".TEMPERATURE", StringComparison.Ordinal)
                && p.Type == "TEMPERATURE" && p.Id.Split('.')[0] != boiler && p.Id.Split('.')[0] != firebox
                && p.Id != tmTemperature);
            if (temperature != null)
            {
                loco.HasTemperature = true;
                loco.Temperature = temperature.Value;
            }
        }

        private static string ResourceName(ResourceContainerType type) =>
            type == ResourceContainerType.ELECTRIC_CHARGE ? "charge" : type.ToString().ToLowerInvariant();

        private string GetCarTypeName(TrainCar car)
        {
            try
            {
                if (car.carLivery != null)
                    return car.carLivery.id;
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private float GetCarMass(TrainCar car)
        {
            try
            {
                // Game masses are in kg: car, cargo and resources.
                if (car.massController != null)
                    return car.massController.TotalMass / 1000f;
                return (car.carLivery?.parentType?.mass ?? 0f) / 1000f;
            }
            catch
            {
                return 0f;
            }
        }

        private float GetCarLength(TrainCar car)
        {
            try
            {
                return car.InterCouplerDistance;
            }
            catch
            {
                return 0f;
            }
        }

        private float GetDamagePercent(TrainCar car)
        {
            try
            {
                if (car.CarDamage != null)
                    return (1f - car.CarDamage.currentHealth / car.CarDamage.maxHealth) * 100f;
                return 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private string GetCargoType(TrainCar car)
        {
            try
            {
                if (car.logicCar == null) return string.Empty;
                var cargoType = car.logicCar.CurrentCargoTypeInCar;
                if (cargoType == DV.ThingTypes.CargoType.None) return string.Empty;
                return cargoType.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Estimates braking distance as d = v² / (2a), with a from the brake state and mass, car count and damage penalties.</summary>
        private BrakingInfo CalculateBrakingInfo(TrainData data, float speedMs)
        {
            var info = new BrakingInfo { SpeedMs = speedMs };

            if (data.Locomotive == null || data.TotalMass <= 0f)
                return info;

            var loco = data.Locomotive;

            // Empirical, in m/s². Emergency braking on a light train and full service braking.
            const float MAX_DECEL = 2.5f;
            const float SERVICE_DECEL = 1.5f;

            float avgDamage = 0f;
            int wagonCount = 0;
            foreach (var car in data.Cars)
            {
                if (!car.IsLocomotive)
                {
                    avgDamage += car.DamagePercent;
                    wagonCount++;
                }
            }
            avgDamage = wagonCount > 0 ? avgDamage / wagonCount : 0f;

            // 100% damage loses 50% effectiveness.
            float brakeEffectiveness = Mathf.Clamp01(1f - (avgDamage / 200f));
            info.BrakeEffectiveness = brakeEffectiveness;

            float effectiveMaxDecel = MAX_DECEL * brakeEffectiveness;
            info.MaxDecelerationRate = effectiveMaxDecel;

            float trainBrakeFactor = loco.TrainBrakePosition;
            // The independent brake mostly affects the loco itself.
            float indBrakeFactor = loco.IndependentBrakePosition * 0.15f;
            float handbrakeFactor = loco.HandbrakePosition > 0.1f ? 0.1f : 0f;

            float totalBrakeInput = Mathf.Clamp01(trainBrakeFactor + indBrakeFactor + handbrakeFactor);

            float currentDecel = SERVICE_DECEL * totalBrakeInput * brakeEffectiveness;

            // The factors were tuned while the mass was passed in kg, which always lands on the floors
            // below. The result matched the game, so the kg comparison stays until re-tuned.
            const float REFERENCE_MASS = 200f;
            float massFactor = Mathf.Clamp01(REFERENCE_MASS / Mathf.Max(data.TotalMass * 1000f, 1f));

            // Brake pipe propagation delay: longer trains respond slower.
            int totalCars = data.Cars.Count;
            float propagationFactor = Mathf.Clamp01(1f - (totalCars - 1) * 0.03f);

            // Emergency braking skips these penalties.
            currentDecel *= Mathf.Max(massFactor, 0.4f);
            currentDecel *= propagationFactor;

            float emergencyDecel = effectiveMaxDecel * Mathf.Max(massFactor, 0.6f);

            if (totalBrakeInput > 0.01f)
                currentDecel = Mathf.Max(currentDecel, 0.1f);

            info.DecelerationRate = currentDecel;

            if (currentDecel > 0.01f)
                info.CurrentBrakingDistance = (speedMs * speedMs) / (2f * currentDecel);
            else
                info.CurrentBrakingDistance = float.MaxValue;

            if (emergencyDecel > 0.01f)
                info.EmergencyBrakingDistance = (speedMs * speedMs) / (2f * emergencyDecel);
            else
                info.EmergencyBrakingDistance = float.MaxValue;

            return info;
        }

        private string GetTrackId(TrainCar car)
        {
            try
            {
                if (car.logicCar?.CurrentTrack != null)
                    return car.logicCar.CurrentTrack.ID.FullID;
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Dispose()
        {
        }
    }
}