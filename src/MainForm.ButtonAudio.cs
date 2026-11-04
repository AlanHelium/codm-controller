namespace CODMControllerProbe;

public partial class MainForm
{
    private bool _audioCursorToggle = true;
    private int[] _audioTriggerButtons = Array.Empty<int>();
    private int _audioPreviousButtons, _audioEpoch, _audioQueued;
    private bool _audioArmed, _buttonAudioThisFrame;
    private long _lastButtonAudioAt;

    private void LoadButtonAudio(AppSettings settings)
    {
        _audioCursorToggle = settings.AudioCursorToggle;
        _audioTriggerButtons = (settings.AudioTriggerButtons ?? new()).Where(SettingsStore.IsValidBind).Distinct().ToArray();
        ResetButtonAudio();
    }

    private void ResetButtonAudio()
    {
        _audioArmed = false;
        _buttonAudioThisFrame = false;
        _audioPreviousButtons = 0;
        _audioEpoch++;
    }

    private void TrackButtonAudio(XInputNative.XInputGamepad raw)
    {
        _buttonAudioThisFrame = false;
        int pressed = MappingPolicy.Pressed(raw);
        if (_audioTriggerButtons.Length == 0 || _voiceMode == VoicePlayer.Off || !IsGameKeyTargetForeground(_gameKeys.CurrentTarget))
        { _audioArmed = false; _audioPreviousButtons = pressed; return; }
        if (!_audioArmed) { _audioPreviousButtons = pressed; _audioArmed = true; return; }
        int rising = pressed & ~_audioPreviousButtons;
        _audioPreviousButtons = pressed;
        if (!_audioTriggerButtons.Any(source => (rising & source) != 0)) return;
        _buttonAudioThisFrame = true;
        // Coalesce simultaneous presses and hand playback to the UI thread. The
        // controller loop never reads audio files or initializes the speech engine.
        _lastButtonAudioAt = Environment.TickCount64;
        if (Interlocked.CompareExchange(ref _audioQueued, 1, 0) != 0) return;
        int epoch = _audioEpoch;
        try
        {
            BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _audioQueued, 0);
                if (epoch != _audioEpoch || _loopStop || !_physicalSessionReady
                    || _mappingCaptureActive || _mappingConflictActive
                    || !IsGameKeyTargetForeground(_gameKeys.CurrentTarget)) return;
                VoicePlayer.Play(_voiceMode);
            });
        }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _audioQueued, 0); }
    }
}
