using System.Runtime.InteropServices;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>
/// Radeon Software settings through ADLX (F16.2). The ADLX C# bindings (generated with SWIG from the MIT ADLX SDK) are not
/// part of this build yet, so the integration reports itself unavailable and the AMD tweaks are never offered: no
/// guessed interop is ever called on a real driver.
/// </summary>
internal sealed class AmdAdlx : IAmdSettings
{
    /// <summary>True when the Radeon driver ships ADLX; still not enough to apply settings without the bindings.</summary>
    public static bool DriverHasAdlx => NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, "amdadlx64.dll"), out var library) && Free(library);

    public bool IsAvailable => false;

    public AmdSettingState Read(string feature) =>
        throw new NotSupportedException("Os ajustes da Radeon via ADLX ainda não estão disponíveis nesta versão.");

    public void Write(string feature, AmdSettingState state) =>
        throw new NotSupportedException("Os ajustes da Radeon via ADLX ainda não estão disponíveis nesta versão.");

    private static bool Free(IntPtr library)
    {
        NativeLibrary.Free(library);
        return true;
    }
}
