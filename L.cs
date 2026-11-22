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
        ["title"] = ("Tweaks", "Ajustes"),
        ["intro"] = ("Only tweaks with a measured effect on performance or latency. Status is read live from Windows, so “Optimal” means there is nothing left to do.",
                     "Só ajustes com efeito medido em desempenho ou latência. O status é lido do Windows na hora: “No ideal” quer dizer que não há nada a fazer."),
        ["riskRed"] = ("Changes drivers or boot", "Mexe em driver ou boot"),
        ["riskBlack"] = ("Changes the kernel and security", "Mexe no kernel e na segurança"),
        ["riskNone"] = ("No icon: safe, reverts instantly.", "Sem ícone: seguro, reverte na hora."),
        ["pending"] = ("Reboot pending", "Reinício pendente"),
        ["pendingBody"] = ("Some tweaks only take effect after a reboot.", "Alguns ajustes só valem depois de reiniciar."),
        ["rebootNow"] = ("Reboot now", "Reiniciar agora"),
        ["backupNote"] = ("Originals backed up in", "Originais guardados em"),
        ["indication"] = ("When it helps", "Quando ajuda"),
        ["contra"] = ("Trade-offs", "Contras"),
        ["dosage"] = ("What changes", "O que muda"),
        ["reboot"] = ("Needs reboot", "Exige reinício"),
        ["details"] = ("Details", "Detalhes"),
        ["apply"] = ("Apply", "Aplicar"),
        ["revert"] = ("Revert", "Reverter"),
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
