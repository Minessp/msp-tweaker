namespace MspTweaker;

/// Pure parser checks, no system access. Run: msp-tweaker.exe --selftest
static class SelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        void Check(bool ok, string name) { if (!ok) failures.Add(name); }

        Check(Sys.KvSet("VRROptimizeEnable=0;", "SwapEffectUpgradeEnable", "1") == "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", "KvSet keeps other keys");
        Check(Sys.KvSet("SwapEffectUpgradeEnable=0;AutoHDREnable=1;", "SwapEffectUpgradeEnable", "1") == "AutoHDREnable=1;SwapEffectUpgradeEnable=1;", "KvSet replaces without duplicating");
        Check(Sys.KvSet(null, "A", "1") == "A=1;", "KvSet on missing value");
        Check(Sys.KvGet("A=1;B=2;", "B") == "2" && Sys.KvGet("AB=1;", "A") is null && Sys.KvGet(null, "A") is null, "KvGet");

        const string powercfg = """
            Power Setting GUID: 893dee8e-2bef-41e0-89c6-b55d0929964c  (Estado mínimo do processador)
              Configuração Mínima Possível: 0x00000000
              Configuração Máxima Possível: 0x00000064
              Incremento de Configurações Possíveis: 0x00000001
            Índice de Configuração de Energia CA Atual: 0x00000064
            Índice de Configuração de Energia CC Atual: 0x00000005
            """;
        Check(Sys.ParseAcDc(powercfg) == (100, 5), "ParseAcDc reads the last two values (AC, DC) on localized output");

        const string bcd = """
            Carregador de Inicialização do Windows
            -------------------------------
            identifier              {current}
            description             Windows 11
            useplatformclock        Yes
            hypervisorlaunchtype    Auto
            """;
        var b = Sys.ParseBcd(bcd.Replace("\n", "\r\n"));
        Check(b["hypervisorlaunchtype"] == "Auto" && b.ContainsKey("USEPLATFORMCLOCK") && b.Count == 4, "ParseBcd skips the header and handles CRLF");

        Check(Sys.Encode(Sys.Decode("dword:2")) == "dword:2" && Sys.Decode("sz:a=1;b") is "a=1;b" && Sys.Decode(null) is null, "backup Encode/Decode");

        return failures;
    }
}
