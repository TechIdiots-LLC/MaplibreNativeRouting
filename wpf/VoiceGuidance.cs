using System.Diagnostics;
using MaplibreNative.Routing.Core.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace MaplibreNative.Routing.Wpf;

/// <summary>
/// Speaks navigation's instructions with Windows' own speech (Windows.Media.SpeechSynthesis): the way to start,
/// each turn as it nears, a new route, the arrival. A new instruction cuts off one still being spoken. Off until
/// <see cref="IsEnabled"/> is set.
/// </summary>
public sealed class VoiceGuidance : IDisposable
{
    private SpeechSynthesizer? _synth;
    private MediaPlayer? _player;
    private int _generation;

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

    /// <summary>Says something now, cutting off anything being said.</summary>
    public async void Say(string text)
    {
        LastInstruction = text;
        if (!_enabled || string.IsNullOrWhiteSpace(text)) return;
        int mine = Interlocked.Increment(ref _generation);
        try
        {
            _synth ??= new SpeechSynthesizer();
            if (Voice is not null) _synth.Voice = Voice;
            var stream = await _synth.SynthesizeTextToStreamAsync(text);
            if (mine != Volatile.Read(ref _generation)) return;   // something newer to say
            _player ??= new MediaPlayer();
            _player.Pause();
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceGuidance] {ex.Message}");
        }
    }

    /// <summary>Stops what's being said.</summary>
    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        _player?.Pause();
    }

    public void Dispose()
    {
        _player?.Dispose();
        _synth?.Dispose();
    }
}
