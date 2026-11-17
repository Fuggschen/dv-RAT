using System;
using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Tracks which plan steps are done by comparing the train with the cars expected after each move. No Unity types.</summary>
    public class PlanProgress
    {
        public enum StepState { Pending, Done, Failed }

        private readonly IReadOnlyList<ShuntingPlanner.Move> _moves;
        // Cars in the train before each move, plus the train after the plan.
        private readonly List<HashSet<string>> _expected = new List<HashSet<string>>();
        // Cars outside this set are ignored.
        private readonly HashSet<string> _known;
        private readonly StepState[] _states;

        public PlanProgress(IEnumerable<string> consist, IReadOnlyList<ShuntingPlanner.Move> moves)
        {
            _moves = moves;
            _states = new StepState[moves.Count];
            var train = new HashSet<string>(consist);
            _expected.Add(new HashSet<string>(train));
            foreach (var move in moves)
            {
                if (move.Kind == ShuntingPlanner.MoveKind.PickUp) train.UnionWith(move.Cars);
                else if (move.Kind == ShuntingPlanner.MoveKind.Drop) train.ExceptWith(move.Cars);
                _expected.Add(new HashSet<string>(train));
            }
            _known = new HashSet<string>(_expected.SelectMany(set => set).Concat(moves.SelectMany(m => m.Cars)));
        }

        public IReadOnlyList<StepState> States => _states;

        /// <summary>First step not done yet, or the step count when all are done.</summary>
        public int Current { get; private set; }

        /// <summary>The player left the plan, so only a new plan helps.</summary>
        public bool Failed => Current < _states.Length && _states[Current] == StepState.Failed;

        public bool Finished => Current >= _states.Length;

        /// <summary>Returns true when a step changed its state. Several steps can complete at once.</summary>
        /// <param name="trackOf">Track a car stands on, null when unknown or moving.</param>
        public bool Update(IEnumerable<string> train, Func<ShuntingPlanner.Move, bool> served, Func<string, string> trackOf)
        {
            if (Finished || Failed) return false;
            var now = new HashSet<string>(train.Where(_known.Contains));
            var move = _moves[Current];

            // Waiting for the current step, or halfway through it.
            bool serving = move.Kind == ShuntingPlanner.MoveKind.Serve;
            if (serving ? !served(move) && now.SetEquals(_expected[Current]) : InProgress(move, now))
                return false;

            // Nearest matching step, because a train can match again later.
            int reached = -1;
            for (int step = Current; step < _moves.Count && reached < 0; step++)
            {
                var candidate = _moves[step];
                if (candidate.Kind == ShuntingPlanner.MoveKind.Serve && !served(candidate)) break;
                if (now.SetEquals(_expected[step + 1]) && DroppedWhereDue(candidate, trackOf))
                    reached = step;
            }

            if (reached < 0)
            {
                _states[Current] = StepState.Failed;
                return true;
            }
            for (; Current <= reached; Current++)
                _states[Current] = StepState.Done;
            return true;
        }

        private bool InProgress(ShuntingPlanner.Move move, HashSet<string> now)
        {
            var before = _expected[Current];
            var after = _expected[Current + 1];
            if (now.SetEquals(after)) return false;
            return move.Kind == ShuntingPlanner.MoveKind.PickUp
                ? before.IsSubsetOf(now) && now.IsSubsetOf(after)
                : after.IsSubsetOf(now) && now.IsSubsetOf(before);
        }

        private static bool DroppedWhereDue(ShuntingPlanner.Move move, Func<string, string> trackOf)
        {
            // A rural passenger stop ID is not a track ID.
            if (move.Kind != ShuntingPlanner.MoveKind.Drop || move.Passengers) return true;
            return move.Cars.All(car => trackOf(car) is string track ? track == move.Track : true);
        }
    }
}
