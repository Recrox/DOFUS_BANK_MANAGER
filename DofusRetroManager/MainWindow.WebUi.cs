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
    readonly DispatcherTimer _webTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    /// <summary>Dernier état envoyé : compteur de changements des contrôles, heure du dernier envoi complet.</summary>
    long _webVersion = -1;
    DateTime _webFullAt;
    /// <summary>Envoi complet demandé (thème changé, liste suivie changée) même si aucun contrôle n'a bougé.</summary>
    bool _webForce;
    /// <summary>Contrôles WPF suivis par la page (noms x:Name), envoyés quand leur état change.</summary>
    HashSet<string> _webWatched = new();
    readonly Dictionary<string, string> _webSent = new();
    /// <summary>Lignes déjà converties en JSON, par contrôle : une liste n'est reconvertie que si elle a été remplacée.</summary>
    readonly Dictionary<string, (object Source, string Json)> _webRows = new();
    readonly Dictionary<string, byte[]?> _webIcons = new();
    /// <summary>Gestionnaires de clic sur une ligne que la page peut déclencher (le reste passe par les boutons nommés).</summary>
    static readonly HashSet<string> WebRowHandlers =
        ["HuntDropsToggle_Click", "HuntAlertToggle_Click", "HuntAlertDelete_Click", "BankRecipes_Click", "OrganizerShow_Click", "OrganizerExclude_Click", "OrganizerMain_Click", "FmWeightReset_Click"];

    async Task ShowWebUiAsync()
    {
        try
        {
            // Le contrôle WebView2 ne s'initialise qu'une fois affiché : le conteneur est visible dès le départ
            if (_web == null)
            {
                var dataDir = System.IO.Path.Combine(GameData.AppDir, "WebView2");
#if DEBUG
                // Tests : un dossier à part, pour tourner à côté de l'application installée déjà ouverte
                if (Environment.GetEnvironmentVariable("DRM_WEBVIEW2_DIR") is { Length: > 0 } testDir) dataDir = testDir;
#endif
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
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
            _webSent.Clear(); // la page reçoit tout l'état à nouveau
            _webForce = true;
            ThemeManager.Changed += () => _webForce = true;
            _webTimer.Start();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // Sans WebView2, l'application n'a pas d'interface : le joueur est invité à l'installer
            if (MessageBox.Show(this, "Dofus Retro Manager a besoin de Microsoft Edge WebView2 Runtime, absent de ce PC.\n"
                + "Il est fourni avec Windows 11 ; sous Windows 10, installez-le depuis le site de Microsoft, puis relancez l'application.\n\n"
                + "Ouvrir la page de téléchargement ?",
                "Dofus Retro Manager", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                OpenUrl(WebView2DownloadUrl);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "L'interface n'a pas pu s'ouvrir : " + ex.Message, "Dofus Retro Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
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
        var icon = Regex.Match(path, @"^icon/(item|spell|head|chall)/(\d+)\.png$");
        if (icon.Success)
        {
            if (!_webIcons.TryGetValue(path, out var png))
            {
                int id = int.Parse(icon.Groups[2].Value);
                _webIcons[path] = png = Png(icon.Groups[1].Value switch
                {
                    "item" => ItemIcons.Get(id),
                    "head" => CharacterIcons.Get(id), // tête du personnage : classe × 10 + sexe
                    "chall" => ChallengeIcons.Get(id), // challenge : son icône (champ g du fichier de langue)
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

    Dictionary<string, WebControl>? _webControls;

    /// <summary>Contrôle (sans affichage) par son nom : les champs WebControl de la fenêtre (MainWindow.Controls.cs).</summary>
    WebControl? Control(string name)
    {
        _webControls ??= GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => typeof(WebControl).IsAssignableFrom(f.FieldType))
            .ToDictionary(f => f.Name, f => (WebControl)f.GetValue(this)!);
        return _webControls.GetValueOrDefault(name);
    }

    /// <summary>Lignes d'un tableau, dans l'ordre envoyé à la page.</summary>
    static IList? RowsOf(WebList list) => list.ItemsSource as IList ?? list.ItemsSource?.Cast<object>().ToList();

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
            var el = name != null ? Control(name) : null;
            int index = m.TryGetProperty("index", out var ix) ? ix.GetInt32() : -1;
            switch (m.GetProperty("type").GetString())
            {
                case "watch":
                    // La page ne suit que l'onglet affiché : nouvelle liste à chaque changement d'onglet
                    _webWatched = m.GetProperty("names").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
                    _webForce = true;
                    break;
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
                        case WebTextBox tb: tb.Text = value.GetString() ?? ""; break;
                        case WebCombo cb: cb.SelectedIndex = value.GetInt32(); break;
                        case WebTabs tc: tc.SelectedIndex = value.GetInt32(); break;
                        case WebToggle tg: tg.IsChecked = value.GetBoolean(); break;
                        case WebSlider range: range.Value = value.GetDouble(); break;
                    }
                    break;
                case "click":
                    if (el is WebButton { IsEnabled: true } button)
                    {
                        // Déjà confirmé dans la page (ex. « Oublier l'objet » de l'onglet FM) : pas de seconde confirmation Windows
                        _webConfirmed = m.TryGetProperty("confirmed", out var c) && c.GetBoolean();
                        try { button.RaiseClick(); }
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
                    if (el is WebList grid && RowsOf(grid) is { } rows && index >= 0 && index < rows.Count) grid.SelectedItem = rows[index];
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
                    if (el is WebPanel panel && panel.Children.OfType<ToggleButton>().ElementAtOrDefault(index) is { } toggle)
                        toggle.IsChecked = toggle.IsChecked != true;
                    break;
                case "trade-del":
                    // « Mes échanges » : ligne oubliée (rapprochement erroné)
                    ForgetTrade(index);
                    break;
                case "cell":
                    // Case cliquée sur la carte de la page : zone, points d'intérêt, ressources
                    ShowCarteCellInfo(m.GetProperty("sa").GetInt32(), m.GetProperty("x").GetInt32(), m.GetProperty("y").GetInt32());
                    break;
                case "row":
                    var handler = m.GetProperty("handler").GetString();
                    if (handler != null && WebRowHandlers.Contains(handler) && el is WebList rowList && RowsOf(rowList) is { } list
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
        // Rien n'a changé : rien n'est relu ni envoyé (sauf une vérification toutes les 5 s, pour ce que le code modifie
        // sans passer par un contrôle, ex. une ligne de tableau modifiée sur place)
        var now = DateTime.Now;
        if (!_webForce && WebControl.Version == _webVersion && now - _webFullAt < TimeSpan.FromSeconds(5)) return;
        _webForce = false;
        _webVersion = WebControl.Version;
        _webFullAt = now;
        var changed = new StringBuilder();
        // La carte (données lourdes) n'est envoyée que si la page affiche l'onglet Carte
        // Le livre TTG (toute la collection) n'est calculé que si la page l'affiche (« _ttgBook » dans sa liste suivie)
        var extra = MainTabs.SelectedItem == CarteTab ? new[] { "_theme", "_style", "_changelog", "_carte", "_cartePos", "_carteRes" }
            : ["_theme", "_style", "_changelog"];
        foreach (var name in _webWatched.Concat(extra).Distinct())
        {
            string json;
            try
            {
                json = name switch
                {
                    "_theme" => JsonSerializer.Serialize(ThemeManager.Current),
                    "_style" => JsonSerializer.Serialize(ThemeManager.Style),
                    "_changelog" => _changelogAsk.ToString(),
                    "_carte" => CarteWebJson(),
                    "_cartePos" => CartePlayersJson(),
                    "_carteRes" => CarteResourcesJson(),
                    "_ttgBook" => JsonSerializer.Serialize(BuildTtgBook()),
                    "_trades" => TradesJson(),
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
        var el = Control(name);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (el != null) w.WriteBoolean("v", el.Visibility == Visibility.Visible);
            if (el == BrisageStats)
            {
                // Seuils du Brisage (contrôles créés dans le code) : caractéristique, cochée, minimum, maximum
                w.WriteStartArray("items");
                foreach (var f in _statFilters)
                {
                    w.WriteStartObject();
                    w.WriteString("t", f.Stat.Name);
                    w.WriteString("s", f.Short);
                    if (f.RuneId is { } rune) w.WriteNumber("r", rune);
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
                case WebSlider range:
                    w.WriteNumber("val", range.Value);
                    w.WriteNumber("min", range.Minimum);
                    w.WriteNumber("max", range.Maximum);
                    break;
                case WebColumn column:
                    w.WriteString("t", column.Header as string);
                    break;
                case WebCombo cb:
                    w.WriteStartArray("items");
                    foreach (var item in cb.Items) w.WriteStringValue(item is ContentControl cc ? cc.Content?.ToString() : item?.ToString());
                    w.WriteEndArray();
                    w.WriteNumber("i", cb.SelectedIndex);
                    break;
                case WebTabs tc:
                    w.WriteNumber("i", tc.SelectedIndex);
                    break;
                case WebList ic:
                    // Lignes reconverties seulement si la liste a été remplacée
                    if (ic.ItemsSource == null || !_webRows.TryGetValue(name, out var cached) || !ReferenceEquals(cached.Source, ic.ItemsSource))
                        _webRows[name] = cached = (ic.ItemsSource!, RowsJson(RowsOf(ic)));
                    w.WritePropertyName("rows");
                    w.WriteRawValue(cached.Json, skipInputValidation: true);
                    if (ic.SelectedItem != null && RowsOf(ic) is { } rows) w.WriteNumber("sel", rows.IndexOf(ic.SelectedItem));
                    break;
                case WebTextBox tb:
                    w.WriteString("t", tb.Text);
                    break;
                case WebText tbk:
                    w.WriteString("t", tbk.Text);
                    if (tbk.ToolTip is string tip && tip.Length > 0) w.WriteString("tt", tip); // détail au survol
                    w.WriteString("c", BrushCss(tbk.Foreground));
                    break;
                case WebToggle tg:
                    w.WriteBoolean("on", tg.IsChecked == true);
                    w.WriteString("t", tg.Content as string);
                    break;
                case WebButton button:
                    w.WriteString("t", button.Content as string);
                    w.WriteBoolean("e", button.IsEnabled);
                    break;
                case WebChart chart:
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
                case WebPanel panel:
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
                case WebShape shape:
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
