using System.IO;
using MonSelect.Core.Engine;
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Windows;

namespace MonSelect.App;

/// <summary>Arma el grafo de objetos y mantiene la config sincronizada con el disco.</summary>
public sealed class Bootstrap : IDisposable
{
    private readonly Win32MonitorSystem _monitorSystem = new();
    private readonly Win32WindowSystem _windowSystem = new();
    private readonly WindowWatcher _watcher = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ApplyLogFile _file = new();

    private FileSystemWatcher? _configWatcher;
    private CancellationTokenSource? _reloadDebounce;

    public RuleEngine Engine { get; }
    public ApplyLog Log { get; } = new();
    public string? LastConfigError { get; private set; }

    public event Action? ConfigChanged;

    public Bootstrap()
    {
        var styles = new StyleStore(ConfigPaths.Borderless);
        styles.Load();

        Engine = new RuleEngine(
            new WindowProbe(_windowSystem),
            new MonitorRegistry(_monitorSystem),
            new WindowPlacer(_windowSystem, styles),
            new RetryScheduler(_windowSystem, new RealDelay()),
            Log);
    }

    public void Start()
    {
        EnsureConfigExists();
        ReloadConfig();
        WatchConfig();
        _file.Prune();

        _watcher.WindowAppeared += hwnd => _ = HandleAndLogAsync(hwnd);

        _watcher.Start();
    }

    /// <summary>Encola trabajo en el hilo dueño de las ventanas.</summary>
    public void Post(Action work) => _watcher.Post(work);

    private async Task HandleAndLogAsync(nint hwnd)
    {
        var before = Log.Recent().Count;
        await Engine.HandleAsync(hwnd, _cts.Token).ConfigureAwait(false);

        foreach (var entry in Log.Recent().Skip(before))
            _file.Write(entry);
    }

    private void EnsureConfigExists()
    {
        if (File.Exists(ConfigPaths.Rules))
            return;

        YamlStore.Save(ConfigPaths.Rules, ConfigSeed.Seed(_monitorSystem.GetMonitors()));
    }

    /// <summary>
    /// Una config ilegible deja las reglas anteriores en memoria. Quedarse sin
    /// reglas por un dos puntos faltante sería peor que el error.
    /// </summary>
    /// <remarks>
    /// Un guardado atómico (temp file + rename) puede dejar rules.yaml
    /// momentáneamente bloqueado justo cuando el debounce dispara la lectura;
    /// un único reintento corto absorbe esa carrera sin esconder un error real.
    /// </remarks>
    public void ReloadConfig()
    {
        try
        {
            Engine.UpdateRules(YamlStore.Load(ConfigPaths.Rules));
            LastConfigError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                Thread.Sleep(50);
                Engine.UpdateRules(YamlStore.Load(ConfigPaths.Rules));
                LastConfigError = null;
            }
            catch (RuleSetFormatException retryEx)
            {
                LastConfigError = retryEx.Message;
            }
            catch (Exception retryEx) when (retryEx is IOException or UnauthorizedAccessException)
            {
                LastConfigError = retryEx.Message;
            }
        }
        catch (RuleSetFormatException ex)
        {
            LastConfigError = ex.Message;
        }

        ConfigChanged?.Invoke();
    }

    private void WatchConfig()
    {
        Directory.CreateDirectory(ConfigPaths.Root);

        _configWatcher = new FileSystemWatcher(ConfigPaths.Root, "rules.yaml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        // Un editor que guarda atómicamente (temp file + rename, el default de VS
        // Code y de muchos otros) nunca dispara Changed sobre rules.yaml: dispara
        // Created o Renamed en su lugar. Los tres van al mismo debounce, así que
        // una secuencia de varios eventos por el mismo guardado sólo recarga una vez.
        _configWatcher.Changed += (_, _) => DebouncedReload();
        _configWatcher.Created += (_, _) => DebouncedReload();
        _configWatcher.Renamed += (_, _) => DebouncedReload();
    }

    /// <summary>Los editores escriben en varios pasos; sin debounce se recarga a medio guardar.</summary>
    private void DebouncedReload()
    {
        _reloadDebounce?.Cancel();
        _reloadDebounce = new CancellationTokenSource();
        var token = _reloadDebounce.Token;

        _ = Task.Delay(300, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                ReloadConfig();
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _configWatcher?.Dispose();
        _reloadDebounce?.Dispose();
        _watcher.Dispose();
        _cts.Dispose();
    }
}
