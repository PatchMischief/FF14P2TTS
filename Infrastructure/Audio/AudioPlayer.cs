using System;
using System.IO;
using System.Media;
using System.Text;
using NAudio.Wave;

namespace FF14P2TTS.Infrastructure.Audio;

/// <summary>Shared audio decoding and playback used by the TTS providers.</summary>
public static class AudioPlayer
{
    /// <summary>Decodes the given audio (WAV, MP3, or raw PCM) and plays it synchronously.</summary>
    public static void PlayAudio(
        byte[] audio,
        string tag,
        int sampleRate,
        short channels,
        short bitsPerSample,
        double volumeFactor = 1.0)
    {
        if (audio.Length < 4)
            return;

        var wav = IsWav(audio)
            ? audio
            : IsMp3(audio)
                ? DecodeMp3ToWav(audio)
                : WrapPcmAsWav(audio, sampleRate, channels, bitsPerSample);

        if (Math.Abs(volumeFactor - 1.0) >= 0.001)
            wav = ScaleWavVolume(wav, volumeFactor);

        var path = Path.Combine(Path.GetTempPath(), $"ff14tts_{tag}_{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(path, wav);
            using var player = new SoundPlayer(path);
            player.PlaySync();
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>Wraps raw little-endian PCM samples in a standard 44-byte WAV header.</summary>
    public static byte[] WrapPcmAsWav(byte[] pcm, int sampleRate, short channels, short bitsPerSample)
    {
        const short audioFormatPcm = 1;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;

        using var stream = new MemoryStream(44 + pcm.Length);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write(audioFormatPcm);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Scales the 16-bit PCM samples inside a WAV buffer by a 0.0-2.0 multiplier.</summary>
    public static byte[] ScaleWavVolume(byte[] wav, double factor)
    {
        var result = new byte[wav.Length];
        Buffer.BlockCopy(wav, 0, result, 0, wav.Length);

        var dataOffset = FindWavDataOffset(wav);
        if (dataOffset < 44)
            return result;

        for (var i = dataOffset; i < result.Length - 1; i += 2)
        {
            var sample = (short)(result[i] | (result[i + 1] << 8));
            var scaled = (int)(sample * factor);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            result[i] = (byte)(scaled & 0xFF);
            result[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        return result;
    }

    private static int FindWavDataOffset(byte[] wav)
    {
        for (var i = 0; i < wav.Length - 8; i++)
        {
            if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
                return i + 8;
        }
        return 44;
    }

    private static byte[] DecodeMp3ToWav(byte[] mp3)
    {
        using var input = new MemoryStream(mp3);
        using var reader = new Mp3FileReader(input);
        using var output = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(output, reader);
        return output.ToArray();
    }

    private static bool IsMp3(byte[] audio) =>
        (audio.Length > 3 && audio[0] == 'I' && audio[1] == 'D' && audio[2] == '3')
        || (audio.Length > 1 && audio[0] == 0xFF && (audio[1] & 0xE0) == 0xE0);

    private static bool IsWav(byte[] audio) =>
        audio.Length >= 12
        && audio[0] == 'R' && audio[1] == 'I' && audio[2] == 'F' && audio[3] == 'F'
        && audio[8] == 'W' && audio[9] == 'A' && audio[10] == 'V' && audio[11] == 'E';
}
