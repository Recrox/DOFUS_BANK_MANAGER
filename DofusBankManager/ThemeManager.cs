using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using DofusBankManager.Data;

namespace DofusBankManager;

/// <summary>
/// Thèmes de l'interface (Jour, Crépuscule, Nuit). Le dictionnaire de couleurs du thème actif
/// est le premier de App.Resources ; les styles le référencent en DynamicResource.
/// </summary>
public static class ThemeManager
{
    public const string Jour = "Jour", Crepuscule = "Crepuscule", Nuit = "Nuit";
    /// <summary>Styles : Classique (bois et orange) ou Moderne (verre et dégradés). Chacun décline les trois thèmes.</summary>
    public const string Classique = "Classique", Moderne = "Moderne";

    static string SettingsPath => Path.Combine(GameData.AppDir, "settings.json");

    public static string Current { get; private set; } = Crepuscule;
    public static string Style { get; private set; } = Classique;
    /// <summary>Interface HTML/CSS (WebView2) au lieu de l'interface WPF, retenue d'un lancement à l'autre.</summary>
    public static bool HtmlUi { get; private set; }

    public static void SetHtmlUi(bool html)
    {
        HtmlUi = html;
        Save();
    }

    public static event Action? Changed;

    public static void LoadSaved()
    {
        // Premier lancement (aucun réglage enregistré) : interface HTML, style Moderne. Ensuite, le choix de l'utilisateur
        // est retenu ; un settings.json d'une ancienne version sans ces champs garde l'interface classique.
        var settings = new Settings { Style = Moderne, HtmlUi = true };
        try
        {
            if (File.Exists(SettingsPath))
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch { settings = new Settings(); }
        Style = settings.Style == Moderne ? Moderne : Classique;
        HtmlUi = settings.HtmlUi;
        Apply(settings.Theme, save: false);
    }

    /// <summary>Change de style en gardant le thème (Jour, Crépuscule ou Nuit).</summary>
    public static void ApplyStyle(string style)
    {
        Style = style == Moderne ? Moderne : Classique;
        Apply(Current);
    }

    public static void Apply(string name, bool save = true)
    {
        if (name is not (Jour or Crepuscule or Nuit)) name = Crepuscule;
        // Moderne : Themes/ModerneNuit.xaml… ; Classique : Themes/Nuit.xaml…
        var file = (Style == Moderne ? Moderne : "") + name;
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{file}.xaml") };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict; else merged.Add(dict);
        Current = name;

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        if (save) Save();
        Changed?.Invoke();
    }

    /// <summary>Barre de titre Windows sombre pour les thèmes sombres.</summary>
    public static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = Current == Jour ? 0 : 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { Theme = Current, Style = Style, HtmlUi = HtmlUi }));
        }
        catch { }
    }

    sealed class Settings
    {
        public string Theme { get; set; } = Crepuscule;
        public string Style { get; set; } = Classique;
        public bool HtmlUi { get; set; }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
