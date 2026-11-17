using DV.Logic.Job;
using UnityEngine;

namespace DVRAT
{
    public static class TrackNames
    {
        /// <summary>E.g. "W-001" on the line or "S-0228-HB" in a station. Falls back to the object name for switches without one.</summary>
        public static string Name(this Junction junction)
        {
            string id = junction.junctionData.junctionIdLong;
            return string.IsNullOrEmpty(id) || id == Junction.JunctionData.ID_MARKER_NONE ? junction.gameObject.name : id;
        }

        /// <summary>"HB" for "S-0228-HB", null for switches on the line.</summary>
        public static string StationOf(this Junction junction)
        {
            var parts = junction.Name().Split('-');
            return parts.Length == 3 && parts[0] == "S" ? parts[2] : null;
        }

        /// <summary>-1 (reverse) to 1 (forward), 0 in neutral or without a reverser. The game uses 0 to 1 with neutral at 0.5.</summary>
        public static float ReverserDirection(TrainCar car)
        {
            var reverser = car != null && car.SimController != null ? car.SimController.controlsOverrider?.Reverser : null;
            return reverser == null ? 0f : (reverser.Value - 0.5f) * 2f;
        }

        /// <summary>At a standstill the nearest loco's reverser decides, in neutral the car's facing.</summary>
        public static bool MovesForward(TrainCar car)
        {
            float speed = car.GetForwardSpeed();
            if (Mathf.Abs(speed) > 0.1f) return speed > 0f;

            var cars = car.trainset?.cars;
            int own = cars?.IndexOf(car) ?? -1;
            TrainCar loco = null;
            for (int i = 0; cars != null && i < cars.Count; i++)
                if (cars[i] != null && cars[i].IsLoco && (loco == null || Mathf.Abs(i - own) < Mathf.Abs(cars.IndexOf(loco) - own)))
                    loco = cars[i];

            float direction = ReverserDirection(loco);
            if (Mathf.Abs(direction) < 0.1f) return true;
            // The loco may face the other way than the car the tablet is on.
            return Vector3.Dot(car.transform.forward, loco.transform.forward * direction) >= 0f;
        }

        /// <summary>The logic car has no track on a switch, so the front bogie's track is used.</summary>
        public static RailTrack CurrentTrack(TrainCar car)
        {
            if (car == null) return null;
            var track = car.logicCar?.CurrentTrack?.RailTrack();
            if (track != null) return track;
            var bogies = car.Bogies;
            return bogies != null && bogies.Length > 0 ? bogies[bogies.Length - 1].track : null;
        }

        /// <summary>The game has none while the bogies are on different tracks, so the bogie track with a station ID is used.</summary>
        public static Track CurrentTrack(Car car)
        {
            if (car == null) return null;
            if (car.CurrentTrack != null) return car.CurrentTrack;
            var front = car.FrontBogieTrack;
            var rear = car.RearBogieTrack;
            if (rear?.ID != null && !rear.ID.IsGeneric()) return rear;
            if (front?.ID != null && !front.ID.IsGeneric()) return front;
            return rear ?? front;
        }
    }
}
