using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Loco resource use per minute, from the change between readings. No Unity types, so it is unit testable.</summary>
    public class ResourceRates
    {
        /// <summary>Readings closer than this keep the previous rates, as the change is too small to measure.</summary>
        public const float MinSeconds = 0.5f;

        /// <summary>After a longer gap the last reading is too old to compare.</summary>
        public const float MaxSeconds = 10f;

        private string _locoId;
        private float _time;
        private Dictionary<string, float> _amounts = new Dictionary<string, float>();
        private Dictionary<string, float> _rates = new Dictionary<string, float>();

        /// <summary>Sets <see cref="ResourceLevel.PerMinute"/> on each level. A level that went up gets no rate.</summary>
        public void Update(string locoId, IDictionary<string, ResourceLevel> levels, float seconds)
        {
            if (locoId != _locoId)
            {
                _locoId = locoId;
                _amounts.Clear();
                _rates.Clear();
            }

            float elapsed = seconds - _time;
            if (_amounts.Count > 0 && elapsed >= 0f && elapsed < MinSeconds)
            {
                foreach (var pair in levels)
                    if (_rates.TryGetValue(pair.Key, out float kept))
                        pair.Value.PerMinute = kept;
                return;
            }

            bool compare = elapsed >= MinSeconds && elapsed <= MaxSeconds;
            var amounts = new Dictionary<string, float>();
            var rates = new Dictionary<string, float>();
            foreach (var pair in levels)
            {
                float amount = pair.Value.Amount;
                amounts[pair.Key] = amount;
                if (compare && _amounts.TryGetValue(pair.Key, out float before) && before >= amount)
                {
                    float rate = (before - amount) / elapsed * 60f;
                    pair.Value.PerMinute = rate;
                    rates[pair.Key] = rate;
                }
            }

            _amounts = amounts;
            _rates = rates;
            _time = seconds;
        }
    }
}
