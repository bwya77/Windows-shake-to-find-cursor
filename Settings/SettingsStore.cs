using System.IO;
using System.Text.Json;
using System.Threading;

namespace ShakeToBigCursor.Settings;

/// <summary>
/// Loads and persists app settings, then signals other processes so the tray app
/// and WinUI 3 settings app stay in sync without a restart.
/// </summary>
public sealed class SettingsStore : IDisposable
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsShakeToFindCursor");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private const string SignalName = @"Local\WindowsShakeToFindCursor_Settings_Changed_v1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public AppSettings Current { get; private set; } = new();

    public event Action<AppSettings>? Changed;

    private readonly FileSystemWatcher? watcher;
    private readonly EventWaitHandle? signal;
    private readonly Thread? signalThread;
    private readonly object gate = new();
    private string lastJson = "";
    private volatile bool disposed;

    public SettingsStore()
    {
        Load();

        try
        {
            Directory.CreateDirectory(Dir);
            watcher = new FileSystemWatcher(Dir, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Renamed += OnFileEvent;
        }
        catch
        {
            watcher = null;
        }

        try
        {
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            signalThread = new Thread(SignalLoop)
            {
                IsBackground = true,
                Name = "WindowsShakeToFindCursorSettingsSignal",
            };
            signalThread.Start();
        }
        catch
        {
            signal = null;
        }
    }

    private void Load()
    {
        try
        {
            var json = ReadAllTextShared(FilePath);
            if (json != null)
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (loaded != null)
                {
                    loaded.Normalize();
                    Current = loaded;
                    lastJson = JsonSerializer.Serialize(loaded, JsonOpts);
                    return;
                }
            }
        }
        catch
        {
            // Fall through to defaults.
        }

        Current = new AppSettings();
        Save(Current);
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Current = settings;
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        lock (gate)
        {
            lastJson = json;
        }

        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath))
            {
                File.Replace(tmp, FilePath, null);
            }
            else
            {
                File.Move(tmp, FilePath);
            }
        }
        catch
        {
            // Non-fatal: settings just won't persist this time.
        }

        Changed?.Invoke(settings);

        try
        {
            signal?.Set();
            signal?.Set();
        }
        catch
        {
            // Signaling is best-effort.
        }
    }

    private void SignalLoop()
    {
        while (!disposed && signal != null)
        {
            try
            {
                if (signal.WaitOne(1000) && !disposed)
                {
                    ReloadIfChanged();
                }
            }
            catch
            {
                return;
            }
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => ReloadIfChanged();

    private void ReloadIfChanged()
    {
        string? json;
        try
        {
            json = ReadAllTextShared(FilePath);
        }
        catch
        {
            return;
        }

        if (json == null)
        {
            return;
        }

        AppSettings? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
        }
        catch
        {
            return;
        }

        if (loaded == null)
        {
            return;
        }

        loaded.Normalize();
        var normalizedJson = JsonSerializer.Serialize(loaded, JsonOpts);
        lock (gate)
        {
            if (normalizedJson == lastJson)
            {
                return;
            }

            lastJson = normalizedJson;
        }

        Current = loaded;
        Changed?.Invoke(loaded);
    }

    private static string? ReadAllTextShared(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    public void Dispose()
    {
        disposed = true;

        if (watcher != null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileEvent;
            watcher.Created -= OnFileEvent;
            watcher.Renamed -= OnFileEvent;
            watcher.Dispose();
        }

        if (signal != null)
        {
            try
            {
                signal.Set();
            }
            catch
            {
                // Wake-up is best-effort during shutdown.
            }

            signalThread?.Join(500);
            signal.Dispose();
        }
    }
}
