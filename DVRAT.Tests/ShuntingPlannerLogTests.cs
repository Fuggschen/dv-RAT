using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DVRAT;
using Xunit;
using Xunit.Abstractions;

/// <summary>Replays "Shunting plan input:" lines from Player.log.</summary>
public class ShuntingPlannerLogTests
{
    private readonly ITestOutputHelper _output;

    public ShuntingPlannerLogTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Four shunting jobs at the Harbor: two warehouses (HB-D7L, HB-C3L), 34 cars on four tracks.
    private const string HarborFourJobsInput =
        "consist [] waiting [HB-E9I: CFF872 CFF094 CFF361 CFF723 CFF252 CFF420 CFF519 CFF022 CFF547 CFF813 CFF977 CFF618 CFF551; HB-G2S: CCH573 CCH315 CCH092 CCH137 CFF708 CFF673 CFF943 CFF058; HB-G1S: COL320 COL494 COL809 COL927 COL586 COL164 COL256; HB-D4I: CFF416 CFF713 CFF790 CFF080 CFF070 CFF106] cars [CFF872 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF094 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF361 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF723 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF252 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF420 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF519 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF022 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF547 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF813 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF977 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF618 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CFF551 18.3m [Serve:HB-D7L Deliver:HB-C1S]; CCH573 14.8m [Serve:HB-C3L Deliver:HB-E3O]; CCH315 14.8m [Serve:HB-C3L Deliver:HB-E3O]; CCH092 14.8m [Serve:HB-C3L Deliver:HB-E3O]; CCH137 14.8m [Serve:HB-C3L Deliver:HB-E3O]; CFF708 18.3m [Serve:HB-C3L Deliver:HB-E3O]; CFF673 18.3m [Serve:HB-C3L Deliver:HB-E3O]; CFF943 18.3m [Serve:HB-C3L Deliver:HB-E3O]; CFF058 18.3m [Serve:HB-C3L Deliver:HB-E3O]; COL320 14.8m [Serve:HB-C3L Deliver:HB-E4O]; COL494 14.8m [Serve:HB-C3L Deliver:HB-E4O]; COL809 14.8m [Serve:HB-C3L Deliver:HB-E4O]; COL927 14.8m [Serve:HB-C3L Deliver:HB-E4O]; COL586 14.8m [Serve:HB-C3L Deliver:HB-E4O]; COL164 14.8m [Serve:HB-C3L Deliver:HB-E4O]; COL256 14.8m [Serve:HB-C3L Deliver:HB-E4O]; CFF416 18.3m [Serve:HB-D7L Deliver:HB-G6S]; CFF713 18.3m [Serve:HB-D7L Deliver:HB-G6S]; CFF790 18.3m [Serve:HB-D7L Deliver:HB-G6S]; CFF080 18.3m [Serve:HB-D7L Deliver:HB-G6S]; CFF070 18.3m [Serve:HB-D7L Deliver:HB-G6S]; CFF106 18.3m [Serve:HB-D7L Deliver:HB-G6S]] space [HB-G6S 150m; HB-D7L 629m; HB-C1S 304m; HB-G1S 262m; HB-D4I 254m; HB-C3L 466m; HB-E3O 209m; HB-E9I 445m; HB-E4O 328m; HB-G2S 261m]";

    [Fact]
    public void HarborFourJobs()
    {
        var (consist, waiting, cars, space) = Parse(HarborFourJobsInput);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var plan = ShuntingPlanner.Solve(consist, waiting, cars, 150000, null, CancellationToken.None, space);

        _output.WriteLine($"{watch.Elapsed.TotalSeconds:F1} s, optimal {plan.ProvenOptimal}, error {plan.Error}");
        for (int i = 0; i < plan.Moves.Count; i++)
            _output.WriteLine($"{i + 1}. {plan.Moves[i].Kind} {plan.Moves[i].Track} {plan.Moves[i].Cars.Count}: {string.Join(" ", plan.Moves[i].Cars)}");
        // 4 pickups, one stop per warehouse, 4 drops.
        Assert.Null(plan.Error);
        Assert.True(plan.ProvenOptimal);
        Assert.Equal(10, plan.Moves.Count);
        Assert.Equal(2, plan.Moves.Count(m => m.Kind == ShuntingPlanner.MoveKind.Serve));
        // Each warehouse is finished before the next, so no car is carried along.
        Assert.Equal("PickUp PickUp Serve Drop Drop PickUp PickUp Serve Drop Drop",
            string.Join(" ", plan.Moves.Select(m => m.Kind)));
    }

    // Two freight hauls from the Harbor to Steel Mill; other cars leave SM-B3I 42 m for the 11 cars of one job.
    private const string FullDestinationInput =
        "consist [] waiting [HB-D6O: CFF502 CFF448 CFF971 CFF537 CFF155 CFF193 CFF167 CFF404 CFF753 CFF670 CFF785; HB-E10O: CFF683 CFF361 CFF444 CFF410 CFF216] cars [CFF502 18.3m [Deliver:SM-B3I]; CFF448 18.3m [Deliver:SM-B3I]; CFF971 18.3m [Deliver:SM-B3I]; CFF537 18.3m [Deliver:SM-B3I]; CFF155 18.3m [Deliver:SM-B3I]; CFF193 18.3m [Deliver:SM-B3I]; CFF167 18.3m [Deliver:SM-B3I]; CFF404 18.3m [Deliver:SM-B3I]; CFF753 18.3m [Deliver:SM-B3I]; CFF670 18.3m [Deliver:SM-B3I]; CFF785 18.3m [Deliver:SM-B3I]; CFF683 18.3m [Deliver:SM-A6I]; CFF361 18.3m [Deliver:SM-A6I]; CFF444 18.3m [Deliver:SM-A6I]; CFF410 18.3m [Deliver:SM-A6I]; CFF216 18.3m [Deliver:SM-A6I]] space [SM-A6I 136m; SM-B3I 42m; HB-D6O 554m; HB-E10O 375m]";

    [Fact]
    public void FullDestinationIsPlannedWithWarning()
    {
        var (consist, waiting, cars, space) = Parse(FullDestinationInput);

        var plan = ShuntingPlanner.Solve(consist, waiting, cars, 150000, null, CancellationToken.None, space);

        Assert.Null(plan.Error);
        Assert.Equal(new[] { "SM-B3I has room for 42 m, the cars to deliver there need 201 m. Other cars stand on it." },
            plan.Warnings);
        // Both cuts picked up, each dropped on its own track.
        Assert.Equal("PickUp PickUp Drop Drop", string.Join(" ", plan.Moves.Select(m => m.Kind)));
    }

    private static (List<string>, Dictionary<string, List<string>>, List<ShuntingPlanner.Car>, Dictionary<string, float>) Parse(string line)
    {
        string Section(string name) => Regex.Match(line, name + @" \[(.*?)\](?= \w+ \[|$)").Groups[1].Value;

        var consist = Section("consist").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
        var waiting = new Dictionary<string, List<string>>();
        foreach (string part in Section("waiting").Split(';').Where(p => p.Contains(":")))
        {
            var pieces = part.Split(':');
            waiting[pieces[0].Trim()] = pieces[1].Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        var cars = new List<ShuntingPlanner.Car>();
        foreach (Match m in Regex.Matches(Regex.Match(line, @"cars \[(.*)\] space").Groups[1].Value, @"(\S+) ([\d.]+)m \[([^\]]*)\]"))
        {
            var car = new ShuntingPlanner.Car { Id = m.Groups[1].Value, Length = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) };
            foreach (string target in m.Groups[3].Value.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = target.Split(':');
                car.Targets.Add(new ShuntingPlanner.Target(
                    kv[0] == "Serve" ? ShuntingPlanner.TargetKind.Serve : ShuntingPlanner.TargetKind.Deliver, kv[1]));
            }
            cars.Add(car);
        }

        var space = new Dictionary<string, float>();
        foreach (Match m in Regex.Matches(Regex.Match(line, @"space \[(.*)\]$").Groups[1].Value, @"(\S+) (\d+)m"))
            space[m.Groups[1].Value] = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return (consist, waiting, cars, space);
    }
}
