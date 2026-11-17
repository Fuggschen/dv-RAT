using System.Collections.Generic;
using System.Linq;
using DV.Utils;

namespace DVRAT
{
    /// <summary>Plain-data copy of the player's train and active jobs for <see cref="ShuntingPlanner"/>. Main thread only.</summary>
    public class ShuntingInput
    {
        /// <summary>Gap between coupled cars, from CarSpawner.</summary>
        private const float CouplingGap = 0.3f;

        /// <summary>Car IDs on the planned side of the loco, from the loco outwards.</summary>
        public List<string> Consist = new List<string>();
        public Dictionary<string, List<string>> Waiting = new Dictionary<string, List<string>>();
        public List<ShuntingPlanner.Car> Cars = new List<ShuntingPlanner.Car>();

        /// <summary>Meters each track has for the planned cars, see <see cref="ShuntingPlanner.Solve"/>.</summary>
        public Dictionary<string, float> TrackSpace = new Dictionary<string, float>();

        /// <summary>Places of the plan and the train start. The worker adds the distances.</summary>
        public ShuntingPlanner.RouteCosts Routes = new ShuntingPlanner.RouteCosts();

        /// <summary>Null when the network could not be read.</summary>
        public TrackGraph Graph;

        /// <summary>Track indices in <see cref="Graph"/> of each place in <see cref="Routes"/>.</summary>
        public Dictionary<string, List<int>> PlaceTracks = new Dictionary<string, List<int>>();

        /// <summary>Things the plan leaves out, shown above the steps.</summary>
        public List<string> Notes = new List<string>();

        public string Error;

        /// <param name="network">Null leaves out the distances between stations.</param>
        public static ShuntingInput FromGame(IEnumerable<JobInfo> activeJobs, TrainCar trainCar, TrackNetwork network)
        {
            var input = new ShuntingInput();
            var jobCars = activeJobs.SelectMany(j => j.Cars).Where(c => c.Legs.Count > 0).ToList();
            if (jobCars.Count == 0)
            {
                input.Error = "The active jobs have nothing left to move.";
                return input;
            }

            // Track of each planned car, so its length counts as free space there.
            var carTrack = new Dictionary<string, string>();
            var train = trainCar?.trainset?.cars;
            int loco = train?.FindIndex(c => c != null && c.IsLoco) ?? -1;
            var jobIds = new HashSet<string>(jobCars.Select(c => c.Id));
            if (train != null && loco >= 0)
            {
                // trainset.cars runs end to end. Plan the side with job cars.
                var front = train.Take(loco).Reverse().ToList();
                var back = train.Skip(loco + 1).ToList();
                bool frontHasJobs = front.Any(c => jobIds.Contains(c.ID)), backHasJobs = back.Any(c => jobIds.Contains(c.ID));
                var side = frontHasJobs && !backHasJobs ? front : back;
                var other = side == front ? back : front;
                if (frontHasJobs && backHasJobs)
                    input.Notes.Add("Job cars on both sides of the loco; only the rear side is planned.");

                input.Consist = side.Select(c => c.ID).ToList();
                var otherIds = new HashSet<string>(other.Select(c => c.ID));
                jobCars.RemoveAll(c => otherIds.Contains(c.Id));
                jobIds.ExceptWith(otherIds);

                foreach (var car in side.Where(c => !jobIds.Contains(c.ID)))
                    input.Cars.Add(new ShuntingPlanner.Car { Id = car.ID, Length = car.logicCar.length + CouplingGap });
                foreach (var car in side)
                    carTrack[car.ID] = TrackNames.CurrentTrack(car.logicCar)?.ID.FullDisplayID;
            }
            else if (jobCars.Any(c => c.IsCoupled))
            {
                input.Error = "No locomotive in the train.";
                return input;
            }

            foreach (var car in jobCars)
            {
                bool coupled = input.Consist.Contains(car.Id);
                if (!coupled && car.Track == null)
                {
                    input.Notes.Add($"{car.Id} is moving or on another train and is left out.");
                    continue;
                }
                input.Cars.Add(new ShuntingPlanner.Car
                {
                    Id = car.Id,
                    Targets = ShuntingPlanner.TargetsFromLegs(car.Legs),
                    Length = car.Length + CouplingGap,
                });
                if (coupled) continue;

                carTrack[car.Id] = car.Track;
                if (!input.Waiting.TryGetValue(car.Track, out var list))
                    input.Waiting[car.Track] = list = new List<string>();
                list.Add(car.Id);
            }

            input.AddTrackSpace(carTrack);
            input.AddPlaces(network, trainCar);
            return input;
        }

        /// <summary>Maps each track the plan uses, and the train, to a place. A station is one place, a rural stop or other lone track is its own.</summary>
        private void AddPlaces(TrackNetwork network, TrainCar trainCar)
        {
            if (network == null) return;
            Graph = network.Graph;
            var byName = new Dictionary<string, int>();
            for (int i = 0; i < Graph.Tracks.Count; i++)
                if (Graph.Tracks[i].Name != null && !byName.ContainsKey(Graph.Tracks[i].Name))
                    byName[Graph.Tracks[i].Name] = i;
            var rural = new Dictionary<string, int>();
            foreach (var stop in RuralStations.All())
                if (stop.Track != null && network.TrackIndex.TryGetValue(stop.Track, out int index))
                    rural[stop.Id] = index;

            string PlaceFor(string track)
            {
                if (rural.TryGetValue(track, out int stop))
                    return Own(track, stop);
                // A Passenger Jobs stop inside a game station has that station's ID.
                if (network.StationTracks.TryGetValue(track, out var whole))
                {
                    Routes.Places[track] = track;
                    PlaceTracks[track] = whole;
                    return track;
                }
                string station = ShuntingPlanner.PlaceOf(track).station;
                if (network.StationTracks.TryGetValue(station, out var tracks))
                {
                    PlaceTracks[station] = tracks;
                    return station;
                }
                return byName.TryGetValue(track, out int index) ? Own(track, index) : null;
            }
            string Own(string track, int index)
            {
                Routes.Places[track] = track;
                PlaceTracks[track] = new List<int> { index };
                return track;
            }

            foreach (string track in Waiting.Keys.Concat(Cars.SelectMany(c => c.Targets.Select(t => t.Track))).Distinct().ToList())
                PlaceFor(track);
            var here = trainCar != null ? TrackNames.CurrentTrack(trainCar) : null;
            if (here != null && network.TrackIndex.TryGetValue(here, out int start))
                Routes.Start = PlaceFor(Graph.Tracks[start].Name);
        }

        /// <summary>Free length of each used track. Planned cars count as free because the plan moves them.</summary>
        private void AddTrackSpace(Dictionary<string, string> carTrack)
        {
            var used = new HashSet<string>(Waiting.Keys);
            foreach (var car in Cars)
                foreach (var target in car.Targets)
                    used.Add(target.Track);

            var lengths = Cars.ToDictionary(c => c.Id, c => c.Length);
            foreach (var railTrack in SingletonBehaviour<RailTrackRegistryBase>.Instance.AllTracks)
            {
                var track = railTrack != null ? railTrack.LogicTrack() : null;
                string name = track?.ID.FullDisplayID;
                if (name == null || !used.Contains(name) || TrackSpace.ContainsKey(name)) continue;

                float planned = carTrack.Where(p => p.Value == name).Sum(p => lengths.TryGetValue(p.Key, out float l) ? l : 0f);
                TrackSpace[name] = (float)track.length - track.OccupiedLength + planned;
            }
        }

        /// <summary>One log line that reproduces the plan in a test.</summary>
        public string Describe()
        {
            string cars = string.Join("; ", Cars.Select(c =>
                $"{c.Id} {c.Length:F1}m [{string.Join(" ", c.Targets.Select(t => $"{t.Kind}:{t.Track}"))}]"));
            string waiting = string.Join("; ", Waiting.Select(w => $"{w.Key}: {string.Join(" ", w.Value)}"));
            string space = string.Join("; ", TrackSpace.Select(t => $"{t.Key} {t.Value:F0}m"));
            string places = string.Join("; ", Routes.Places.Keys);
            return $"consist [{string.Join(" ", Consist)}] waiting [{waiting}] cars [{cars}] space [{space}] " +
                $"start [{Routes.Start}] own places [{places}]";
        }
    }
}
