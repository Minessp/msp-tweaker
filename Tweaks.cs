namespace MspTweaker;

public enum Stripe { None, Red, Black }

/// State: true = already optimal, false = Windows default, null = not applicable to this PC.
public sealed record Tweak(
    string Id, Loc Group, Stripe Stripe, Loc Title,
    Loc Indication, Loc Effect, Loc Contraindication, Loc Dosage,
    bool Reboot, Func<bool?> State, Action<string> ApplyTo)
{
    public void Apply() => ApplyTo(Id);
    public void Revert() => Backup.Restore(Id);
}

static class Catalog
{
    static readonly Loc Power = new("Power", "Energia"), Gpu = new("GPU", "GPU"),
                        Network = new("Network", "Rede"), Kernel = new("Kernel & boot", "Kernel e boot");

    const string GameDvr = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR";
    const string DxPrefs = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    const string Graphics = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    const string DeliveryOpt = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    const string DeviceGuard = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard";
    const string Hvci = DeviceGuard + @"\Scenarios\HypervisorEnforcedCodeIntegrity";
    const string MemMgmt = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    const string WifiSub = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", WifiPsm = "12bbebe6-58d6-4636-95bb-3217ef867c1a";

    static readonly string[] Moderation = ["*InterruptModeration"];
    static readonly string[] Eee = ["*EEE", "EEE", "EEELinkAdvertisement", "EnableGreenEthernet", "AdvancedEEE", "GigaLite"];
    static readonly string[] ClockTweaks = ["useplatformclock", "disabledynamictick", "useplatformtick", "tscsyncpolicy"];

    const string BitLockerEn = " With BitLocker on, Msp Tweaker suspends protection for one reboot so Windows does not ask for the recovery key.";
    const string BitLockerPt = " Com BitLocker ligado, o Msp Tweaker suspende a proteção por um reinício para o Windows não pedir a chave de recuperação.";

    static Loc Same(string s) => new(s, s);

    public static readonly Tweak[] All =
    [
        new("plan", Power, Stripe.None,
            new("Max Performance power plan", "Plano Desempenho Máximo"),
            new("Desktop, or laptop on AC power, running games, real-time audio or anything sensitive to micro-stutter.",
                "Desktop, ou notebook na tomada, rodando jogos, áudio em tempo real ou qualquer coisa sensível a travadinhas."),
            new("Keeps the minimum CPU clock at 100% and turns off core parking and PCIe power saving (ASPM). Removes the clock ramp-up delay: the gain shows in 1% lows and DPC latency, little in average fps.",
                "Mantém o clock mínimo da CPU em 100% e desliga o estacionamento de núcleos e a economia do PCIe (ASPM). Some o atraso de subida de frequência: o ganho aparece nos 1% lows e na latência DPC, pouco na média de fps."),
            new("More power draw and heat at idle. Drains the battery faster. Laptops with Modern Standby may refuse the plan.",
                "Mais consumo e calor em ociosidade. Na bateria, drena mais rápido. Em notebooks com Modern Standby o Windows pode recusar o plano."),
            Same($"powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 {Sys.PlanGuid}\npowercfg /setactive {Sys.PlanGuid}"),
            false,
            () => Sys.PowerGet("SUB_PROCESSOR", "PROCTHROTTLEMIN").Ac == 100,
            Sys.PlanApply),

        new("recording", Gpu, Stripe.None,
            new("Background recording off", "Gravação em segundo plano desligada"),
            new("Anyone not using Xbox Game Bar's “record what happened”.",
                "Quem não usa o “gravar o que aconteceu” da Xbox Game Bar."),
            new("With continuous recording on, the GPU encodes video all the time, costing fps and VRAM. Off, the cost is zero. If it is already off (the Windows default), this tweak has nothing to do.",
                "Com a gravação contínua ligada, a GPU codifica vídeo o tempo todo, custando fps e VRAM. Desligada, custo zero. Se já está desligada (padrão do Windows), este ajuste não tem o que fazer."),
            new("You lose the Win+Alt+G shortcut that saves the last minutes of gameplay.",
                "Perde o atalho Win+Alt+G de salvar os últimos minutos de jogo."),
            Same(GameDvr + @"\HistoricalCaptureEnabled = 0"),
            false,
            () => Sys.RegGet(GameDvr, "HistoricalCaptureEnabled") is not 1,
            id => Sys.RegSet(id, GameDvr, "HistoricalCaptureEnabled", 0)),

        new("flip", Gpu, Stripe.None,
            new("Optimizations for windowed games", "Otimização para jogos em janela"),
            new("DirectX 10/11 games in windowed or borderless fullscreen.",
                "Jogos DirectX 10/11 em janela ou em tela cheia sem borda."),
            new("Swaps the legacy blit presentation model for flip model: lower frame latency, plus VRR and Auto HDR in windowed mode. Described by Microsoft on the DirectX Blog (2022).",
                "Troca o modelo de apresentação antigo (blit) pelo flip model: menos latência de quadro, e libera VRR e Auto HDR em janela. Recurso descrito pela Microsoft no DirectX Blog (2022)."),
            new("An occasional old game may show a black screen or a broken overlay. If that happens, revert.",
                "Um ou outro jogo antigo pode mostrar tela preta ou overlay quebrado. Se acontecer, reverta."),
            new(DxPrefs + "\\DirectXUserGlobalSettings += SwapEffectUpgradeEnable=1;\n(other keys in the string are preserved)",
                DxPrefs + "\\DirectXUserGlobalSettings += SwapEffectUpgradeEnable=1;\n(as outras chaves da string são preservadas)"),
            false,
            () => Sys.KvGet(Sys.RegGet(DxPrefs, "DirectXUserGlobalSettings") as string, "SwapEffectUpgradeEnable") == "1",
            id => Sys.RegSet(id, DxPrefs, "DirectXUserGlobalSettings",
                Sys.KvSet(Sys.RegGet(DxPrefs, "DirectXUserGlobalSettings") as string, "SwapEffectUpgradeEnable", "1"))),

        new("hags", Gpu, Stripe.Red,
            new("Hardware-accelerated GPU scheduling", "Agendamento de GPU acelerado por hardware"),
            new("GPU and driver with support (WDDM 2.7+). Required for DLSS Frame Generation.",
                "GPU e driver com suporte (WDDM 2.7+). Obrigatório para DLSS Frame Generation."),
            new("The GPU manages its own work queue, taking load off the CPU. In public tests the result varies by game, from zero to a few percent, sometimes negative: measure in your game.",
                "A GPU passa a gerenciar a própria fila de trabalho, tirando carga da CPU. Em testes públicos o resultado varia por jogo, de zero a alguns por cento, às vezes negativo: meça no seu jogo."),
            new("Causes stutter or crashes with some drivers; revert if you notice. Does nothing without GPU support. Requires a reboot.",
                "Com alguns drivers causa stutter ou crash; reverta se notar. Sem suporte da GPU, não faz nada. Exige reinício."),
            Same(Graphics + @"\HwSchMode = 2"),
            true,
            () => Sys.RegGet(Graphics, "HwSchMode") is 2,
            id => Sys.RegSet(id, Graphics, "HwSchMode", 2)),

        new("wifi", Network, Stripe.None,
            new("Wi-Fi without power saving", "Wi-Fi sem economia de energia"),
            new("Laptop gaming on battery over Wi-Fi.",
                "Notebook jogando na bateria pelo Wi-Fi."),
            new("In power-saving mode the adapter dozes between router beacons (~100 ms) and ping gets spikes. On AC power Windows already uses maximum performance: this tweak only changes battery behavior.",
                "No modo economia, a placa cochila entre os beacons do roteador (~100 ms) e o ping ganha picos. Na tomada o Windows já usa desempenho máximo: este ajuste só muda a bateria."),
            new("Battery drains faster with Wi-Fi active. Applies to the active power plan; if you switch plans, apply again.",
                "A bateria dura menos com o Wi-Fi ativo. Vale para o plano de energia ativo; se trocar de plano, aplique de novo."),
            Same($"powercfg /setacvalueindex SCHEME_CURRENT {WifiSub} {WifiPsm} 0\npowercfg /setdcvalueindex SCHEME_CURRENT {WifiSub} {WifiPsm} 0"),
            false,
            () => Sys.HasWifi() ? Sys.PowerGet(WifiSub, WifiPsm) == (0, 0) : null,
            id => Sys.PowerSet(id, WifiSub, WifiPsm, 0)),

        new("moderation", Network, Stripe.Red,
            new("Ethernet without interrupt moderation", "Ethernet sem moderação de interrupções"),
            new("Competitive gaming over cable, with CPU headroom.",
                "Jogo competitivo no cabo, com CPU sobrando."),
            new("The adapter stops batching packets before signaling the CPU, so each packet arrives tens to hundreds of microseconds sooner. It does not change ping in milliseconds: it cuts delay and jitter inside the PC.",
                "A placa para de juntar pacotes antes de avisar a CPU, e cada pacote chega dezenas a centenas de microssegundos antes. Não muda o ping em milissegundos: reduz atraso e jitter dentro do PC."),
            new("More interrupts and CPU use under heavy traffic (large downloads). The connection drops for about 2 seconds when applying and reverting.",
                "Mais interrupções e uso de CPU sob tráfego pesado (downloads grandes). A conexão cai por uns 2 segundos ao aplicar e ao reverter."),
            new("Set-NetAdapterAdvancedProperty -RegistryKeyword *InterruptModeration -RegistryValue 0\n(only physical Ethernet adapters that expose the property)",
                "Set-NetAdapterAdvancedProperty -RegistryKeyword *InterruptModeration -RegistryValue 0\n(só placas Ethernet físicas que expõem a propriedade)"),
            false,
            () => Sys.NicAll(Moderation, "0"),
            id => Sys.NicSet(id, Moderation, "0")),

        new("eee", Network, Stripe.Red,
            new("Ethernet without power saving (EEE / Green)", "Ethernet sem economia de energia (EEE / Green)"),
            new("Ping spikes or drops on cable, especially with Realtek adapters.",
                "Quedas ou picos de ping no cabo, principalmente com placas Realtek."),
            new("Energy Efficient Ethernet puts the link to sleep when idle. Waking up costs microseconds and, with some adapter/switch combinations, causes spikes and link drops. Off, the link stays awake.",
                "O Energy Efficient Ethernet põe o link para dormir quando ocioso. Acordar custa microssegundos e, em algumas combinações de placa e switch, gera picos e quedas de link. Desligado, o link fica sempre acordado."),
            new("Uses less than 1 W more. The connection drops for about 2 seconds when applying and reverting.",
                "Gasta menos de 1 W a mais. A conexão cai por uns 2 segundos ao aplicar e ao reverter."),
            new(string.Join(", ", Eee) + " = 0\n(only the ones your adapter has)",
                string.Join(", ", Eee) + " = 0\n(só as que a sua placa tiver)"),
            false,
            () => Sys.NicAll(Eee, "0"),
            id => Sys.NicSet(id, Eee, "0")),

        new("update", Network, Stripe.None,
            new("Windows Update does not saturate the connection", "Windows Update não satura a conexão"),
            new("Ping that spikes out of nowhere mid-game, with no other device using the network.",
                "Ping que dispara do nada no meio do jogo, sem outro aparelho usando a rede."),
            new("A download that fills the link fills the router's queue (bufferbloat), and every game packet waits in it. Capping background downloads at 20% of measured bandwidth and cutting P2P upload leaves room.",
                "Um download que lota o link enche a fila do roteador (bufferbloat), e todo pacote do jogo espera nessa fila. Limitar o segundo plano a 20% da banda medida e cortar o upload P2P deixa espaço livre."),
            new("Updates download more slowly. Does not fix bufferbloat caused by other devices at home: only SQM on the router does.",
                "Updates baixam mais devagar. Não resolve o bufferbloat causado por outros aparelhos da casa: para isso, só SQM no roteador."),
            Same(DeliveryOpt + "\\DODownloadMode = 0\n" + DeliveryOpt + "\\DOPercentageMaxBackgroundBandwidth = 20"),
            false,
            () => Sys.RegGet(DeliveryOpt, "DODownloadMode") is 0 && Sys.RegGet(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth") is int p && p is > 0 and <= 20,
            id =>
            {
                Sys.RegSet(id, DeliveryOpt, "DODownloadMode", 0);
                Sys.RegSet(id, DeliveryOpt, "DOPercentageMaxBackgroundBandwidth", 20);
            }),

        new("hvci", Kernel, Stripe.Black,
            new("Memory Integrity (HVCI) off", "Integridade de Memória (HVCI) desligada"),
            new("Personal gaming PC, where the measured gain is worth more than the extra protection.",
                "PC pessoal de jogo, onde o ganho medido vale mais que a proteção extra."),
            new("With HVCI, all kernel code is validated by the hypervisor; turning it off removes that cost. Microsoft itself suggests turning it off for gaming in “Options to optimize gaming performance in Windows 11” (2023). Public tests measured from zero to double-digit % depending on the game.",
                "Com HVCI, todo código de kernel é validado pelo hipervisor; desligar tira esse custo. A própria Microsoft sugere desligar para jogos no guia “Options to optimize gaming performance in Windows 11” (2023). Testes públicos mediram de zero a dois dígitos de % conforme o jogo."),
            new("Malicious or vulnerable drivers can load without this barrier. Corporate policies and some anti-cheats may require HVCI on. Requires a reboot.",
                "Drivers maliciosos ou vulneráveis passam a carregar sem essa barreira. Políticas corporativas e alguns anti-cheats podem exigir HVCI ligado. Exige reinício."),
            Same(Hvci + @"\Enabled = 0"),
            true,
            () => Sys.Ps(@"(Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard).SecurityServicesRunning -contains 2") == "False",
            id => Sys.RegSet(id, Hvci, "Enabled", 0)),

        new("vbs", Kernel, Stripe.Black,
            new("Hypervisor and VBS off", "Hipervisor e VBS desligados"),
            new("Anyone not using WSL2, Docker Desktop, Hyper-V, Windows Sandbox or a Hyper-V-based Android emulator.",
                "Quem não usa WSL2, Docker Desktop, Hyper-V, Windows Sandbox nem emulador Android baseado em Hyper-V."),
            new("With VBS, all of Windows runs as a guest of a hypervisor, and context switches and TLB misses cost more. Turning it off removes the layer. The same Microsoft guide (2023) suggests turning off Virtual Machine Platform for gaming.",
                "Com VBS, o Windows inteiro roda como convidado de um hipervisor, e trocas de contexto e falhas de TLB custam mais. Desligar remove a camada. O mesmo guia da Microsoft (2023) sugere desligar a Plataforma de Máquina Virtual para jogos."),
            new("Breaks WSL2, Docker Desktop, Hyper-V, Sandbox and Windows Hello Enhanced Sign-in Security. Also turns off Memory Integrity. If VBS is enforced by corporate policy or UEFI lock, it will not turn off. Requires a reboot." + BitLockerEn,
                "Quebra WSL2, Docker Desktop, Hyper-V, Sandbox e o Windows Hello com segurança aprimorada. Desliga junto a Integridade de Memória. Se a VBS foi imposta por política corporativa ou UEFI lock, não desliga. Exige reinício." + BitLockerPt),
            Same("bcdedit /set {current} hypervisorlaunchtype off\n" + DeviceGuard + @"\EnableVirtualizationBasedSecurity = 0"),
            true,
            () => Sys.Ps("(Get-CimInstance Win32_ComputerSystem).HypervisorPresent") == "False",
            id =>
            {
                Sys.BcdSet(id, "hypervisorlaunchtype", "off");
                Sys.RegSet(id, DeviceGuard, "EnableVirtualizationBasedSecurity", 0);
            }),

        new("spectre", Kernel, Stripe.Black,
            new("Spectre/Meltdown mitigations off", "Mitigações Spectre/Meltdown desligadas"),
            new("Intel up to 8th gen (Skylake to Coffee Lake), on a gaming-only PC.",
                "Intel até a 8ª geração (Skylake a Coffee Lake), em PC só para jogo."),
            new("The 2018 mitigations make every kernel entry more expensive (disk, network, system calls). On old Intel the cost is measurable; on CPUs with hardware fixes (Intel 9th gen onward, AMD Zen 2 onward) the gain is close to zero.",
                "As mitigações de 2018 encarecem cada entrada no kernel (disco, rede, chamadas de sistema). Nas Intel antigas o custo é mensurável; em CPUs com correção em hardware (Intel 9ª geração em diante, AMD Zen 2 em diante) o ganho fica perto de zero."),
            new("Opens the machine to side-channel attacks that read other processes' memory. Requires a reboot.",
                "Abre a máquina para ataques de canal lateral, que leem memória de outros processos. Exige reinício."),
            Same(MemMgmt + "\\FeatureSettingsOverride = 3\n" + MemMgmt + "\\FeatureSettingsOverrideMask = 3"),
            true,
            () => Sys.RegGet(MemMgmt, "FeatureSettingsOverride") is 3 && Sys.RegGet(MemMgmt, "FeatureSettingsOverrideMask") is 3,
            id =>
            {
                Sys.RegSet(id, MemMgmt, "FeatureSettingsOverride", 3);
                Sys.RegSet(id, MemMgmt, "FeatureSettingsOverrideMask", 3);
            }),

        new("msi", Kernel, Stripe.Black,
            new("Message-signaled interrupts (MSI) on GPU and network", "Interrupções por mensagem (MSI) na GPU e na rede"),
            new("High DPC latency in LatencyMon pointing at the video or network driver.",
                "Latência DPC alta no LatencyMon apontando o driver de vídeo ou de rede."),
            new("Swaps line-based interrupts (IRQ, which may be shared) for PCIe messages: no IRQ sharing and lower interrupt latency. Modern NVIDIA, AMD and Intel drivers already enable it; this only changes what is off.",
                "Troca a interrupção por linha (IRQ, que pode ser compartilhada) por mensagem PCIe: sem disputa de IRQ e menos latência de interrupção. Drivers modernos de NVIDIA, AMD e Intel já ligam sozinhos; aqui só muda o que estiver desligado."),
            new("If the device does not truly support MSI, it may fail to start, even with a black screen at boot. To recover: Safe Mode, open Msp Tweaker and revert. Requires a reboot.",
                "Se o dispositivo não suportar MSI de verdade, ele pode não iniciar, inclusive com tela preta no boot. Para sair: Modo Seguro, abrir o Msp Tweaker e reverter. Exige reinício."),
            new(@"HKLM\SYSTEM\CurrentControlSet\Enum\PCI\<device>\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties\MSISupported = 1",
                @"HKLM\SYSTEM\CurrentControlSet\Enum\PCI\<dispositivo>\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties\MSISupported = 1"),
            true,
            () =>
            {
                var devices = Sys.MsiDevices();
                return devices.Length == 0 ? null : devices.All(d => Sys.RegGet(Sys.MsiKey(d), "MSISupported") is 1);
            },
            id =>
            {
                foreach (var d in Sys.MsiDevices())
                    if (Sys.RegGet(Sys.MsiKey(d), "MSISupported") is not 1)
                        Sys.RegSet(id, Sys.MsiKey(d), "MSISupported", 1);
            }),

        new("clock", Kernel, Stripe.Red,
            new("Clean boot clock “tweaks”", "Limpar “tweaks” de relógio do boot"),
            new("A PC that has been through other optimizers.",
                "PC que já passou por outros otimizadores."),
            new("useplatformclock forces HPET as the system clock: every time read gets much slower, with fps loss measured in games. disabledynamictick, useplatformtick and tscsyncpolicy have no proven gain on Windows 10/11. Removing them restores the default.",
                "useplatformclock força o HPET como relógio do sistema: cada leitura de tempo fica muito mais lenta, com queda de fps medida em jogos. disabledynamictick, useplatformtick e tscsyncpolicy não têm ganho comprovado no Windows 10/11. Remover devolve o padrão."),
            new("Only keep one of these if it was set for a specific reason, such as hardware debugging. Requires a reboot." + BitLockerEn,
                "Só faz sentido manter algum desses se foi posto por um motivo específico, como depuração de hardware. Exige reinício." + BitLockerPt),
            new("bcdedit /deletevalue {current} <element>\n(" + string.Join(", ", ClockTweaks) + ", only those present)",
                "bcdedit /deletevalue {current} <elemento>\n(" + string.Join(", ", ClockTweaks) + ", só os que existirem)"),
            true,
            () =>
            {
                var bcd = Sys.Bcd();
                return !ClockTweaks.Any(bcd.ContainsKey);
            },
            id =>
            {
                var bcd = Sys.Bcd();
                foreach (var e in ClockTweaks)
                    if (bcd.ContainsKey(e)) Sys.BcdSet(id, e, null);
            }),
    ];
}
