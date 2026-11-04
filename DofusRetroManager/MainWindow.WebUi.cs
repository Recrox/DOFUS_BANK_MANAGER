using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DofusRetroManager.Data;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DofusRetroManager;

/// <summary>
/// Interface HTML/CSS (WebView2). Elle ne recalcule rien : elle affiche l'état des contrôles WPF (tableaux, textes,
/// filtres), qui restent la seule source de vérité, et leur renvoie les actions (saisie, sélection, clic).
/// La page est servie depuis l'exe sous https://app.local/ : aucune requête ne sort de la machine (README, règle 4).
/// </summary>
public partial class MainWindow
{
    const string WebOrigin = "https://app.local/";

    WebView2? _web;
    readonly DispatcherTimer _webTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    /// <summary>Contrôles WPF suivis par la page (noms x:Name), envoyés quand leur état change.</summary>
    HashSet<string> _webWatched = new();
    readonly Dictionary<string, string> _webSent = new();
    /// <summary>Lignes déjà converties en JSON, par contrôle : une liste n'est reconvertie que si elle a été remplacée.</summary>
    readonly Dictionary<string, (object Source, string Json)> _webRows = new();
    readonly Dictionary<string, byte[]?> _webIcons = new();
    /// <summary>Gestionnaires de clic sur une ligne que la page peut déclencher (le reste passe par les boutons nommés).</summary>
    static readonly HashSet<string> WebRowHandlers =
        ["HuntDropsToggle_Click", "BankRecipes_Click", "OrganizerShow_Click", "OrganizerExclude_Click", "OrganizerMain_Click", "FmWeightReset_Click"];

    void WebUiButton_Click(object sender, RoutedEventArgs e) => _ = ShowWebUiAsync();

    async Task ShowWebUiAsync()
    {
        try
        {
            // Le contrôle WebView2 ne s'initialise qu'une fois affiché : le conteneur doit être visible avant l'attente
            // (sinon, au lancement avec l'interface HTML retenue, l'initialisation ne se terminait jamais)
            WpfRoot.Visibility = Visibility.Collapsed;
            WebHost.Visibility = Visibility.Visible;
            if (_web == null)
            {
                var env = await CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(GameData.AppDir, "WebView2"));
                _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 10, 24) };
                WebHost.Content = _web;
                await _web.EnsureCoreWebView2Async(env);
                var core = _web.CoreWebView2;
                var settings = core.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
#if !DEBUG
                settings.AreDevToolsEnabled = false;
#endif
                // Tout est servi depuis l'exe ; aucune autre adresse n'est chargée ni ouverte
                core.AddWebResourceRequestedFilter(WebOrigin + "*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnWebResource;
                core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(WebOrigin)) e.Cancel = true; };
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.WebMessageReceived += OnWebMessage;
                _webTimer.Tick += (_, _) => PushWebState();
                core.Navigate(WebOrigin + "index.html");
            }
            WpfRoot.Visibility = Visibility.Collapsed;
            WebHost.Visibility = Visibility.Visible;
            _webSent.Clear(); // la page reçoit tout l'état à nouveau
            _webTimer.Start();
            ThemeManager.SetHtmlUi(true);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowWpfUi();
            AddLog("Interface HTML indisponible : le runtime Microsoft Edge WebView2 n'est pas installé.");
            MessageBox.Show(this, "L'interface HTML a besoin de Microsoft Edge WebView2 Runtime, absent de ce PC.\n"
                + "Il est fourni avec Windows 11 ; sous Windows 10, installez-le depuis le site de Microsoft.",
                "Dofus Retro Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowWpfUi();
            AddLog("Interface HTML indisponible : " + ex.Message);
        }
    }

    void ShowWpfUi()
    {
        _webTimer.Stop();
        WebHost.Visibility = Visibility.Collapsed;
        WpfRoot.Visibility = Visibility.Visible;
        ThemeManager.SetHtmlUi(false);
    }

    // --- Fichiers servis à la page ---

    void OnWebResource(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var path = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath.TrimStart('/'));
        byte[]? bytes = null;
        string type = "text/plain";
        try { (bytes, type) = WebFile(path); }
        catch (Exception ex) { AddLog($"Interface HTML : {path} illisible ({ex.Message})."); }
        e.Response = _web!.CoreWebView2.Environment.CreateWebResourceResponse(
            bytes == null ? null : new MemoryStream(bytes), bytes == null ? 404 : 200, bytes == null ? "Not Found" : "OK",
            $"Content-Type: {type}\r\nCache-Control: no-cache");
    }

    (byte[]? Bytes, string Type) WebFile(string path)
    {
        var icon = Regex.Match(path, @"^icon/(item|spell|head)/(\d+)\.png$");
        if (icon.Success)
        {
            if (!_webIcons.TryGetValue(path, out var png))
            {
                int id = int.Parse(icon.Groups[2].Value);
                _webIcons[path] = png = Png(icon.Groups[1].Value switch
                {
                    "item" => ItemIcons.Get(id),
                    "head" => CharacterIcons.Get(id), // tête du personnage : classe × 10 + sexe
                    _ => SpellIcons.Get(id, _data?.SpellLooks.GetValueOrDefault(id)),
                }, 64);
            }
            return (png, "image/png");
        }
        // Carte : tuiles et icônes rendues depuis les fichiers du client installé (rien n'est livré avec l'application)
        if (path.StartsWith("carte/")) return (CarteWebFile(path), "image/png");
        if (Regex.IsMatch(path, @"^fonts/PlusJakartaSans-\w+\.ttf$"))
        {
            using var font = Application.GetResourceStream(new Uri("pack://application:,,,/Fonts/" + path[6..]))?.Stream;
            return (font == null ? null : ReadAll(font), "font/ttf");
        }
        // Journal des changements (bouton « Nouveautés ») : un script, la page n'ayant pas le droit de faire de requêtes
        if (path == "changelog.js")
            return (Encoding.UTF8.GetBytes("window.CHANGELOG = " + JsonSerializer.Serialize(new
            {
                current = UpdateChecker.CurrentText,
                entries = Changelog.Entries.Select(e => new { version = e.Version, date = e.Date, items = e.Items }),
            }) + ";"), "text/javascript; charset=utf-8");
        // Page : Web/index.html, app.css, app.js intégrés à l'exe
        using var res = typeof(MainWindow).Assembly.GetManifestResourceStream("web/" + path);
        var mime = System.IO.Path.GetExtension(path) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
        return (res == null ? null : ReadAll(res), mime);
    }

    static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Icône vectorielle du jeu rendue en PNG carré (proportions gardées, centrée).</summary>
    static byte[]? Png(ImageSource? img, int size)
    {
        if (img == null || img.Width <= 0 || img.Height <= 0) return null;
        double scale = size / Math.Max(img.Width, img.Height);
        double w = img.Width * scale, h = img.Height * scale;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawImage(img, new Rect((size - w) / 2, (size - h) / 2, w, h));
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // --- Actions de la page ---

    /// <summary>Action en cours confirmée par la page : les gestionnaires sautent leur propre confirmation (MessageBox).</summary>
    bool _webConfirmed;

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
            var el = name != null ? FindName(name) : null;
            int index = m.TryGetProperty("index", out var ix) ? ix.GetInt32() : -1;
            switch (m.GetProperty("type").GetString())
            {
                case "watch":
                    _webWatched = m.GetProperty("names").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
                    _webSent.Clear();
                    break;
                case "ui":
                    ShowWpfUi();
                    return;
                case "theme":
                    ThemeManager.Apply(m.GetProperty("value").GetString() ?? ThemeManager.Crepuscule);
                    break;
                case "style":
                    ThemeManager.ApplyStyle(m.GetProperty("value").GetString() ?? ThemeManager.Classique);
                    break;
                case "set":
                    var value = m.GetProperty("value");
                    switch (el)
                    {
                        case TextBox tb: tb.Text = value.GetString() ?? ""; break;
                        case ComboBox cb: cb.SelectedIndex = value.GetInt32(); break;
                        case TabControl tc: tc.SelectedIndex = value.GetInt32(); break;
                        case ToggleButton tg: tg.IsChecked = value.GetBoolean(); break;
                        case RangeBase range: range.Value = value.GetDouble(); break;
                    }
                    break;
                case "click":
                    if (el is ButtonBase { IsEnabled: true } button)
                    {
                        // Déjà confirmé dans la page (ex. « Oublier l'objet » de l'onglet FM) : pas de seconde confirmation Windows
                        _webConfirmed = m.TryGetProperty("confirmed", out var c) && c.GetBoolean();
                        try { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
                        finally { _webConfirmed = false; }
                    }
                    break;
                case "show":
                    // Lien vers un objet : ingrédient d'une recette -> HDV, objet du brisage -> sa recette
                    if (m.TryGetProperty("id", out var shownId) && shownId.TryGetInt32(out var shownItem))
                    {
                        if (m.TryGetProperty("tab", out var tab) && tab.GetString() == "recipe")
                        {
                            if (!ShowRecipeOf(shownItem)) AddLog($"{_data?.GetItem(shownItem).Name} : pas de recette.");
                        }
                        else ShowInHdv(shownItem);
                    }
                    break;
                case "fmweight":
                    // Poids d'une stat saisi dans l'écran « Poids des stats » (onglet FM)
                    SetFmWeight(index, m.GetProperty("value").GetString() ?? "");
                    break;
                case "select":
                    if (el is Selector { ItemsSource: IList rows } grid && index >= 0 && index < rows.Count) grid.SelectedItem = rows[index];
                    break;
                case "stat":
                    // Seuil du Brisage : field = on (case), min ou max
                    if (_statFilters.ElementAtOrDefault(index) is { } filter)
                    {
                        var v = m.GetProperty("value");
                        switch (m.GetProperty("field").GetString())
                        {
                            case "on": filter.Box.IsChecked = v.GetBoolean(); break;
                            case "min": filter.Min.Text = v.GetString() ?? ""; break;
                            case "max": filter.Max.Text = v.GetString() ?? ""; break;
                        }
                    }
                    break;
                case "reorder":
                    // Glisser-déposer de l'onglet Switcheur : ligne index déposée sur la ligne to
                    int to = m.GetProperty("to").GetInt32();
                    if (el == OrganizerGrid && OrganizerGrid.ItemsSource is IList<OrganizerRow> organizerRows
                        && index >= 0 && index < organizerRows.Count && to >= 0 && to < organizerRows.Count)
                        MoveInOrder(organizerRows[index], organizerRows[to]);
                    break;
                case "toggle":
                    if (el is Panel panel && panel.Children.OfType<ToggleButton>().ElementAtOrDefault(index) is { } toggle)
                        toggle.IsChecked = toggle.IsChecked != true;
                    break;
                case "row":
                    var handler = m.GetProperty("handler").GetString();
                    if (handler != null && WebRowHandlers.Contains(handler) && el is ItemsControl { ItemsSource: IList list }
                        && index >= 0 && index < list.Count)
                        GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)?
                            .Invoke(this, [new FrameworkElement { DataContext = list[index] }, new RoutedEventArgs()]);
                    break;
            }
            PushWebState();
        }
        catch (Exception ex) { AddLog($"Interface HTML : action ignorée ({ex.Message})."); }
    }

    // --- État envoyé à la page ---

    void PushWebState()
    {
        if (_web?.CoreWebView2 == null || WebHost.Visibility != Visibility.Visible) return;
        var changed = new StringBuilder();
        // La carte (données lourdes) n'est envoyée que si la page affiche l'onglet Carte
        var extra = MainTabs.SelectedItem == CarteTab ? new[] { "_theme", "_style", "_carte", "_cartePos", "_carteRes" } : ["_theme", "_style"];
        foreach (var name in _webWatched.Concat(extra))
        {
            string json;
            try
            {
                json = name switch
                {
                    "_theme" => JsonSerializer.Serialize(ThemeManager.Current),
                    "_style" => JsonSerializer.Serialize(ThemeManager.Style),
                    "_carte" => CarteWebJson(),
                    "_cartePos" => CartePlayersJson(),
                    "_carteRes" => CarteResourcesJson(),
                    _ => ElementJson(name),
                };
            }
            catch (Exception ex) { AddLog($"Interface HTML : {name} ignoré ({ex.Message})."); continue; }
            if (_webSent.TryGetValue(name, out var old) && old == json) continue;
            _webSent[name] = json;
            changed.Append(changed.Length == 0 ? "" : ",").Append(JsonSerializer.Serialize(name)).Append(':').Append(json);
        }
        if (changed.Length > 0) _web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"update\",\"data\":{" + changed + "}}");
    }

    /// <summary>
    /// État d'un contrôle nommé : v (visible) et, selon son type, t (texte), c (couleur), on (coché), i (choix),
    /// items (options), rows et sel (lignes et ligne sélectionnée), pts (points d'un graphique).
    /// </summary>
    string ElementJson(string name)
    {
        var el = FindName(name);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (el is UIElement ui) w.WriteBoolean("v", ui.Visibility == Visibility.Visible);
            if (el == BrisageStats)
            {
                // Seuils du Brisage (contrôles créés dans le code) : caractéristique, cochée, minimum, maximum
                w.WriteStartArray("items");
                foreach (var f in _statFilters)
                {
                    w.WriteStartObject();
                    w.WriteString("t", f.Stat.Name);
                    w.WriteBoolean("on", f.Box.IsChecked == true);
                    w.WriteString("min", f.Min.Text);
                    w.WriteString("max", f.Max.Text);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                el = null;
            }
            switch (el)
            {
                case RangeBase range:
                    w.WriteNumber("val", range.Value);
                    w.WriteNumber("min", range.Minimum);
                    w.WriteNumber("max", range.Maximum);
                    break;
                case DataGridColumn column:
                    w.WriteString("t", column.Header as string);
                    break;
                case ComboBox cb:
                    w.WriteStartArray("items");
                    foreach (var item in cb.Items) w.WriteStringValue(item is ContentControl cc ? cc.Content?.ToString() : item?.ToString());
                    w.WriteEndArray();
                    w.WriteNumber("i", cb.SelectedIndex);
                    break;
                case TabControl tc:
                    w.WriteNumber("i", tc.SelectedIndex);
                    break;
                case ItemsControl ic:
                    var source = ic.ItemsSource;
                    if (source != null && !(source is IList))
                        source = source.Cast<object>().ToList();
                    if (source == null || !_webRows.TryGetValue(name, out var cached) || !ReferenceEquals(cached.Source, ic.ItemsSource))
                        _webRows[name] = cached = (ic.ItemsSource!, RowsJson(source));
                    w.WritePropertyName("rows");
                    w.WriteRawValue(cached.Json, skipInputValidation: true);
                    if (ic is Selector sel && source is IList rows) w.WriteNumber("sel", sel.SelectedItem == null ? -1 : rows.IndexOf(sel.SelectedItem));
                    break;
                case TextBox tb:
                    w.WriteString("t", tb.Text);
                    break;
                case TextBlock tbk:
                    w.WriteString("t", tbk.Text);
                    w.WriteString("c", BrushCss(tbk.Foreground));
                    break;
                case ToggleButton tg:
                    w.WriteBoolean("on", tg.IsChecked == true);
                    w.WriteString("t", tg.Content as string);
                    break;
                case ButtonBase button:
                    w.WriteString("t", button.Content as string);
                    w.WriteBoolean("e", button.IsEnabled);
                    break;
                case PriceChart chart:
                    w.WriteStartArray("pts");
                    foreach (var (time, price) in chart.Points)
                    {
                        w.WriteStartArray();
                        w.WriteNumberValue(new DateTimeOffset(time).ToUnixTimeMilliseconds());
                        w.WriteNumberValue(price);
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                    break;
                case Panel panel:
                    // Groupe de boutons à cocher (ex. nombre de cases des recettes)
                    w.WriteStartArray("items");
                    foreach (var t in panel.Children.OfType<ToggleButton>())
                    {
                        w.WriteStartObject();
                        w.WriteString("t", t.Content?.ToString());
                        w.WriteBoolean("on", t.IsChecked == true);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    break;
                case Shape shape:
                    w.WriteString("c", BrushCss(shape.Fill));
                    break;
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static string RowsJson(IEnumerable? rows)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            if (rows == null) w.WriteNullValue();
            else WriteJson(w, rows, 0);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Convertit une ligne (record) en JSON : propriétés publiques, couleurs en CSS, images ignorées.</summary>
    static void WriteJson(Utf8JsonWriter w, object? v, int depth)
    {
        switch (v)
        {
            case null: w.WriteNullValue(); return;
            case string s: w.WriteStringValue(s); return;
            case bool b: w.WriteBooleanValue(b); return;
            case int or long or short or byte: w.WriteNumberValue(Convert.ToInt64(v)); return;
            case double d: if (double.IsFinite(d)) w.WriteNumberValue(d); else w.WriteNullValue(); return;
            case float f: if (float.IsFinite(f)) w.WriteNumberValue(f); else w.WriteNullValue(); return;
            case decimal m: w.WriteNumberValue(m); return;
            case DateTime t: w.WriteStringValue(t.ToString("o")); return;
            case Enum e: w.WriteStringValue(e.ToString()); return;
            case Brush brush: w.WriteStringValue(BrushCss(brush)); return;
            case Recipe r: w.WriteStartObject(); w.WriteNumber("ResultId", r.ResultId); w.WriteEndObject(); return;
            case ImageSource or Visual: w.WriteNullValue(); return;
        }
        if (depth >= 6) { w.WriteNullValue(); return; }
        if (v is IEnumerable list)
        {
            w.WriteStartArray();
            foreach (var x in list) WriteJson(w, x, depth + 1);
            w.WriteEndArray();
            return;
        }
        w.WriteStartObject();
        foreach (var p in Props(v.GetType()))
        {
            object? x;
            try { x = p.GetValue(v); } catch { x = null; }
            w.WritePropertyName(p.Name);
            WriteJson(w, x, depth + 1);
        }
        w.WriteEndObject();
    }

    static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropCache = new();

    static PropertyInfo[] Props(Type t) => PropCache.GetOrAdd(t, type => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetIndexParameters().Length == 0 && !typeof(ImageSource).IsAssignableFrom(p.PropertyType))
        .ToArray());

    static string? BrushCss(Brush? brush)
    {
        var c = brush switch
        {
            SolidColorBrush s => s.Color,
            GradientBrush { GradientStops.Count: > 0 } g => g.GradientStops[0].Color,
            _ => (Color?)null,
        };
        // Opacité au format CSS (point décimal, quelle que soit la langue de Windows)
        return c is { } col
            ? $"rgba({col.R},{col.G},{col.B},{(col.A / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})"
            : null;
    }
}
