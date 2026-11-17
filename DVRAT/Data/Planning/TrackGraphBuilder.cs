using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.Utils;
using UnityEngine;

namespace DVRAT
{
    /// <summary>A <see cref="TrackGraph"/> with the game objects its indices refer to. Main thread only, except <see cref="Graph"/>.</summary>
    public class TrackNetwork
    {
        public TrackGraph Graph { get; } = new TrackGraph();
        public List<RailTrack> Tracks { get; } = new List<RailTrack>();
        public List<Junction> Junctions { get; } = new List<Junction>();
        public Dictionary<RailTrack, int> TrackIndex { get; } = new Dictionary<RailTrack, int>();

        /// <summary>Track indices per station yard ID, taken from the logic track ID prefix.</summary>
        public Dictionary<string, List<int>> StationTracks { get; } = new Dictionary<string, List<int>>();

        public int SourceTrackCount { get; private set; }

        public static TrackNetwork Build()
        {
            var network = new TrackNetwork();
            var allTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.AllTracks;
            network.SourceTrackCount = allTracks.Length;
            var yardIds = new HashSet<string>(StationController.allStations
                .Where(s => s?.stationInfo != null).Select(s => s.stationInfo.YardID));
            var locoSpawns = LocoSpawnTracks();

            foreach (var track in allTracks.Where(t => t != null))
            {
                var points = track.GetKinkedPointSet();
                string logicId = LogicId(track);
                int index = network.Graph.AddTrack(logicId ?? track.gameObject.name, (float)points.span, TrackGeometry.GradePercent(points),
                    IsYardTrack(track) || locoSpawns.Contains(track.gameObject.name));
                network.Tracks.Add(track);
                network.TrackIndex[track] = index;

                string yard = logicId?.Split('-')[0];
                if (yard != null && yardIds.Contains(yard))
                {
                    if (!network.StationTracks.TryGetValue(yard, out var list))
                        network.StationTracks[yard] = list = new List<int>();
                    list.Add(index);
                }
            }

            var junctionIndex = new Dictionary<Junction, int>();
            foreach (var track in network.Tracks)
            {
                network.ConnectEnd(track, true, junctionIndex);
                network.ConnectEnd(track, false, junctionIndex);
            }
            return network;
        }

        private void ConnectEnd(RailTrack track, bool forward, Dictionary<Junction, int> junctionIndex)
        {
            var branches = forward ? track.GetAllOutBranches() : track.GetAllInBranches();
            if (branches == null) return;

            var junction = forward ? track.outJunction : track.inJunction;
            // Only a train coming from the in branch faces the switch.
            bool facing = junction != null && junction.inBranch?.track == track;
            int from = TrackIndex[track];

            foreach (var branch in branches)
            {
                if (branch?.track == null || !TrackIndex.TryGetValue(branch.track, out int to)) continue;

                // Entering at the first point means moving forward.
                if (facing)
                    Graph.Connect(from, forward, to, branch.first, JunctionIndex(junction, junctionIndex),
                        junction.outBranches.FindIndex(b => b.track == branch.track));
                else
                    Graph.Connect(from, forward, to, branch.first);
            }
        }

        private int JunctionIndex(Junction junction, Dictionary<Junction, int> junctionIndex)
        {
            if (!junctionIndex.TryGetValue(junction, out int index))
            {
                index = Junctions.Count;
                Junctions.Add(junction);
                junctionIndex[junction] = index;
            }
            return index;
        }

        /// <summary>Object names of loco spawn tracks. Some are through tracks, but routes avoid them like yard tracks.</summary>
        private static HashSet<string> LocoSpawnTracks() => new HashSet<string>(
            Resources.FindObjectsOfTypeAll<StationLocoSpawner>()
                .Where(s => s.gameObject.scene.IsValid())
                .Select(s => s.locoSpawnTrack != null ? s.locoSpawnTrack.gameObject.name : s.locoSpawnTrackName)
                .Where(name => !string.IsNullOrEmpty(name)));

        /// <summary>A track with a station ID that is not a main line. Connecting tracks have generic IDs.</summary>
        private static bool IsYardTrack(RailTrack track)
        {
            try
            {
                var id = track.LogicTrack()?.ID;
                // The track type is private, but the sign part ends with it.
                return id != null && !id.IsGeneric() && !id.SignIDTrackPart.EndsWith(TrackID.MAIN_LINE_TYPE, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string LogicId(RailTrack track)
        {
            try
            {
                return track.LogicTrack()?.ID.FullID;
            }
            catch
            {
                return null;
            }
        }
    }

    public static class TrackGeometry
    {
        /// <summary>Average grade from the first to the last point, in percent.</summary>
        public static float GradePercent(DV.PointSet.EquiPointSet points)
        {
            if (points?.points == null || points.points.Length < 2) return 0f;

            var first = points.points[0].position;
            var last = points.points[points.points.Length - 1].position;
            double horizontal = System.Math.Sqrt((last.x - first.x) * (last.x - first.x) + (last.z - first.z) * (last.z - first.z));
            return horizontal < 0.001 ? 0f : (float)((last.y - first.y) / horizontal * 100.0);
        }

        public static Vector3 ToVector3(Vector3d position) => new Vector3((float)position.x, (float)position.y, (float)position.z);
    }
}
