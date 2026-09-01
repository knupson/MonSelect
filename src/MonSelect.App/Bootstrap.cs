using System.IO;
using MonSelect.Core.Engine;
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Windows;

namespace MonSelect.App;

/// <summary>Arma el grafo de objetos y mantiene la config sincronizada con el disco.</summary>
public sealed class Bootstrap : IDisposable
{
    /// <summary>
    /// Presupuesto por llamada mutante contra una ventana ajena (SetStyle,
    /// ApplyFrameChange, SetPlacement, Show). Más que de sobra para una app
    /// legítimamente ocupada: reposicionar, incluso cuando la app pelea y se
    /// vuelve a mover sola después, vuelve en milisegundos de un solo dígito.
    /// Ver BoundedWindowSystem para por qué hace falta acotar esto en vez de
    /// dejarlo síncrono sin límite (hang-fix-2-report.md).
    /// </summary>
    private static readonly TimeSpan WindowCallBudget = TimeSpan.FromSeconds(1);

    private readonly Win32MonitorSystem _monitorSystem = new();
    private readonly IWindowSystem _windowSystem = new BoundedWindowSystem(new Win32WindowSystem(), WindowCallBudget);
    private readonly WindowWatcher _watcher = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ApplyLogFile _file = new();
    private readonly WindowPlacer _placer;
    private readonly MonitorRegistry _monitorRegistry;
    private readonly WindowProbe _probe;

    private FileSystemWatcher? _configWatcher;
    private CancellationTokenSource? _reloadDebounce;

    public RuleEngine Engine { get; }
    public ApplyLog Log { get; } = new();
    public string? LastConfigError { get; private set; }

    /// <summary>
    /// Último RuleSet cargado con éxito. La GUI lo necesita para mostrar qué
    /// regla matchea cada ventana (RuleMatcher.FirstMatch) y para resolver alias
    /// de monitor — RuleEngine lo guarda puertas adentro y no lo expone.
    /// </summary>
    public RuleSet CurrentRuleSet { get; private set; } = RuleSet.Empty;

    /// <summary>
    /// Sólo para lecturas desde la GUI (GetVisibleBounds al crear una regla,
    /// inspección de ventanas para la tabla). Cualquier llamada que mute una
    /// ventana tiene que pasar por <see cref="Post"/>, no usar esto directo.
    /// </summary>
    public IWindowSystem WindowSystem => _windowSystem;

    /// <summary>Monitores conectados, para el mapa de la GUI y para resolver alias.</summary>
    public MonitorRegistry Monitors => _monitorRegistry;

    /// <summary>
    /// Acceso directo al subsistema de monitores, para GetMonitorForRect — lo
    /// que necesita la GUI para saber en qué monitor está cada ventana abierta.
    /// MonitorRegistry no lo envuelve porque RuleEngine no lo necesita.
    /// </summary>
    public IMonitorSystem MonitorSystem => _monitorSystem;

    /// <summary>Describe una ventana. Sólo lectura, segura desde el hilo de la GUI.</summary>
    public WindowProbe Probe => _probe;

    public event Action? ConfigChanged;

    public Bootstrap()
    {
        var styles = new StyleStore(ConfigPaths.Borderless);
        styles.Load();
        _placer = new WindowPlacer(_windowSystem, styles);
        _monitorRegistry = new MonitorRegistry(_monitorSystem);
        _probe = new WindowProbe(_windowSystem);

        Engine = new RuleEngine(
            _probe,
            _monitorRegistry,
            _placer,
            new RetryScheduler(_windowSystem, new RealDelay()),
            Log);
    }

    /// <summary>Carga la config sin instalar el hook. Para el modo --apply-now.</summary>
    public void StartForOneShot()
    {
        EnsureConfigExists();
        ReloadConfig();
    }

    public void Start()
    {
        EnsureConfigExists();
        ReloadConfig();
        WatchConfig();
        _file.Prune();

        // Sincrónico a propósito. WindowAppeared ya corre en el hilo de
        // placement, que es el dueño único de la mutación de ventanas. Con
        // fire-and-forget, cada continuación async y cada escritura al log
        // caían en el ThreadPool; como BoundedWindowSystem bloquea hilos con
        // Thread.Join, el pool se agotaba y el pipeline quedaba varado con el
        // proceso vivo y el hook entregando eventos normalmente.
        Log.EntryAdded += _file.Write;
        _watcher.WindowAppeared += hwnd => HandleAndLogAsync(hwnd).GetAwaiter().GetResult();

        _watcher.Start();
    }

    /// <summary>Encola trabajo en el hilo dueño de las ventanas.</summary>
    public void Post(Action work) => _watcher.Post(work);

    /// <summary>
    /// Revierte todas las ventanas borderless registradas. Sin invocar esto desde
    /// algún lado, StyleStore/WindowPlacer.Revert nunca corre en la app real y un
    /// borderless no tiene vuelta atrás (F1 acceptance, defecto 3). Llamar sólo
    /// desde dentro de <see cref="Post"/>: muta ventanas ajenas.
    /// </summary>
    public int RevertAllBorderless() => _placer.RevertAll();

    /// <summary>
    /// El archivo se alimenta desde <see cref="ApplyLog.EntryAdded"/>, no
    /// comparando conteos antes y después. <see cref="ApplyLog"/> es un buffer
    /// circular de capacidad fija: una vez lleno, su Count queda clavado y un
    /// diff por conteo no vuelve a ver nada nunca más — el archivo se cortaba en
    /// seco y parecía que el motor se había colgado, cuando seguía colocando
    /// ventanas con normalidad.
    /// </summary>
    private Task HandleAndLogAsync(nint hwnd)
        => Engine.HandleAsync(hwnd, _cts.Token);

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
            var set = YamlStore.Load(ConfigPaths.Rules);
            Engine.UpdateRules(set);
            CurrentRuleSet = set;
            LastConfigError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                Thread.Sleep(50);
                var set = YamlStore.Load(ConfigPaths.Rules);
                Engine.UpdateRules(set);
                CurrentRuleSet = set;
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
