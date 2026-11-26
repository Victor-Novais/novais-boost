using System.Runtime.InteropServices;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>
/// NVIDIA driver settings (DRS) on the global/base profile through NVAPI.
/// Function ids and the NVDRS_SETTING layout were verified against the installed driver.
/// </summary>
internal sealed class NvidiaDrs : INvidiaSettings
{
    private const int SettingSize = 12320;          // sizeof(NVDRS_SETTING_V1)
    private const int SettingIdOffset = 4100;
    private const int SettingTypeOffset = 4104;
    private const int CurrentValueOffset = 8220;
    private const int SettingNotFound = -160;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
    private static extern IntPtr QueryInterface(uint id);

    private delegate int InitializeFn();
    private delegate int CreateSessionFn(out IntPtr session);
    private delegate int SessionFn(IntPtr session);
    private delegate int GetBaseProfileFn(IntPtr session, out IntPtr profile);
    private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);
    private delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
    private delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint settingId);

    private static readonly Lazy<bool> Initialized = new(() => Function<InitializeFn>(0x0150E828)() == 0);

    public static bool IsAvailable
    {
        get
        {
            try
            {
                return NativeLibrary.TryLoad("nvapi64.dll", out _) && Initialized.Value;
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    public NvidiaSettingState Read(uint settingId) => WithBaseProfile(save: false, (session, profile) =>
    {
        var buffer = Marshal.AllocHGlobal(SettingSize);
        try
        {
            Clear(buffer);
            var result = Function<GetSettingFn>(0x73BF8338)(session, profile, settingId, buffer);
            if (result == SettingNotFound)
                return new NvidiaSettingState(false, 0);
            Check(result, "ler");
            return new NvidiaSettingState(true, unchecked((uint)Marshal.ReadInt32(buffer, CurrentValueOffset)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    });

    public void Write(uint settingId, NvidiaSettingState state) => WithBaseProfile(save: true, (session, profile) =>
    {
        if (!state.Defined)
        {
            var result = Function<DeleteSettingFn>(0xE4A26362)(session, profile, settingId);
            if (result != SettingNotFound)
                Check(result, "restaurar o padrão de");
            return state;
        }

        var buffer = Marshal.AllocHGlobal(SettingSize);
        try
        {
            Clear(buffer);
            Marshal.WriteInt32(buffer, SettingIdOffset, unchecked((int)settingId));
            Marshal.WriteInt32(buffer, SettingTypeOffset, 0); // NVDRS_DWORD_TYPE
            Marshal.WriteInt32(buffer, CurrentValueOffset, unchecked((int)state.Value));
            Check(Function<SetSettingFn>(0x577DD202)(session, profile, buffer), "gravar");
            return state;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    });

    private static T WithBaseProfile<T>(bool save, Func<IntPtr, IntPtr, T> operation)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("Driver NVIDIA (NVAPI) não encontrado.");
        Check(Function<CreateSessionFn>(0x0694D52E)(out var session), "abrir a sessão de");
        try
        {
            Check(Function<SessionFn>(0x375DBD6B)(session), "carregar");
            Check(Function<GetBaseProfileFn>(0xDA8466A0)(session, out var profile), "abrir o perfil global de");
            var result = operation(session, profile);
            if (save)
                Check(Function<SessionFn>(0xFCBC7E14)(session), "salvar");
            return result;
        }
        finally
        {
            Function<SessionFn>(0xDAD9CFF8)(session);
        }
    }

    private static void Clear(IntPtr buffer)
    {
        Marshal.Copy(new byte[SettingSize], 0, buffer, SettingSize);
        Marshal.WriteInt32(buffer, 0, SettingSize | (1 << 16)); // version = size | (1 << 16)
    }

    private static T Function<T>(uint id) where T : Delegate
    {
        var pointer = QueryInterface(id);
        return pointer == IntPtr.Zero
            ? throw new InvalidOperationException($"Função NVAPI 0x{id:X8} indisponível neste driver.")
            : Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private static void Check(int result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"Falha ao {operation} configurações do driver NVIDIA (NVAPI {result}).");
    }
}
