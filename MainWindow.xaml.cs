using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using Microsoft.Win32;

namespace MspTweaker;

public partial class MainWindow : Window
{
    const string Prefs = @"HKCU\Software\msp-tweaker";
    readonly List<Card> cards = Catalog.All.Select(t => new Card(t)).ToList();
    readonly ListCollectionView systemView, gamesView;
    readonly string? cpu;
    string? gamePath;
    bool gamePathRead;
    TaskCompletionSource<bool>? confirmation;
    bool restorePointDone; // created, or knowingly skipped, in this session

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public MainWindow()
    {
        Theme.Set(Sys.RegGet(Prefs, "theme") is string theme ? theme == "dark"
            : Sys.RegGet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme") is 0);
        if (Sys.RegGet(Prefs, "lang") is string lang) L.Set(lang == "pt");

        InitializeComponent();
        systemView = View(c => !Catalog.IsGame(c.T));
        gamesView = View(c => Catalog.IsGame(c.T));
        List.ItemsSource = systemView;

        const string nt = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        cpu = Regex.Replace(Sys.CpuName(), @"\((R|TM)\)|\s*CPU\s*@.*$", "").Trim() is { Length: > 0 } s ? s : null;
        SpecWin.Text = $"{Sys.RegGet(nt, "DisplayVersion")}, build {Sys.RegGet(nt, "CurrentBuild")}";
        BackupLocation.Text = Backup.Location;
        SetLanguage(L.Pt);
        ThemeButton.Content = Theme.Dark ? "\uE706" : "\uE708"; // sun leads to light, moon to dark

        Loaded += async (_, _) =>
        {
            foreach (var c in cards) _ = Read(c);
            SpecGpu.Text = await Task.Run(() =>
            {
                try { return Sys.Ps("(Get-CimInstance Win32_VideoController | Where-Object PNPDeviceID -like 'PCI\\*' | ForEach-Object Name) -join ', '"); }
                catch (Exception) { return "?"; }
            });
            gamePath = await Task.Run(() => { try { return Sys.FortniteExe(); } catch (Exception) { return null; } });
            gamePathRead = true;
            UpdateGamePath();
        };
        // a maximized WindowStyle=None window overflows the screen by the resize border
        StateChanged += (_, _) => Root.Margin = new Thickness(WindowState == WindowState.Maximized ? 7 : 0);
        SourceInitialized += (_, _) =>
        {
            var round = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND
            if (DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)) != 0)
                BorderThickness = new Thickness(1); // before Windows 11, DWM draws no corners or border
        };
    }

    ListCollectionView View(Predicate<Card> keep)
    {
        var v = new ListCollectionView(cards) { Filter = o => keep((Card)o) };
        v.GroupDescriptions.Add(new PropertyGroupDescription("T.Group"));
        return v;
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (List is null) return; // fires during InitializeComponent
        var games = TabGames.IsChecked == true;
        List.ItemsSource = games ? gamesView : systemView;
        SpecPanel.Visibility = games ? Visibility.Collapsed : Visibility.Visible;
        GamePanel.Visibility = DiagPanel.Visibility = games ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
    }

    void SetLanguage(bool pt)
    {
        L.Set(pt);
        LangEn.SetResourceReference(ForegroundProperty, pt ? "Subtle" : "Text");
        LangPt.SetResourceReference(ForegroundProperty, pt ? "Text" : "Subtle");
        SpecCpu.Text = cpu ?? L.T("Unknown", "Desconhecida");
        UpdateSummary();
        UpdateGamePath();
        foreach (var c in cards) c.Refresh(); // Loc values only re-translate when their bindings refresh
        systemView.Refresh(); // group headers are Loc too, and only rebuild with the view
        gamesView.Refresh();
    }

    // the exe sits in <install>\FortniteGame\Binaries\Win64
    void UpdateGamePath() =>
        GamePath.Text = !gamePathRead ? L.T("Looking for the game…", "Procurando o jogo…")
            : gamePath is null ? L.T("Not installed on this PC.", "Não instalado neste PC.")
            : L.T("Installed in ", "Instalado em ") + Path.GetFullPath(Path.Combine(Path.GetDirectoryName(gamePath)!, @"..\..\.."));

    void UpdateSummary()
    {
        var games = TabGames.IsChecked == true;
        var shown = cards.Where(c => Catalog.IsGame(c.T) == games).ToList();
        if (shown.Any(c => c.Busy && c.State is null)) { Summary.Text = L.T("Reading Windows…", "Lendo o Windows…"); return; }
        var open = shown.Count(c => c.State == false);
        Summary.Text = open switch
        {
            0 => L.T("Nothing left to improve here", "Nada a melhorar aqui"),
            1 => L.T("1 tweak can improve this PC", "1 ajuste pode melhorar este PC"),
            _ => L.T($"{open} tweaks can improve this PC", $"{open} ajustes podem melhorar este PC"),
        };
    }

    async Task Read(Card c)
    {
        c.Busy = true;
        try { c.State = await Task.Run(c.T.State); }
        catch (Exception ex) { c.State = null; c.Error = L.T("Could not read the state: ", "Não consegui ler o estado: ") + Msg(ex); }
        finally { c.Busy = false; UpdateSummary(); }
    }

    async Task Execute(Card c, Action action, string work)
    {
        c.Error = null;
        c.Work = work;
        c.Busy = true;
        try
        {
            await Task.Run(action);
            if (c.T.Reboot) c.RebootPending = true;
        }
        catch (Exception ex) { c.Error = Msg(ex); }
        c.Work = null;
        Pending.Visibility = cards.Any(x => x.RebootPending) ? Visibility.Visible : Visibility.Collapsed;
        await Read(c);
    }

    async void Switch_Click(object sender, RoutedEventArgs e)
    {
        var c = (Card)((FrameworkElement)sender).DataContext;
        List.SelectedItem = c;
        if (c.IsOn) { await Execute(c, c.T.Revert, L.T("Reverting…", "Revertendo…")); return; }
        if (c.T.Stripe != Stripe.None)
        {
            var ok = await Confirm(c.T.Stripe, c.T.Title.ToString(), L.T("Trade-offs: ", "Contras: ") + c.T.Contraindication, c.T.Dosage.ToString(),
                c.T.Stripe == Stripe.Black ? L.T("I understand the risk, apply", "Entendo o risco, aplicar") : L.T("Apply", "Aplicar"));
            if (!ok || !await EnsureRestorePoint(c)) return;
        }
        await Execute(c, c.T.Apply, L.T("Applying…", "Aplicando…"));
    }

    async Task<bool> EnsureRestorePoint(Card c)
    {
        if (restorePointDone) return true;
        c.Work = L.T("Creating restore point…", "Criando ponto de restauração…");
        c.Busy = true;
        try
        {
            await Task.Run(Sys.CreateRestorePoint);
            return restorePointDone = true;
        }
        catch (Exception ex)
        {
            return restorePointDone = await Confirm(Stripe.None, L.T("No restore point", "Sem ponto de restauração"),
                L.T("Windows did not create the restore point: ", "O Windows não criou o ponto de restauração: ") + Msg(ex) + "\n\n" +
                L.T("Msp Tweaker's Revert still works, but without the system's safety net.",
                    "O Reverter do Msp Tweaker continua funcionando, mas fica sem a rede de segurança do sistema."),
                null, L.T("Continue without it", "Seguir sem ponto"));
        }
        finally { c.Work = null; c.Busy = false; }
    }

    async void Diag_Click(object sender, RoutedEventArgs e)
    {
        DiagRun.IsEnabled = false;
        DiagResults.Visibility = Visibility.Visible;
        DiagLan.Text = DiagWan.Text = L.T("Measuring…", "Medindo…");
        DiagVerdict.Visibility = Visibility.Collapsed;
        try
        {
            var gw = await Task.Run(Sys.Gateway);
            var lan = gw is null ? null : await Task.Run(() => Sys.PingTest(gw, "lan"));
            var wan = await Task.Run(() => Sys.PingTest(System.Net.IPAddress.Parse("1.1.1.1"), "wan"));
            DiagLan.Text = lan is null ? L.T("No gateway found", "Gateway não encontrado") : Describe(lan);
            DiagWan.Text = Describe(wan);

            // a bad first hop means Wi-Fi or the cable; a clean first hop with trouble past it points at the ISP or the route
            var lanBad = lan is { } l && (l.Lost > 0 || l.JitterMs > 5 || l.AvgMs > 10);
            var wanBad = wan.Lost * 100 > wan.Sent || wan.JitterMs > 10;
            DiagVerdict.Text = lanBad
                ? L.T("The problem starts inside your home: loss or jitter already on the way to the router. Use a cable, or try the Wi-Fi tweak in System.",
                      "O problema começa dentro de casa: perda ou variação já no caminho até o roteador. Use cabo, ou teste o ajuste de Wi-Fi em Sistema.")
                : wanBad
                    ? L.T("Your home network is clean; loss or jitter starts past the router (ISP or route). Check whether another device is uploading.",
                          "Sua rede de casa está limpa; a perda ou variação começa depois do roteador (provedor ou rota). Veja se outro aparelho está enviando arquivos.")
                    : L.T("No loss and steady latency. For the server ping, turn on Net Debug Stats in Fortnite's game UI settings.",
                          "Sem perda e latência estável. Para o ping do servidor, ligue o Net Debug Stats nas configurações de interface do Fortnite.");
            DiagVerdict.SetResourceReference(TextBlock.ForegroundProperty, lanBad || wanBad ? "Warn" : "Muted");
            DiagVerdict.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { DiagLan.Text = DiagWan.Text = Msg(ex); }
        finally { DiagRun.IsEnabled = true; }
    }

    static string Describe(Sys.PingStats s) =>
        s.Lost == s.Sent ? L.T("No reply", "Sem resposta")
        : L.T($"{s.AvgMs:0} ms, jitter {s.JitterMs:0.0} ms, {100.0 * s.Lost / s.Sent:0}% loss",
              $"{s.AvgMs:0} ms, variação {s.JitterMs:0.0} ms, {100.0 * s.Lost / s.Sent:0}% de perda");

    async void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (await Confirm(Stripe.None, L.T("Reboot now?", "Reiniciar agora?"),
                L.T("Save your open work. Windows restarts in 5 seconds.", "Salve o que estiver aberto. O Windows reinicia em 5 segundos."),
                null, L.T("Reboot", "Reiniciar")))
            Sys.Run(Sys.Shutdown, "/r /t 5");
    }

    Task<bool> Confirm(Stripe stripe, string title, string body, string? dosage, string ok)
    {
        OvRisk.Visibility = stripe == Stripe.None ? Visibility.Collapsed : Visibility.Visible;
        OvRiskIcon.Text = stripe == Stripe.Black ? "\uE945" : "\uE7BA";
        OvRiskIcon.SetResourceReference(TextBlock.ForegroundProperty, stripe == Stripe.Black ? "Danger" : "Warn");
        OvRiskText.SetResourceReference(TextBlock.ForegroundProperty, stripe == Stripe.Black ? "Danger" : "Warn");
        OvRiskText.Text = Card.RiskLabel(stripe);
        OvTitle.Text = title;
        OvBody.Text = body;
        OvDosageBox.Visibility = dosage is null ? Visibility.Collapsed : Visibility.Visible;
        OvDosage.Text = dosage;
        OvOk.Content = ok;
        OvOk.Style = (Style)FindResource(stripe == Stripe.Black ? "DangerButton" : "Primary");

        Main.IsEnabled = false; // traps keyboard focus in the dialog
        Overlay.Visibility = Visibility.Visible;
        OvCancel.Focus(); // the safe default is to change nothing
        confirmation = new();
        return confirmation.Task;
    }

    void Answer(bool r)
    {
        Overlay.Visibility = Visibility.Collapsed;
        Main.IsEnabled = true;
        confirmation?.TrySetResult(r);
    }

    static void SavePref(string name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\msp-tweaker", true);
        k.SetValue(name, value);
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        Theme.Set(!Theme.Dark);
        ThemeButton.Content = Theme.Dark ? "\uE706" : "\uE708";
        SavePref("theme", Theme.Dark ? "dark" : "light");
    }

    void LangEn_Click(object sender, RoutedEventArgs e) { SetLanguage(false); SavePref("lang", "en"); }
    void LangPt_Click(object sender, RoutedEventArgs e) { SetLanguage(true); SavePref("lang", "pt"); }
    void OvOk_Click(object sender, RoutedEventArgs e) => Answer(true);
    void OvCancel_Click(object sender, RoutedEventArgs e) => Answer(false);
    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    static string Msg(Exception ex) => ex.GetBaseException().Message.Trim();
}

/// Visible state of one tweak. Every change notifies everything: few fields, few cards.
public sealed class Card(Tweak t) : INotifyPropertyChanged
{
    public Tweak T { get; } = t;

    bool? state;
    bool busy = true, rebootPending;
    string? error, work;

    public bool? State { get => state; set { state = value; Changed(); } }
    public bool Busy { get => busy; set { busy = value; Changed(); } }
    public bool RebootPending { get => rebootPending; set { rebootPending = value; Changed(); } }
    public string? Error { get => error; set { error = value; Changed(); } }
    public string? Work { get => work; set { work = value; Changed(); } }

    public bool HasError => error is not null;
    public bool IsOn => state == true;
    // off can always be applied; on can only be switched off if this app made the change and holds the original
    public bool CanToggle => !busy && state is not null && (state == false || Backup.Has(T.Id));
    // already optimal, but set by Windows or by hand, so there is no original to go back to
    public bool OnLocked => state == true && !busy && !Backup.Has(T.Id);
    public string SwitchName => T.Title.ToString();
    public bool ShowRisk => T.Stripe != Stripe.None;
    public string RiskSentence => RiskLabel(T.Stripe);

    public string StatusText =>
        busy ? work ?? L.T("Reading…", "Lendo…")
        : error is not null ? L.T("Failed", "Falhou")
        : rebootPending ? L.T("Reboot pending", "Reinício pendente")
        : state is null ? L.T("Does not apply", "Não se aplica")
        : state == true && Backup.Has(T.Id) ? L.T("Applied", "Aplicado")
        : state == true ? L.T("Already optimal", "Já no ideal")
        : L.T("Can improve", "Pode melhorar");

    public string Tone =>
        busy ? "busy"
        : error is not null ? "bad"
        : rebootPending ? "warn"
        : state is null ? "na"
        : state == true ? "ok"
        : "todo";

    internal static string RiskLabel(Stripe s) => s switch
    {
        Stripe.Black => L.T("Changes the kernel or security. Apply at your own risk.", "Mexe no kernel ou na segurança. Aplique por sua conta e risco."),
        Stripe.Red => L.T("Changes a driver setting. Read the trade-offs first.", "Mexe em configuração de driver. Leia os contras antes."),
        _ => "",
    };

    public void Refresh() => Changed();

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
