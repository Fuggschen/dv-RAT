using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace DVRAT
{
    /// <summary>Reads Solar Gadgets power networks: the PowerStatus from each car's SolarPowerNetwork.GetStatus, by reflection. A missing member switches the reader off with one log warning.</summary>
    public static class SolarGadgetsPower
    {
        private static readonly ModWatch Mod = new ModWatch("SolarGadgets");
        private const string NetworkTypeName = "SolarGadgets.Game.SolarPowerNetwork";

        private static bool _searched;
        private static bool _failed;
        private static Type _network;
        private static MethodInfo _getStatus;
        private static FieldInfo _supply, _demand, _charge, _capacity, _flow, _stored, _room, _batteries, _consumers;
        private static FieldInfo _batteryName, _batterySoldered, _batteryCharge, _batteryCapacity;
        private static FieldInfo _consumerName, _consumerDraw;

        public static bool Active => Find();

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        /// <summary>Null without a network or when it has nothing in it.</summary>
        public static CarPower Read(TrainCar car)
        {
            if (car == null || car.Customization == null || !Find()) return null;
            try
            {
                var network = car.Customization.GetComponent(_network);
                if (network == null) return null;
                object status = _getStatus.Invoke(network, null);
                var power = new CarPower
                {
                    CarId = car.ID,
                    Supply = (float)_supply.GetValue(status),
                    Demand = (float)_demand.GetValue(status),
                    Charge = (float?)_charge.GetValue(status),
                    Capacity = (float)_capacity.GetValue(status),
                    Net = (float)_flow.GetValue(status),
                    Stored = (float)_stored.GetValue(status),
                    Room = (float)_room.GetValue(status),
                };
                foreach (object battery in (IEnumerable)_batteries.GetValue(status))
                    power.Batteries.Add(new PowerBattery
                    {
                        Name = (string)_batteryName.GetValue(battery),
                        Soldered = (bool)_batterySoldered.GetValue(battery),
                        Charge = (float)_batteryCharge.GetValue(battery),
                        Capacity = (float)_batteryCapacity.GetValue(battery),
                    });
                foreach (object consumer in (IEnumerable)_consumers.GetValue(status))
                    power.Consumers.Add(new PowerConsumer
                    {
                        Name = (string)_consumerName.GetValue(consumer),
                        Draw = (float)_consumerDraw.GetValue(consumer),
                    });

                // The mod adds a network to every car with a gadget, even a non-power one.
                bool empty = power.Batteries.Count == 0 && power.Consumers.Count == 0 && power.Supply <= 0f;
                return empty ? null : power;
            }
            catch (Exception ex)
            {
                Fail($"reading the power of {car.ID} failed: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (_searched) return _network != null;
            _searched = true;

            _network = Mod.FindType(NetworkTypeName);
            if (_network == null)
            {
                Fail($"{NetworkTypeName} was not found");
                return false;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            _getStatus = _network.GetMethod("GetStatus", flags, null, Type.EmptyTypes, null);
            var status = _getStatus?.ReturnType;
            var battery = status?.GetNestedType("BatteryStatus");
            var consumer = status?.GetNestedType("ConsumerStatus");
            _supply = status?.GetField("Supply", flags);
            _demand = status?.GetField("Demand", flags);
            _charge = status?.GetField("Charge", flags);
            _capacity = status?.GetField("Capacity", flags);
            _flow = status?.GetField("Flow", flags);
            _stored = status?.GetField("Stored", flags);
            _room = status?.GetField("Room", flags);
            _batteries = status?.GetField("Batteries", flags);
            _consumers = status?.GetField("Consumers", flags);
            _batteryName = battery?.GetField("Name", flags);
            _batterySoldered = battery?.GetField("Soldered", flags);
            _batteryCharge = battery?.GetField("Charge", flags);
            _batteryCapacity = battery?.GetField("Capacity", flags);
            _consumerName = consumer?.GetField("Name", flags);
            _consumerDraw = consumer?.GetField("Draw", flags);

            var members = new MemberInfo[]
            {
                _getStatus, _supply, _demand, _charge, _capacity, _flow, _stored, _room, _batteries, _consumers,
                _batteryName, _batterySoldered, _batteryCharge, _batteryCapacity, _consumerName, _consumerDraw,
            };
            if (members.Any(m => m == null))
            {
                Fail("its power status was not found");
                return false;
            }
            RATMain.Log("Solar Gadgets found, its batteries and consumers are shown on the Power page");
            return true;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"Solar Gadgets: {reason}. Its power is left out.");
        }
    }
}
