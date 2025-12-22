using System;
using System.Timers;

namespace RCCarController
{
    public class PartyDaySessionManager : IDisposable
    {
        private readonly System.Timers.Timer countdownTimer;
        private DateTime? sessionEndsAt;
        private bool disposed;

        public bool SessionActive => sessionEndsAt.HasValue && sessionEndsAt.Value > DateTime.UtcNow;
        public TimeSpan Remaining => SessionActive ? sessionEndsAt!.Value - DateTime.UtcNow : TimeSpan.Zero;

        public event Action? SessionStarted;
        public event Action? SessionEnded;
        public event Action<TimeSpan>? Tick;

        public PartyDaySessionManager(int sessionSeconds = 240)
        {
            SessionDurationSeconds = sessionSeconds;
            countdownTimer = new System.Timers.Timer(1000);
            countdownTimer.Elapsed += CountdownTimer_Elapsed;
            countdownTimer.AutoReset = true;
        }

        public int SessionDurationSeconds { get; set; }

        public void StartSession()
        {
            sessionEndsAt = DateTime.UtcNow.AddSeconds(SessionDurationSeconds);
            countdownTimer.Start();
            SessionStarted?.Invoke();
            Tick?.Invoke(Remaining);
        }

        public void StopSession()
        {
            if (!SessionActive)
            {
                sessionEndsAt = null;
                countdownTimer.Stop();
                return;
            }

            sessionEndsAt = null;
            countdownTimer.Stop();
            SessionEnded?.Invoke();
        }

        private void CountdownTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!SessionActive)
            {
                sessionEndsAt = null;
                countdownTimer.Stop();
                SessionEnded?.Invoke();
                return;
            }

            Tick?.Invoke(Remaining);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            countdownTimer?.Stop();
            countdownTimer?.Dispose();
        }
    }
}
