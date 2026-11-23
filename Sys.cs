using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MspTweaker;

/// System access. Every write records the old value in Backup first, so Revert restores it exactly.
static class Sys
{
    // absolute paths: an elevated app must not resolve tools from the current directory
    static readonly string Powercfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
    static readonly string Bcdedit = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");
    static readonly string PowerShell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
    public static readonly string Shutdown = Path.Combine(Environment.SystemDirectory, "shutdown.exe");

    [DllImport("kernel32.dll")] static extern uint GetOEMCP();
    static readonly Encoding Oem = OemEncoding();
    static Encoding OemEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetOEMCP()); // console tools write in the OEM code page
    }

    public static string Run(string exe, string args, Encoding? enc = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = enc ?? Oem, StandardErrorEncoding = enc ?? Oem,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + exe);
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} {args}\n{(err.Result + output).Trim()}");
        return output;
    }

    public static string Ps(string script)
    {
        var full = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
                   "try{" + script + "}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}";
        var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return Run(PowerShell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64, new UTF8Encoding(false)).Trim();
    }

    static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    // ---------- registry ----------

    public static object? RegGet(string path, string name)
    {
        using var k = Hive(path, out var sub).OpenSubKey(sub);
        return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public static void RegSet(string id, string path, string name, object value)
    {
        Backup.Remember(id, $"reg|{name}|{path}", Encode(RegGet(path, name)));
        RegWrite(path, name, value);
    }

    static void RegWrite(string path, string name, object? value)
    {
        if (value is null)
        {
            using var existing = Hive(path, out var s).OpenSubKey(s, true);
            existing?.DeleteValue(name, false);
            return;
        }
        using var k = Hive(path, out var sub).CreateSubKey(sub, true);
        k.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);
    }

    static RegistryKey Hive(string path, out string sub)
    {
        var i = path.IndexOf('\\');
        sub = path[(i + 1)..];
        return path[..i] switch
        {
            "HKLM" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            _ => throw new ArgumentException("unknown hive: " + path),
        };
    }

    internal static string? Encode(object? v) => v switch
    {
        null => null,
        int i => "dword:" + i,
        string s => "sz:" + s,
        _ => throw new NotSupportedException("unexpected registry value type: " + v.GetType().Name),
    };

    internal static object? Decode(string? s) =>
        s is null ? null : s.StartsWith("dword:") ? int.Parse(s[6..]) : s[3..];

    // "A=1;B=2;" format used by DirectXUserGlobalSettings
    internal static string? KvGet(string? s, string key) =>
        (s ?? "").Split(';').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == key)?[1];

    internal static string KvSet(string? s, string key, string value) =>
        string.Concat((s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Split('=', 2)[0] != key).Append($"{key}={value}").Select(p => p + ";"));

    // ---------- powercfg ----------

    public const string PlanGuid = "6d5f7a10-3c2d-4f6e-8a1b-2c3d4e5f6071";

    public static string ActiveScheme() =>
        Regex.Match(Run(Powercfg, "/getactivescheme"), @"[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}", RegexOptions.IgnoreCase).Value;

    // labels are localized; the last two hex values are always the current AC and DC index
    internal static (int Ac, int Dc) ParseAcDc(string q)
    {
        var hex = Regex.Matches(q, @"0x([0-9a-f]{8})", RegexOptions.IgnoreCase);
        if (hex.Count < 2) throw new InvalidOperationException("unexpected powercfg output:\n" + q.Trim());
        return (Convert.ToInt32(hex[^2].Groups[1].Value, 16), Convert.ToInt32(hex[^1].Groups[1].Value, 16));
    }

    public static (int Ac, int Dc) PowerGet(string sub, string setting) =>
        ParseAcDc(Run(Powercfg, $"/q {ActiveScheme()} {sub} {setting}"));

    public static void PowerSet(string id, string sub, string setting, int value)
    {
        var scheme = ActiveScheme();
        var (ac, dc) = ParseAcDc(Run(Powercfg, $"/q {scheme} {sub} {setting}"));
        Backup.Remember(id, $"pwr|{scheme}|{sub}|{setting}", $"{ac},{dc}");
        PowerWrite(scheme, sub, setting, value, value);
    }

    static void PowerWrite(string scheme, string sub, string setting, int ac, int dc)
    {
        Run(Powercfg, $"/setacvalueindex {scheme} {sub} {setting} {ac}");
        Run(Powercfg, $"/setdcvalueindex {scheme} {sub} {setting} {dc}");
        Run(Powercfg, "/setactive " + ActiveScheme()); // re-activate so the change takes effect
    }

    // shortcut: Modern Standby laptops may refuse the plan; the powercfg error shows on the card. Upgrade: fall back to the Settings power mode.
    public static void PlanApply(string id)
    {
        Backup.Remember(id, "plan", ActiveScheme());
        if (!Run(Powercfg, "/list").Contains(PlanGuid, StringComparison.OrdinalIgnoreCase))
        {
            Run(Powercfg, $"-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 {PlanGuid}");
            Run(Powercfg, $"/changename {PlanGuid} \"msp-tweaker - Max Performance\"");
        }
        Run(Powercfg, "/setactive " + PlanGuid);
    }

    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public static void PlanActivate(string id, string guid)
    {
        Backup.Remember(id, "plan", ActiveScheme());
        Run(Powercfg, "/setactive " + guid);
    }

    // ---------- hardware ----------

    public static string CpuName() =>
        RegGet(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") as string ?? "";

    // dual-CCD X3D parts: only one CCD has the V-Cache, and AMD's driver parks the other one during games
    public static bool DualCcdX3D() => Regex.IsMatch(CpuName(), @"\b(7900|7950|9900|9950)X3D\b");

    public static int RefreshRate() =>
        int.Parse(Ps("(Get-CimInstance Win32_VideoController | Where-Object PNPDeviceID -like 'PCI\\*' | Where-Object CurrentRefreshRate | Select-Object -First 1).CurrentRefreshRate"));

    // ---------- NVIDIA driver profile (NVAPI DRS, the store the NVIDIA Control Panel writes) ----------

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr NvQuery(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvOut(out IntPtr h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvSession(IntPtr s);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvProfile(IntPtr s, out IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvGet(IntPtr s, IntPtr p, uint id, [In, Out] byte[] setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvSet(IntPtr s, IntPtr p, [In] byte[] setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvDelete(IntPtr s, IntPtr p, uint id);

    // NVDRS_SETTING_V1: version, 2048-char name, id, type, location, two flags, then two 4100-byte value unions
    const int SettingSize = 12320, OffId = 4100, OffPredefined = 4112, OffCurrent = 8220;
    const uint SettingVer = SettingSize | (1u << 16);
    const int NvSettingNotFound = -160;

    // classic export ids first; newer drivers also answer to the ids Profile Inspector uses
    static T Nv<T>(uint id, uint alt = 0) where T : Delegate
    {
        var p = NvQuery(id);
        if (p == IntPtr.Zero && alt != 0) p = NvQuery(alt);
        return p == IntPtr.Zero ? throw new InvalidOperationException($"NVAPI 0x{id:X8} not found") : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    static void NvCheck(int r, string what)
    {
        if (r != 0) throw new InvalidOperationException($"NVAPI {what}: {r}");
    }

    public static bool HasNvidia()
    {
        try { return Nv<NvCall>(0x0150E828)() == 0; }
        catch (DllNotFoundException) { return false; }
    }

    static T Drs<T>(Func<IntPtr, IntPtr, T> body)
    {
        NvCheck(Nv<NvCall>(0x0150E828)(), "Initialize");
        NvCheck(Nv<NvOut>(0x0694D52E)(out var s), "CreateSession");
        try
        {
            NvCheck(Nv<NvSession>(0x375DBD6B)(s), "LoadSettings");
            NvCheck(Nv<NvProfile>(0xDA8466A0)(s, out var p), "GetBaseProfile");
            return body(s, p);
        }
        finally { Nv<NvSession>(0xDAD9CFF8)(s); }
    }

    // effective value on the global (base) profile; Predefined = the driver's own default, nothing set by anyone
    public static (uint Value, bool Predefined)? DrsGet(uint setting) => Drs<(uint, bool)?>((s, p) =>
    {
        var b = new byte[SettingSize];
        BitConverter.TryWriteBytes(b, SettingVer);
        var r = Nv<NvGet>(0x73BF8338, 0xEA99498D)(s, p, setting, b);
        if (r == NvSettingNotFound) return null;
        NvCheck(r, "GetSetting");
        return (BitConverter.ToUInt32(b, OffCurrent), BitConverter.ToUInt32(b, OffPredefined) != 0);
    });

    public static void DrsSet(string id, uint setting, uint value)
    {
        var old = DrsGet(setting);
        Backup.Remember(id, $"drs|{setting:X8}", old is { Predefined: false } o ? o.Value.ToString() : null);
        DrsWrite(setting, value);
    }

    // null deletes the user value, so the driver default applies again
    static void DrsWrite(uint setting, uint? value) => Drs((s, p) =>
    {
        if (value is null)
        {
            var r = Nv<NvDelete>(0xE4A26362, 0xD20D29DF)(s, p, setting);
            if (r != NvSettingNotFound) NvCheck(r, "DeleteProfileSetting");
        }
        else
        {
            var b = new byte[SettingSize]; // settingType 0 = DWORD
            BitConverter.TryWriteBytes(b, SettingVer);
            BitConverter.TryWriteBytes(b.AsSpan(OffId), setting);
            BitConverter.TryWriteBytes(b.AsSpan(OffCurrent), value.Value);
            NvCheck(Nv<NvSet>(0x577DD202, 0x8A2CF5F5)(s, p, b), "SetSetting");
        }
        NvCheck(Nv<NvSession>(0xFCBC7E14)(s), "SaveSettings");
        return 0;
    });

    // ---------- Wi-Fi background scan ----------

    [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern int WlanSetInterface(IntPtr handle, ref Guid iface, int opcode, int size, ref int data, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr memory);

    public const string WifiTask = "msp-tweaker Wi-Fi scan guard";
    static readonly string Schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    // Windows turns background scan back on after every reconnect, and the setting only lasts while our WLAN handle is open,
    // so this runs as a resident process started by a logon task.
    // shortcut: polls every 5 s instead of WlanRegisterNotification; upgrade if the 5 s window after a reconnect matters.
    public static void WifiGuard()
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return;
        while (true)
        {
            if (WlanEnumInterfaces(h, IntPtr.Zero, out var list) == 0)
            {
                // WLAN_INTERFACE_INFO_LIST: two DWORDs, then 532-byte entries that start with the interface GUID
                var n = Marshal.ReadInt32(list);
                for (var i = 0; i < n; i++)
                {
                    var g = Marshal.PtrToStructure<Guid>(list + 8 + i * 532);
                    int off = 0, on = 1;
                    WlanSetInterface(h, ref g, 2, sizeof(int), ref off, IntPtr.Zero); // background_scan_enabled
                    WlanSetInterface(h, ref g, 3, sizeof(int), ref on, IntPtr.Zero);  // media_streaming_mode
                }
                WlanFreeMemory(list);
            }
            Thread.Sleep(5000);
        }
    }

    public static bool TaskExists(string name)
    {
        try { Run(Schtasks, $"/query /tn \"{name}\""); return true; }
        catch (InvalidOperationException) { return false; }
    }

    public static void WifiGuardInstall(string id)
    {
        Backup.Remember(id, "task|" + WifiTask, null);
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("unknown exe path");
        Run(Schtasks, $"/create /tn \"{WifiTask}\" /sc onlogon /rl highest /f /tr \"\\\"{exe}\\\" --wifi-guard\"");
        Run(Schtasks, $"/run /tn \"{WifiTask}\"");
    }

    static void TaskRemove(string name)
    {
        if (!TaskExists(name)) return;
        try { Run(Schtasks, $"/end /tn \"{name}\""); } catch (InvalidOperationException) { } // not running
        Run(Schtasks, $"/delete /tn \"{name}\" /f");
    }

    // ---------- network adapter registry ----------

    const string NetClass = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    // class subkeys of physical adapters whose description matches; the class key is where NDIS reads keywords like *RSS
    public static string[] NicKeys(string descriptionRegex) =>
        Ps($"$c = Get-ChildItem 'Registry::{NetClass.Replace("HKLM", "HKEY_LOCAL_MACHINE")}' -ErrorAction SilentlyContinue; " +
           $"Get-NetAdapter -Physical | Where-Object InterfaceDescription -match {Q(descriptionRegex)} | ForEach-Object {{ $g = $_.InterfaceGuid; " +
           "$c | Where-Object { (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).NetCfgInstanceId -eq $g } | ForEach-Object { $_.Name } }")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(k => k.Replace("HKEY_LOCAL_MACHINE", "HKLM")).ToArray();

    public static void NicRestart(string descriptionRegex) =>
        Ps($"Get-NetAdapter -Physical | Where-Object InterfaceDescription -match {Q(descriptionRegex)} | Restart-NetAdapter");

    // ---------- connection test ----------

    public sealed record PingStats(string Target, int Sent, int Lost, double AvgMs, double JitterMs);

    // jitter = mean difference between consecutive replies, the RFC 3550 idea without its smoothing
    public static PingStats PingTest(System.Net.IPAddress target, string label, int count = 40)
    {
        using var ping = new System.Net.NetworkInformation.Ping();
        var times = new List<double>();
        for (var i = 0; i < count; i++)
        {
            var r = ping.Send(target, 1000);
            if (r.Status == System.Net.NetworkInformation.IPStatus.Success) times.Add(r.RoundtripTime);
            Thread.Sleep(50);
        }
        var jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
        return new(label, count, count - times.Count, times.Count > 0 ? times.Average() : 0, jitter);
    }

    [DllImport("iphlpapi.dll")] static extern int GetBestInterface(uint destination, out uint index);

    // gateway of the interface Windows routes internet traffic through, so VPN adapters with their own gateway are skipped
    public static System.Net.IPAddress? Gateway()
    {
        if (GetBestInterface(BitConverter.ToUInt32(System.Net.IPAddress.Parse("1.1.1.1").GetAddressBytes()), out var index) != 0) return null;
        return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.Supports(System.Net.NetworkInformation.NetworkInterfaceComponent.IPv4) && n.GetIPProperties().GetIPv4Properties()?.Index == index)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    }

    // ---------- bcdedit ----------

    internal static Dictionary<string, string> ParseBcd(string s) =>
        s.Split('\n').Select(l => Regex.Match(l.TrimEnd(), @"^([a-z]+)\s+(\S.*)$")).Where(m => m.Success)
         .GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
         .ToDictionary(g => g.Key, g => g.First().Groups[2].Value, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> Bcd() => ParseBcd(Run(Bcdedit, "/enum {current}"));

    public static void BcdSet(string id, string element, string? value)
    {
        Bcd().TryGetValue(element, out var old);
        Backup.Remember(id, "bcd|" + element, old);
        BcdWrite(element, value);
    }

    static void BcdWrite(string element, string? value)
    {
        // changing boot config with BitLocker on can trigger recovery; Microsoft's procedure is to suspend for one reboot
        Ps("if (Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue) { $v = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction SilentlyContinue; " +
           "if ($v.ProtectionStatus -eq 'On') { Suspend-BitLocker -MountPoint $env:SystemDrive -RebootCount 1 | Out-Null } }");
        Run(Bcdedit, value is null ? $"/deletevalue {{current}} {element}" : $"/set {{current}} {element} {value}");
    }

    // ---------- network adapter ----------

    const string Ethernet = "Get-NetAdapter -Physical | Where-Object NdisPhysicalMedium -eq 14"; // 14 = 802.3

    public static List<(string Name, string Keyword, string Value)> NicProps(string[] keywords) =>
        Ps($"{Ethernet} | Get-NetAdapterAdvancedProperty -AllProperties | Where-Object {{ $_.RegistryKeyword -in @({string.Join(",", keywords.Select(Q))}) }} | " +
           "ForEach-Object { \"$($_.Name)`t$($_.RegistryKeyword)`t$($_.RegistryValue)\" }")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(l => l.Split('\t')).Select(p => (p[0], p[1], p[2])).ToList();

    // null = no adapter has these properties
    public static bool? NicAll(string[] keywords, string value)
    {
        var props = NicProps(keywords);
        return props.Count == 0 ? null : props.All(p => p.Value == value);
    }

    public static void NicSet(string id, string[] keywords, string value)
    {
        var props = NicProps(keywords).Where(p => p.Value != value).ToList();
        foreach (var p in props) Backup.Remember(id, $"nic|{p.Keyword}|{p.Name}", p.Value);
        NicWrite(props.Select(p => (p.Name, p.Keyword, value)).ToList());
    }

    static void NicWrite(List<(string Name, string Keyword, string Value)> props)
    {
        if (props.Count == 0) return;
        Ps(string.Concat(props.Select(p => $"Set-NetAdapterAdvancedProperty -Name {Q(p.Name)} -RegistryKeyword {Q(p.Keyword)} -RegistryValue {Q(p.Value)} -NoRestart;")) +
           string.Concat(props.Select(p => p.Name).Distinct().Select(n => $"Restart-NetAdapter -Name {Q(n)};")));
    }

    public static bool HasWifi() => Ps("[bool](Get-NetAdapter -Physical | Where-Object NdisPhysicalMedium -eq 9)") == "True"; // 9 = 802.11

    // ---------- MSI ----------

    public static string[] MsiDevices() =>
        Ps("Get-CimInstance Win32_PnPEntity -Filter \"PNPClass='Display' OR PNPClass='Net'\" | Where-Object DeviceID -like 'PCI\\*' | ForEach-Object DeviceID")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string MsiKey(string device) =>
        $@"HKLM\SYSTEM\CurrentControlSet\Enum\{device}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";

    // ---------- Fortnite ----------

    public const string FortniteProcess = "FortniteClient-Win64-Shipping";
    public static readonly string FortniteIni = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini");

    // Epic's launcher manifest lists every installed game with its folder
    public static string? FortniteExe()
    {
        var dat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\UnrealEngineLauncher\LauncherInstalled.dat");
        if (!File.Exists(dat)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(dat));
        foreach (var e in doc.RootElement.GetProperty("InstallationList").EnumerateArray())
            if (e.GetProperty("AppName").GetString() == "Fortnite")
            {
                var exe = Path.Combine(e.GetProperty("InstallLocation").GetString()!, @"FortniteGame\Binaries\Win64", FortniteProcess + ".exe");
                return File.Exists(exe) ? exe : null;
            }
        return null;
    }

    // physical GPUs only: remote and basic display adapters are not PCI devices
    public static int GpuCount() => int.Parse(Ps("@(Get-CimInstance Win32_VideoController | Where-Object PNPDeviceID -like 'PCI\\*').Count"));

    // ---------- ini ----------

    public static string? IniGet(string path, string section, string key) => IniValue(File.ReadAllLines(path), section, key);

    public static void IniSet(string id, string path, string section, string key, string value)
    {
        GameClosed();
        Backup.Remember(id, $"ini|{section}|{key}|{path}", IniGet(path, section, key));
        IniWrite(path, section, key, value);
    }

    static void IniWrite(string path, string section, string key, string? value)
    {
        GameClosed();
        File.WriteAllLines(path, IniEdit(File.ReadAllLines(path), section, key, value));
    }

    // shortcut: only Fortnite's ini is written; move this check to the caller when a second game is added
    static void GameClosed()
    {
        if (Process.GetProcessesByName(FortniteProcess).Length > 0)
            throw new InvalidOperationException(L.T("Close Fortnite first: it rewrites its settings file when it exits.",
                                                    "Feche o Fortnite antes: ele reescreve o arquivo de configurações ao sair."));
    }

    // header index, end of the section body (exclusive), key line; -1 when missing
    static (int Head, int End, int Line) IniFind(string[] lines, string section, string key)
    {
        int head = Array.FindIndex(lines, l => l.Trim() == $"[{section}]"), end = lines.Length, line = -1;
        if (head < 0) return (-1, -1, -1);
        for (var i = head + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('[')) { end = i; break; }
            if (line < 0 && lines[i].StartsWith(key + "=")) line = i;
        }
        return (head, end, line);
    }

    internal static string? IniValue(string[] lines, string section, string key)
    {
        var line = IniFind(lines, section, key).Line;
        return line < 0 ? null : lines[line][(key.Length + 1)..];
    }

    // null deletes the key; a new key goes after the last non-blank line of its section
    internal static string[] IniEdit(string[] lines, string section, string key, string? value)
    {
        var (head, end, line) = IniFind(lines, section, key);
        var list = lines.ToList();
        if (line >= 0)
        {
            if (value is null) list.RemoveAt(line);
            else list[line] = $"{key}={value}";
        }
        else if (value is not null && head < 0)
        {
            if (list.Count > 0 && list[^1].Trim() != "") list.Add("");
            list.AddRange([$"[{section}]", $"{key}={value}"]);
        }
        else if (value is not null)
        {
            var at = end;
            while (at > head + 1 && list[at - 1].Trim() == "") at--;
            list.Insert(at, $"{key}={value}");
        }
        return [.. list];
    }

    // ---------- system safety ----------

    public static void CreateRestorePoint() =>
        Ps("Checkpoint-Computer -Description 'msp-tweaker' -RestorePointType MODIFY_SETTINGS -WarningVariable w -WarningAction SilentlyContinue; if ($w) { throw $w[0].Message }");

    // ---------- undo ----------

    public static void Undo(string key, string? old)
    {
        var kind = key[..key.IndexOf('|')];
        var p = key.Split('|', 3);
        switch (kind)
        {
            case "reg": RegWrite(p[2], p[1], Decode(old)); break;
            case "nic": NicWrite([(p[2], p[1], old!)]); break;
            case "bcd": BcdWrite(p[1], old); break;
            case "plan": Run(Powercfg, "/setactive " + old); break;
            case "ini":
                var n = key.Split('|', 4);
                IniWrite(n[3], n[1], n[2], old);
                break;
            case "drs": DrsWrite(Convert.ToUInt32(p[1], 16), old is null ? null : uint.Parse(old)); break;
            case "task": TaskRemove(p[1]); break;
            case "pwr":
                var q = key.Split('|');
                var v = old!.Split(',');
                PowerWrite(q[1], q[2], q[3], int.Parse(v[0]), int.Parse(v[1]));
                break;
            default: throw new InvalidOperationException("unknown backup entry: " + key);
        }
    }
}

/// Original value of everything msp-tweaker changed, per tweak. Lives in HKLM so a standard user
/// cannot plant a "backup" that Revert would then apply as admin.
static class Backup
{
    public sealed record Entry(string Key, string? Old);

    const string Key = @"SOFTWARE\msp-tweaker";
    public const string Location = @"HKLM\" + Key;
    static readonly Dictionary<string, List<Entry>> Data = Load();

    static Dictionary<string, List<Entry>> Load()
    {
        using var k = Registry.LocalMachine.OpenSubKey(Key);
        return k?.GetValue("backup") is string json
            ? JsonSerializer.Deserialize<Dictionary<string, List<Entry>>>(json) ?? new()
            : new();
    }

    static void Save()
    {
        using var k = Registry.LocalMachine.CreateSubKey(Key, true);
        k.SetValue("backup", JsonSerializer.Serialize(Data));
    }

    public static bool Has(string id) { lock (Data) return Data.ContainsKey(id); }

    // keep the true original, not an earlier write of ours
    public static void Remember(string id, string key, string? old)
    {
        lock (Data)
        {
            if (!Data.TryGetValue(id, out var list)) Data[id] = list = [];
            if (list.Any(e => e.Key == key)) return;
            list.Add(new(key, old));
            Save();
        }
    }

    // undo in reverse order; failed entries stay so Revert can be retried
    public static void Restore(string id)
    {
        List<Entry> list;
        lock (Data)
        {
            if (!Data.TryGetValue(id, out var l)) return;
            list = [.. l];
        }
        var errors = new List<string>();
        foreach (var e in Enumerable.Reverse(list))
        {
            try
            {
                Sys.Undo(e.Key, e.Old);
                lock (Data)
                {
                    Data[id].Remove(e);
                    if (Data[id].Count == 0) Data.Remove(id);
                    Save();
                }
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
    }
}
