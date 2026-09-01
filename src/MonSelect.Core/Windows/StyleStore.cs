using System.Text.Json;

namespace MonSelect.Core.Windows;

/// <param name="Handle">hwnd como long, para que serialice igual en 32 y 64 bits.</param>
/// <param name="ProcessStartTicks">
/// Desambigua un pid reciclado: si el proceso arrancó en otro momento, el
/// registro es de una ventana que ya no existe.
/// </param>
public sealed record BorderlessRecord(
    long Handle,
    uint ProcessId,
    long ProcessStartTicks,
    uint OriginalStyle);

/// <summary>
/// Recuerda el style original de las ventanas a las que se les quitó el marco.
/// Se persiste en disco porque, sin eso, un reinicio de MonSelect deja ventanas
/// sin barra de título que el usuario no puede restaurar.
/// </summary>
public sealed class StyleStore(string path)
{
    private readonly Dictionary<long, BorderlessRecord> _records = new();

    /// <summary>No pisa un registro existente: el segundo style ya está mutilado.</summary>
    public void Remember(BorderlessRecord record)
        => _records.TryAdd(record.Handle, record);

    public bool TryGet(long handle, out BorderlessRecord record)
        => _records.TryGetValue(handle, out record!);

    public BorderlessRecord? Forget(long handle)
    {
        if (!_records.Remove(handle, out var record))
            return null;

        return record;
    }

    public IReadOnlyCollection<BorderlessRecord> All() => _records.Values;

    /// <summary>
    /// Persists records atomically: writes to a temp file, then replaces the target.
    /// Swallows I/O errors to keep the tray app responsive; in-memory records stay intact.
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = path + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(_records.Values));
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempPath); }
                catch { }
                throw;
            }
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Failed to save styles to {path}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Failed to save styles to {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Un archivo ilegible se descarta en silencio. Perder el historial de styles
    /// es molesto; no arrancar por eso sería peor.
    /// </summary>
    public void Load()
    {
        _records.Clear();

        if (!File.Exists(path))
            return;

        try
        {
            var loaded = JsonSerializer.Deserialize<List<BorderlessRecord>>(File.ReadAllText(path));
            foreach (var record in loaded ?? new List<BorderlessRecord>())
                _records[record.Handle] = record;
        }
        catch (JsonException)
        {
            _records.Clear();
        }
    }
}
