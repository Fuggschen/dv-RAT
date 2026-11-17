using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace DVRAT
{
    /// <summary>The player's jobs in tabs, plus the shunting plan for the active jobs. Active marks live in <see cref="ActiveJobStore"/>.</summary>
    public class JobsWidget : IPageWidget
    {
        private enum Tab { Accepted, Coupled, Active, Plan }

        private const int RowsPerScreen = 4;
        private const float EntryHeight = 78f;
        private const int EntryFont = 15;
        private const int StepRowsPerScreen = 9;

        /// <summary>Search states stored before falling back to a quick plan. About 200 MB at most.</summary>
        private const int MaxSearchNodes = 150000;

        private Tab _tab = Tab.Accepted;
        private PagedList _list;
        private string _pageId;

        // Planning runs on a worker thread, which writes these.
        private volatile bool _planning;
        private volatile float _progress;
        private CancellationTokenSource _cancel;
        private ShuntingPlanner.Plan _plan;
        private List<string> _notes = new List<string>();
        // Places and start of the last plan, for sending its stations to the route planner.
        private ShuntingPlanner.RouteCosts _routes;
        private Dictionary<string, string> _carJobs = new Dictionary<string, string>();
        // Null without a plan.
        private PlanProgress _tracker;

        /// <summary>Page variable that keeps the open tab.</summary>
        private const string TabVar = "jobsTab";

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _pageId = pageId;
            if (context.Tablet != null
                && context.Tablet.Session.Vars(pageId, null).TryGetValue(TabVar, out string savedTab)
                && Enum.TryParse(savedTab, out Tab tab))
                _tab = tab;
            var jobs = context.Jobs;
            var tabs = UIFactory.Row(parent, 34);
            TabButton(tabs, context, Tab.Accepted, $"Accepted ({jobs?.AcceptedJobs.Count ?? 0})");
            TabButton(tabs, context, Tab.Coupled, $"Coupled ({jobs?.CoupledJobs.Count ?? 0})");
            TabButton(tabs, context, Tab.Active, $"Active ({ActiveJobs(jobs).Count()})");
            TabButton(tabs, context, Tab.Plan, "Plan");
            UIFactory.Divider(parent);

            if (_tab == Tab.Plan)
                BuildPlan(parent, context);
            else
                _list = new PagedList(parent, rows ?? RowsPerScreen, EmptyText(), EntryHeight, context.View(pageId), "jobs", _tab.ToString());
        }

        public void Refresh(PageContext context)
        {
            // The plan is tracked on every tab so steps done meanwhile are ticked off.
            bool planChanged = TrackPlan(context);
            if (_tab != Tab.Plan)
                _list?.SetRows(JobsOnTab(context.Jobs), (cells, job) => JobRow(cells, context, job));
            else if (planChanged && !_planning)
                ShowPlan();
        }

        /// <summary>True when a step changed its state.</summary>
        private bool TrackPlan(PageContext context)
        {
            var train = context.Tablet != null ? context.Tablet.TrainCar?.trainset?.cars : null;
            if (_tracker == null || train == null || context.Jobs == null) return false;

            var jobCars = new Dictionary<string, JobCar>();
            foreach (var car in context.Jobs.AcceptedJobs.Concat(context.Jobs.PlannedJobs).SelectMany(j => j.Cars))
                jobCars[car.Id] = car;

            // A car whose job is gone (handed in) has nothing left to do.
            bool Served(ShuntingPlanner.Move move) => move.Cars.All(id => !jobCars.TryGetValue(id, out var car)
                || !car.Legs.Any(leg => leg.Kind != JobLegKind.Transport && leg.ToTrack == move.Track));
            string TrackOf(string id) => jobCars.TryGetValue(id, out var car) ? car.Track : null;

            return _tracker.Update(train.Where(c => c != null).Select(c => c.ID), Served, TrackOf);
        }

        private void BuildPlan(RectTransform parent, PageContext context)
        {
            var row = UIFactory.Row(parent, 38);
            if (_planning)
                UIFactory.Width(UIFactory.Button(row, "Cancel", () => _cancel?.Cancel()), 200);
            else
                UIFactory.Width(UIFactory.Button(row, "Calculate", () => Calculate(context)), 200);
            var route = _planning || _plan?.Error != null || _plan == null ? null
                : ShuntingPlanner.RouteOf(_plan.Moves, _routes, new HashSet<string>(Stations.All().Select(s => s.Key)));
            if (route.HasValue)
                UIFactory.Width(UIFactory.Button(row, "Plan Route", () => PlanRoute(context, route.Value)), 160);
            UIFactory.Width(UIFactory.Label(row, PlanStatus(), Theme.FontBody,
                _plan?.Error != null ? Theme.Warning : Theme.TextDim), 0, 1);

            if (_planning)
            {
                ProgressBar.Create(parent, () => _progress);
                UIFactory.Height(UIFactory.Label(parent, "Trying every order of moves. This can take a while.",
                    Theme.FontSmall, Theme.TextDim), Theme.RowHeight);
                _list = null;
                return;
            }

            _list = new PagedList(parent, StepRowsPerScreen, "Tick jobs as active, then press Calculate.", Theme.RowHeight,
                context.View(_pageId), "plan");
            ShowPlan();
        }

        private const string PlannerPage = "planner";

        /// <summary>Fills the route planner with the plan's stations and opens it.</summary>
        private static void PlanRoute(PageContext context, (string start, List<string> stops, string destination) route)
        {
            if (context.Tablet == null) return;
            var session = context.Tablet.Session;
            session.PlannerFrom = route.start;
            session.PlannerStops = route.stops;
            session.PlannerTo = route.destination;
            session.PlannerHasRoute = false;
            context.Navigator.Open(PlannerPage);
        }

        /// <summary>Shows the notes and steps. Done steps are green, a step done differently is red.</summary>
        private void ShowPlan()
        {
            if (_list == null) return;
            var lines = new List<(string text, Color color)>();
            if (_tracker != null && _tracker.Failed)
                lines.Add(("Not done as planned. Press Calculate to plan from here.", Theme.Warning));
            if (_plan != null)
                lines.AddRange(_plan.Warnings.Select(warning => (warning, Theme.Warning)));
            lines.AddRange(_notes.Select(note => (note, Theme.TextDim)));
            if (_plan != null && _plan.Error == null)
                lines.AddRange(_plan.Moves.Select((move, i) => ($"{i + 1}. {StepText(move, _carJobs)}", StepColor(i))));
            if (_tracker != null && _tracker.Finished && _plan?.Moves.Count > 0)
                lines.Add(("All steps done.", Theme.Done));
            _list.SetRows(lines, (cells, line) => cells.Label(line.text, color: line.color));
        }

        private Color StepColor(int step)
        {
            switch (_tracker != null && step < _tracker.States.Count ? _tracker.States[step] : PlanProgress.StepState.Pending)
            {
                case PlanProgress.StepState.Done: return Theme.Done;
                case PlanProgress.StepState.Failed: return Theme.Warning;
                default: return Theme.Text;
            }
        }

        private string PlanStatus()
        {
            if (_planning) return "Searching...";
            if (_plan == null) return "";
            if (_plan.Error != null) return _plan.Error;
            if (_plan.Moves.Count == 0) return "Nothing to shunt.";
            string kind = _plan.ProvenOptimal ? "fewest possible" : "search limit hit, not proven to be the fewest";
            return $"{_plan.Moves.Count} moves ({kind})";
        }

        private void Calculate(PageContext context)
        {
            if (_planning) return;
            var active = ActiveJobs(context.Jobs).ToList();
            if (active.Count == 0)
            {
                _plan = new ShuntingPlanner.Plan { Error = "No active jobs." };
                context.Rebuild();
                return;
            }
            if (active.Any(j => j.IsShunting) && active.Any(j => !j.IsShunting))
            {
                _plan = new ShuntingPlanner.Plan { Error = "Shunting jobs can't be planned together with other jobs. Untick one kind." };
                context.Rebuild();
                return;
            }

            ShuntingInput input;
            using (Timing.Measure("Reading the yard for the shunting plan"))
                input = ShuntingInput.FromGame(active, context.Tablet != null ? context.Tablet.TrainCar : PlayerManager.Car,
                    context.Tablet?.TrackNetwork);
            _notes = input.Notes;
            _routes = input.Routes;
            _tracker = null;
            _carJobs = active.SelectMany(j => j.Cars.Select(c => (car: c.Id, job: j.JobId)))
                .GroupBy(p => p.car).ToDictionary(g => g.Key, g => g.First().job);
            if (input.Error != null)
            {
                _plan = new ShuntingPlanner.Plan { Error = input.Error };
                context.Rebuild();
                return;
            }

            RATMain.Log("Shunting plan input: " + input.Describe());
            _planning = true;
            _progress = 0f;
            _cancel?.Dispose();
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            context.Rebuild();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                ShuntingPlanner.Plan plan;
                try
                {
                    if (input.Graph != null)
                        input.Routes.Meters = StationDistances.Compute(input.Graph, input.PlaceTracks, token);
                    plan = ShuntingPlanner.Solve(input.Consist, input.Waiting, input.Cars, MaxSearchNodes, p => _progress = p, token,
                        input.TrackSpace, input.Routes);
                }
                catch (OperationCanceledException)
                {
                    plan = new ShuntingPlanner.Plan { Error = "Cancelled." };
                }
                catch (Exception ex)
                {
                    plan = new ShuntingPlanner.Plan { Error = "Planning failed: " + ex.Message };
                }

                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    if (input.Routes.Meters.Count > 0)
                        RATMain.Log("Shunting plan distances: " +
                            string.Join("; ", input.Routes.Meters.Select(m => $"{m.Key.Item1}-{m.Key.Item2} {m.Value:F0}m")));
                    _planning = false;
                    _plan = plan;
                    _tracker = plan.Error == null ? new PlanProgress(input.Consist, plan.Moves) : null;
                    if (context.Navigator.Current == _pageId)
                        context.Rebuild();
                });
            });
        }

        /// <summary>E.g. "HB-C3L: couple 3 (CFF872 - CFF361) for HB-FH-12". Cars run from the loco side outwards.</summary>
        private static string StepText(ShuntingPlanner.Move move, IReadOnlyDictionary<string, string> carJobs)
        {
            int count = move.Cars.Count;
            string range = count == 0 ? "" : count == 1 ? $" ({move.Cars[0]})" : $" ({move.Cars[0]} - {move.Cars[count - 1]})";
            var jobs = move.Cars.Select(c => carJobs.TryGetValue(c, out string job) ? job : null).Where(j => j != null).Distinct().ToList();
            string forJobs = jobs.Count > 0 ? " for " + string.Join(", ", jobs) : "";
            switch (move.Kind)
            {
                case ShuntingPlanner.MoveKind.Drop:
                    return $"{move.Track}: uncouple the last {count}{range}{forJobs}";
                case ShuntingPlanner.MoveKind.PickUp:
                    return $"{move.Track}: couple {count}{range}{forJobs}";
                default:
                    return move.Passengers
                        ? $"{move.Track}: Stop at Station{forJobs}"
                        : $"{move.Track}: load or unload {count}{range}{forJobs}";
            }
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context) => _list?.SoftKeys() ?? Enumerable.Empty<SoftKey>();

        private static bool IsTaken(PageContext context, JobInfo job) =>
            context.Jobs?.AcceptedJobs.Any(j => j.JobId == job.JobId) ?? true;

        private void TabButton(RectTransform row, PageContext context, Tab tab, string caption)
        {
            var button = UIFactory.Button(row, caption, () =>
            {
                _tab = tab;
                if (context.Tablet != null)
                    context.Tablet.Session.Vars(_pageId, null)[TabVar] = tab.ToString();
                context.Rebuild();
            }, Theme.FontSmall,
                _tab == tab ? Theme.Highlight : Theme.TextDim);
            button.interactable = _tab != tab;
            UIFactory.Width(button, 0, 1);
        }

        private IEnumerable<JobInfo> JobsOnTab(JobData jobs)
        {
            if (jobs == null) return Enumerable.Empty<JobInfo>();
            switch (_tab)
            {
                case Tab.Coupled: return jobs.CoupledJobs;
                case Tab.Active: return ActiveJobs(jobs);
                default: return jobs.AcceptedJobs;
            }
        }

        /// <summary>Taken jobs marked active, then jobs marked on the Station page but not taken. Marks are read live.</summary>
        private static IEnumerable<JobInfo> ActiveJobs(JobData jobs) => jobs == null ? Enumerable.Empty<JobInfo>()
            : jobs.AcceptedJobs.Concat(jobs.PlannedJobs).Where(j => ActiveJobStore.IsActive(j.JobId));

        private string EmptyText()
        {
            switch (_tab)
            {
                case Tab.Coupled: return "No job has cars in this train.";
                case Tab.Active: return "No active jobs. Tick a job under Accepted, or send jobs from the Station page.";
                default: return "No jobs accepted.";
            }
        }

        private static void JobRow(IRowCells cells, PageContext context, JobInfo job)
        {
            bool active = ActiveJobStore.IsActive(job.JobId);
            string id = job.JobId;
            cells.Button(active ? "[x]" : "[ ]", () => { ActiveJobStore.Toggle(id); context.Rebuild(); },
                color: active ? Theme.Highlight : Theme.Text, fixedSize: new Vector2(44, 36), does: id);

            string coupled = (job.IsCoupled ? "  (coupled)" : "") + (IsTaken(context, job) ? "" : "  (not taken)");
            string steps = job.Steps.Count > 0 ? string.Join("  |  ", job.Steps) : "All tasks done, hand in the job";
            cells.Label(
                $"{job.JobTypeDisplay}  {job.JobId}{coupled}   {Stations.Name(job.OriginStation)} to {Stations.Name(job.DestinationStation)}\n" +
                $"{steps}\n" +
                $"{job.WagonCount} cars  |  {job.TotalMass:F0} t  |  {job.CargoType}  |  ${job.Payment:F0}",
                size: EntryFont, color: active ? Theme.Highlight : Theme.Text, anchor: TextAnchor.UpperLeft);
        }
    }
}
