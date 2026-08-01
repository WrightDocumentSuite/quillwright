using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Drives a real Word installation over late binding, so that files this library writes can be
/// put in front of the only parser whose verdict counts.
/// </summary>
/// <remarks>
/// Automation is late bound so nothing here depends on an interop assembly, and Word is told
/// not to prompt: a file it would have offered to repair comes back empty or throws instead of
/// hanging the run waiting on a dialog nobody can see.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WordOracle
{
    /// <summary>
    /// Whether the oracle can run. Word takes seconds per file and cannot run on a build
    /// server, so these tests are opt-in through <c>QUILLWRIGHT_WORD_ORACLE</c>.
    /// </summary>
    public static bool Enabled =>
        OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("QUILLWRIGHT_WORD_ORACLE") is not (null or "" or "0") &&
        Type.GetTypeFromProgID("Word.Application") is not null;

    /// <summary>Opens a file in Word read-only, asks something about it and closes it again.</summary>
    /// <param name="path">File to open. It is deleted afterwards.</param>
    /// <param name="ask">What to ask of the opened document.</param>
    public static object Inspect(string path, Func<object, object> ask)
    {
        object? application = null;
        object? documents = null;
        object? opened = null;
        try
        {
            application = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!);
            Set(application!, "Visible", false);
            Set(application!, "DisplayAlerts", 0);

            documents = Get(application!, "Documents");
            opened = Invoke(documents!, "Open", path, false, true, false);

            return ask(opened!);
        }
        finally
        {
            if (opened is not null)
            {
                Invoke(opened, "Close", 0);
                Marshal.ReleaseComObject(opened);
            }

            if (documents is not null)
                Marshal.ReleaseComObject(documents);

            if (application is not null)
            {
                Invoke(application, "Quit", 0);
                Marshal.ReleaseComObject(application);
            }

            File.Delete(path);
        }
    }

    /// <summary>Reads a property that takes an index rather than exposing a collection.</summary>
    /// <param name="target">Object to read from.</param>
    /// <param name="name">Property name.</param>
    /// <param name="index">The index the property takes.</param>
    public static object Indexed(object target, string name, object index) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, [index], CultureInfo.InvariantCulture)!;

    /// <summary>Reads a property.</summary>
    /// <param name="target">Object to read from.</param>
    /// <param name="name">Property name.</param>
    public static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture);

    /// <summary>Writes a property.</summary>
    /// <param name="target">Object to write to.</param>
    /// <param name="name">Property name.</param>
    /// <param name="value">Value to set.</param>
    public static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value], CultureInfo.InvariantCulture);

    /// <summary>Calls a method.</summary>
    /// <param name="target">Object to call on.</param>
    /// <param name="name">Method name.</param>
    /// <param name="arguments">Arguments to pass.</param>
    public static object? Invoke(object target, string name, params object[] arguments) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments, CultureInfo.InvariantCulture);
}
