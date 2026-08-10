using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Quillwright.Rtf.Tests;

/// <summary>Late-bound Microsoft Word oracle for the opt-in RTF interoperability tests.</summary>
[SupportedOSPlatform("windows")]
internal static class RtfWordOracle
{
    internal static bool Enabled =>
        OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("QUILLWRIGHT_WORD_ORACLE") is not (null or "" or "0") &&
        Type.GetTypeFromProgID("Word.Application") is not null;

    internal static object Inspect(string path, Func<object, object> ask)
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

    internal static object Indexed(object target, string name, object index) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty | BindingFlags.InvokeMethod,
            null,
            target,
            [index],
            CultureInfo.InvariantCulture)!;

    internal static object? Get(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            null,
            target,
            null,
            CultureInfo.InvariantCulture);

    private static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty,
            null,
            target,
            [value],
            CultureInfo.InvariantCulture);

    private static object? Invoke(object target, string name, params object[] arguments) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            null,
            target,
            arguments,
            CultureInfo.InvariantCulture);
}
