# Audio visualization and transition design

The earlier visualizer had twelve overlapping, octave-wide bandpass measurements,
46 ms analysis blocks, a 50 ms input polling delay, and additional frame-based
smoothing. Broadband music could keep almost every displayed band high, while
small geometric movements made transients difficult to see. Analysis also ran
ahead of the hardware audio buffer rather than measuring the audible mix.

The revised primary path uses Windows process-loopback capture, scoped to Musik's
process tree. This excludes unrelated applications and naturally includes both
songs during a crossfade. The capture format is 48 kHz floating-point stereo;
floating point preserves quiet output without 16-bit quantization artifacts.
Capture is optional: unsupported systems or activation failures use the existing
mpv filter-bank path, and audio playback does not depend on capture availability.

The analyzer uses Hann windows of 4096 samples for bass and 2048 for other bands,
with a 512-sample hop: 93.75 updates per second. Forty-eight logarithmic bands
cover 45 Hz–18 kHz. Fractional bin overlap helps the lower bands; stereo power is
combined after separate FFTs, avoiding cancellation of opposite-phase channels.
Short windows respond quickly; bass needs a longer window to distinguish nearby
frequencies. These window sizes impose a real frequency/time tradeoff; this is
not a claim of zero audio-to-screen latency.

The level mapping reserves 10 dB of peak headroom and uses a 42 dB display range,
with a nonlinear curve that expands visible differences between moderate and
strong bands. Sensitivity recovers slowly during quieter passages and freezes
during digital silence, rather than pushing every quiet frame back to the top.
The renderer uses time-based 8 ms attack / 85 ms release, targets a 16 ms frame
interval, and lets the playback chrome refresh separately. After 120 ms without
fresh capture frames, the spectrum returns to silence instead of sticking.
Cat contours have stronger local movement and radial strokes; halo styles add
audio-driven bass movement. No random motion is used as a substitute for audio.

Fades are installed once as FFmpeg `afade` quarter-sine envelopes. FFmpeg computes
gain per audio sample, while the application only manages voice lifetime and
progress. Manual Next/Previous/selection have no incoming fade-in: the new voice starts
at the selected volume while the old voice uses the chosen outgoing duration. Automatic queue changes retain matching
equal-power envelopes. The previous voice is kept alive until the replacement
starts; a not-yet-loaded network source can still delay the requested change.
The player requests a 60 ms audio buffer, balancing control response with room
for normal device scheduling; the device can choose a larger buffer.

## Primary references

- [Microsoft process-loopback sample](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/ApplicationLoopback)
  and [activation requirements](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_activation_params)
  establish process-scoped capture and the Windows build requirement.
- [CAVA core](https://github.com/karlstav/cava/blob/master/cavacore.c) provides a
  reference for Hann windows, logarithmic bands, and separate bass resolution.
  This implementation uses its own managed FFT and level mapping.
- [mpv audio buffer documentation](https://mpv.io/manual/stable/#options-audio-buffer)
  describes buffering and responsiveness tradeoffs.
- [FFmpeg afade documentation](https://ffmpeg.org/ffmpeg-filters.html#afade)
  documents the audio-sample fade envelopes and curves.

## Verification

The regression suite checks tone placement from bass through treble, opposite-
phase stereo, 100 ms transient response, display headroom, quieter passages,
silence, and panel bounds. The optional `--audio-probe` additionally checks actual
WASAPI capture, simultaneous old/new track energy 220 ms after a manual change,
a slower incoming automatic fade, pause, trimming, seek, and cleanup.

## Faster selection and mirrored Aurora Wave

Two bounded preload slots keep the queued next song and the highlighted song
independent. Highlight changes are debounced by 180 ms to avoid launching a
player for every arrow-key repeat; Enter bypasses the debounce. Preloads prepare
the decoder and, when available, the audio output before playback is requested.
The next queue entry begins preloading immediately after a song starts. Unused
preload process cleanup is scheduled off the playback switch path, so killing an
unrelated process cannot delay unpausing the requested song. An uncached online
track still needs source resolution and enough received audio to play.

Aurora Wave uses the same live FFT data to draw a spectral curtain mirrored above
and below the centerline. Pixel-level symmetry, small-panel bounds, persistence,
and selection/queue preload reuse are covered by regression checks. The audio
probe times prepared manual switches against the first detected incoming tone.
