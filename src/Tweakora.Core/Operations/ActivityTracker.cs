using System;
using System.Diagnostics;
using System.Threading;

namespace Tweakora.Core.Operations;

public interface IActivityTracker
{
    bool IsActive { get; }
    string CurrentAction { get; }
    event Action<bool, string>? OnActivityChanged;
    event Action<string>? OnActivityCompleted;
    IDisposable BeginActivity(string actionName);
    void UpdateAction(string actionName);
    void CompleteActivity(string? completedMessage = null);
}

public sealed class ActivityTracker : IActivityTracker
{
    private int _activeCount;
    private readonly object _lock = new();
    private Stopwatch? _stopwatch;

    public bool IsActive => Volatile.Read(ref _activeCount) > 0;
    public string CurrentAction { get; private set; } = string.Empty;

    public event Action<bool, string>? OnActivityChanged;
    public event Action<string>? OnActivityCompleted;

    public IDisposable BeginActivity(string actionName)
    {
        lock (_lock)
        {
            _activeCount++;
            CurrentAction = actionName;
            _stopwatch = Stopwatch.StartNew();
        }
        OnActivityChanged?.Invoke(true, actionName);
        return new ActivityScope(this);
    }

    public void UpdateAction(string actionName)
    {
        lock (_lock)
        {
            CurrentAction = actionName;
        }
        OnActivityChanged?.Invoke(true, actionName);
    }

    public void CompleteActivity(string? completedMessage = null)
    {
        double elapsedSeconds = 0;
        bool wasLast = false;
        lock (_lock)
        {
            if (_activeCount > 0) _activeCount--;
            wasLast = _activeCount == 0;
            if (_stopwatch != null)
            {
                _stopwatch.Stop();
                elapsedSeconds = Math.Max(0.1, Math.Round(_stopwatch.Elapsed.TotalSeconds, 1));
            }
        }

        string msg = completedMessage ?? $"Completed in {elapsedSeconds:0.1}s";
        OnActivityChanged?.Invoke(!wasLast, CurrentAction);
        if (wasLast)
        {
            OnActivityCompleted?.Invoke(msg);
        }
    }

    private sealed class ActivityScope : IDisposable
    {
        private readonly ActivityTracker _tracker;
        private int _disposed;

        public ActivityScope(ActivityTracker tracker) => _tracker = tracker;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _tracker.CompleteActivity();
            }
        }
    }
}
