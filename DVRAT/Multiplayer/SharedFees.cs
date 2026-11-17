using System.Collections.Generic;
using System.Linq;
using DV.ServicePenalty;
using DV.Utils;
using Newtonsoft.Json;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Career manager debts exist only on the host, so the host describes its fees here and a client's tablet shows those.</summary>
    public static class SharedFees
    {
        /// <summary>A company loco's debt, or an owned loco's service price.</summary>
        public class LocoFee
        {
            [JsonProperty("price")] public float Price { get; set; }
            [JsonProperty("service")] public bool IsService { get; set; }
        }

        private class Snapshot
        {
            [JsonProperty("fees")] public List<Fee> Fees { get; set; } = new List<Fee>();
            [JsonProperty("locos")] public Dictionary<string, LocoFee> Locos { get; set; } = new Dictionary<string, LocoFee>();
        }

        private static Snapshot _received = new Snapshot();

        private const float DebtRefreshSeconds = 10f;
        private static float _nextDebtRefresh;

        public static bool FromHost => MultiplayerSync.InGame && !MultiplayerSync.IsHost;

        public static List<Fee> Fees() => FromHost ? new List<Fee>(_received.Fees) : ReadFees();

        /// <summary>Null when the loco has no fee.</summary>
        public static LocoFee ForLoco(TrainCar car)
        {
            if (car == null) return null;
            if (FromHost)
                return _received.Locos.TryGetValue(car.ID, out var fee) ? fee : null;
            return ReadLocoFee(car);
        }

        public static string Describe()
        {
            var snapshot = new Snapshot { Fees = ReadFees() };
            foreach (var debt in SingletonBehaviour<LocoDebtController>.Instance?.trackedLocosDebts ?? new List<ExistingLocoDebt>())
                if (debt?.car != null)
                    snapshot.Locos[debt.car.ID] = LocoFeeOf(debt, false);
            foreach (var state in SingletonBehaviour<OwnedCarsStateController>.Instance?.existingOwnedCarStates ?? new List<ExistingOwnedCarDebt>())
                if (state?.car != null && !snapshot.Locos.ContainsKey(state.car.ID))
                    snapshot.Locos[state.car.ID] = LocoFeeOf(state, true);
            return JsonConvert.SerializeObject(snapshot);
        }

        /// <summary>Takes the JSON from <see cref="Describe"/>.</summary>
        public static void Receive(string json)
        {
            try
            {
                _received = JsonConvert.DeserializeObject<Snapshot>(json) ?? new Snapshot();
            }
            catch (JsonException ex)
            {
                RATMain.LogWarning($"Multiplayer: the host's fees could not be read: {ex.Message}");
            }
        }

        public static void Clear() => _received = new Snapshot();

        private static List<Fee> ReadFees()
        {
            var fees = new List<Fee>();
            var debts = SingletonBehaviour<CareerManagerDebtController>.Instance;
            if (debts == null) return fees;
            // Walks every job's debt, about 12 ms with many jobs, so it is throttled to DebtRefreshSeconds.
            if (Time.time >= _nextDebtRefresh)
            {
                _nextDebtRefresh = Time.time + DebtRefreshSeconds;
                debts.RefreshExistingDebtsState();
            }
            for (int i = 0; i < debts.NumberOfNonZeroPricedDebts; i++)
            {
                var debt = debts.GetIthNonZeroDebt(i);
                if (debt == null) continue;
                fees.Add(new Fee { Id = debt.ID, Kind = FeeKind(debt.GetDebtType()), Price = debt.GetTotalPrice(), Payable = debt.IsPayable });
            }
            fees.Sort((a, b) => b.Price.CompareTo(a.Price));
            return fees;
        }

        private static LocoFee ReadLocoFee(TrainCar car)
        {
            DisplayableDebt debt = SingletonBehaviour<LocoDebtController>.Instance?.trackedLocosDebts.FirstOrDefault(d => d.car == car);
            if (debt != null) return LocoFeeOf(debt, false);
            debt = SingletonBehaviour<OwnedCarsStateController>.Instance?.existingOwnedCarStates.FirstOrDefault(d => d.car == car);
            return debt != null ? LocoFeeOf(debt, true) : null;
        }

        private static LocoFee LocoFeeOf(DisplayableDebt debt, bool service)
        {
            // Updates to the loco's current resources, as the career manager does.
            debt.UpdateDebtState();
            return new LocoFee { Price = debt.GetTotalPrice(), IsService = service };
        }

        private static string FeeKind(DebtType type)
        {
            switch (type)
            {
                case DebtType.ExistingJob: case DebtType.StagedJob: return "job";
                case DebtType.ExistingLoco: case DebtType.StagedLoco: return "loco";
                case DebtType.ExistingOwnedCar: case DebtType.StagedOwnedCar: return "owned";
                default: return "other";
            }
        }
    }
}
