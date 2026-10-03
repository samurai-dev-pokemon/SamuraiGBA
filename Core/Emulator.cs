using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SamuraiGBA.Core;

/// <summary>
/// Hosts the mGBA libretro core (the same C API RetroArch uses). Everything that touches the core runs on one
/// dedicated emulation thread, as libretro requires.
/// </summary>
public sealed unsafe class Emulator
{
    // ---- libretro ABI ----------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)] internal struct GameInfo { public IntPtr Path; public IntPtr Data; public UIntPtr Size; public IntPtr Meta; }
    [StructLayout(LayoutKind.Sequential)] internal struct AvInfo { public uint BaseW, BaseH, MaxW, MaxH; public float Aspect; public double Fps, SampleRate; }

    const uint ENV_GET_SYSTEM_DIRECTORY = 9, ENV_SET_PIXEL_FORMAT = 10, ENV_GET_VARIABLE = 15,
               ENV_GET_VARIABLE_UPDATE = 17, ENV_GET_SAVE_DIRECTORY = 31;
    const uint MEMORY_SAVE_RAM = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] internal delegate bool EnvCb(uint cmd, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void VideoCb(IntPtr data, uint w, uint h, UIntPtr pitch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void AudioCb(short l, short r);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate UIntPtr AudioBatchCb(IntPtr data, UIntPtr frames);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void PollCb();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate short InputCb(uint port, uint device, uint index, uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void VoidFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetEnvFn(EnvCb cb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetVideoFn(VideoCb cb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetAudioFn(AudioCb cb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetAudioBatchFn(AudioBatchCb cb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetPollFn(PollCb cb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetInputFn(InputCb cb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] internal delegate bool LoadFn(ref GameInfo g);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void AvFn(out AvInfo a);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate UIntPtr SizeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] internal delegate bool SerFn(IntPtr d, UIntPtr n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr MemFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate UIntPtr MemSizeFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void CheatSetFn(uint index, [MarshalAs(UnmanagedType.U1)] bool enabled, [MarshalAs(UnmanagedType.LPStr)] string code);

    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    VoidFn _init = null!, _deinit = null!, _run = null!, _reset = null!, _unload = null!, _cheatReset = null!;
    SizeFn _serSize = null!;
    SerFn _ser = null!, _unser = null!;
    MemFn _mem = null!;
    MemSizeFn _memSize = null!;
    CheatSetFn _cheatSet = null!;
    LoadFn _load = null!;
    AvFn _av = null!;

    // keep callback delegates alive for the lifetime of the object
    readonly EnvCb envCb; readonly VideoCb videoCb; readonly AudioCb audioCb;
    readonly AudioBatchCb audioBatchCb; readonly PollCb pollCb; readonly InputCb inputCb;

    // ---- public surface --------------------------------------------------------------------------------------
    public event Action? Stopped;
    public event Action<string>? Message;
    public readonly object FrameLock = new();
    public int[] Frame = new int[240 * 160];
    public int W = 240, H = 160;
    public bool FrameReady;
    public volatile bool Running, Paused;
    public volatile float Speed = 1f;
    public List<string> Cheats = new();
    public float Volume = 0.8f;
    public bool Mute;

    // ---- internals -------------------------------------------------------------------------------------------
    readonly ConcurrentQueue<Action> queue = new();
    Thread? thread;
    volatile bool stop;
    int pixFmt; // 0 = 0RGB1555, 1 = XRGB8888, 2 = RGB565
    IntPtr dirPtr;
    AudioOut? audio;
    byte[]? lastSram;
    string savePath = "", stateBase = "";

    public Emulator()
    {
        envCb = OnEnv; videoCb = OnVideo; audioCb = OnAudio;
        audioBatchCb = OnAudioBatch; pollCb = () => InputState.Poll(); inputCb = OnInput;
    }

    public void Start(string corePath, string romPath)
    {
        Stop();
        stop = false; Paused = false; Speed = 1f;
        string name = Path.GetFileNameWithoutExtension(romPath);
        savePath = Path.Combine(AppPaths.Saves, name + ".sav");
        stateBase = Path.Combine(AppPaths.States, name);
        thread = new Thread(() => Loop(corePath, romPath)) { IsBackground = true, Name = "Samurai-Emu", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    public void Stop()
    {
        stop = true;
        thread?.Join(4000);
        thread = null;
    }

    public void Reset() => queue.Enqueue(() => { _reset(); Message?.Invoke("Reset"); });

    public void SetCheats(IEnumerable<string> codes)
    {
        var list = codes.ToList();
        queue.Enqueue(() => ApplyCheats(list));
    }

    public void SaveState(int slot) => queue.Enqueue(() =>
    {
        int n = (int)(ulong)_serSize();
        var b = new byte[n];
        bool ok;
        fixed (byte* p = b) ok = _ser((IntPtr)p, (UIntPtr)(ulong)n);
        if (ok) { File.WriteAllBytes($"{stateBase}.state{slot}", b); Message?.Invoke($"State saved (slot {slot})"); }
        else Message?.Invoke("Save state failed");
    });

    public void LoadState(int slot) => queue.Enqueue(() =>
    {
        var f = $"{stateBase}.state{slot}";
        if (!File.Exists(f)) { Message?.Invoke($"No state in slot {slot}"); return; }
        var b = File.ReadAllBytes(f);
        bool ok;
        fixed (byte* p = b) ok = _unser((IntPtr)p, (UIntPtr)(ulong)b.Length);
        Message?.Invoke(ok ? $"State loaded (slot {slot})" : "Load state failed");
    });

    public void ApplyVolume() { if (audio != null) { audio.Volume = Mute ? 0 : Volume; } }

    // ---- thread ----------------------------------------------------------------------------------------------
    void Bind(IntPtr lib)
    {
        T Fn<T>(string n) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, n));
        _init = Fn<VoidFn>("retro_init"); _deinit = Fn<VoidFn>("retro_deinit");
        _run = Fn<VoidFn>("retro_run"); _reset = Fn<VoidFn>("retro_reset");
        _unload = Fn<VoidFn>("retro_unload_game"); _cheatReset = Fn<VoidFn>("retro_cheat_reset");
        _load = Fn<LoadFn>("retro_load_game"); _av = Fn<AvFn>("retro_get_system_av_info");
        _serSize = Fn<SizeFn>("retro_serialize_size");
        _ser = Fn<SerFn>("retro_serialize"); _unser = Fn<SerFn>("retro_unserialize");
        _mem = Fn<MemFn>("retro_get_memory_data"); _memSize = Fn<MemSizeFn>("retro_get_memory_size");
        _cheatSet = Fn<CheatSetFn>("retro_cheat_set");
        Fn<SetEnvFn>("retro_set_environment")(envCb);
        Fn<SetVideoFn>("retro_set_video_refresh")(videoCb);
        Fn<SetAudioFn>("retro_set_audio_sample")(audioCb);
        Fn<SetAudioBatchFn>("retro_set_audio_sample_batch")(audioBatchCb);
        Fn<SetPollFn>("retro_set_input_poll")(pollCb);
        Fn<SetInputFn>("retro_set_input_state")(inputCb);
    }

    void Loop(string corePath, string romPath)
    {
        IntPtr lib = IntPtr.Zero; GCHandle romH = default; IntPtr pathPtr = IntPtr.Zero;
        bool inited = false, loaded = false;
        timeBeginPeriod(1);
        try
        {
            dirPtr = Marshal.StringToHGlobalAnsi(AppPaths.Root);
            lib = NativeLibrary.Load(corePath);
            Bind(lib);
            _init(); inited = true;

            var rom = File.ReadAllBytes(romPath);
            romH = GCHandle.Alloc(rom, GCHandleType.Pinned);
            pathPtr = Marshal.StringToHGlobalAnsi(romPath);
            var gi = new GameInfo { Path = pathPtr, Data = romH.AddrOfPinnedObject(), Size = (UIntPtr)(ulong)rom.Length, Meta = IntPtr.Zero };
            if (!_load(ref gi)) throw new InvalidOperationException("The core could not load this ROM.");
            loaded = true;

            _av(out var av);
            double fps = av.Fps > 1 ? av.Fps : 59.7275;
            audio = new AudioOut((int)Math.Round(av.SampleRate > 1 ? av.SampleRate : 32768), Mute ? 0 : Volume);

            LoadSram();
            ApplyCheats(Cheats);
            Running = true;

            var sw = Stopwatch.StartNew();
            double next = 0; int frames = 0;
            while (!stop)
            {
                while (queue.TryDequeue(out var act))
                {
                    try { act(); } catch (Exception ex) { Message?.Invoke(ex.Message); }
                }
                if (Paused) { Thread.Sleep(15); next = sw.Elapsed.TotalMilliseconds; continue; }

                audio.Silent = Speed > 1.01f;
                _run();
                if (++frames % 300 == 0) FlushSram(false);

                next += 1000.0 / fps / Math.Max(0.25f, Speed);
                double now = sw.Elapsed.TotalMilliseconds;
                if (now - next > 100) next = now;       // fell behind; don't try to catch up
                double wait = next - now;
                if (wait > 2) Thread.Sleep((int)(wait - 1));
                while (sw.Elapsed.TotalMilliseconds < next) Thread.SpinWait(20);
            }
        }
        catch (Exception ex)
        {
            Message?.Invoke("Emulator error: " + ex.Message);
        }
        finally
        {
            Running = false;
            try { if (loaded) { FlushSram(true); _unload(); } } catch { }
            try { if (inited) _deinit(); } catch { }
            audio?.Dispose(); audio = null;
            if (romH.IsAllocated) romH.Free();
            if (pathPtr != IntPtr.Zero) Marshal.FreeHGlobal(pathPtr);
            if (dirPtr != IntPtr.Zero) { Marshal.FreeHGlobal(dirPtr); dirPtr = IntPtr.Zero; }
            try { if (lib != IntPtr.Zero) NativeLibrary.Free(lib); } catch { }
            timeEndPeriod(1);
            Stopped?.Invoke();
        }
    }

    void ApplyCheats(List<string> codes)
    {
        _cheatReset();
        uint i = 0;
        foreach (var raw in codes)
        {
            var lines = raw.Split('\n', '\r', '+').Select(l => l.Trim().ToUpperInvariant()).Where(l => l.Length > 0);
            var joined = string.Join("+", lines);
            if (joined.Length > 0) _cheatSet(i++, true, joined);
        }
    }

    void LoadSram()
    {
        int size = (int)(ulong)_memSize(MEMORY_SAVE_RAM);
        var ptr = _mem(MEMORY_SAVE_RAM);
        if (size <= 0 || ptr == IntPtr.Zero || !File.Exists(savePath)) return;
        var data = File.ReadAllBytes(savePath);
        Marshal.Copy(data, 0, ptr, Math.Min(size, data.Length));
        lastSram = data;
    }

    void FlushSram(bool force)
    {
        int size = (int)(ulong)_memSize(MEMORY_SAVE_RAM);
        var ptr = _mem(MEMORY_SAVE_RAM);
        if (size <= 0 || ptr == IntPtr.Zero) return;
        var cur = new byte[size];
        Marshal.Copy(ptr, cur, 0, size);
        if (!force && lastSram != null && cur.AsSpan().SequenceEqual(lastSram)) return;
        try { File.WriteAllBytes(savePath, cur); lastSram = cur; } catch { }
    }

    // ---- libretro callbacks ----------------------------------------------------------------------------------
    bool OnEnv(uint cmd, IntPtr data)
    {
        switch (cmd & 0xFFFF)
        {
            case ENV_SET_PIXEL_FORMAT:
                pixFmt = Marshal.ReadInt32(data);
                return pixFmt is 0 or 1 or 2;
            case ENV_GET_SYSTEM_DIRECTORY:
            case ENV_GET_SAVE_DIRECTORY:
                Marshal.WriteIntPtr(data, dirPtr);
                return true;
            case ENV_GET_VARIABLE_UPDATE:
                Marshal.WriteByte(data, 0);
                return true;
            case ENV_GET_VARIABLE:
                return false; // use the core's built-in defaults
            default:
                return false;
        }
    }

    void OnVideo(IntPtr data, uint w, uint h, UIntPtr pitch)
    {
        if (data == IntPtr.Zero || w == 0 || h == 0) return;
        int iw = (int)w, ih = (int)h, p = (int)(ulong)pitch;
        lock (FrameLock)
        {
            if (Frame.Length != iw * ih) Frame = new int[iw * ih];
            W = iw; H = ih;
            byte* src = (byte*)data;
            fixed (int* dst = Frame)
            {
                for (int y = 0; y < ih; y++)
                {
                    int* d = dst + y * iw;
                    byte* row = src + (long)y * p;
                    if (pixFmt == 1) Buffer.MemoryCopy(row, d, iw * 4, iw * 4);
                    else
                    {
                        ushort* s = (ushort*)row;
                        for (int x = 0; x < iw; x++)
                        {
                            int v = s[x], r, g, b;
                            if (pixFmt == 2) { r = (v >> 11) & 31; g = (v >> 5) & 63; b = v & 31; r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2); }
                            else { r = (v >> 10) & 31; g = (v >> 5) & 31; b = v & 31; r = (r << 3) | (r >> 2); g = (g << 3) | (g >> 2); b = (b << 3) | (b >> 2); }
                            d[x] = (r << 16) | (g << 8) | b;
                        }
                    }
                }
            }
            FrameReady = true;
        }
    }

    void OnAudio(short l, short r)
    {
        short* tmp = stackalloc short[2];
        tmp[0] = l; tmp[1] = r;
        audio?.Write((IntPtr)tmp, 1);
    }

    UIntPtr OnAudioBatch(IntPtr data, UIntPtr frames)
    {
        audio?.Write(data, (int)(ulong)frames);
        return frames;
    }

    short OnInput(uint port, uint device, uint index, uint id)
        => (port == 0 && device == 1 && InputState.IsDown(id)) ? (short)1 : (short)0;
}
