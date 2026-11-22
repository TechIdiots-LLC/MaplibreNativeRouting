using System.Diagnostics;
using MaplibreNative.Routing.Core.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace MaplibreNative.Routing.Wpf;

/// <summary>
/// Speaks navigation's instructions with Windows' own speech (Windows.Media.SpeechSynthesis): the way to start,
/// each turn as it nears, a new route, the arrival. A new instruction waits for the one being spoken to finish; of
/// several waiting, only the newest is said. Off until <see cref="IsEnabled"/> is set.
/// </summary>
public sealed class VoiceGuidance : IDisposable
{
    private SpeechSynthesizer? _synth;
    private MediaPlayer? _player;
    private int _generation;
    private string? _next;      // waiting for what's being said to finish (UI thread)
    private bool _playing;

    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

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

    /// <summary>One of SpeechSynthesizer.AllVoices; Windows' default voice otherwise.</summary>
    public VoiceInformation? Voice { get; set; }

    /// <summary>The last instruction, spoken or not: for a button that repeats it.</summary>
    public string? LastInstruction { get; private set; }

    /// <summary>Says something once what's being said has finished (in place of anything else waiting).</summary>
    public void Say(string text)
    {
        LastInstruction = text;
        if (!_enabled || string.IsNullOrWhiteSpace(text)) return;
        // The engine says things from its own thread; what is waiting is the UI thread's
        if (_ui is { } ui && SynchronizationContext.Current != ui) { ui.Post(_ => Say(text), null); return; }
        if (_playing) { _next = text; return; }
        Speak(text);
    }

    private async void Speak(string text)
    {
        _playing = true;
        int mine = Interlocked.Increment(ref _generation);
        try
        {
            _synth ??= new SpeechSynthesizer();
            if (Voice is not null) _synth.Voice = Voice;
            var stream = await _synth.SynthesizeTextToStreamAsync(text);
            if (mine != Volatile.Read(ref _generation)) return;   // stopped meanwhile
            if (_player is null)
            {
                _player = new MediaPlayer();
                _player.MediaEnded += (_, _) => OnFinished();
                _player.MediaFailed += (_, _) => OnFinished();
            }
            _player.Pause();
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceGuidance] {ex.Message}");
            OnFinished();
        }
    }

    /// <summary>What was being said has ended: the newest waiting instruction, if any.</summary>
    private void OnFinished()
    {
        void Next()
        {
            _playing = false;
            if (_next is { } next && _enabled) { _next = null; Speak(next); }
        }
        if (_ui is { } ui && SynchronizationContext.Current != ui) ui.Post(_ => Next(), null);
        else Next();
    }

    /// <summary>Stops what's being said.</summary>
    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        _next = null;
        _playing = false;
        _player?.Pause();
    }

    public void Dispose()
    {
        _player?.Dispose();
        _synth?.Dispose();
    }
}
