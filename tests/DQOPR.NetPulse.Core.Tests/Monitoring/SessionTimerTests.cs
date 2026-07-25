using DQOPR.NetPulse.Core.Monitoring;

namespace DQOPR.NetPulse.Core.Tests.Monitoring;

public sealed class SessionTimerTests
{
    [Fact]
    public void ActiveElapsedExcludesPausedDuration()
    {
        var clock = new FakeClock();
        var timer = new SessionTimer(clock);

        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(5));
        timer.Pause();
        clock.Advance(TimeSpan.FromSeconds(20));
        timer.Resume();
        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(8), timer.ActiveElapsed);
        Assert.Equal(TimeSpan.FromSeconds(20), timer.PausedDuration);
    }
}
