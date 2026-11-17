using DVRAT;
using Xunit;

namespace DVRAT.Tests
{
    public class JobCarTests
    {
        private static JobCar CarWith(params JobLeg[] legs)
        {
            var car = new JobCar { Id = "BXK123" };
            car.Legs.AddRange(legs);
            return car;
        }

        [Fact]
        public void NextTaskNamesTheFirstLegAndItsTrack()
        {
            var car = CarWith(
                new JobLeg { Kind = JobLegKind.Load, ToTrack = "CW-WH1" },
                new JobLeg { Kind = JobLegKind.Transport, FromTrack = "CW-WH1", ToTrack = "HB-C3L" });

            Assert.Equal("Load at CW-WH1", car.NextTask);
            Assert.Equal("Move to HB-C3L", CarWith(car.Legs[1]).NextTask);
            Assert.Equal("Unload at HB-WH2", CarWith(new JobLeg { Kind = JobLegKind.Unload, ToTrack = "HB-WH2" }).NextTask);
        }

        [Fact]
        public void FinalTrackIsWhereTheLastLegEnds()
        {
            var car = CarWith(
                new JobLeg { Kind = JobLegKind.Load, ToTrack = "CW-WH1" },
                new JobLeg { Kind = JobLegKind.Transport, FromTrack = "CW-WH1", ToTrack = "HB-C3L" });

            Assert.Equal("HB-C3L", car.FinalTrack);
        }

        [Fact]
        public void ACarWithoutLegsIsDone()
        {
            var car = CarWith();

            Assert.Equal("Done", car.NextTask);
            Assert.Null(car.FinalTrack);
        }

        [Theory]
        [InlineData("ShuntingLoad", true)]
        [InlineData("ShuntingUnload", true)]
        [InlineData("Transport", false)]
        [InlineData("EmptyHaul", false)]
        // Passenger Jobs' own job types have no name in the game's enum.
        [InlineData("101", false)]
        public void OnlyShuntingLoadAndUnloadAreShunting(string type, bool shunting)
        {
            Assert.Equal(shunting, new JobInfo { JobType = type }.IsShunting);
        }
    }
}
