using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MspTweaker;

public partial class MainWindow : Window
{
    const string Prefs = @"HKCU\Software\msp-tweaker";
    readonly List<Card> cards = Catalog.All.Select(t => new Card(t)).ToList();
    readonly ListCollectionView view;
    readonly string? cpu;
    TaskCompletionSource<bool>? confirmation;
    bool restorePointDone; // created, or knowingly skipped, in this session

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public MainWindow()
    {
        Theme.Set(Sys.RegGet(Prefs, "theme") is string theme ? theme == "dark"
            : Sys.RegGet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme") is 0);
        if (Sys.RegGet(Prefs, "lang") is string lang) L.Set(lang == "pt");

        InitializeComponent();
        view = new ListCollectionView(cards);
        view.GroupDescriptions.Add(new PropertyGroupDescription("T.Group"));
        Cards.ItemsSource = view;
        Bank.ItemsSource = view.Groups;

        const string nt = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        cpu = Sys.RegGet(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") is string s
            ? Regex.Replace(s, @"\((R|TM)\)|\s*CPU\s*@.*$", "").Trim() : null;
        WinVer.Text = $"Windows {Sys.RegGet(nt, "DisplayVersion")}, build {Sys.RegGet(nt, "CurrentBuild")}";
        BackupLocation.Text = Backup.Location;
        SetLanguage(L.Pt);
        ThemeButton.Content = Theme.Dark ? "" : ""; // sun leads to light, moon to dark

        Loaded += (_, _) => { foreach (var c in cards) _ = Read(c); };
        // a maximized WindowStyle=None window overflows the screen by the resize border
        StateChanged += (_, _) => Root.Margin = new Thickness(WindowState == WindowState.Maximized ? 7 : 0);
        SourceInitialized += (_, _) =>
        {
            var round = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND
            if (DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)) != 0)
                BorderThickness = new Thickness(1); // before Windows 11, DWM draws no corners or border
        };
    }

    void SetLanguage(bool pt)
    {
        L.Set(pt);
        LangEn.SetResourceReference(ForegroundProperty, pt ? "Subtle" : "Text");
        LangPt.SetResourceReference(ForegroundProperty, pt ? "Text" : "Subtle");
        Cpu.Text = cpu ?? L.T("Unknown CPU", "CPU desconhecida");
        UpdateSummary();
        view.Refresh(); // cards and group headers show Loc values, which only re-translate when rebuilt
    }

    void UpdateSummary()
    {
        if (cards.Any(c => c.Busy && c.State is null)) { Summary.Text = L.T("Reading Windows…", "Lendo o Windows…"); Skipped.Text = ""; return; }
        var open = cards.Count(c => c.State == false);
        var skipped = cards.Count(c => c.State is null && !c.HasError);
        Summary.Text = open switch
        {
            0 => L.T("Everything is optimal.", "Tudo no ideal."),
            1 => L.T("1 tweak can still improve this PC.", "1 ajuste ainda pode melhorar este PC."),
            _ => L.T($"{open} tweaks can still improve this PC.", $"{open} ajustes ainda podem melhorar este PC."),
        };
        Skipped.Text = skipped switch
        {
            0 => "",
            1 => L.T("1 does not apply to this PC.", "1 não se aplica a este PC."),
            _ => L.T($"{skipped} do not apply to this PC.", $"{skipped} não se aplicam a este PC."),
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

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var c = (Card)((FrameworkElement)sender).DataContext;
        if (c.T.Stripe != Stripe.None)
        {
            var ok = await Confirm(c.T.Stripe, c.T.Title.ToString(), L.T("Trade-offs: ", "Contras: ") + c.T.Contraindication, c.T.Dosage.ToString(),
                c.T.Stripe == Stripe.Black ? L.T("I understand the risk, apply", "Entendo o risco, aplicar") : L.I["apply"]);
            if (!ok || !await EnsureRestorePoint(c)) return;
        }
        await Execute(c, c.T.Apply, L.T("Applying…", "Aplicando…"));
    }

    async void Revert_Click(object sender, RoutedEventArgs e)
    {
        var c = (Card)((FrameworkElement)sender).DataContext;
        await Execute(c, c.T.Revert, L.T("Reverting…", "Revertendo…"));
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

    async void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (await Confirm(Stripe.None, L.T("Reboot now?", "Reiniciar agora?"),
                L.T("Save your open work. Windows restarts in 5 seconds.", "Salve o que estiver aberto. O Windows reinicia em 5 segundos."),
                null, L.T("Reboot", "Reiniciar")))
            Sys.Run(Sys.Shutdown, "/r /t 5");
    }

    Task<bool> Confirm(Stripe stripe, string title, string body, string? dosage, string ok)
    {
        var risky = stripe == Stripe.None ? Visibility.Collapsed : Visibility.Visible;
        OvHatch.Visibility = OvRisk.Visibility = risky;
        OvHatch.Background = (Brush)FindResource(stripe == Stripe.Black ? "HatchDanger" : "HatchWarn");
        OvRiskIcon.Text = stripe == Stripe.Black ? "" : "";
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
        ThemeButton.Content = Theme.Dark ? "" : "";
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
    bool busy = true, rebootPending, open;
    string? error, work;

    public bool? State { get => state; set { state = value; Changed(); } }
    public bool Busy { get => busy; set { busy = value; Changed(); } }
    public bool RebootPending { get => rebootPending; set { rebootPending = value; Changed(); } }
    public bool Open { get => open; set { open = value; Changed(); } }
    public string? Error { get => error; set { error = value; Changed(); } }
    public string? Work { get => work; set { work = value; Changed(); } }

    public bool Free => !busy;
    public bool HasError => error is not null;
    public bool ShowApply => state == false;
    public bool ShowRevert => Backup.Has(T.Id);
    public string ApplyName => L.T("Apply: ", "Aplicar: ") + T.Title;
    public string RevertName => L.T("Revert: ", "Reverter: ") + T.Title;
    public string RiskText => T.Stripe == Stripe.Black ? L.I["riskBlack"] : L.I["riskRed"];
    public bool ShowMeta => T.Stripe != Stripe.None || T.Reboot;

    public string StatusText =>
        busy ? work ?? L.T("Reading…", "Lendo…")
        : error is not null ? L.T("Failed", "Falhou")
        : rebootPending ? L.T("Reboot pending", "Reinício pendente")
        : state is null ? L.T("Does not apply", "Não se aplica")
        : state == true && Backup.Has(T.Id) ? L.T("Applied", "Aplicado")
        : state == true ? L.T("Optimal", "No ideal")
        : L.T("Windows default", "Padrão do Windows");

    // LED states: ok = lit gold, off = dark, na = hollow, warn = amber blink, bad = red, busy = pulse
    public string Tone =>
        busy ? "busy"
        : error is not null ? "bad"
        : rebootPending ? "warn"
        : state is null ? "na"
        : state == true ? "ok"
        : "off";

    internal static string RiskLabel(Stripe s) => s switch
    {
        Stripe.Black => L.T("Changes the kernel and security. Apply at your own risk.", "Mexe no kernel e na segurança. Aplique por sua conta e risco."),
        Stripe.Red => L.T("Changes drivers or boot. Read the trade-offs first.", "Mexe em driver ou boot. Leia os contras antes."),
        _ => "",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
