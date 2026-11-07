namespace DofusRetroManager.Hunt;

/// <summary>
/// Historique des sessions de combat (onglet Combat) : chaque session terminée (« Nouvelle session », ou une heure
/// sans combat) est résumée et gardée sans limite dans chasse.json. Seuls les totaux sont gardés : combats, XP, kamas,
/// objets obtenus, par personnage.
/// </summary>
public sealed partial class HuntTracker
{
    /// <summary>Sessions terminées, de la plus ancienne à la plus récente.</summary>
    public IReadOnlyList<SessionSummary> Sessions => _store.Sessions;

    /// <summary>Résumé de la session en cours (null sans combat), pour l'afficher en tête de l'historique.</summary>
    public SessionSummary? CurrentSession()
    {
        var fights = _store.Fights.Where(f => f.Time >= _store.SessionStart).ToList();
        return fights.Count == 0 ? null : Summarize(fights, _store.SessionStart, DateTime.Now, SessionDuration.TotalSeconds, false);
    }

    /// <summary>Oublie une session de l'historique (ligne erronée).</summary>
    public void ForgetSession(DateTime start)
    {
        if (_store.Sessions.RemoveAll(s => s.Start == start) == 0) return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Range la session en cours dans l'historique, avant d'en ouvrir une autre. End : fin de la session.</summary>
    void ArchiveSession(DateTime end)
    {
        var fights = _store.Fights.Where(f => f.Time >= _store.SessionStart).ToList();
        if (fights.Count == 0) return;
        if (end < fights[^1].Time) end = fights[^1].Time;
        var seconds = ((_store.SessionPausedAt is { } p && p < end ? p : end) - _store.SessionStart).TotalSeconds - _store.SessionPausedSeconds;
        _store.Sessions.Add(Summarize(fights, _store.SessionStart, end, seconds, false));
    }

    /// <summary>
    /// Premier lancement avec l'historique : les sessions passées sont reconstituées d'après les combats gardés,
    /// coupées après une heure sans combat (comme une nouvelle session automatique). Les pauses ne sont pas connues.
    /// </summary>
    void RebuildSessions()
    {
        if (_store.Sessions.Count > 0) return;
        var past = _store.Fights.Where(f => f.Time < _store.SessionStart).OrderBy(f => f.Time).ToList();
        var chunk = new List<FightRecord>();
        foreach (var f in past)
        {
            if (chunk.Count > 0 && f.Time - chunk[^1].Time > SessionGap) Flush();
            chunk.Add(f);
        }
        Flush();

        void Flush()
        {
            if (chunk.Count == 0) return;
            var start = chunk[0].Time.AddSeconds(-chunk[0].DurationSeconds);
            _store.Sessions.Add(Summarize(chunk, start, chunk[^1].Time, (chunk[^1].Time - start).TotalSeconds, true));
            chunk = [];
        }
    }

    static SessionSummary Summarize(List<FightRecord> fights, DateTime start, DateTime end, double seconds, bool rebuilt)
    {
        var s = new SessionSummary { Start = start, End = end, Seconds = Math.Max(0, seconds), Rebuilt = rebuilt };
        foreach (var f in fights)
        {
            Add(s.Total, f);
            var name = f.Character.Length > 0 ? f.Character : "?";
            if (!s.Characters.TryGetValue(name, out var c)) s.Characters[name] = c = new();
            Add(c, f);
        }
        return s;

        static void Add(SessionTotals t, FightRecord f)
        {
            t.Fights++;
            t.Xp += f.Xp;
            t.Kamas += f.Kamas;
            foreach (var d in f.Drops) t.Drops[d.ItemId] = t.Drops.GetValueOrDefault(d.ItemId) + d.Quantity;
        }
    }
}
