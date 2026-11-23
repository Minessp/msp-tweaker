using System.IO;

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

    static readonly Loc Fortnite = new("Game settings", "Configurações do jogo");
    const string FnUser = "/Script/FortniteGame.FortGameUserSettings", FnRhi = "D3DRHIPreference", FnSg = "ScalabilityGroups";
    static readonly (string Section, string Key, string Value)[] FnCompetitive =
    [
        (FnSg, "sg.ShadowQuality", "0"), (FnSg, "sg.GlobalIlluminationQuality", "0"),
        (FnSg, "sg.ReflectionQuality", "0"), (FnSg, "sg.PostProcessQuality", "0"),
        (FnUser, "bUseNanite", "False"), (FnUser, "bMotionBlur", "False"),
    ];

    const string BitLockerEn = " With BitLocker on, Msp Tweaker suspends protection for one reboot so Windows does not ask for the recovery key.";
    const string BitLockerPt = " Com BitLocker ligado, o Msp Tweaker suspende a proteção por um reinício para o Windows não pedir a chave de recuperação.";

    static Loc Same(string s) => new(s, s);

    static readonly Loc Nvidia = new("NVIDIA driver", "Driver NVIDIA"), Cpu = new("Processor", "Processador");
    public static bool IsGame(Tweak t) => t.Group == Fortnite;

    const string KernelKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    const uint DrsP2 = 0x50166C5E, DrsShaderCache = 0x00AC8497, DrsPrerender = 0x007BA09E, DrsUll = 0x10835000, DrsUllState = 0x0005F543;
    const string I225 = "I22[56]";

    static bool DrsIs(uint setting, Func<uint, bool> ok) => Sys.DrsGet(setting) is { } v && ok(v.Value);

    // Each one targets a measured queue or a known defect; the app only writes when the live state is worse than it should be.
    static readonly Tweak[] Active =
    [
        new("nv-p2", Nvidia, Stripe.Red,
            new("CUDA without dropping the memory clock", "CUDA sem derrubar o clock da memória"),
            new("NVIDIA GPU, playing with Discord, a browser, OBS or anything else that uses CUDA, NVENC or video decoding open.",
                "GPU NVIDIA, jogando com Discord, navegador, OBS ou qualquer coisa que use CUDA, NVENC ou decodificação de vídeo aberta."),
            new("The driver setting “CUDA – Force P2 State” is on by default: as soon as any app touches CUDA, the GPU memory drops to the P2 clock, even with a game running. NVIDIA confirmed it as the cause of the 2023 Discord downclock, measured at about 200 MHz on RTX 30 and over 500 MHz on the GTX 10 series. Off, the memory stays at full clock.",
                "A configuração “CUDA – Force P2 State” do driver vem ligada: assim que algum app toca em CUDA, a memória da GPU cai para o clock P2, mesmo com o jogo rodando. A NVIDIA confirmou que essa foi a causa da queda de clock do Discord em 2023, medida em cerca de 200 MHz nas RTX 30 e mais de 500 MHz na série GTX 10. Desligada, a memória fica no clock cheio."),
            new("Changes a driver setting that NVIDIA hides from its control panel. The clock drop is measured; the fps gain depends on how memory-bound the game is and was not measured. Compute or mining apps tuned around P2 may draw a little more power.",
                "Muda uma configuração do driver que a NVIDIA esconde do painel. A queda de clock é medida; o ganho de fps depende de quanto o jogo depende da memória e não foi medido. Apps de cálculo ou mineração ajustados para o P2 podem gastar um pouco mais de energia."),
            Same("NVIDIA global profile (NVAPI DRS)\nCUDA_FORCE_P2 0x50166C5E = 0"),
            false,
            () => Sys.HasNvidia() ? DrsIs(DrsP2, v => v == 0) : null,
            id => Sys.DrsSet(id, DrsP2, 0)),

        new("nv-shader", Nvidia, Stripe.None,
            new("10 GB NVIDIA shader cache", "Cache de shaders NVIDIA de 10 GB"),
            new("DirectX 12 and Vulkan games that stutter in the first matches, again after a while, or after playing other big games.",
                "Jogos DirectX 12 e Vulkan que engasgam nas primeiras partidas, de novo depois de um tempo, ou depois de jogar outros jogos grandes."),
            new("DX12 games compile shader pipelines while you play, and the driver keeps them in a disk cache. Fortnite alone compiles about 30,000 per match (Epic). When the cache is full the driver throws old ones away and they compile again mid-match, which shows as stutter. 10 GB keeps several large games cached at once.",
                "Jogos DX12 compilam os pipelines de shader durante o jogo, e o driver guarda tudo num cache em disco. Só o Fortnite compila cerca de 30 mil por partida (Epic). Com o cache cheio, o driver descarta os antigos e eles compilam de novo no meio da partida, e isso aparece como engasgo. Com 10 GB, vários jogos grandes cabem no cache ao mesmo tempo."),
            new("Uses up to 10 GB in %LOCALAPPDATA%\\NVIDIA\\DXCache. No Fortnite measurement exists; raising it fixed stutter in another UE5 game (Borderlands 4).",
                "Usa até 10 GB em %LOCALAPPDATA%\\NVIDIA\\DXCache. Não existe medição no Fortnite; aumentar o cache corrigiu engasgos em outro jogo UE5 (Borderlands 4)."),
            Same("NVIDIA global profile (NVAPI DRS)\nSHADER_CACHE_SIZE 0x00AC8497 = 10240 MB"),
            false,
            () => Sys.HasNvidia() ? DrsIs(DrsShaderCache, v => v >= 10240) : null,
            id => Sys.DrsSet(id, DrsShaderCache, 10240)),

        new("nv-latency", Nvidia, Stripe.None,
            new("Low latency for DX11 games without Reflex", "Baixa latência para jogos DX11 sem Reflex"),
            new("DirectX 9/11 games without NVIDIA Reflex that run GPU-bound (GPU near 100%).",
                "Jogos DirectX 9/11 sem NVIDIA Reflex que rodam limitados pela GPU (GPU perto de 100%)."),
            new("When the GPU is the bottleneck, the CPU queues frames ahead and every queued frame is input delay. This limits the queue to one frame and submits it just in time (the control panel's Ultra mode). Battle(non)sense measured the gain only in GPU-bound scenes.",
                "Quando a GPU é o gargalo, a CPU enfileira quadros à frente, e cada quadro na fila é atraso de comando. Isto limita a fila a um quadro e entrega o quadro na hora certa (o modo Ultra do painel). O Battle(non)sense mediu ganho só em cenas limitadas pela GPU."),
            new("DirectX 12 and Vulkan ignore it, and games with Reflex (Fortnite, Valorant, CS2) replace it. In CPU-bound games it can cost a little fps or add stutter; a frame cap below what your PC holds works even better.",
                "DirectX 12 e Vulkan ignoram, e jogos com Reflex (Fortnite, Valorant, CS2) substituem. Em jogos limitados pela CPU pode custar um pouco de fps ou causar engasgos; um limite de fps abaixo do que o PC sustenta funciona ainda melhor."),
            Same("NVIDIA global profile (NVAPI DRS)\nPRERENDERLIMIT 0x007BA09E = 1\n0x10835000 = 1, 0x0005F543 = 2 (Ultra)"),
            false,
            () => Sys.HasNvidia() ? DrsIs(DrsPrerender, v => v == 1) && DrsIs(DrsUll, v => v == 1) && DrsIs(DrsUllState, v => v == 2) : null,
            id =>
            {
                Sys.DrsSet(id, DrsPrerender, 1);
                Sys.DrsSet(id, DrsUll, 1);
                Sys.DrsSet(id, DrsUllState, 2);
            }),

        new("timer", Cpu, Stripe.Black,
            new("Global timer resolution", "Resolução de timer global"),
            new("RTSS or another external frame limiter, or games that stutter or drop fps when their window is covered.",
                "RTSS ou outro limitador de fps externo, ou jogos que engasgam ou caem de fps com a janela coberta."),
            new("Since Windows 11 a process only gets a fine timer if it asks for one itself, and loses it while its window is minimized or covered. Everything else sleeps in 15.6 ms steps, which breaks frame pacing in external limiters. This key restores the old system-wide behavior. Measured: Sleep(1) went from 15.5 ms to 1.5 ms (PC-Tuning).",
                "Desde o Windows 11, um processo só ganha timer fino se pedir ele mesmo, e perde enquanto a janela está minimizada ou coberta. O resto dorme em degraus de 15,6 ms, o que quebra o ritmo de quadros de limitadores externos. Esta chave devolve o comportamento antigo, para o sistema todo. Medido: Sleep(1) foi de 15,5 ms para 1,5 ms (PC-Tuning)."),
            new("Changes kernel timer behavior for the whole system: more timer interrupts and a little more idle power. Only matters while some process requests a high resolution. Not verified on every 24H2/25H2 build. Requires a reboot.",
                "Muda o comportamento do timer do kernel para o sistema todo: mais interrupções de timer e um pouco mais de consumo em ociosidade. Só faz diferença enquanto algum processo pede resolução alta. Não verificado em todos os builds 24H2/25H2. Exige reinício."),
            Same(KernelKey + "\\GlobalTimerResolutionRequests = 1"),
            true,
            () => Environment.OSVersion.Version.Build >= 22000 ? Sys.RegGet(KernelKey, "GlobalTimerResolutionRequests") is 1 : null,
            id => Sys.RegSet(id, KernelKey, "GlobalTimerResolutionRequests", 1)),

        new("x3d", Cpu, Stripe.None,
            new("Ryzen X3D: games on the V-Cache cores", "Ryzen X3D: jogos nos núcleos com V-Cache"),
            new("Ryzen 7900X3D, 7950X3D, 9900X3D or 9950X3D.",
                "Ryzen 7900X3D, 7950X3D, 9900X3D ou 9950X3D."),
            new("Only one of the two CCDs has the 3D V-Cache. AMD's driver parks the other one while a game runs, but only with the Balanced power plan, the chipset driver installed and Xbox Game Bar recognizing the game. When that fails, reviews measured 7 to 17% fewer fps (Far Cry 6 16.9%, Cyberpunk 2077 about 12%).",
                "Só um dos dois CCDs tem o 3D V-Cache. O driver da AMD estaciona o outro enquanto o jogo roda, mas só com o plano de energia Equilibrado, o driver de chipset instalado e a Xbox Game Bar reconhecendo o jogo. Quando isso falha, reviews mediram de 7 a 17% menos fps (Far Cry 6 16,9%, Cyberpunk 2077 cerca de 12%)."),
            new("Replaces a performance power plan with Balanced. For games Game Bar does not detect, open it with Win+G and tick “Remember this is a game”.",
                "Troca um plano de desempenho pelo Equilibrado. Para jogos que a Game Bar não detecta, abra com Win+G e marque “Lembrar que isto é um jogo”."),
            Same("powercfg /setactive " + Sys.BalancedGuid + "\nAMD 3D V-Cache Performance Optimizer service (amd3dvcache) present"),
            false,
            () => !Sys.DualCcdX3D() ? null
                : Sys.RegGet(@"HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache", "Start") is not null && Sys.ActiveScheme() == Sys.BalancedGuid,
            id =>
            {
                if (Sys.RegGet(@"HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache", "Start") is null)
                    throw new InvalidOperationException(L.T("Install the AMD chipset driver first: it brings the 3D V-Cache optimizer that parks the cores.",
                                                            "Instale o driver de chipset da AMD antes: ele traz o otimizador 3D V-Cache que estaciona os núcleos."));
                Sys.PlanActivate(id, Sys.BalancedGuid);
            }),

        new("wifi-scan", Network, Stripe.None,
            new("Wi-Fi without background scans", "Wi-Fi sem varredura em segundo plano"),
            new("Playing on Wi-Fi with ping spikes that repeat about every minute.",
                "Jogando no Wi-Fi com picos de ping que se repetem mais ou menos a cada minuto."),
            new("Even when connected, the WLAN service scans for other networks about every 60 s; the radio leaves your channel and the ping to the router jumps from under 1 ms to hundreds (800 to 1300 ms in forum captures). A small resident process keeps scanning off while connected, through the official WLAN API, and turns on media streaming mode.",
                "Mesmo conectado, o serviço de WLAN procura outras redes a cada 60 s mais ou menos; o rádio sai do seu canal e o ping até o roteador salta de menos de 1 ms para centenas (800 a 1300 ms em capturas de fórum). Um pequeno processo residente mantém a varredura desligada enquanto conectado, pela API oficial de WLAN, e liga o modo de streaming de mídia."),
            new("The network list in the taskbar refreshes less while connected, and roaming between mesh access points gets slower. Starts at logon as msp-tweaker.exe --wifi-guard: if you move the exe, apply again. Evidence is consistent forum captures, not a lab test.",
                "A lista de redes da barra de tarefas atualiza menos enquanto conectado, e a troca entre pontos de acesso mesh fica mais lenta. Inicia no logon como msp-tweaker.exe --wifi-guard: se mover o exe, aplique de novo. A evidência são capturas de fórum consistentes, não teste de laboratório."),
            new("Scheduled task “" + Sys.WifiTask + "” at logon\nWlanSetInterface: background_scan_enabled = 0, media_streaming_mode = 1",
                "Tarefa agendada “" + Sys.WifiTask + "” no logon\nWlanSetInterface: background_scan_enabled = 0, media_streaming_mode = 1"),
            false,
            () => Sys.HasWifi() ? Sys.TaskExists(Sys.WifiTask) : null,
            Sys.WifiGuardInstall),

        new("rss", Network, Stripe.Red,
            new("Intel I225/I226: repair RSS", "Intel I225/I226: reparar o RSS"),
            new("Intel I225-V or I226-V 2.5 GbE adapter whose driver install left RSS unconfigured.",
                "Placa Intel I225-V ou I226-V 2,5 GbE cuja instalação de driver deixou o RSS sem configuração."),
            new("Some I225/I226 driver installs never write the RSS keywords, so all receive processing runs on a single core (documented by djdallmann/GamingPCSetup). With RSS on, it spreads across cores and the DPC load on that core drops. The gain is in microseconds of CPU time, not in ping.",
                "Algumas instalações de driver da I225/I226 nunca gravam as chaves de RSS, e todo o processamento de recepção roda num núcleo só (documentado por djdallmann/GamingPCSetup). Com RSS ligado, ele se espalha entre os núcleos e a carga de DPC naquele núcleo cai. O ganho é em microssegundos de CPU, não em ping."),
            new("The connection drops for about 2 seconds while the adapter restarts. Revert takes effect after a reboot.",
                "A conexão cai por uns 2 segundos enquanto a placa reinicia. O reverter vale depois de reiniciar."),
            new("*RSS = 1, *NumRssQueues = 2 (adapter class key, only when missing)",
                "*RSS = 1, *NumRssQueues = 2 (chave de classe da placa, só se faltar)"),
            true,
            () => Sys.NicKeys(I225) is { Length: > 0 } keys ? keys.All(k => Sys.RegGet(k, "*RSS") is "1") : null,
            id =>
            {
                foreach (var k in Sys.NicKeys(I225))
                {
                    if (Sys.RegGet(k, "*RSS") is not "1") Sys.RegSet(id, k, "*RSS", "1");
                    if (Sys.RegGet(k, "*NumRssQueues") is null) Sys.RegSet(id, k, "*NumRssQueues", "2");
                }
                Sys.NicRestart(I225);
            }),

        new("fn-render", Fortnite, Stripe.None,
            new("Performance rendering mode", "Modo de renderização Desempenho"),
            new("PCs where Fortnite is limited by the GPU or CPU: GTX 10/16 series, integrated graphics, 4-core CPUs.",
                "PCs em que o Fortnite fica limitado pela GPU ou CPU: GTX série 10/16, vídeo integrado, CPUs de 4 núcleos."),
            new("Epic's lightweight renderer (the same feature level as the mobile version) cuts memory use and the CPU and GPU work per frame. On weak hardware it is the biggest fps gain the game offers; Epic documents it as the -FeatureLevelEs31 launch option.",
                "O renderizador leve da Epic (o mesmo nível de recursos da versão mobile) reduz o uso de memória e o trabalho de CPU e GPU por quadro. Em hardware fraco é o maior ganho de fps que o jogo oferece; a Epic documenta o mesmo efeito como a opção de inicialização -FeatureLevelEs31."),
            new("Simpler visuals, with no Nanite, Lumen or ray tracing. On a strong GPU, DirectX 12 can give steadier 1% lows: compare both in your own match.",
                "Visual mais simples, sem Nanite, Lumen nem ray tracing. Em GPU forte, o DirectX 12 pode dar 1% lows mais estáveis: compare os dois na sua partida."),
            Same($"GameUserSettings.ini\n[{FnRhi}] PreferredFeatureLevel=es31"),
            false,
            () => FnAll((FnRhi, "PreferredFeatureLevel", "es31")),
            id => FnSet(id, (FnRhi, "PreferredFeatureLevel", "es31"))),

        new("fn-reflex", Fortnite, Stripe.None,
            new("NVIDIA Reflex On + Boost", "NVIDIA Reflex Ligado + Boost"),
            new("NVIDIA GPU (GTX 900 series or newer).", "GPU NVIDIA (série GTX 900 ou mais nova)."),
            new("Reflex paces the CPU so frames never wait in the GPU queue; Boost keeps GPU clocks up when the CPU is the bottleneck. NVIDIA measured up to 46 to 54% less system latency in Fortnite (RTX 40/50) and up to 16% more from Boost in CPU-bound scenes.",
                "O Reflex dá o ritmo da CPU para os quadros nunca esperarem na fila da GPU; o Boost mantém o clock da GPU alto quando a CPU é o gargalo. A NVIDIA mediu até 46 a 54% menos latência de sistema no Fortnite (RTX 40/50) e até 16% a mais com o Boost em cenas limitadas pela CPU."),
            new("Vendor numbers, no independent test. Boost raises GPU power a little: on a hot laptop pick On without Boost in the game menu. That LatencyTweak2=2 means On + Boost is inferred from the menu's three states.",
                "Números do fabricante, sem teste independente. O Boost aumenta um pouco o consumo da GPU: num notebook quente, escolha Ligado sem Boost no menu do jogo. Que LatencyTweak2=2 é Ligado + Boost foi deduzido dos três estados do menu."),
            Same($"GameUserSettings.ini\n[{FnUser}] LatencyTweak2=2"),
            false,
            () => Sys.HasNvidia() ? FnAll((FnUser, "LatencyTweak2", "2")) : null,
            id => FnSet(id, (FnUser, "LatencyTweak2", "2"))),

        new("fn-cap", Fortnite, Stripe.None,
            new("Frame rate cap for G-Sync/FreeSync", "Limite de fps para G-Sync/FreeSync"),
            new("G-Sync or FreeSync monitor.", "Monitor com G-Sync ou FreeSync."),
            new("Above the refresh rate, frames leave the VRR range and fall into V-Sync or tearing, and the frame queue grows. A cap 3% below the refresh rate keeps every frame inside VRR with steady frame times (Blur Busters). The game's own limiter adds less delay than NVIDIA's or RTSS.",
                "Acima da taxa do monitor, os quadros saem da faixa do VRR e caem em V-Sync ou tearing, e a fila de quadros cresce. Um limite 3% abaixo da taxa mantém todo quadro dentro do VRR, com tempos de quadro estáveis (Blur Busters). O limitador do próprio jogo atrasa menos que o da NVIDIA ou o do RTSS."),
            new("Without VRR, a cap below the refresh rate adds delay: leave it unlimited and let Reflex work. The value comes from the current refresh rate of your main monitor.",
                "Sem VRR, um limite abaixo da taxa do monitor aumenta o atraso: deixe ilimitado e deixe o Reflex trabalhar. O valor vem da taxa atual do seu monitor principal."),
            Same($"GameUserSettings.ini\n[{FnUser}] FrameRateLimit = refresh × 0.97"),
            false,
            () =>
            {
                // shortcut: VRR support is not detected; under 100 Hz VRR is rare and a cap below the refresh rate only adds delay
                if (!File.Exists(Sys.FortniteIni) || Sys.RefreshRate() < 100) return null;
                var v = Sys.IniGet(Sys.FortniteIni, FnUser, "FrameRateLimit");
                return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cap)
                    && cap > 0 && cap < Sys.RefreshRate();
            },
            id => FnSet(id, (FnUser, "FrameRateLimit", $"{Math.Floor(Sys.RefreshRate() * 0.97):0}.000000"))),
    ];

    // replaced by the deep catalog (2026-11-23): obvious or unmeasured; kept so earlier Applies stay revertible
    static readonly Tweak[] Old =
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

        new("fn-vsync", Fortnite, Stripe.None,
            new("Fortnite: VSync off", "Fortnite: VSync desligado"),
            new("Anyone who wants the lowest input delay, especially on 60 to 144 Hz monitors.",
                "Quem quer o menor atraso de comando, principalmente em monitores de 60 a 144 Hz."),
            new("VSync holds each finished frame until the next screen refresh and lets a queue of frames build up, which adds up to a few frames of input delay. Off, the frame goes to the screen as soon as it is ready.",
                "O VSync segura cada quadro pronto até a próxima atualização da tela e deixa uma fila de quadros se formar, somando alguns quadros de atraso de comando. Desligado, o quadro vai para a tela assim que fica pronto."),
            new("Tearing can show up. With G-Sync or FreeSync, cap the frame rate a few fps below the refresh rate in the game so it stays inside the VRR range.",
                "Pode aparecer tearing (tela rasgada). Com G-Sync ou FreeSync, limite o fps no jogo alguns quadros abaixo da taxa do monitor para ficar dentro da faixa do VRR."),
            Same($"GameUserSettings.ini\n[{FnUser}] bUseVSync=False"),
            false,
            () => FnAll((FnUser, "bUseVSync", "False")),
            id => FnSet(id, (FnUser, "bUseVSync", "False"))),

        new("fn-gfx", Fortnite, Stripe.None,
            new("Fortnite: competitive graphics", "Fortnite: gráficos competitivos"),
            new("Competitive play where fps and visibility matter more than looks.",
                "Jogo competitivo, onde fps e visibilidade valem mais que o visual."),
            new("Shadows, global illumination and reflections are the most expensive settings per frame, and Nanite and post-processing add more on top. Turning them down frees the most GPU time of any graphics setting, and without shadows, players in dark areas are easier to see. View distance and textures stay as they are.",
                "Sombras, iluminação global e reflexos são os ajustes mais caros por quadro, e Nanite e pós-processamento somam mais. Baixá-los libera mais tempo de GPU do que qualquer outro ajuste gráfico, e sem sombras fica mais fácil ver jogadores em áreas escuras. Distância de visão e texturas ficam como estão."),
            new("The game clearly looks worse: flat lighting, no shadows, no reflections.",
                "O jogo fica visivelmente mais feio: luz chapada, sem sombras, sem reflexos."),
            Same("GameUserSettings.ini\n" + string.Join("\n", FnCompetitive.Select(k => $"{k.Key}={k.Value}"))),
            false,
            () => FnAll(FnCompetitive),
            id => FnSet(id, FnCompetitive)),

        new("fn-gpu", Fortnite, Stripe.None,
            new("Fortnite on the dedicated GPU", "Fortnite na GPU dedicada"),
            new("Laptops and PCs with integrated graphics plus a dedicated GPU.",
                "Notebooks e PCs com vídeo integrado e uma GPU dedicada."),
            new("On hybrid systems Windows can run the game on the integrated GPU, at a fraction of the fps. This sets the per-app preference in Settings > System > Display > Graphics to High performance, so Fortnite always gets the dedicated card.",
                "Em sistemas híbridos o Windows pode rodar o jogo na GPU integrada, com uma fração do fps. Isto grava a preferência por app de Configurações > Sistema > Tela > Gráficos como Alto desempenho, e o Fortnite sempre usa a placa dedicada."),
            new("On a laptop, the dedicated GPU drains the battery faster while the game runs.",
                "No notebook, a GPU dedicada drena a bateria mais rápido enquanto o jogo roda."),
            new(DxPrefs + "\\<FortniteClient-Win64-Shipping.exe> = GpuPreference=2;",
                DxPrefs + "\\<FortniteClient-Win64-Shipping.exe> = GpuPreference=2;"),
            false,
            () => Sys.FortniteExe() is not { } exe || Sys.GpuCount() < 2 ? null
                : Sys.KvGet(Sys.RegGet(DxPrefs, exe) as string, "GpuPreference") == "2",
            id =>
            {
                var exe = Sys.FortniteExe() ?? throw new InvalidOperationException(L.T("Fortnite is not installed.", "O Fortnite não está instalado."));
                Sys.RegSet(id, DxPrefs, exe, Sys.KvSet(Sys.RegGet(DxPrefs, exe) as string, "GpuPreference", "2"));
            }),
    ];

    // null when Fortnite has never run here (no settings file yet)
    static bool? FnAll(params (string Section, string Key, string Value)[] keys)
    {
        if (!File.Exists(Sys.FortniteIni)) return null;
        var lines = File.ReadAllLines(Sys.FortniteIni);
        return keys.All(k => string.Equals(Sys.IniValue(lines, k.Section, k.Key), k.Value, StringComparison.OrdinalIgnoreCase));
    }

    static void FnSet(string id, params (string Section, string Key, string Value)[] keys)
    {
        foreach (var k in keys)
            if (!string.Equals(Sys.IniGet(Sys.FortniteIni, k.Section, k.Key), k.Value, StringComparison.OrdinalIgnoreCase))
                Sys.IniSet(id, Sys.FortniteIni, k.Section, k.Key, k.Value);
    }

    // dropped for lack of measured gain; listed only while their backup exists so Revert still works
    static readonly Tweak[] Retired =
    [
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
    ];

    public static readonly Tweak[] All = [.. Active, .. Old.Concat(Retired).Where(t => Backup.Has(t.Id))];
}
