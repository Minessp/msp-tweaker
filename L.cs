using System.ComponentModel;
using System.Globalization;

namespace MspTweaker;

/// UI language. XAML binds static labels as {Binding [key], Source={x:Static m:L.I}}.
public sealed class L : INotifyPropertyChanged
{
    public static readonly L I = new();
    public static bool Pt { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";

    public static string T(string en, string pt) => Pt ? pt : en;

    public static void Set(bool pt)
    {
        Pt = pt;
        I.PropertyChanged?.Invoke(I, new PropertyChangedEventArgs("Item[]"));
    }

    public string this[string key] => Labels.TryGetValue(key, out var t) ? T(t.En, t.Pt) : key;

    static readonly Dictionary<string, (string En, string Pt)> Labels = new()
    {
        ["system"] = ("System", "Sistema"),
        ["games"] = ("Games", "Jogos"),
        ["tweaks"] = ("Tweaks", "Ajustes"),
        ["cpu"] = ("Processor", "Processador"),
        ["gpu"] = ("Graphics", "Vídeo"),
        ["effect"] = ("What it does", "O que faz"),
        ["contra"] = ("Trade-offs", "Contras"),
        ["dosage"] = ("What changes", "O que muda"),
        ["reboot"] = ("Needs reboot", "Exige reinício"),
        ["rebootNow"] = ("Reboot now", "Reiniciar agora"),
        ["backupNote"] = ("Originals backed up in", "Originais guardados em"),
        ["diag"] = ("Connection test", "Teste de conexão"),
        ["diagBody"] = ("40 pings to your router and 40 to the internet (1.1.1.1). Shows whether loss or jitter starts inside your home or past it. Nothing is changed.",
                        "40 pings até o roteador e 40 até a internet (1.1.1.1). Mostra se perda ou variação começa dentro de casa ou depois dela. Nada é alterado."),
        ["diagRun"] = ("Run test", "Testar"),
        ["diagLan"] = ("Router", "Roteador"),
        ["diagWan"] = ("Internet", "Internet"),
        ["cancel"] = ("Cancel", "Cancelar"),
        ["theme"] = ("Switch theme", "Trocar tema"),
        ["minimize"] = ("Minimize", "Minimizar"),
        ["close"] = ("Close", "Fechar"),
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// A bilingual string.
public sealed record Loc(string En, string Pt)
{
    public override string ToString() => L.T(En, Pt);
}
