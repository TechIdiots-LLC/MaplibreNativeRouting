using System.Diagnostics;
using MaplibreNative.Routing.Core.Navigation;

namespace MaplibreNative.Routing;

/// <summary>
/// Speaks navigation's instructions with the platform's text-to-speech (MAUI's TextToSpeech): the way to start,
/// each turn as it nears, a new route, the arrival. A new instruction waits for the one being spoken to finish (it
/// cut it off, mid-word); of several waiting, only the newest is said, so it never falls behind. Off until
/// <see cref="IsEnabled"/> is set.
/// </summary>
public sealed class VoiceGuidance
{
    private readonly object _gate = new();
    private CancellationTokenSource? _speaking;
    private string? _next;      // waiting for what's being said to finish
    private bool _busy;         // a speaking loop is running

    public VoiceGuidance(NavigationEngine engine)
    {
        engine.VoiceInstruction += (_, text) => Say(text);
    }

    /// <summary>Speak the instructions; turning it off stops one being spoken.</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) Stop();
        }
    }
    private bool _enabled;

    /// <summary>The voice's language, as a locale from TextToSpeech.GetLocalesAsync(); the device's by default.</summary>
    public Locale? Locale { get; set; }

    /// <summary>0 to 1; the device's volume by default.</summary>
    public float? Volume { get; set; }

    /// <summary>The last instruction, spoken or not: for a button that repeats it.</summary>
    public string? LastInstruction { get; private set; }

    /// <summary>Says something once what's being said has finished (in place of anything else waiting).</summary>
    public void Say(string text)
    {
        LastInstruction = text;
        if (!_enabled || string.IsNullOrWhiteSpace(text)) return;
        lock (_gate)
        {
            _next = text;
            if (_busy) return;
            _busy = true;
        }
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            while (true)
            {
                string? say;
                CancellationTokenSource cts;
                lock (_gate)
                {
                    say = _next;
                    _next = null;
                    if (say is null) { _busy = false; return; }
                    _speaking = cts = new CancellationTokenSource();
                }
                try
                {
                    await TextToSpeech.Default.SpeakAsync(say, new SpeechOptions { Locale = Locale, Volume = Volume }, cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Debug.WriteLine($"[VoiceGuidance] {ex.Message}"); }
            }
        });
    }

    /// <summary>Stops what's being said, and what's waiting.</summary>
    public void Stop()
    {
        CancellationTokenSource? speaking;
        lock (_gate)
        {
            _next = null;
            speaking = _speaking;
            _speaking = null;
        }
        speaking?.Cancel();
    }
}
