using System;
using System.Threading;
using System.Windows.Threading;

namespace GroundControl;

/// <summary>
/// One-way nudge from a second, short-lived instance to the running one: "the user asked for
/// the settings window". A named auto-reset event is enough — there is no payload to pass —
/// and it works regardless of window visibility, unlike a broadcast window message.
/// </summary>
public sealed class InstanceSignal : IDisposable
{
    private const string EventName = @"Local\GroundControl.ShowSettings";

    private readonly EventWaitHandle _handle;
    private readonly RegisteredWaitHandle _registration;

    /// <summary>Starts listening. <paramref name="onSignal"/> runs on <paramref name="dispatcher"/>'s thread.</summary>
    public InstanceSignal(Dispatcher dispatcher, Action onSignal)
    {
        _handle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _handle,
            (_, _) => dispatcher.BeginInvoke(onSignal),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Signals the running instance. False if there is nobody listening.</summary>
    public static bool Send()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(EventName, out var handle)) return false;
            using (handle) return handle.Set();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _handle.Dispose();
    }
}
