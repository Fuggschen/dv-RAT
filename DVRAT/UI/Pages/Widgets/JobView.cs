using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>One job in detail. Shared by the Yard page and <see cref="JobWidget"/>.</summary>
    public class JobView
    {
        private const float RowHeight = 22f;
        private const float IdWidth = 80f;
        private const float KeyWidth = 90f;
        private const float StatusWidth = 200f;

        private readonly Text _title;
        private readonly Text _status;
        private readonly Text _route;
        private readonly Text _cars;
        private readonly Text _pay;
        private readonly Text _licenses;
        private readonly PagedList _list;

        /// <param name="view">The page's view state, see <see cref="PagedList"/>.</param>
        /// <param name="jobId">Keys the list screen, so another job starts on the first screen.</param>
        public JobView(RectTransform parent, int? rows, IDictionary<string, string> view = null, string jobId = "")
        {
            var title = UIFactory.Row(parent, RowHeight, name: "Title");
            _title = UIFactory.Width(UIFactory.Label(title, "", Theme.FontBody, Theme.Highlight, TextAnchor.MiddleLeft, FontStyle.Bold), 0, 1);
            _status = UIFactory.Width(UIFactory.Label(title, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight), StatusWidth);
            _route = UIFactory.KeyValue(parent, "Route", "", KeyWidth);
            _cars = UIFactory.KeyValue(parent, "Cars", "", KeyWidth);
            _pay = UIFactory.KeyValue(parent, "Pay", "", KeyWidth);
            _licenses = UIFactory.KeyValue(parent, "Licenses", "", KeyWidth);
            UIFactory.Divider(parent);
            _list = new PagedList(parent, rows ?? 0, "", RowHeight, view, "job", jobId);
        }

        /// <summary>Shows the job <paramref name="jobId"/>. Null shows that no job is picked.</summary>
        public void Refresh(string jobId)
        {
            bool taken = false;
            var job = jobId != null ? WorldDataProvider.FindJob(jobId, out taken) : null;
            if (job == null)
            {
                _title.text = jobId ?? "";
                _status.text = jobId != null ? "Handed in or gone" : "No job picked";
                _route.text = _cars.text = _pay.text = _licenses.text = "";
                _list.SetRows(new List<JobCar>(), (cells, car) => { });
                return;
            }

            _title.text = $"{job.JobTypeDisplay}  {job.JobId}";
            _status.text = (taken ? "Taken" : "Offered") + (job.IsActive ? ", planned" : "");
            string tracks = job.StartTrackId != null && job.DestinationTrackId != null ? $"  |  {job.StartTrackId} > {job.DestinationTrackId}" : "";
            _route.text = $"{Stations.Name(job.OriginStation)} to {Stations.Name(job.DestinationStation)}{tracks}";
            _cars.text = $"{Format.Count(job.WagonCount, "car")}  |  {job.TotalMass:F0} t  |  {job.TotalLength:F0} m"
                + (job.CargoType != null ? $"  |  {job.CargoType}" : "");
            _pay.text = $"${job.Payment:F0}" + (job.TimeLeft.HasValue ? $"  |  bonus ${job.Bonus:F0}, {TimeLeft(job.TimeLeft.Value)}" : "");
            _licenses.text = job.Licenses.Count > 0 ? string.Join(", ", job.Licenses) : "None";
            _licenses.color = job.MissingLicenses.Count > 0 ? Theme.Warning : Theme.Text;

            var steps = job.Steps.Count > 0 ? job.Steps : new List<string> { "All tasks done, hand in the job" };
            var tasks = new System.Action<IRowCells>[] { cells => cells.Cell("Tasks", 0, Theme.Highlight, FontStyle.Bold) }
                .Concat(steps.Select(step => (System.Action<IRowCells>)(cells => cells.Cell(step, 0, Theme.Text))))
                .ToList();
            var cars = new System.Action<IRowCells>[] { CarHeaders }
                .Concat(job.Cars.Select(car => (System.Action<IRowCells>)(cells => CarRow(cells, car))))
                .ToList();
            _list.SetGroups(new IReadOnlyList<System.Action<IRowCells>>[] { tasks, cars });
        }

        public IEnumerable<SoftKey> SoftKeys() => _list.SoftKeys();

        private static void CarHeaders(IRowCells cells)
        {
            cells.Cell("Cars", IdWidth, Theme.Highlight, FontStyle.Bold);
            cells.Cell("Type", 0, Theme.Highlight, FontStyle.Bold);
            cells.Cell("On track", IdWidth, Theme.Highlight, FontStyle.Bold);
            cells.Cell("Next task", 0, Theme.Highlight, FontStyle.Bold);
        }

        private static void CarRow(IRowCells cells, JobCar car)
        {
            cells.Cell(car.Id, IdWidth, Theme.Text);
            cells.Cell(car.CarName ?? car.CarType ?? "", 0, Theme.Text);
            cells.Cell(car.Track ?? "Moving", IdWidth, Theme.Text);
            cells.Cell(car.NextTask, 0, Theme.Text);
        }

        /// <summary>A non-positive time means the bonus is gone.</summary>
        private static string TimeLeft(System.TimeSpan time) =>
            time > System.TimeSpan.Zero ? $"{Format.Duration(time)} left" : "time is up";
    }
}
