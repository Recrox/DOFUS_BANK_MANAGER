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

public partial class MainWindow
{
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
                        if (t.Tag is string extra) w.WriteString("d", extra); // données d'affichage (ex. « tête|niveau » d'un personnage)
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
