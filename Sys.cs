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
