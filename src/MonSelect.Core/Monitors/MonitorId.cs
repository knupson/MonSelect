namespace MonSelect.Core.Monitors;

/// <summary>
/// Clave estable de un monitor: el monitorDevicePath que devuelve QueryDisplayConfig.
/// No se usa el índice \\.\DISPLAYn porque se reasigna al reconectar, ni el serial
/// EDID porque en hardware real aparece duplicado o en cero (spec, sección 3.2).
/// </summary>
public readonly record struct MonitorId(string DevicePath)
{
    public bool Equals(MonitorId other)
        => string.Equals(DevicePath, other.DevicePath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(DevicePath);

    public override string ToString() => DevicePath;
}
