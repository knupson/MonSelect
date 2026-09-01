using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MonSelect.Core.Win32;

/// <summary>
/// Datos del proceso dueño de una ventana. El command line se lee del PEB en
/// vez de con WMI porque Win32_Process cuesta entre 50 y 200 ms por consulta, y
/// esto corre en el camino crítico de una ventana que acaba de aparecer.
/// </summary>
public static class ProcessQuery
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_VM_READ = 0x0010;

    // Usadas por IsWow64Process2 para distinguir un proceso x64 nativo de uno
    // corriendo bajo emulación WOW64 (p.ej. una app de 32 bits en Windows 64 bits).
    private const ushort IMAGE_FILE_MACHINE_UNKNOWN = 0x0000;
    private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint Reserved1;
        public nint PebBaseAddress;
        public nint Reserved2_0;
        public nint Reserved2_1;
        public nuint UniqueProcessId;
        public nint Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(
        nint process, uint flags, StringBuilder buffer, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        nint process, nint address, nint buffer, nint size, out nint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process2(
        nint process, out ushort processMachine, out ushort nativeMachine);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(nint process, out bool wow64Process);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint process, int infoClass, ref ProcessBasicInformation info, int length, out int returned);

    public static string? GetExePath(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
            return null;

        try
        {
            var size = 1024u;
            var buffer = new StringBuilder((int)size);
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static long GetStartTicks(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.StartTime.Ticks;
        }
        catch (Exception)
        {
            // Proceso muerto, elevado, o de otro usuario. Cero es un valor válido
            // de "no sé": sólo se usa para desambiguar pids reciclados.
            return 0;
        }
    }

    /// <summary>
    /// Devuelve null cuando el proceso es elevado, de otro usuario, o no tiene la
    /// arquitectura que estos offsets asumen. Eso no es un error: la regla que
    /// dependa del command line simplemente no matchea.
    /// </summary>
    public static unsafe string? GetCommandLine(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
        if (handle == 0)
            return null;

        try
        {
            // Los offsets de abajo (PEB.ProcessParameters y ..CommandLine) son
            // válidos únicamente para un proceso x64 nativo. Un target de 32 bits
            // corriendo bajo WOW64 tiene un layout de PEB distinto en esas mismas
            // direcciones: la lectura no fallaría, devolvería basura con forma de
            // string. Se rehúsa a leer en vez de adivinar.
            if (!IsNativeX64(handle))
                return null;

            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf(info), out _) != 0)
                return null;
            if (info.PebBaseAddress == 0)
                return null;

            // PEB.ProcessParameters está en el offset 0x20 en x64.
            nint parameters;
            var pointerSize = (nint)sizeof(nint);
            if (!ReadProcessMemory(handle, info.PebBaseAddress + 0x20,
                    (nint)(&parameters), pointerSize, out var read1) || read1 != pointerSize)
                return null;

            // RTL_USER_PROCESS_PARAMETERS.CommandLine está en 0x70 en x64.
            UnicodeString commandLine;
            var unicodeStringSize = (nint)Marshal.SizeOf<UnicodeString>();
            if (!ReadProcessMemory(handle, parameters + 0x70,
                    (nint)(&commandLine), unicodeStringSize, out var read2) || read2 != unicodeStringSize)
                return null;

            if (commandLine.Length == 0 || commandLine.Buffer == 0)
                return null;

            var bytes = Marshal.AllocHGlobal(commandLine.Length);
            try
            {
                if (!ReadProcessMemory(handle, commandLine.Buffer, bytes, commandLine.Length, out var read3)
                        || read3 != (nint)commandLine.Length)
                    return null;

                return Marshal.PtrToStringUni(bytes, commandLine.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(bytes);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// True cuando <paramref name="handle"/> es un proceso x64 nativo (no WOW64),
    /// que es el único layout de PEB que <see cref="GetCommandLine"/> sabe leer.
    /// </summary>
    private static bool IsNativeX64(nint handle)
    {
        try
        {
            if (IsWow64Process2(handle, out var processMachine, out var nativeMachine))
                return processMachine == IMAGE_FILE_MACHINE_UNKNOWN && nativeMachine == IMAGE_FILE_MACHINE_AMD64;
        }
        catch (EntryPointNotFoundException)
        {
            // IsWow64Process2 no existe antes de Windows 10 1709; caer al fallback.
        }

        // IsWow64Process sólo dice si el target corre bajo WOW64, no cuál es la
        // arquitectura nativa del sistema. Alcanza igual: este binario es x64, así
        // que un proceso que no está bajo WOW64 en esta máquina es x64 nativo.
        return IsWow64Process(handle, out var isWow64) && !isWow64;
    }
}
