using DailySummary.Core.Abstractions;

namespace DailySummary.Providers.Transcription;

/// <summary>
/// Local, keyless speech-to-text via Whisper.net (whisper.cpp). Scaffolded stub — a real build
/// loads a ggml model, decodes the audio to 16 kHz mono WAV (ffmpeg), and runs the processor.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    public Task<string> TranscribeAsync(Stream audio, CancellationToken ct) =>
        throw new NotImplementedException(
            "WhisperTranscriber is a scaffold stub. Wire up Whisper.net (ggml model) + ffmpeg decode. See SPEC.md §6.");
}
