using System.Runtime.InteropServices;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>
/// NVIDIA driver settings (DRS) through NVAPI: the global/base profile and per-application profiles.
/// Function ids and the NVDRS_SETTING / NVDRS_PROFILE / NVDRS_APPLICATION_V1 layouts were verified (read-only)
/// against the installed driver.
/// </summary>
internal sealed class NvidiaDrs : INvidiaSettings, INvidiaProfiles
{
    private const int SettingSize = 12320;          // sizeof(NVDRS_SETTING_V1)
    private const int SettingIdOffset = 4100;
    private const int SettingTypeOffset = 4104;
    private const int CurrentValueOffset = 8220;
    private const int SettingNotFound = -160;
    private const int ExecutableNotFound = -166;
    private const int ProfileSize = 4116;           // sizeof(NVDRS_PROFILE_V1): name at 4
    private const int ApplicationSize = 12296;      // sizeof(NVDRS_APPLICATION_V1): appName at 8
    private const string OwnProfilePrefix = "NOVAIS boost - ";

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
    private static extern IntPtr QueryInterface(uint id);

    private delegate int InitializeFn();
    private delegate int CreateSessionFn(out IntPtr session);
    private delegate int SessionFn(IntPtr session);
    private delegate int GetBaseProfileFn(IntPtr session, out IntPtr profile);
    private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);
    private delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
    private delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint settingId);
    private delegate int FindApplicationFn(IntPtr session, [MarshalAs(UnmanagedType.LPWStr)] string app, out IntPtr profile, IntPtr application);
    private delegate int ProfileInfoFn(IntPtr session, IntPtr profile, IntPtr info);
    private delegate int CreateProfileFn(IntPtr session, IntPtr info, out IntPtr profile);
    private delegate int CreateApplicationFn(IntPtr session, IntPtr profile, IntPtr application);
    private delegate int DeleteProfileFn(IntPtr session, IntPtr profile);

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
        ReadSetting(session, profile, settingId) is { } value ? new NvidiaSettingState(true, value) : new NvidiaSettingState(false, 0));

    public void Write(uint settingId, NvidiaSettingState state) => WithBaseProfile(save: true, (session, profile) =>
    {
        if (state.Defined)
            WriteSetting(session, profile, settingId, state.Value);
        else
            DeleteSetting(session, profile, settingId);
        return state;
    });

    // ------------------------------------------------------------------ per-application profiles (F16.1)

    public NvidiaProfileState Read(string exe, uint settingId) => WithSession(save: false, session =>
    {
        if (FindApplication(session, exe) is not { } profile)
            return new NvidiaProfileState(false, false, 0);
        return ReadSetting(session, profile, settingId) is { } value ? new NvidiaProfileState(true, true, value) : new NvidiaProfileState(true, false, 0);
    });

    public void Write(string exe, uint settingId, NvidiaProfileState state) => WithSession(save: true, session =>
    {
        var profile = FindApplication(session, exe);
        if (!state.AppKnown)
        {
            // Back to "no profile": delete only a profile NOVAIS boost created; in any other profile, only the setting.
            if (profile is { } existing)
            {
                if (ProfileName(session, existing).StartsWith(OwnProfilePrefix, StringComparison.Ordinal))
                    Check(Function<DeleteProfileFn>(0x17093206)(session, existing), "apagar o perfil de");
                else
                    DeleteSetting(session, existing, settingId);
            }
            return state;
        }
        profile ??= CreateProfile(session, exe);
        if (state.Defined)
            WriteSetting(session, profile.Value, settingId, state.Value);
        else
            DeleteSetting(session, profile.Value, settingId);
        return state;
    });

    private delegate int EnumProfilesFn(IntPtr session, uint index, out IntPtr profile);

    /// <summary>20.8 (uninstall): deletes every leftover profile NOVAIS boost created ("NOVAIS boost - jogo.exe").</summary>
    public static int DeleteOwnProfiles()
    {
        if (!IsAvailable)
            return 0;
        return WithSession(save: true, session =>
        {
            var own = new List<IntPtr>();
            for (uint index = 0; Function<EnumProfilesFn>(0xBC371EE0)(session, index, out var profile) == 0; index++)
                if (ProfileName(session, profile).StartsWith(OwnProfilePrefix, StringComparison.Ordinal))
                    own.Add(profile);
            foreach (var profile in own)
                Check(Function<DeleteProfileFn>(0x17093206)(session, profile), "apagar o perfil de");
            return own.Count;
        });
    }

    private static IntPtr? FindApplication(IntPtr session, string exe)
    {
        var application = Allocate(ApplicationSize);
        try
        {
            var result = Function<FindApplicationFn>(0xEEE566B2)(session, exe, out var profile, application);
            if (result == ExecutableNotFound)
                return null;
            Check(result, "procurar o perfil do jogo nas");
            return profile;
        }
        finally
        {
            Marshal.FreeHGlobal(application);
        }
    }

    private static string ProfileName(IntPtr session, IntPtr profile)
    {
        var info = Allocate(ProfileSize);
        try
        {
            Check(Function<ProfileInfoFn>(0x61CD6FD6)(session, profile, info), "ler o perfil das");
            return Marshal.PtrToStringUni(info + 4) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(info);
        }
    }

    private static IntPtr CreateProfile(IntPtr session, string exe)
    {
        var info = Allocate(ProfileSize);
        var application = Allocate(ApplicationSize);
        try
        {
            WriteString(info, 4, OwnProfilePrefix + exe);
            Check(Function<CreateProfileFn>(0xCC176068)(session, info, out var profile), "criar o perfil nas");
            WriteString(application, 8, exe);
            Check(Function<CreateApplicationFn>(0x4347A9DE)(session, profile, application), "adicionar o jogo ao perfil nas");
            return profile;
        }
        finally
        {
            Marshal.FreeHGlobal(info);
            Marshal.FreeHGlobal(application);
        }
    }

    // ------------------------------------------------------------------ shared

    private static uint? ReadSetting(IntPtr session, IntPtr profile, uint settingId)
    {
        var buffer = Marshal.AllocHGlobal(SettingSize);
        try
        {
            Clear(buffer);
            var result = Function<GetSettingFn>(0x73BF8338)(session, profile, settingId, buffer);
            if (result == SettingNotFound)
                return null;
            Check(result, "ler");
            return unchecked((uint)Marshal.ReadInt32(buffer, CurrentValueOffset));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void WriteSetting(IntPtr session, IntPtr profile, uint settingId, uint value)
    {
        var buffer = Marshal.AllocHGlobal(SettingSize);
        try
        {
            Clear(buffer);
            Marshal.WriteInt32(buffer, SettingIdOffset, unchecked((int)settingId));
            Marshal.WriteInt32(buffer, SettingTypeOffset, 0); // NVDRS_DWORD_TYPE
            Marshal.WriteInt32(buffer, CurrentValueOffset, unchecked((int)value));
            Check(Function<SetSettingFn>(0x577DD202)(session, profile, buffer), "gravar");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void DeleteSetting(IntPtr session, IntPtr profile, uint settingId)
    {
        var result = Function<DeleteSettingFn>(0xE4A26362)(session, profile, settingId);
        if (result != SettingNotFound)
            Check(result, "restaurar o padrão de");
    }

    private static T WithBaseProfile<T>(bool save, Func<IntPtr, IntPtr, T> operation) => WithSession(save, session =>
    {
        Check(Function<GetBaseProfileFn>(0xDA8466A0)(session, out var profile), "abrir o perfil global de");
        return operation(session, profile);
    });

    private static T WithSession<T>(bool save, Func<IntPtr, T> operation)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("Driver NVIDIA (NVAPI) não encontrado.");
        Check(Function<CreateSessionFn>(0x0694D52E)(out var session), "abrir a sessão de");
        try
        {
            Check(Function<SessionFn>(0x375DBD6B)(session), "carregar");
            var result = operation(session);
            if (save)
                Check(Function<SessionFn>(0xFCBC7E14)(session), "salvar");
            return result;
        }
        finally
        {
            Function<SessionFn>(0xDAD9CFF8)(session);
        }
    }

    private static IntPtr Allocate(int size)
    {
        var buffer = Marshal.AllocHGlobal(size);
        Marshal.Copy(new byte[size], 0, buffer, size);
        Marshal.WriteInt32(buffer, 0, size | (1 << 16));
        return buffer;
    }

    private static void WriteString(IntPtr buffer, int offset, string text)
    {
        var chars = (text.Length > 2047 ? text[..2047] : text).ToCharArray();
        Marshal.Copy(chars, 0, buffer + offset, chars.Length);
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
