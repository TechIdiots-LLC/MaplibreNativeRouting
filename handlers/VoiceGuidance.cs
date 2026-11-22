using System.Diagnostics;
using MaplibreNative.Routing.Core.Navigation;

namespace MaplibreNative.Routing;

/// <summary>
/// Speaks navigation's instructions with the platform's text-to-speech (MAUI's TextToSpeech): the way to start,
/// each turn as it nears, a new route, the arrival. A new instruction cuts off one still being spoken, so the
/// nearest turn is always the one heard. Off until <see cref="IsEnabled"/> is set.
/// </summary>
public sealed class VoiceGuidance
{
    private CancellationTokenSource? _speaking;

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

    /// <summary>Says something now, cutting off anything being said.</summary>
    public void Say(string text)
    {
        LastInstruction = text;
        if (!_enabled || string.IsNullOrWhiteSpace(text)) return;
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _speaking, cts)?.Cancel();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions { Locale = Locale, Volume = Volume }, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[VoiceGuidance] {ex.Message}"); }
        });
    }

    /// <summary>Stops what's being said.</summary>
    public void Stop() => Interlocked.Exchange(ref _speaking, null)?.Cancel();
}
