using System.Runtime.InteropServices;
using NAudio.Wave;

namespace SamuraiGBA.Core;

public sealed class AudioOut : IDisposable
{
    readonly WaveOutEvent output;
    readonly BufferedWaveProvider provider;
    byte[] buf = new byte[1 << 16];
    public bool Silent;

    public AudioOut(int sampleRate, float volume)
    {
        provider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 2))
        {
            BufferDuration = TimeSpan.FromMilliseconds(300),
            DiscardOnBufferOverflow = true,
        };
        output = new WaveOutEvent { DesiredLatency = 90, Volume = volume };
        output.Init(provider);
        output.Play();
    }

    public float Volume { set => output.Volume = Math.Clamp(value, 0f, 1f); }

    public void Write(IntPtr samples, int frames)
    {
        if (Silent || frames <= 0) return;
        int bytes = frames * 4;
        if (buf.Length < bytes) buf = new byte[bytes];
        Marshal.Copy(samples, buf, 0, bytes);
        provider.AddSamples(buf, 0, bytes);
    }

    public void Dispose()
    {
        try { output.Stop(); output.Dispose(); } catch { }
    }
}
