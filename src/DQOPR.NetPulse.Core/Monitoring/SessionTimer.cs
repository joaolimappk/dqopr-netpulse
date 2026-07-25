using DQOPR.NetPulse.Core.Time;

namespace DQOPR.NetPulse.Core.Monitoring;

public sealed class SessionTimer(IMonitoringClock clock)
{
    private readonly IMonitoringClock clock = clock;
    private long startedAtTimestamp;
    private long pausedAtTimestamp;
    private TimeSpan activeBeforePause;
    private TimeSpan pausedDuration;

    public bool IsStarted { get; private set; }

    public bool IsPaused { get; private set; }

    public TimeSpan ActiveElapsed
    {
        get
        {
            if (!IsStarted)
            {
                return TimeSpan.Zero;
            }

            return IsPaused
                ? activeBeforePause
                : activeBeforePause + clock.GetElapsedTime(startedAtTimestamp);
        }
    }

    public TimeSpan PausedDuration => pausedDuration + (IsPaused ? clock.GetElapsedTime(pausedAtTimestamp) : TimeSpan.Zero);

    public void Start()
    {
        startedAtTimestamp = clock.GetTimestamp();
        activeBeforePause = TimeSpan.Zero;
        pausedDuration = TimeSpan.Zero;
        IsStarted = true;
        IsPaused = false;
    }

    public void Pause()
    {
        if (!IsStarted || IsPaused)
        {
            return;
        }

        activeBeforePause += clock.GetElapsedTime(startedAtTimestamp);
        pausedAtTimestamp = clock.GetTimestamp();
        IsPaused = true;
    }

    public void Resume()
    {
        if (!IsStarted || !IsPaused)
        {
            return;
        }

        pausedDuration += clock.GetElapsedTime(pausedAtTimestamp);
        startedAtTimestamp = clock.GetTimestamp();
        IsPaused = false;
    }
}
