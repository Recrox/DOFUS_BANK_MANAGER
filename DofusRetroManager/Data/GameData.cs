using System.IO;
using System.Net.Http;
using System.Xml.Linq;

namespace DofusRetroManager.Data;

/// <summary>
/// Données statiques du jeu (objets, recettes, métiers) extraites des fichiers de langue
/// de Dofus Retro, téléchargés depuis le CDN d'Ankama puis mis en cache localement.
/// </summary>
public sealed partial class GameData
{
    const string DefaultDataUrl = "https://dofusretro.cdn.ankama.com/static-data/612/";
    const string Lang = "fr";
    static readonly string[] Files = ["items", "crafts", "skills", "jobs", "effects", "monsters", "maps", "spells", "monsterdrops", "hints", "dungeons", "fightChallenge", "houses", "interactiveobjects", "quests", "ttg"];
    /// <summary>Fichiers dont l'absence n'empêche pas de démarrer (ajoutés après coup, peut-être pas encore en cache).</summary>
    static readonly string[] Optional = ["monsters", "maps", "spells", "monsterdrops", "hints", "dungeons", "fightChallenge", "houses", "interactiveobjects", "quests", "ttg"];
    /// <summary>Type d'objet « Objet de Quête » : ne tombe que pendant la quête.</summary>
    const int QuestItemType = 24;

    public static string AppDir { get; } = ResolveAppDir();

    /// <summary>
    /// Dossier des données du joueur. L'application s'appelait « Dofus Bank Manager » jusqu'à la 1.32 :
    /// son dossier (banques, prix, réglages…) est renommé au premier lancement, ou réutilisé si c'est impossible.
    /// </summary>
    static string ResolveAppDir()
    {
#if DEBUG
        // Tests : données à part (copie), pour ne jamais toucher à celles du joueur
        if (Environment.GetEnvironmentVariable("DRM_APPDIR") is { Length: > 0 } testDir) return testDir;
#endif
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(local, "DofusRetroManager");
        var legacy = Path.Combine(local, "DofusBankManager");
        if (Directory.Exists(dir) || !Directory.Exists(legacy)) return dir;
        try
        {
            Directory.Move(legacy, dir);
            return dir;
        }
        catch { return legacy; }
    }

    public Dictionary<int, ItemInfo> Items { get; } = new();
    public List<Recipe> Recipes { get; } = new();
    public List<string> JobNames { get; } = new();
    public Dictionary<int, EffectInfo> Effects { get; } = new();
    public Dictionary<int, MonsterInfo> Monsters { get; } = new();
    /// <summary>Drops officiels par monstre (fichier de langue « monsterdrops »).</summary>
    public Dictionary<int, List<MonsterDrop>> MonsterDrops { get; } = new();
    public Dictionary<int, MapInfo> Maps { get; } = new();
    /// <summary>Points d'intérêt de la carte (fichier de langue « hints ») et leurs catégories (identifiant -> nom).</summary>
    public List<MapHint> Hints { get; } = new();
    public Dictionary<int, string> HintCategories { get; } = new();
    /// <summary>Catégorie ajoutée aux points d'intérêt pour les maisons (fichier de langue « houses » : porte de chaque maison).</summary>
    public const int HouseCategory = 100;
    /// <summary>Ressources récoltables (arbres, minerais, céréales, poissons…) par identifiant d'objet interactif.</summary>
    public Dictionary<int, HarvestResource> Resources { get; } = new();
    /// <summary>Graphisme posé sur une case de map (calque d'objets 2) -> objet interactif (fichier « interactiveobjects »).</summary>
    public Dictionary<int, int> InteractiveGfx { get; } = new();
    /// <summary>Quête du Dofus Ocre : pour chaque étape (1 à 34), les monstres dont il faut rapporter l'âme.</summary>
    public List<List<int>> OcreSteps { get; } = new();
    /// <summary>Carte du classeur TTG -> objet carte (fichier de langue « ttg » : TTG.c[carte].o).</summary>
    public Dictionary<int, int> TtgCardItems { get; } = new();
    /// <summary>Cartes du classeur TTG (normales et brillantes), par numéro, pour le livre de l'onglet TTG.</summary>
    public Dictionary<int, TtgCard> TtgCards { get; } = new();
    /// <summary>Familles de cartes TTG, par identifiant.</summary>
    public Dictionary<int, TtgFamily> TtgFamilies { get; } = new();
    /// <summary>Noms des challenges de combat (fichier de langue « fightChallenge »), ex. 5 = Économe.</summary>
    public Dictionary<int, string> FightChallenges { get; } = new();
    /// <summary>Règle de chaque challenge (« d ») et son icône (« g » : clips/challenges/&lt;g&gt;.swf du client).</summary>
    public Dictionary<int, string> FightChallengeRules { get; } = new();
    public Dictionary<int, int> FightChallengeGfx { get; } = new();
    /// <summary>Noms des sorts (fichier de langue « spells »).</summary>
    public Dictionary<int, string> Spells { get; } = new();
    /// <summary>Icônes des sorts (fond, symbole, couleurs à l'état normal).</summary>
    public Dictionary<int, SpellLook> SpellLooks { get; } = new();
    /// <summary>Durée en tours des effets d'un sort, par niveau (index 0 = niveau 1) : la plus longue de ses effets.</summary>
    public Dictionary<int, int[]> SpellTurns { get; } = new();
    /// <summary>
    /// XP de base des monstres (niveau et XP du grade 1), livrée avec l'application ou lue dans un
    /// monstres.json placé dans le dossier de l'application. Le fichier de langue « monsters » d'Ankama ne contient pas l'XP.
    /// </summary>
    public Dictionary<int, (int Level, long Xp)> MonsterXp { get; } = new();

    /// <summary>XP de base d'un monstre à ce niveau : celle du grade 1, proportionnelle au niveau pour les autres grades (estimation).</summary>
    public double? BaseXp(int monsterId, int level) =>
        MonsterXp.TryGetValue(monsterId, out var m) ? m.Xp * (double)Math.Max(1, level) / Math.Max(1, m.Level) : null;

    public ItemInfo GetItem(int id) =>
        Items.TryGetValue(id, out var it) ? it : new ItemInfo(id, $"Objet #{id}", 0, 0, "?", 0);

    public MonsterInfo GetMonster(int id) =>
        Monsters.TryGetValue(id, out var m) ? m : new MonsterInfo(id, $"Monstre #{id}", -1, "?", 0, 0, 0);

    /// <summary>Position et zone d'une map, ex. « [4,-18] Amakna (Village d'Amakna) » ; l'identifiant si la map est inconnue.</summary>
    public string MapLabel(int id) => Maps.TryGetValue(id, out var m) ? m.Label : $"map {id}";

    /// <summary>Libellé d'un effet avec sa valeur, ex. « +12 en force ».</summary>
    public string FormatEffect(int effectId, int value)
    {
        if (!Effects.TryGetValue(effectId, out var e)) return $"Effet {effectId} : {value}";
        var s = System.Text.RegularExpressions.Regex.Replace(e.Description, @"#1\{[^}]*\}#2", value.ToString());
        return s.Replace("#1", value.ToString()).Replace("#2", "").Replace("#3", "").Trim();
    }

    public static async Task<GameData> LoadAsync(Action<string> log)
    {
        var dir = Path.Combine(AppDir, "lang");
        Directory.CreateDirectory(dir);
        var paths = await DownloadAsync(dir, log);

        var data = new GameData();
        var items = Avm1.Run(await File.ReadAllBytesAsync(paths["items"]));
        var crafts = Avm1.Run(await File.ReadAllBytesAsync(paths["crafts"]));
        var skills = Avm1.Run(await File.ReadAllBytesAsync(paths["skills"]));
        var jobs = Avm1.Run(await File.ReadAllBytesAsync(paths["jobs"]));
        var effects = Avm1.Run(await File.ReadAllBytesAsync(paths["effects"]));

        if (effects.GetValueOrDefault("E") is AsObject E)
            foreach (var (k, v) in E)
                if (v is AsObject o && int.TryParse(k, out var eid))
                    data.Effects[eid] = new EffectInfo(eid, Avm1.ToStr(o.GetValueOrDefault("d")),
                        Avm1.ToStr(o.GetValueOrDefault("o")) == "-", (int)Avm1.ToNum(o.GetValueOrDefault("c")));

        var types = new Dictionary<int, string>();
        if (items.GetValueOrDefault("I") is AsObject I)
        {
            if (I.GetValueOrDefault("t") is AsObject t)
                foreach (var (k, v) in t)
                    if (v is AsObject o) types[int.Parse(k)] = Avm1.ToStr(o.GetValueOrDefault("n"));
            if (I.GetValueOrDefault("u") is AsObject u)
                foreach (var (k, v) in u)
                {
                    if (v is not AsObject o) continue;
                    int id = int.Parse(k), type = (int)Avm1.ToNum(o.GetValueOrDefault("t"));
                    // ef : { c11: 200, c10: 50, … } = caractéristique -> valeur maximale
                    var stats = new Dictionary<int, int>();
                    if (o.GetValueOrDefault("ef") is AsObject ef)
                        foreach (var (c, value) in ef)
                            if (c.StartsWith('c') && int.TryParse(c[1..], out var cid))
                                stats[cid] = (int)Avm1.ToNum(value);
                    data.Items[id] = new ItemInfo(id, Avm1.ToStr(o.GetValueOrDefault("n")),
                        (int)Avm1.ToNum(o.GetValueOrDefault("l")), type, types.GetValueOrDefault(type, "?"),
                        (int)Avm1.ToNum(o.GetValueOrDefault("g"))) { MaxStats = stats };
                }
        }

        var jobNames = new Dictionary<int, string>();
        if (jobs.GetValueOrDefault("J") is AsObject J)
            foreach (var (k, v) in J)
                if (v is AsObject o) jobNames[int.Parse(k)] = Avm1.ToStr(o.GetValueOrDefault("n"));

        // Compétence -> liste des objets fabricables : donne le métier de chaque recette
        var jobsByResult = new Dictionary<int, SortedSet<string>>();
        if (skills.GetValueOrDefault("SK") is AsObject SK)
            foreach (var v in SK.Values)
            {
                if (v is not AsObject o || o.GetValueOrDefault("cl") is not List<object?> cl) continue;
                var job = jobNames.GetValueOrDefault((int)Avm1.ToNum(o.GetValueOrDefault("j")), "?");
                if (job == "-Base-") job = "Sans métier";
                foreach (var r in cl)
                {
                    int rid = (int)Avm1.ToNum(r);
                    if (!jobsByResult.TryGetValue(rid, out var set)) jobsByResult[rid] = set = new();
                    set.Add(job);
                }
            }

        if (crafts.GetValueOrDefault("CR") is AsObject CR)
            foreach (var (k, v) in CR)
            {
                if (v is not List<object?> list) continue;
                var ings = list.OfType<List<object?>>()
                    .Where(x => x.Count >= 2)
                    .Select(x => new Ingredient((int)Avm1.ToNum(x[0]), (int)Avm1.ToNum(x[1])))
                    .ToList();
                if (ings.Count == 0) continue;
                int rid = int.Parse(k);
                data.Recipes.Add(new Recipe(rid, ings,
                    jobsByResult.TryGetValue(rid, out var js) ? js.ToList() : ["?"]));
            }

        // Monstres : M[id] = { n: nom, b: race, g1…g5: { l: niveau } }, MR[race] = { n: nom }
        if (paths.TryGetValue("monsters", out var monstersPath))
        {
            var monsters = Avm1.Run(await File.ReadAllBytesAsync(monstersPath));
            var races = new Dictionary<int, string>();
            if (monsters.GetValueOrDefault("MR") is AsObject MR)
                foreach (var (k, v) in MR)
                    if (v is AsObject o && int.TryParse(k, out var rid)) races[rid] = Avm1.ToStr(o.GetValueOrDefault("n"));
            if (monsters.GetValueOrDefault("M") is AsObject M)
                foreach (var (k, v) in M)
                {
                    if (v is not AsObject o || !int.TryParse(k, out var mid)) continue;
                    // Grades g1…g5, dans l'ordre
                    var levels = o.Where(kv => kv.Key.Length > 1 && kv.Key[0] == 'g' && int.TryParse(kv.Key.AsSpan(1), out _)
                            && kv.Value is AsObject)
                        .OrderBy(kv => int.Parse(kv.Key.AsSpan(1)))
                        .Select(kv => (int)Avm1.ToNum(((AsObject)kv.Value!).GetValueOrDefault("l"))).ToList();
                    int race = (int)Avm1.ToNum(o.GetValueOrDefault("b"));
                    data.Monsters[mid] = new MonsterInfo(mid, Avm1.ToStr(o.GetValueOrDefault("n")),
                        race, races.GetValueOrDefault(race, "?"),
                        levels.DefaultIfEmpty().Min(), levels.DefaultIfEmpty().Max(),
                        levels.Count >= 3 ? levels[2] : levels.DefaultIfEmpty().Max()) { Grades = levels, IsBoss = o.GetValueOrDefault("d") is true, Gfx = (int)Avm1.ToNum(o.GetValueOrDefault("g")) };
                }
        }

        // Drops : MD[monstre][objet] = { t: [taux par grade], pp, dc?, sr?, sc?: [{ criterion, expected }], c }.
        // Le champ c ne marque pas les drops conditionnels (tous ceux de la Larve Bleue l'ont) : on lit sc.
        if (paths.TryGetValue("monsterdrops", out var dropsPath)
            && Avm1.Run(await File.ReadAllBytesAsync(dropsPath)).GetValueOrDefault("MD") is AsObject MD)
            foreach (var (mk, mv) in MD)
            {
                if (mv is not AsObject byItem || !int.TryParse(mk, out var mid)) continue;
                var list = new List<MonsterDrop>();
                foreach (var (ik, iv) in byItem)
                {
                    if (iv is not AsObject o || !int.TryParse(ik, out var iid) || o.GetValueOrDefault("t") is not List<object?> t) continue;
                    bool condition = o.GetValueOrDefault("sc") is List<object?> sc
                        && sc.OfType<AsObject>().Any(x => x.GetValueOrDefault("expected") is true);
                    int odds = (int)Avm1.ToNum(o.GetValueOrDefault("sr"));
                    list.Add(new MonsterDrop(iid, t.Select(Avm1.ToNum).ToList(), (int)Avm1.ToNum(o.GetValueOrDefault("pp")),
                        (int)Avm1.ToNum(o.GetValueOrDefault("dc")), odds,
                        odds == 0 && (condition || data.GetItem(iid).TypeId == QuestItemType)));
                }
                data.MonsterDrops[mid] = list;
            }

        // Maps : MA.m[id] = { x, y, sa: sous-zone }, MA.sa[id] = { n: nom, a: zone }, MA.a[id] = { n: nom }
        if (paths.TryGetValue("maps", out var mapsPath)
            && Avm1.Run(await File.ReadAllBytesAsync(mapsPath)).GetValueOrDefault("MA") is AsObject MA)
        {
            static Dictionary<int, AsObject> Table(AsObject? o) => o == null ? new()
                : o.Where(kv => kv.Value is AsObject && int.TryParse(kv.Key, out _)).ToDictionary(kv => int.Parse(kv.Key), kv => (AsObject)kv.Value!);
            var areas = Table(MA.GetValueOrDefault("a") as AsObject);
            var subAreas = Table(MA.GetValueOrDefault("sa") as AsObject);
            foreach (var (id, m) in Table(MA.GetValueOrDefault("m") as AsObject))
            {
                var sa = subAreas.GetValueOrDefault((int)Avm1.ToNum(m.GetValueOrDefault("sa")));
                var area = sa != null ? areas.GetValueOrDefault((int)Avm1.ToNum(sa.GetValueOrDefault("a"))) : null;
                data.Maps[id] = new MapInfo(id, (int)Avm1.ToNum(m.GetValueOrDefault("x")), (int)Avm1.ToNum(m.GetValueOrDefault("y")),
                    area != null ? Avm1.ToStr(area.GetValueOrDefault("n")) : "",
                    sa != null ? Avm1.ToStr(sa.GetValueOrDefault("n")) : "",
                    area != null ? (int)Avm1.ToNum(area.GetValueOrDefault("sua")) : 0);
            }
        }

        // Points d'intérêt : HIC[id] = { n: catégorie }, HI = [ { n: nom, c: catégorie, g: icône, m: map } ].
        // Pour un donjon, n est l'identifiant du donjon : DU[id] = { n: nom, lvl: niveau } (fichier « dungeons »)
        if (paths.TryGetValue("hints", out var hintsPath) && data.Maps.Count > 0)
        {
            var hints = Avm1.Run(await File.ReadAllBytesAsync(hintsPath));
            var dungeons = paths.TryGetValue("dungeons", out var duPath)
                ? Avm1.Run(await File.ReadAllBytesAsync(duPath)).GetValueOrDefault("DU") as AsObject : null;
            if (hints.GetValueOrDefault("HIC") is AsObject HIC)
                foreach (var (k, v) in HIC)
                    if (v is AsObject o && int.TryParse(k, out var cid)) data.HintCategories[cid] = Avm1.ToStr(o.GetValueOrDefault("n"));
            if (hints.GetValueOrDefault("HI") is List<object?> HI)
                foreach (var h in HI.OfType<AsObject>())
                {
                    if (!data.Maps.TryGetValue((int)Avm1.ToNum(h.GetValueOrDefault("m")), out var map)) continue;
                    var name = Avm1.ToStr(h.GetValueOrDefault("n")).Trim();
                    if (dungeons?.GetValueOrDefault(name) is AsObject du)
                        name = Avm1.ToStr(du.GetValueOrDefault("n")) + (du.GetValueOrDefault("lvl") is { } lvl ? $" (niv. {Avm1.ToNum(lvl)})" : "");
                    data.Hints.Add(new MapHint(name, (int)Avm1.ToNum(h.GetValueOrDefault("c")), (int)Avm1.ToNum(h.GetValueOrDefault("g")), map));
                }
        }

        // Maisons : H.h[id] = { n: nom }, H.d[map] = { c{case}: maison } (map de la porte)
        if (paths.TryGetValue("houses", out var housesPath) && data.Maps.Count > 0
            && Avm1.Run(await File.ReadAllBytesAsync(housesPath)).GetValueOrDefault("H") is AsObject H
            && H.GetValueOrDefault("h") is AsObject houses && H.GetValueOrDefault("d") is AsObject doors)
        {
            data.HintCategories[HouseCategory] = "Maisons";
            foreach (var (mk, mv) in doors)
                if (int.TryParse(mk, out var mapId) && data.Maps.TryGetValue(mapId, out var map) && mv is AsObject cells)
                    foreach (var hid in cells.Values.Select(v => ((int)Avm1.ToNum(v)).ToString()).Distinct())
                        if (houses.GetValueOrDefault(hid) is AsObject house)
                            data.Hints.Add(new MapHint(Avm1.ToStr(house.GetValueOrDefault("n")).Trim(), HouseCategory, 0, map));
        }

        // Challenges : FC[id] = { n: nom, d: règle }
        if (paths.TryGetValue("fightChallenge", out var fcPath)
            && Avm1.Run(await File.ReadAllBytesAsync(fcPath)).GetValueOrDefault("FC") is AsObject FC)
            foreach (var (k, v) in FC)
                if (v is AsObject o && int.TryParse(k, out var cid))
                {
                    data.FightChallenges[cid] = Avm1.ToStr(o.GetValueOrDefault("n"));
                    data.FightChallengeRules[cid] = Avm1.ToStr(o.GetValueOrDefault("d"));
                    if (o.GetValueOrDefault("g") is double g) data.FightChallengeGfx[cid] = (int)g;
                }

        // Sorts : S[id] = { n: nom, i: { b: fond, up: symbole, bc/fc/pc: couleurs du fond, du cadre et du symbole }, … }
        if (paths.TryGetValue("spells", out var spellsPath)
            && Avm1.Run(await File.ReadAllBytesAsync(spellsPath)).GetValueOrDefault("S") is AsObject S)
            foreach (var (k, v) in S)
                if (v is AsObject o && int.TryParse(k, out var sid))
                {
                    data.Spells[sid] = Avm1.ToStr(o.GetValueOrDefault("n"));
                    // Niveaux : l1…l6 = [effets, effets critiques, …] ; effet = [type, min, max, valeur, durée en tours, …]
                    var turns = Enumerable.Range(1, 6).Select(l => o.GetValueOrDefault("l" + l) is List<object?> { Count: > 0 } lv
                        && lv[0] is List<object?> effects
                            ? effects.OfType<List<object?>>().Select(e => e.Count > 4 ? (int)Avm1.ToNum(e[4]) : 0).DefaultIfEmpty(0).Max() : 0).ToArray();
                    if (turns.Any(t => t > 0)) data.SpellTurns[sid] = turns;
                    if (o.GetValueOrDefault("i") is AsObject i)
                    {
                        static int First(object? c) => c is List<object?> { Count: > 0 } l ? (int)Avm1.ToNum(l[0]) : 0xFFFFFF;
                        data.SpellLooks[sid] = new SpellLook((int)Avm1.ToNum(i.GetValueOrDefault("b")), (int)Avm1.ToNum(i.GetValueOrDefault("up")),
                            First(i.GetValueOrDefault("bc")), First(i.GetValueOrDefault("fc")), First(i.GetValueOrDefault("pc")));
                    }
                }

        // Ressources : IO.d[id] = { n: nom, t: type (1 = ressource) }, IO.g[graphisme] = id ;
        // compétences de récolte : SK[id] = { io: objet interactif, j: métier, i: objet obtenu }
        if (paths.TryGetValue("interactiveobjects", out var ioPath)
            && Avm1.Run(await File.ReadAllBytesAsync(ioPath)).GetValueOrDefault("IO") is AsObject IO
            && skills.GetValueOrDefault("SK") is AsObject harvestSkills)
        {
            var bySkill = harvestSkills.Values.OfType<AsObject>()
                .Where(o => o.ContainsKey("i") && o.ContainsKey("io") && (int)Avm1.ToNum(o.GetValueOrDefault("j")) > 1)
                .GroupBy(o => (int)Avm1.ToNum(o.GetValueOrDefault("io")))
                .ToDictionary(g => g.Key, g => g.Select(o =>
                {
                    int job = (int)Avm1.ToNum(o.GetValueOrDefault("j"));
                    return new HarvestSkill(job, jobNames.GetValueOrDefault(job, "?"), (int)Avm1.ToNum(o.GetValueOrDefault("i")));
                }).DistinctBy(s => (s.JobId, s.ItemId)).ToList());
            if (IO.GetValueOrDefault("d") is AsObject ioDefs)
                foreach (var (k, v) in ioDefs)
                    if (v is AsObject o && int.TryParse(k, out var ioId) && (int)Avm1.ToNum(o.GetValueOrDefault("t")) == 1
                        && bySkill.TryGetValue(ioId, out var harvest))
                        data.Resources[ioId] = new HarvestResource(ioId, Avm1.ToStr(o.GetValueOrDefault("n")).Trim(), harvest);
            if (IO.GetValueOrDefault("g") is AsObject gfx)
                foreach (var (k, v) in gfx)
                    if (int.TryParse(k, out var g)) data.InteractiveGfx[g] = (int)Avm1.ToNum(v);
        }

        // Classeur TTG : TTG.c[carte] = { o: objet carte (-1 : brillante), e: créature représentée, v: 1 si brillante },
        // TTG.e[créature] = { n: nom, f: famille, a: rareté }, TTG.f[famille] = { n: nom, o: objet (paquet) }
        if (paths.TryGetValue("ttg", out var ttgPath)
            && Avm1.Run(await File.ReadAllBytesAsync(ttgPath)).GetValueOrDefault("TTG") is AsObject TTG
            && TTG.GetValueOrDefault("c") is AsObject cards)
        {
            var entities = TTG.GetValueOrDefault("e") as AsObject;
            if (TTG.GetValueOrDefault("f") is AsObject families)
                foreach (var (k, v) in families)
                    if (v is AsObject o && int.TryParse(k, out var fam))
                        data.TtgFamilies[fam] = new TtgFamily(fam, Avm1.ToStr(o.GetValueOrDefault("n")).Trim(), (int)Avm1.ToNum(o.GetValueOrDefault("o")));
            foreach (var (k, v) in cards)
            {
                if (v is not AsObject o || !int.TryParse(k, out var card)) continue;
                int item = (int)Avm1.ToNum(o.GetValueOrDefault("o"));
                if (item > 0) data.TtgCardItems[card] = item;
                var entity = entities?.GetValueOrDefault(((int)Avm1.ToNum(o.GetValueOrDefault("e"))).ToString()) as AsObject;
                if (entity == null) continue;
                data.TtgCards[card] = new TtgCard(card, Avm1.ToStr(entity.GetValueOrDefault("n")).Trim(),
                    (int)Avm1.ToNum(entity.GetValueOrDefault("f")), (int)Avm1.ToNum(entity.GetValueOrDefault("a")),
                    Math.Max(0, item), Avm1.ToNum(o.GetValueOrDefault("v")) == 1);
            }
        }

        if (paths.TryGetValue("quests", out var questsPath))
            try { LoadOcre(data, Avm1.Run(await File.ReadAllBytesAsync(questsPath))); }
            catch (Exception ex) { log($"Quête du Dofus Ocre illisible : {ex.Message}"); }

        LoadMonsterXp(data, log);
        data.JobNames.AddRange(data.Recipes.SelectMany(r => r.Jobs).Distinct().Order());
        log($"Données du jeu : {data.Items.Count} objets, {data.Recipes.Count} recettes.");
        return data;
    }
}
