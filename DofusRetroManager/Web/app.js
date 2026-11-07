'use strict';
// Interface HTML de Dofus Retro Manager. La page n'a pas de logique métier : elle affiche l'état des contrôles WPF
// (reçu par messages, clé = nom x:Name du contrôle) et leur renvoie les actions de l'utilisateur.

const S = {};                                 // état reçu
const local = { sort: {}, limit: {}, open: {} }; // tri, pagination et arbres dépliés, propres à la page
const post = m => window.chrome.webview.postMessage(m);

// Contrôles suivis, par onglet (les autres onglets s'ouvrent dans l'interface classique)
const COMMON = ['VersionText', 'CaptureDot', 'CaptureStatus', 'BankStatus', 'CaptureButton', 'AdminButton', 'UpdateBanner',
  'UpdateTitle', 'UpdateText', 'UpdateActionButton', 'UpdateLaterButton', 'UpdateNotesButton', 'MainTabs'];
const TABS = [
  { key: 'bank', label: 'Banque', watch: ['BankOwner', 'BankCharacter', 'BankSearch', 'BankTypeFilter', 'BankCount', 'BankValue', 'BankGrid'] },
  { key: 'recipes', label: 'Recettes', watch: ['RecipeSearch', 'JobFilter', 'ModeFilter', 'SellMinPanel', 'SellMin',
    'LevelMin', 'LevelMax', 'SlotFilters', 'IncludeInventory', 'InventoryHint', 'RecipeCount', 'IngredientChip', 'IngredientChipText',
    'RecipeGrid', 'DetailTitle', 'DetailSub', 'DetailCostPanel', 'DetailCraftCount', 'DetailCraftMax', 'DetailCostTitle', 'DetailCost',
    'DetailCostNote', 'DetailProfit', 'DetailProfitOwned', 'DetailStatsPanel', 'DetailStats', 'DetailIngredients'] },
  { key: 'brisage', label: 'Brisage', watch: ['BrisageSearch', 'BrisageSource', 'BrisageTypeFilter', 'BrisageLevelMin',
    'BrisageLevelMax', 'BrisageCount', 'BrisageStats', 'BrisageGrid', 'BrisageGainColumn', 'BrisageEmpty', 'BrisageDetailTitle',
    'BrisageDetailSub', 'BrisageGoRecipe', 'BrisageBreakCount', 'BrisageBreakMax', 'BrisageDetailTotal', 'BrisageRunes'] },
  { key: 'hdv', label: 'HDV', watch: ['HdvSearch', 'HdvTypeFilter', 'HdvShowAll', 'HdvCount', 'HdvGrid', 'HdvEmpty',
    'HdvDetailTitle', 'HdvDetailSub', 'HdvChart', 'HdvHistory', 'HdvForget',
    'ShareToggle', 'ShareSync', 'SharePanel', 'ShareEnabled', 'ShareUrl', 'ShareKey', 'ShareTest', 'ShareSave', 'ShareStatus'] },
  { key: 'ttg', label: 'TTG', watch: ['TtgSearch', 'TtgRarity', 'TtgCharacter', 'TtgMissing', 'TtgShow', 'TtgShowAll', 'TtgDaysBox', 'TtgCount',
    'TtgCollection', 'TtgGrid', 'TtgBestColumn', 'TtgEmpty', 'TtgDetailTitle', 'TtgDetailSub', 'TtgOffersMode', 'TtgOffersTitle',
    'TtgOffers', 'TtgMerchantChart', 'TtgHdvChart'] },
  { key: 'pda', label: 'PDA', watch: ['PdaSearch', 'PdaSource', 'PdaCharacter', 'PdaCategory', 'PdaShowAll', 'PdaCount', 'PdaValue',
    'PdaGrid', 'PdaEmpty', 'PdaDetailTitle', 'PdaDetailSub', 'PdaChart'] },
  { key: 'ocre', label: 'Ocre', watch: ['OcreCharacter', 'OcreStep', 'OcreKindFilter', 'OcreSummary', 'OcreGrid', 'OcreDetailTitle',
    'OcreDetailSub', 'OcreSouls'] },
  { key: 'combat', label: 'Combat', watch: ['HuntCharacterBox', 'HuntLevel', 'HuntGroupLevel', 'HuntWisdom', 'HuntProspection',
    'HuntGroupProspection', 'HuntSound', 'HuntVolume', 'HuntVolumeText', 'HuntStatus', 'EconomeStatus', 'EconomeSpells', 'HuntMap', 'HuntGroupGrid', 'HuntEmpty',
    'HuntSession', 'HuntPauseSession', 'HuntProjection', 'HuntLearned', 'HuntArchiGrid', 'HuntFightGrid', 'HuntDropsTitle', 'HuntDropGrid', 'HuntArchiPopup',
    'HuntAlertsToggle', 'HuntAlertMonster', 'HuntAlertCount', 'HuntAlertMin', 'HuntAlertMax', 'HuntAlertStatus', 'HuntAlertList',
    'ChronoTime', 'ChronoStart', 'ChronoReset', 'ChronoFights', 'ChronoGains', 'ChronoRate', 'ChronoGrid', 'AreneChallenges',
    'AreneOtherChallenges', 'AreneGroup', 'AreneGroupEdit', 'AreneGroupName', 'AreneGroupStatus', 'AreneGroupMembers'] },
  { key: 'windows', label: 'Switcheur', watch: ['OrganizerEnabled', 'HotkeyDebounce', 'OrganizerClickSwitch', 'OrganizerClickKey', 'OrganizerClickLeft', 'OrganizerClickRight', 'OrganizerFixBounce', 'NotifyAutoSwitchTurn', 'OrganizerSplitMode', 'OrganizerMaximize', 'OrganizerLockOrder', 'HotkeyNext', 'HotkeyPrevious', 'HotkeyMain', 'HotkeySadida', 'SadidaReturn', 'HotkeyStatus', 'NotifyFight', 'NotifyTurn',
    'NotifyExchange', 'NotifyParty', 'NotifyToast', 'NotifyFlash', 'NotifySound', 'NotifyAutoSwitch', 'OrganizerCount', 'OrganizerGrid', 'OverlayEnabled', 'OverlayEconome', 'OverlayGroups', 'OverlayHarvest',
    'OverlayRotation', 'OverlayOpacity', 'OverlayOpacityText', 'OverlayMove', 'OrganizerSaveOrder', 'OrganizerResetOrder',
    'OrganizerOrderStatus'] },
  { key: 'fm', label: 'FM', watch: ['FmItemCombo', 'FmItemName', 'FmReliquat', 'FmReliquatInput', 'FmStats',
    'FmHistoryGrid', 'FmEmpty', 'FmWeights', 'FmWeightsPanel', 'FmWeightsGrid'] },
  { key: 'carte', label: 'Carte', watch: ['CarteHarvestTitle', 'CarteHarvest', 'CarteHarvestList', 'CarteCellPanel', 'CarteCellTitle',
    'CarteCellInfo', 'CarteCellHarvest', 'CarteCellList'] },
  { key: 'journal', label: 'Journal', watch: ['LogBox', 'LogPacketsBox'] },
];
// Contrôles suivis : l'en-tête et l'onglet affiché seulement (et le livre TTG quand il est ouvert) ; l'application ne
// relit et n'envoie que ceux-là. La liste est renvoyée à chaque changement d'onglet.
let watching = '';
function watchTab(active) {
  const names = COMMON.concat(TABS[active]?.watch || [], local.book ? ['_ttgBook'] : [], local.trades && TABS[active]?.key === 'hdv' ? ['_trades'] : []);
  const key = names.join(',');
  if (key === watching) return;
  watching = key;
  post({ type: 'watch', names });
}
watchTab(0);

// --- Utilitaires ---
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
// Milliers séparés par une espace insécable ordinaire (l'espace fine du format français n'existe pas dans la police)
const fmt = n => n == null ? '—' : Math.round(n).toLocaleString('fr-FR').replace(/\s/g, ' ');
const pct = n => n == null ? '—' : (n > 0 ? '+' : '') + Math.round(n * 100) + ' %';
const T = n => S[n]?.t ?? '';
const shown = n => S[n] != null && S[n].v !== false;
const rows = n => S[n]?.rows || [];
const itemImg = (id, cls = '') => `<img class="ico ${cls}" src="icon/item/${id}.png" alt="" loading="lazy">`;
const attrs = o => Object.entries(o).map(([k, v]) => `data-${k}="${esc(v)}"`).join(' ');

function select(name, extra = '') {
  const s = S[name];
  if (!s?.items) return '';
  return `<select data-set="${name}" ${extra}>${s.items.map((o, i) => `<option value="${i}" ${i === s.i ? 'selected' : ''}>${esc(o)}</option>`).join('')}</select>`;
}
const input = (name, placeholder = '', w = '') =>
  `<input type="search" data-set="${name}" data-fk="${name}" value="${esc(T(name))}" placeholder="${esc(placeholder)}" ${w ? `style="width:${w}"` : ''}>`;
const check = (name, label) => `<label class="check"><input type="checkbox" data-set="${name}" ${S[name]?.on ? 'checked' : ''}>${esc(label)}</label>`;
const btn = (name, label, cls = '') => shown(name) || !S[name]
  ? `<button class="${cls}" data-act="click" data-name="${name}" ${S[name]?.e === false ? 'disabled' : ''}>${esc(S[name]?.t || label)}</button>` : '';
const colored = name => `<span style="${S[name]?.c ? `color:${S[name].c}` : ''}">${esc(T(name))}</span>`;

// Onglet FM : poids des stats, modifiables (Entrée ou sortie du champ), à la place de l'historique
function fmWeights() {
  return `<div class="fm-weights">
    <div class="filters"><div><h3 class="acc">Poids des stats</h3>
      <span class="muted">Poids d'un point de chaque stat, utilisé pour estimer le reliquat. Modifiez une valeur puis Entrée : elle compte pour les runes suivantes (l'historique garde les anciens calculs).</span></div>
      ${btn('FmWeightsReset', 'Tout rétablir', 'small')}</div>
    ${table('FmWeightsGrid', [
      { h: '', f: r => itemImg(r.RuneId, 'sm') },
      { h: 'Rune', k: 'Rune', f: r => `<span class="strong">${esc(r.Rune)}</span>` },
      { h: 'Stat', k: 'Label' },
      { h: 'Poids par point', f: r => `<input type="text" class="weight ${r.Modified ? 'modified' : ''}" data-fmweight="${r.Stat}"
        data-fk="fmweight-${r.Stat}" value="${esc(r.Weight)}">` },
      { h: 'Par défaut', f: r => `<span class="muted">${esc(r.Default)}</span>` },
      { h: '', f: r => r.Modified ? `<button class="small" title="Revenir au poids par défaut" ${attrs({ act: 'row', grid: 'FmWeightsGrid', i: r._i, handler: 'FmWeightReset_Click' })}>Rétablir</button>` : '' },
    ], { select: false })}</div>`;
}

// Tableau triable : cols = [{ h, k (clé de tri), f (rendu), num }] ; clic sur une ligne -> sélection côté WPF
function table(name, cols, opts = {}) {
  const data = rows(name).map((r, i) => ({ ...r, _i: i }));
  const sort = local.sort[name];
  if (sort) {
    const dir = sort.dir;
    data.sort((a, b) => {
      const x = a[sort.k], y = b[sort.k];
      if (x == null && y == null) return 0;
      if (x == null) return 1;
      if (y == null) return -1;
      return (typeof x === 'number' ? x - y : String(x).localeCompare(String(y), 'fr')) * dir;
    });
  }
  const limit = local.limit[name] || 300;
  const sel = S[name]?.sel ?? -1;
  const head = cols.map(c => `<th class="${c.num ? 'num' : ''}" ${c.k ? attrs({ act: 'sort', grid: name, k: c.k }) : ''}>${esc(c.h)}${sort && sort.k === c.k ? `<span class="arrow">${sort.dir > 0 ? '▲' : '▼'}</span>` : ''}</th>`).join('');
  const body = data.slice(0, limit).map(r => {
    const drag = opts.draggable?.(r) ? `draggable="true" data-drag-grid="${name}" data-drag-i="${r._i}"` : '';
    const tr = `<tr class="${opts.select !== false ? 'clickable' : ''} ${r._i === sel ? 'sel' : ''} ${drag ? 'draggable' : ''}" ${drag} ${opts.select !== false ? attrs({ act: 'select', grid: name, i: r._i }) : ''}>`
      + cols.map(c => `<td class="${c.num ? 'num' : ''} ${c.cls || ''}">${c.f ? c.f(r) : esc(r[c.k])}</td>`).join('') + '</tr>';
    return tr + (opts.after ? opts.after(r, cols.length) : '');
  }).join('');
  const more = data.length > limit ? `<button class="more ghost" data-act="more" data-grid="${name}">Afficher plus (${data.length - limit} restantes)</button>` : '';
  const empty = data.length === 0 ? `<div class="empty">${esc(opts.empty || 'Rien à afficher.')}</div>` : '';
  return `<div class="table-wrap"><table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>${empty}${more}</div>`;
}

// Position cliquable (TTG, Combat) : ouvre l'onglet Carte sur sa case ; grisée si la map est inconnue
/** « [12,11] Amakna (Clairière de Brouce Boulgoure) » -> « [12,11] Amakna » ; « 01/11/2026 » -> « 01/11 ». */
const shortMap = t => String(t || '').replace(/\s*\(.*\)\s*$/, '');
const shortDate = t => /^\d\d\/\d\d\/\d{4}$/.test(t || '') ? t.slice(0, 5) : t;
const posLink = (text, go, cls = '') => !text ? '' : go
  ? `<a class="pos-link ${cls}" data-act="goto" data-g="${esc(JSON.stringify(go))}" title="Voir sur la carte : ${esc(text)}">${esc(text)}</a>`
  : `<span class="muted ${cls}" title="${esc(text)}">${esc(text)}</span>`;

// Courbe de prix en SVG (points [ms, prix])
function chart(points) {
  if (!points?.length) return '<div class="empty">Aucun relevé pour l\'instant.</div>';
  const W = 340, H = 170, L = 52, R = 8, Tp = 10, B = 22;
  const ts = points.map(p => p[0]), ps = points.map(p => p[1]);
  const t0 = Math.min(...ts), t1 = Math.max(...ts), p0 = Math.min(...ps), p1 = Math.max(...ps);
  const x = t => L + (t1 === t0 ? (W - L - R) / 2 : (t - t0) / (t1 - t0) * (W - L - R));
  const y = p => Tp + (p1 === p0 ? (H - Tp - B) / 2 : (1 - (p - p0) / (p1 - p0)) * (H - Tp - B));
  const sorted = points.slice().sort((a, b) => a[0] - b[0]);
  const line = sorted.map(p => `${x(p[0]).toFixed(1)},${y(p[1]).toFixed(1)}`).join(' ');
  const area = `${x(sorted[0][0])},${H - B} ${line} ${x(sorted[sorted.length - 1][0])},${H - B}`;
  const ticks = [p1, (p0 + p1) / 2, p0].map(p => `<line class="grid" x1="${L}" x2="${W - R}" y1="${y(p)}" y2="${y(p)}"/>
    <text class="axis" x="${L - 6}" y="${y(p) + 3}" text-anchor="end">${fmt(p)}</text>`).join('');
  const d = t => new Date(t).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' });
  return `<svg class="chart" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none">
    <defs><linearGradient id="chartGrad" x1="0" x2="1"><stop offset="0" stop-color="#8b5cf6"/><stop offset="1" stop-color="#06b6d4"/></linearGradient>
    <linearGradient id="chartFill" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="#8b5cf6" stop-opacity=".35"/><stop offset="1" stop-color="#8b5cf6" stop-opacity="0"/></linearGradient></defs>
    ${ticks}<polygon class="area" points="${area}"/><polyline class="line" points="${line}"/>
    ${sorted.map(p => `<circle cx="${x(p[0])}" cy="${y(p[1])}" r="3"><title>${d(p[0])} : ${fmt(p[1])} k</title></circle>`).join('')}
    <text class="axis" x="${L}" y="${H - 6}">${d(t0)}</text><text class="axis" x="${W - R}" y="${H - 6}" text-anchor="end">${d(t1)}</text>
  </svg>`;
}

// --- Rendu ---
function header() {
  return `<header class="top glass">
    <img class="logo" src="logo.png" alt="DRM">
    <div class="brand">
      <h1><span class="grad">Dofus Retro Manager</span><small>${esc(T('VersionText'))}</small></h1>
      <div class="status"><i class="dot" style="background:${S.CaptureDot?.c || 'gray'};color:${S.CaptureDot?.c || 'gray'}"></i>
        <b>${esc(T('CaptureStatus'))}</b><span>·</span><span class="bank">${esc(T('BankStatus'))}</span></div>
    </div>
    <div class="actions">
      <div class="theme-picker" title="Style">${['Classique', 'Moderne'].map(s =>
        `<button class="${(S._style || 'Moderne') === s ? 'on' : ''}" data-act="style" data-v="${s}">${s}</button>`).join('')}</div>
      <div class="theme-picker" title="Thème">${[['Jour', '☀ Jour'], ['Crepuscule', '◐ Crépuscule'], ['Nuit', '☾ Nuit']].map(([t, l]) =>
        `<button class="${(S._theme || 'Crepuscule') === t ? 'on' : ''}" data-act="theme" data-v="${t}">${l}</button>`).join('')}</div>
      ${btn('AdminButton', 'Relancer en administrateur')}
      ${btn('CaptureButton', 'Démarrer la capture', 'primary')}
      <button data-act="changelog" title="Les changements de chaque version">Nouveautés</button>
    </div>
  </header>`;
}

// Journal des changements (CHANGELOG.md, servi par l'application sous changelog.js)
function changelog() {
  if (!local.changelog) return '';
  const log = window.CHANGELOG || { current: '', entries: [] };
  const inline = s => esc(s).replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
  return `<div class="modal-back" data-act="changelog-close"><div class="modal glass" data-act="none">
      <div class="row"><h2>Nouveautés</h2><button class="small ghost right" data-act="changelog-close">✕</button></div>
      <div class="sub">Vous utilisez la version ${esc(log.current)}.</div>
      <div class="modal-body">${log.entries.length ? log.entries.map(e => `<div class="card changelog ${e.version === log.current ? 'current' : ''}">
          <div class="row"><b class="acc">Version ${esc(e.version)}</b>${e.version === log.current ? '<span class="chip accent">installée</span>' : ''}
            <span class="muted right">${esc(e.date)}</span></div>
          <ul>${e.items.map(i => `<li>${inline(i)}</li>`).join('')}</ul></div>`).join('')
        : '<div class="empty">Le journal des changements est introuvable.</div>'}</div>
    </div></div>`;
}

// « Mes échanges » (onglet HDV) : achats et ventes de vos personnages. Totaux sur la période choisie, offres en cours,
// objets les plus échangés et journal. Les données (S._trades) viennent de l'application ; les filtres sont faits ici.
local.tf = { c: '', d: 30, k: '' };
function tradesView() {
  const all = S._trades;
  if (!all) return '<div class="empty">Chargement des échanges…</div>';
  const f = local.tf;
  const since = f.d ? Date.now() - f.d * 864e5 : 0;
  const mine = all.filter(t => !f.c || t.c === f.c);
  const buys = mine.filter(t => t.k === 'Achat' && t.t >= since);
  const listed = mine.filter(t => t.k === 'Offre' && t.t >= since);
  const sold = mine.filter(t => t.k === 'Offre' && t.st === 'Vendue' && (t.ct ?? t.t) >= since);
  const onSale = mine.filter(t => t.k === 'Offre' && t.st === 'En vente');
  const sum = (l, k = 'p') => l.reduce((a, t) => a + (t[k] || 0), 0);
  const spent = sum(buys), earned = sum(sold), taxes = sum(listed, 'tax'), net = earned - spent - taxes;
  const chars = [...new Set(all.map(t => t.c))].filter(Boolean).sort();
  const tile = (label, value, sub, cls = '', tip = '') => `<div class="card kpi" title="${esc(tip)}"><small class="muted">${label}</small>
    <b class="${cls}">${value}</b><small class="muted">${sub}</small></div>`;
  const k = n => fmt(n) + ' k';
  const short = t => new Date(t).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });

  // Objets les plus échangés : achetés, vendus, prix moyens à l'unité
  const byItem = new Map();
  const item = t => byItem.get(t.id) || byItem.set(t.id, { id: t.id, n: t.n, bq: 0, bp: 0, sq: 0, sp: 0, oq: 0, hdv: t.hdv }).get(t.id);
  buys.forEach(t => { const x = item(t); x.bq += t.q; x.bp += t.p; });
  sold.forEach(t => { const x = item(t); x.sq += t.q; x.sp += t.p; });
  onSale.forEach(t => { item(t).oq += t.q; });
  const top = [...byItem.values()].sort((a, b) => (b.bp + b.sp) - (a.bp + a.sp)).slice(0, 12);

  // Journal : un évènement par achat, mise en vente, vente ou retrait
  const events = [];
  for (const t of mine) {
    if (t.k === 'Achat') events.push({ t, at: t.t, kind: 'Achat', cls: 'bad', sign: -1 });
    else {
      events.push({ t, at: t.t, kind: 'Mise en vente', cls: 'muted', sign: 0 });
      if (t.st !== 'En vente' && t.ct) events.push({ t, at: t.ct, kind: t.st === 'Vendue' ? (t.u ? 'Vendue (hors ligne)' : 'Vendue') : t.st === 'Expirée' ? 'Expirée' : 'Retirée', cls: t.st === 'Vendue' ? 'ok' : 'muted', sign: t.st === 'Vendue' ? 1 : 0 });
    }
  }
  const shown = events.filter(e => e.at >= since && (!f.k || e.kind.startsWith(f.k))).sort((a, b) => b.at - a.at).slice(0, 300);
  const kinds = [['', 'Tout'], ['Achat', 'Achats'], ['Mise en vente', 'Mises en vente'], ['Vendue', 'Ventes'], ['Retirée', 'Retraits'], ['Expirée', 'Expirées']];

  return `<div class="trades">
    <div class="filters"><span class="lbl">Personnage</span><select data-tf="c"><option value="">Tous</option>${chars.map(c => `<option ${c === f.c ? 'selected' : ''}>${esc(c)}</option>`).join('')}</select>
      <span class="lbl">Période</span><select data-tf="d">${[[1, '24 h'], [7, '7 jours'], [30, '30 jours'], [0, 'Tout']].map(([v, l]) => `<option value="${v}" ${v === f.d ? 'selected' : ''}>${l}</option>`).join('')}</select>
      <span class="muted small" title="Achats : lot disparu d'une offre consultée et objet reçu au même moment, ou objet reçu dans la boutique d'un marchand. Ventes : message de vente du jeu (en jeu, ou à la connexion pour celles faites hors jeu), offre retirée sans retour de l'objet, ou absente à l'ouverture suivante du mode vente. Rien n'est envoyé hors de ce PC.">ⓘ d'où viennent ces chiffres</span></div>
    <div class="kpis">
      ${tile('Achats', k(spent), `${buys.length} lot(s)`, 'bad', 'Total payé à l\'HDV et chez les marchands')}
      ${tile('Ventes', k(earned), `${sold.length} offre(s) vendue(s)`, 'ok', 'Offres vendues sur la période (prix demandé)')}
      ${tile('Taxes HDV', k(taxes), `${listed.length} mise(s) en vente`, '', 'Taxe payée à la mise en vente (estimée d\'après le taux de l\'HDV)')}
      ${tile('Bilan', (net > 0 ? '+' : '') + k(net), 'ventes − achats − taxes', net >= 0 ? 'ok' : 'bad')}
      ${tile('En vente', k(sum(onSale)), `${onSale.length} offre(s) en cours`, 'acc', 'Offres encore en vente (toutes dates)')}
    </div>
    <div class="split trades-split">
      <div class="table-wrap"><table><thead><tr><th></th><th>Date</th><th>Objet</th><th class="num">Qté</th><th class="num">Prix / u</th><th class="num">Total</th><th>Perso</th><th></th></tr></thead><tbody>
        ${shown.map(e => `<tr><td><span class="chip ${e.cls}">${esc(e.kind)}</span></td><td class="muted">${short(e.at)}</td>
          <td><span class="item-cell">${itemImg(e.t.id, 'sm')}<span class="ellipsis" title="${esc(e.t.n)}">${esc(e.t.n)}</span></span>${e.t.s === 'Marchand' ? ' <span class="chip" title="Acheté dans la boutique d\'un marchand">🏪</span>' : ''}</td>
          <td class="num">×${e.t.q}</td><td class="num">${fmt(e.t.p / e.t.q)}</td><td class="num strong ${e.cls}">${e.sign < 0 ? '−' : e.sign > 0 ? '+' : ''}${fmt(e.t.p)}</td>
          <td class="muted ellipsis" title="${esc(e.t.c)}">${esc(e.t.c.replace(/^\w+-/, ''))}</td>
          <td><button class="small ghost" title="Oublier cette ligne (rapprochement erroné)" data-act="trade-del" data-i="${e.t.i}">✕</button></td></tr>`).join('')}
      </tbody></table>${shown.length === 0 ? '<div class="empty">Aucun échange sur la période. Achetez ou mettez en vente à l\'HDV, application lancée.</div>' : ''}</div>
      <aside class="detail glass"><div class="togs">${kinds.map(([v, l]) => `<button class="pill ${f.k === v ? 'on' : ''}" data-act="trade-kind" data-k="${v}">${l}</button>`).join('')}</div>
        <h3>Objets les plus échangés</h3>
        ${top.map(x => `<div class="top-item">${itemImg(x.id)}<div><div class="name ellipsis" title="${esc(x.n)}">${esc(x.n)}</div>
          <div class="price">${x.bq ? `<span class="bad" title="Acheté : ${x.bq} pour ${k(x.bp)}">▼ ${x.bq} · ${fmt(x.bp / x.bq)}/u</span>` : ''}
          ${x.sq ? ` <span class="ok" title="Vendu : ${x.sq} pour ${k(x.sp)}">▲ ${x.sq} · ${fmt(x.sp / x.sq)}/u</span>` : ''}
          ${x.oq ? ` <span class="acc" title="En vente">⏳ ${x.oq}</span>` : ''}</div></div>
          <span class="muted small" title="Prix unitaire HDV relevé">${x.hdv != null ? 'HDV ' + fmt(x.hdv) : ''}</span></div>`).join('') || '<div class="muted">Rien sur la période.</div>'}
      </aside></div></div>`;
}

// Livre de la collection TTG (bouton « Classeur » de l'onglet TTG), comme celui du jeu : totaux, raretés, familles et cartes
function ttgBook() {
  if (!local.book) return '';
  const B = S._ttgBook;
  if (!B) return `<div class="modal-back" data-act="book-close"><div class="modal glass" data-act="none"><div class="empty">Données des cartes pas encore chargées.</div></div></div>`;
  const bar = (label, done, total, tip) => `<div class="book-prog" data-tip="${esc(tip)}"><span>${label}</span>
    <i><b style="width:${total ? Math.round(100 * done / total) : 0}%"></b></i><small>${done} / ${total}</small></div>`;
  const stars = n => '★'.repeat(Math.max(1, n));
  const fam = B.Families.find(f => f.Id === local.book.fam) || B.Families.find(f => f.Owned > 0) || B.Families[0];
  const list = fam ? (local.book.shiny ? fam.Shiny : fam.Cards) : [];
  // Prix court : « 850 », « 125 K », « 1,2 M » (montant exact au survol)
  const short = n => n == null ? '' : n >= 1e6 ? (n / 1e6).toFixed(1).replace('.', ',').replace(',0', '') + ' M' : n >= 1e4 ? Math.round(n / 1000) + ' K' : fmt(n);
  const card = c => `<div class="book-card s${c.State} ${c.Shiny ? 'shiny' : ''}" data-tip="${esc(c.Tip)}">
      <small class="muted">#${c.Number}</small>${c.State > 0 && c.ArtItem ? itemImg(c.ArtItem, 'card') : '<div class="book-blank"></div>'}
      <b>${esc(c.Name)}</b><span class="stars">${stars(c.Rarity)}</span><span class="acc strong price">${c.Price != null ? fmt(c.Price) + ' k' : ''}</span></div>`;
  return `<div class="modal-back" data-act="book-close"><div class="modal glass book" data-act="none">
      <div class="row"><h2>Collection de ${esc(B.Owner)}</h2><button class="small ghost right" data-act="book-close">✕</button></div>
      <div class="book-pages">
        <section class="book-page">
          <p class="section-title">Par types</p>
          ${bar('Classeur', B.Done, B.Total, `Cartes rangées au classeur. ${B.Owned} possédée(s) en comptant la banque et l'inventaire.`)}
          ${bar('Brillantes', B.ShinyDone, B.ShinyTotal, 'Cartes brillantes rangées au classeur')}
          ${bar('Familles', B.FamiliesDone, B.FamiliesTotal, 'Familles complètes au classeur')}
          <p class="section-title">Par raretés</p>
          ${B.Rarities.map(r => bar(`<span class="stars">${stars(r.Rarity)}</span>`, r.Done, r.Total, `Rareté ${r.Rarity} : cartes rangées au classeur`)).join('')}
          <p class="section-title">Par familles</p>
          <div class="book-fams">${B.Families.map(f => `<button class="book-fam ${f === fam ? 'on' : ''}" data-act="book-fam" data-id="${f.Id}"
              data-tip="${esc(f.Tip)}">
              ${itemImg(f.ItemId)}<i><b style="width:${f.Total ? Math.round(100 * f.Done / f.Total) : 0}%"></b></i><small class="muted">${short(f.PackPrice)}</small></button>`).join('')}</div>
        </section>
        <section class="book-page">
          <div class="row"><h3>${esc(fam?.Name || '')}</h3><span class="muted">${list.filter(c => c.State === 2).length} / ${list.length}</span>
            ${fam?.PackPrice != null ? `<span class="chip" data-tip="Prix du paquet de la famille">📦 ${short(fam.PackPrice)}</span>` : ''}
            ${fam?.MissingCost > 0 && !local.book.shiny ? `<span class="chip" data-tip="Prix connu des cartes manquantes">Manquantes ${short(fam.MissingCost)}</span>` : ''}
            <button class="small right ${local.book.shiny ? 'primary' : 'ghost'}" data-act="book-shiny" title="Cartes brillantes de la famille">✨ Brillantes</button></div>
          <div class="book-cards">${list.map(card).join('')}</div>
          <small class="muted">Couleur : au classeur · pâle : en banque ou inventaire · pointillés : manquante</small>
        </section>
      </div></div></div>`;
}

// Confirmation dans la page (ex. « Oublier l'objet » de l'onglet FM), puis l'action est envoyée à l'application
function confirmBox() {
  if (!local.confirm) return '';
  return `<div class="modal-back" data-act="confirm-close"><div class="modal glass confirm" data-act="none">
      <h2>Confirmer</h2><p>${esc(local.confirm.text)}</p>
      <div class="row"><span class="right"></span><button data-act="confirm-close">Annuler</button>
        <button class="primary" data-act="confirm-ok">Confirmer</button></div>
    </div></div>`;
}

function banner() {
  if (!shown('UpdateBanner')) return '';
  return `<div class="banner glass"><div class="txt"><b>${esc(T('UpdateTitle'))}</b><p>${esc(T('UpdateText'))}</p></div>
    ${btn('UpdateNotesButton', 'Nouveautés')}${btn('UpdateActionButton', 'Télécharger et installer', 'primary')}${btn('UpdateLaterButton', 'Plus tard')}</div>`;
}

// Icônes des onglets (SVG dessinés au trait, couleur du texte) et ce que fait chaque onglet (infobulle)
const TAB_ICONS = {
  bank: '<rect x="3" y="9" width="18" height="11" rx="2"/><path d="M3 9a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4"/><path d="M3 13h18"/><rect x="10.5" y="11.5" width="3" height="4" rx="1"/>',
  recipes: '<path d="M12 6.5C10 5 7 4.5 4 5v14c3-.5 6 0 8 1.5 2-1.5 5-2 8-1.5V5c-3-.5-6 0-8 1.5z"/><path d="M12 6.5v14"/>',
  brisage: '<path d="M6.5 3h11L22 9 12 21 2 9z"/><path d="M12.5 3 10 8.5l3.5 3.5-2.5 4 1 5"/>',
  hdv: '<path d="M3 12V4a1 1 0 0 1 1-1h8l9 9-9 9z"/><circle cx="7.5" cy="7.5" r="1.5"/><path d="M11 14l3-3"/>',
  ttg: '<path d="M14.5 6.5V5.4a2 2 0 0 1 2.4-1.9l2.9.6a2 2 0 0 1 1.6 2.3l-2.3 11.8a2 2 0 0 1-2.3 1.6l-2.3-.4"/><rect x="3.5" y="5.5" width="11" height="15" rx="2"/><path d="M9 10.5l1 2 2 .3-1.5 1.4.4 2.1L9 15.3l-1.9 1 .4-2.1L6 12.8l2-.3z"/>',
  pda: '<path d="M12 2.5l7.5 6.5L12 21.5 4.5 9z"/><path d="M4.5 9h15"/><path d="M9 9l3 12.5L15 9"/><path d="M9 9l3-6.5L15 9"/>',
  ocre: '<path d="M12 2.5C8 2.5 4.5 9 4.5 14a7.5 7.5 0 0 0 15 0C19.5 9 16 2.5 12 2.5z"/><path d="M8 13l3-3 2 3 3-3"/>',
  combat: '<path d="M14.5 17.5 3 6V3h3l11.5 11.5"/><path d="M13 19l6-6"/><path d="M16 16l4 4"/><path d="M9.5 17.5 21 6V3h-3L6.5 14.5"/><path d="M11 19l-6-6"/><path d="M8 16l-4 4"/>',
  windows: '<path d="M4 8h14"/><path d="M14 4l4 4-4 4"/><path d="M20 16H6"/><path d="M10 12l-4 4 4 4"/>',
  fm: '<path d="M4 20 15 9"/><path d="M13.5 7.5l3 3"/><path d="M18 3v4M16 5h4"/><path d="M20.5 11.5v3M19 13h3"/><path d="M9.5 3v2.5M8.25 4.25h2.5"/>',
  carte: '<path d="M3 6.5 9 4l6 2.5L21 4v13.5L15 20l-6-2.5L3 20z"/><path d="M9 4v13.5"/><path d="M15 6.5V20"/>',
  journal: '<rect x="4.5" y="3" width="15" height="18" rx="2"/><path d="M8.5 8h7M8.5 12h7M8.5 16h4"/>',
};
const TAB_TIPS = {
  bank: 'Contenu de votre banque et sa valeur estimée', recipes: 'Recettes faisables avec vos ressources, coût et bénéfice',
  brisage: 'Objets à briser pour générer des runes', hdv: "Prix relevés à l'hôtel de vente", ttg: 'Cartes TTG : collection et prix des marchands',
  pda: "Pierres d'âme : prix par monstre et vos pierres", ocre: 'Quête du Dofus Ocre : étapes, âmes possédées et à acheter', combat: 'Groupes de la map, drops, XP et rentabilité',
  windows: "Passer d'un personnage à l'autre (multi-compte)", fm: 'Forgemagie : runes passées et reliquat',
  carte: "Carte du monde et points d'intérêt", journal: "Messages de l'application",
};
const tabIcon = key => `<svg class="tab-ico" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"
  stroke-linejoin="round" aria-hidden="true">${TAB_ICONS[key] || ''}</svg>`;

function tabs(active) {
  return `<nav class="tabs glass">${TABS.map((t, i) => `<button class="tab ${i === active ? 'on' : ''}" data-act="tab" data-i="${i}"
    title="${esc(TAB_TIPS[t.key] || '')}">${tabIcon(t.key)}${esc(t.label)}</button>`).join('')}</nav>`;
}

const views = {
  bank() {
    return `<div class="filters"><span class="strong acc">${esc(T('BankOwner'))}</span><span class="lbl">Personnage</span>${select('BankCharacter')}${input('BankSearch', 'Rechercher un objet…', '260px')}${select('BankTypeFilter')}
      <span class="chip">${esc(T('BankCount'))}</span>${T('BankValue') ? `<span class="chip accent">${esc(T('BankValue'))}</span>` : ''}</div>
      ${table('BankGrid', [
        { h: '', f: r => itemImg(r.ItemId) },
        { h: 'Objet', k: 'Name', f: r => `<span class="strong">${esc(r.Name)}</span>` },
        { h: 'Quantité', k: 'Quantity', num: true, f: r => `<span class="acc strong">${fmt(r.Quantity)}</span>` },
        { h: 'Niv.', k: 'Level', num: true },
        { h: 'Type', k: 'Type' },
        { h: 'Utilisé dans', k: 'UsedIn', f: r => r.UsedIn > 0 ? `<button class="small" ${attrs({ act: 'row', grid: 'BankGrid', i: r._i, handler: 'BankRecipes_Click' })}>${r.UsedIn} recette(s) ›</button>` : '' },
        { h: 'Prix /u', k: 'UnitPrice', num: true, f: r => fmt(r.UnitPrice) },
        { h: 'Moyen', k: 'AveragePrice', num: true, f: r => `<span class="muted">${fmt(r.AveragePrice)}</span>` },
        { h: 'Valeur HDV', k: 'Value', num: true, f: r => `<span class="acc strong">${fmt(r.Value)}</span>` },
      ], { select: false, empty: 'Aucune banque reçue : ouvrez votre banque en jeu.' })}`;
  },

  recipes() {
    const slots = (S.SlotFilters?.items || []).map((s, i) => `<button class="pill ${s.on ? 'on' : ''}" data-act="toggle" data-name="SlotFilters" data-i="${i}">${esc(s.t)}</button>`).join('');
    const sel = rows('RecipeGrid')[S.RecipeGrid?.sel ?? -1];
    return `<div class="filters">${input('RecipeSearch', 'Recette, type ou ingrédient…', '240px')}${select('JobFilter')}${select('ModeFilter')}
        ${shown('SellMinPanel') ? `<span class="lbl">Vente min.</span>${input('SellMin', '', '110px')}` : ''}
        <span class="lbl">Niveau</span>${input('LevelMin', '', '60px')}<span class="lbl">à</span>${input('LevelMax', '', '60px')}</div>
      <div class="filters"><span class="lbl">Cases</span>${slots}${check('IncludeInventory', 'Inclure l\'inventaire')}
        ${T('InventoryHint') ? colored('InventoryHint') : ''}<span class="chip">${esc(T('RecipeCount'))}</span>
        ${shown('IngredientChip') ? `<span class="chip accent">${esc(T('IngredientChipText'))}<button class="small ghost" data-act="click" data-name="IngredientChipClear">✕</button></span>` : ''}</div>
      <div class="split">
        ${table('RecipeGrid', [
          { h: '', f: r => itemImg(r.Recipe?.ResultId) },
          { h: 'Recette', k: 'Name', f: r => `<span class="strong">${esc(r.Name)}</span><div class="muted ellipsis" title="${esc(r.MissingText)}">${esc(r.MissingText ? 'Manque : ' + r.MissingText : r.Jobs)}</div>` },
          { h: 'Niv.', k: 'Level', num: true },
          { h: 'Cases', k: 'Slots', num: true, f: r => `<span class="chip slots" title="${r.Slots} case(s) dans l'atelier">${r.Slots}</span>` },
          { h: 'Faisable', k: 'Craftable', num: true, f: r => r.Craftable > 0 ? `<span class="ok strong">×${r.Craftable}</span>` : `<span class="muted">${r.MissingKinds} manquant(s)</span>` },
          { h: 'Coût', k: 'Cost', num: true, f: r => fmt(r.Cost) },
          { h: 'Vente', k: 'Sell', num: true, f: r => fmt(r.Sell) },
          { h: 'Bénéfice', k: 'Profit', num: true, f: r => `<span class="strong ${r.ProfitSign > 0 ? 'ok' : r.ProfitSign < 0 ? 'bad' : ''}">${fmt(r.Profit)}</span>` },
          { h: 'Marge', k: 'Margin', num: true, f: r => `<span class="${r.ProfitSign > 0 ? 'ok' : r.ProfitSign < 0 ? 'bad' : 'muted'}">${pct(r.Margin)}</span>` },
        ], { empty: 'Aucune recette ne correspond aux filtres.' })}
        <aside class="detail glass">${sel ? recipeDetail(sel) : '<div class="empty">Sélectionnez une recette.</div>'}</aside>
      </div>`;
  },

  hdv() {
    const sel = rows('HdvGrid')[S.HdvGrid?.sel ?? -1];
    return `<div class="filters">${input('HdvSearch', 'Rechercher un objet…', '260px')}${select('HdvTypeFilter')}
        ${check('HdvShowAll', 'Tous les objets du jeu')}<span class="chip">${esc(T('HdvCount'))}</span>
        <button class="small ${local.trades ? 'primary' : ''}" data-act="trades" title="Achats et ventes de vos personnages (HDV et marchands)">📊 Mes échanges</button>
        <span class="right"></span><span title="Partager vos relevés de prix avec votre groupe">${btn('ShareToggle', 'Partage entre amis', 'small')}</span>
        <span title="Récupère tout de suite les derniers prix du groupe">${btn('ShareSync', 'Synchroniser', 'small')}</span></div>
      ${shown('SharePanel') ? `<div class="card share-panel" title="Seuls l'objet, ses prix et la date du relevé sont envoyés (aucun nom, personnage ni banque).">
        <div class="filters">${check('ShareEnabled', 'Activer le partage')}
          <span class="muted">Serveur</span>${input('ShareUrl', 'https://…', '300px')}
          <span class="muted">Code de groupe</span>${input('ShareKey', 'code reçu en privé', '160px')}
          ${btn('ShareTest', 'Tester', 'small')}${btn('ShareSave', 'Enregistrer', 'small primary')}</div>
        ${T('ShareStatus') ? `<div class="acc strong">${esc(T('ShareStatus'))}</div>` : ''}</div>` : ''}
      ${local.trades ? tradesView() : `<div class="split">
        ${table('HdvGrid', [
          { h: '', f: r => itemImg(r.ItemId) },
          { h: 'Objet', k: 'Name', f: r => `<span class="strong">${esc(r.Name)}</span>` },
          { h: 'Niv.', k: 'Level', num: true },
          { h: 'Type', k: 'Type' },
          { h: '×1', k: 'Lot1', num: true, f: r => fmt(r.Lot1) },
          { h: '×10', k: 'Lot10', num: true, f: r => fmt(r.Lot10) },
          { h: '×100', k: 'Lot100', num: true, f: r => fmt(r.Lot100) },
          { h: 'Unité', k: 'Unit', num: true, f: r => `<span class="acc strong">${fmt(r.Unit)}</span>` },
          { h: 'Moyen', k: 'Average', num: true, f: r => fmt(r.Average) },
          { h: 'Relevé', k: 'SeenAt', f: r => `<span class="muted">${esc(r.Seen)}</span>` },
        ], { empty: 'Aucun prix relevé : consultez des objets dans un hôtel de vente en jeu.' })}
        <aside class="detail glass">${sel ? `
          <div class="head">${itemImg(sel.ItemId, 'lg')}<div><h2>${esc(T('HdvDetailTitle'))}</h2></div></div>
          <div class="sub">${esc(T('HdvDetailSub'))}</div>
          <h3>Évolution du prix unitaire</h3><div class="card">${chart(S.HdvChart?.pts)}</div>
          <h3>Historique des relevés</h3>
          ${rows('HdvHistory').map(h => `<div class="card"><b>${esc(h.When)}</b><div class="lots">${[['×1', h.Lot1], ['×10', h.Lot10], ['×100', h.Lot100]].map(([l, v]) => `<span><b>${l}</b><span class="muted">${esc(v)}</span></span>`).join('')}</div></div>`).join('') || '<div class="muted">Aucun relevé.</div>'}
          ${btn('HdvForget', 'Oublier ce prix', 'ghost')}` : '<div class="empty">Sélectionnez un objet.</div>'}</aside>
      </div>`}`;
  },

  combat() {
    const spells = rows('EconomeSpells').map(s => `<div class="spell ${s.State === 0 ? 'used' : s.State < 0 ? 'twice' : ''}" title="${esc(s.Name + ' — ' + s.Status)}">
      <img src="icon/spell/${s.Id}.png" alt=""><span class="fallback">${esc(s.Name)}</span></div>`).join('');
    const char = S.HuntCharacterBox;
    return `<div class="stat-row">
        ${char?.items?.length ? `<span class="lbl muted">Personnage</span>${select('HuntCharacterBox')}` : ''}
        <div class="stat"><span>Niveau</span><b>${esc(T('HuntLevel'))}</b></div>
        <div class="stat"><span>Niv. groupe</span><b class="acc">${esc(T('HuntGroupLevel'))}</b></div>
        <div class="stat"><span>Sagesse</span><b>${esc(T('HuntWisdom'))}</b></div>
        <div class="stat"><span>Prospection</span><b>${esc(T('HuntProspection'))}</b></div>
        <div class="stat"><span>PP groupe</span><b class="acc">${esc(T('HuntGroupProspection'))}</b></div>
        ${check('HuntSound', 'Alerte sonore archi')}
        <label class="volume" title="Volume de l'alerte archi-monstre">🔊<input type="range" data-set="HuntVolume" data-fk="HuntVolume"
          min="${S.HuntVolume?.min ?? 0}" max="${S.HuntVolume?.max ?? 100}" step="1" value="${S.HuntVolume?.val ?? 70}"
          style="--p:${S.HuntVolume?.val ?? 70}%"><span class="muted">${esc(T('HuntVolumeText'))}</span></label>
        <button class="small" data-act="click" data-name="HuntTestSound">Tester le son</button>
        <button class="small" data-act="click" data-name="HuntNewSession">Nouvelle session</button>
        <span title="Vous vous absentez : le temps ne compte plus dans la rentabilité. Un combat la relance.">${btn('HuntPauseSession', '⏸ Pause', 'small')}</span>
        ${check('HuntArchiPopup', 'Pop-up archi')}${check('HuntAlertsToggle', S.HuntAlertsToggle?.t || 'Alertes monstres')}
      </div>
      ${S.HuntAlertsToggle?.on ? `<div class="glass card"><div class="filters"><span class="lbl">Monstre</span>${input('HuntAlertMonster', 'Craqueleur des plaines', '200px')}
          <span class="lbl">Au moins</span>${input('HuntAlertCount', '', '50px')}<span class="lbl">dans le groupe, niveau</span>${input('HuntAlertMin', '', '56px')}
          <span class="lbl">à</span>${input('HuntAlertMax', '', '56px')}<button class="small primary" data-act="click" data-name="HuntAlertAdd">Ajouter l'alerte</button>
          <button class="small" data-act="click" data-name="HuntAlertTest">Tester le son</button><span class="muted">${esc(T('HuntAlertStatus'))}</span></div>
        <div class="filters">${rows('HuntAlertList').map((a, i) => `<span class="chip ${a.Enabled ? 'on' : 'dim'}">
          <a class="item-link" ${attrs({ act: 'row', grid: 'HuntAlertList', i, handler: 'HuntAlertToggle_Click' })} title="Activer ou couper cette alerte">${a.Enabled ? '🔔' : '🔕'}</a>
          ${esc(a.Text)} <a class="item-link" ${attrs({ act: 'row', grid: 'HuntAlertList', i, handler: 'HuntAlertDelete_Click' })} title="Supprimer">✕</a></span>`).join('') || '<span class="muted">Aucune alerte.</span>'}</div></div>` : ''}
      ${T('HuntStatus') ? `<div class="alert">${esc(T('HuntStatus'))}</div>` : ''}
      ${chronoBlock()}
      <div class="split wide">
        <div class="stack">
          <div class="glass econome"><div class="bar"><span class="t"><b class="acc">Économe</b> · ${esc(T('EconomeStatus'))}</span>
            <button class="small" data-act="click" data-name="EconomeReset">Remettre à zéro</button></div><div class="spells">${spells}</div></div>
          <div class="strong"><a class="pos-link" data-act="goto-me" title="Voir sur la carte">${esc(T('HuntMap'))}</a></div>
          ${table('HuntGroupGrid', [
            { h: 'Monstres (niveaux)', k: 'Monsters', f: r => `<span class="strong">${esc(r.Monsters)}</span>` },
            { h: 'Niv.', k: 'TotalLevel', num: true, f: r => `<span class="acc strong">${fmt(r.TotalLevel)}</span>` },
            { h: 'Étoiles', k: 'Stars' },
            { h: 'XP estimée', k: 'Xp', num: true, f: r => `<span class="strong">${esc(r.XpText)}</span>` },
            { h: 'Drops observés', k: 'Drops', f: r => `<span class="muted ellipsis" title="${esc(r.Drops)}">${esc(r.Drops)}</span>` },
            { h: 'Drops (taux du jeu)', k: 'OfficialDrops', f: r => r.HasDropDetails
              ? `<button class="caret ${r.Expanded ? 'open' : ''}" ${attrs({ act: 'row', grid: 'HuntGroupGrid', i: r._i, handler: 'HuntDropsToggle_Click' })}>▶</button> <span class="ellipsis">${esc(r.OfficialDrops)}</span>` : '' },
            { h: 'Drops estimés', k: 'DropValue', num: true, f: r => `<span class="acc strong">${fmt(r.DropValue)}</span>` },
            { h: 'Archi', k: 'Archi', f: r => r.Archi ? `<span class="bad strong">${esc(r.Archi)}</span>` : '' },
            { h: 'Prix pierre', k: 'ArchiPrice', num: true, f: r => fmt(r.ArchiPrice) },
          ], { select: false, empty: 'Aucun groupe de monstres sur la map.', after: (r, n) => r.Expanded && r.DropDetails?.length ? `<tr><td colspan="${n}" class="drops">
              <table><thead><tr><th></th><th>Objet</th><th class="num">Chance</th><th class="num">Monstres</th><th class="num">PP min.</th><th class="num">Max / combat</th><th class="num">Prix u.</th><th class="num">Valeur / combat</th><th>État</th></tr></thead>
              <tbody>${r.DropDetails.map(d => `<tr class="${d.Muted ? 'dim' : ''}"><td>${itemImg(d.ItemId, 'sm')}</td><td class="strong">${esc(d.Name)}</td>
                <td class="num acc strong">${esc(d.Chance)}</td><td class="num">${esc(d.Monsters)}</td><td class="num">${esc(d.Prospection)}</td>
                <td class="num">${esc(d.MaxPerFight)}</td><td class="num">${esc(d.Price)}</td><td class="num strong">${esc(d.Value)}</td><td>${esc(d.State)}</td></tr>`).join('')}</tbody></table></td></tr>` : '' })}
          <div class="glass card"><p class="section-title">Rentabilité de la session</p><div class="lines">${esc(T('HuntSession'))}</div>
            ${rows('HuntProjection').length ? `<table class="proj"><thead><tr><th>Sur</th><th class="num">XP</th><th class="num">Kamas</th><th class="num">Drops (HDV)</th><th class="num">Total kamas</th></tr></thead><tbody>
              ${rows('HuntProjection').map(p => `<tr><td class="strong">${esc(p.Span)}</td><td class="num">${fmt(p.Xp)}</td><td class="num">${fmt(p.Kamas)}</td><td class="num">${fmt(p.Drops)}</td><td class="num acc strong">${fmt(p.Total)}</td></tr>`).join('')}</tbody></table>` : ''}
            <div class="muted" style="font-size:11.5px;margin-top:6px">${esc(T('HuntLearned'))}</div></div>
        </div>
        <div class="side-tables">
          <div class="mini"><div class="filters" style="justify-content:space-between"><p class="section-title">Derniers archi-monstres vus</p>
            <button class="small" data-act="click" data-name="HuntClearArchis">Vider</button></div>
            ${table('HuntArchiGrid', [
              // Colonne étroite : date courte, niveau sous le nom, position réduite à ses coordonnées et sa zone (texte complet au survol)
              { h: 'Vu', k: 'TimeSort', f: r => `<span class="muted" title="${esc(r.Time)}">${esc(shortDate(r.Time))}</span>` },
              { h: 'Archi-monstre', k: 'Name', f: r => `<span class="strong ellipsis" title="${esc(r.Name)}">${esc(r.Name)}</span>
                <div class="muted small">niv. ${r.Level}</div><div>${posLink(shortMap(r.Map), r.Goto, 'ellipsis')}</div>` },
              { h: 'Pierre', k: 'Price', num: true, f: r => fmt(r.Price) },
            ], { select: false, empty: 'Aucun archi-monstre vu.' })}</div>
          <div class="mini"><p class="section-title">Combats de la session</p>
            ${table('HuntFightGrid', [
              { h: 'Heure', k: 'TimeSort', f: r => `<span class="muted">${esc(r.Time)}</span>` },
              { h: 'Monstres', k: 'Monsters', f: r => `<span class="ellipsis" title="${esc(r.Drops)}">${esc(r.Monsters)}</span>` },
              { h: 'XP', k: 'Xp', num: true, f: r => fmt(r.Xp) },
              { h: 'Kamas', k: 'Kamas', num: true, f: r => `<span class="acc">${fmt(r.Kamas)}</span>` },
              { h: 'Drops', k: 'DropValue', num: true, f: r => `<span title="${esc(r.Drops)}">${fmt(r.DropValue)}</span>` },
            ], { empty: 'Aucun combat sur cette session.' })}</div>
          <div class="mini"><p class="section-title">${esc(T('HuntDropsTitle'))}</p>
            ${table('HuntDropGrid', [
              { h: '', f: r => itemImg(r.ItemId, 'sm') },
              { h: 'Objet', k: 'Name', f: r => `<span class="strong">${esc(r.Name)}</span>` },
              { h: 'Qté', k: 'Quantity', num: true, f: r => fmt(r.Quantity) },
              { h: 'Prix /u', k: 'Unit', num: true, f: r => fmt(r.Unit) },
              { h: 'Total', k: 'Total', num: true, f: r => `<span class="acc strong">${fmt(r.Total)}</span>` },
            ], { select: false, empty: 'Aucun objet obtenu.' })}</div>
        </div>
      </div>`;
  },
};

// Chronomètre de rentabilité et challenges (ancien onglet Arène), en bandeau compact de l'onglet Combat
function chronoBlock() {
  const challenges = rows('AreneChallenges');
  const state = c => c.State === 1 ? 'ok' : c.State === -1 ? 'bad' : 'warn';
  const open = local.open.chrono;
  return `<div class="glass card chrono">
      <span class="time acc" title="Chronomètre : compte les fins de combat des personnages choisis (un combat à plusieurs compte une fois)">⏱ ${esc(T('ChronoTime'))}</span>
      ${btn('ChronoStart', '▶ Démarrer', 'small primary')}${btn('ChronoReset', 'Remettre à zéro', 'small')}
      <span title="Personnages comptés">${select('AreneGroup')}</span>${check('AreneGroupEdit', 'Groupes')}
      <div class="chrono-stats" title="Gains : kamas + drops au prix HDV (sinon prix marchand)">
        <b>${esc(T('ChronoFights'))}</b><b class="acc">${esc(T('ChronoGains'))}</b><span class="muted ellipsis" title="${esc(T('ChronoRate'))}">${esc(T('ChronoRate'))}</span></div>
      <div class="chrono-chal">${challenges.map(c => `<span class="chip ${c.State === -1 ? 'failed' : ''}" title="${esc(c.Rule + ' — ' + c.Status + (c.Announced ? ' (annoncé par le jeu)' : ''))}">
          <b class="${state(c)}">${esc(c.Name)}</b><span class="muted">${esc(c.Character)}</span></span>`).join('')}
        ${T('AreneOtherChallenges') ? `<span class="muted ellipsis" title="${esc(T('AreneOtherChallenges'))}">${esc(T('AreneOtherChallenges'))}</span>` : ''}</div>
      <button class="small" data-act="open" data-key="chrono" title="Combats comptés par le chronomètre">Combats ${open ? '▾' : '▸'}</button>
    </div>
    ${S.AreneGroupEdit?.on ? `<div class="glass card"><div class="filters"><span class="lbl">Nom du groupe</span>${input('AreneGroupName', 'Groupe principal', '180px')}
      <button class="small primary" data-act="click" data-name="AreneGroupSave">Enregistrer le groupe</button>
      <button class="small" data-act="click" data-name="AreneGroupDelete">Supprimer</button><span class="muted">${esc(T('AreneGroupStatus'))}</span></div>
      <div class="filters">${(S.AreneGroupMembers?.items || []).map((m, i) => `<button class="pill ${m.on ? 'on' : ''}" data-act="toggle" data-name="AreneGroupMembers" data-i="${i}">${esc(m.t)}</button>`).join('') || '<span class="muted">Aucun personnage connu.</span>'}</div></div>` : ''}
    ${open ? `<div class="chrono-grid">${table('ChronoGrid', [
      { h: 'Fin', k: 'TimeSort', f: r => esc(r.Time) },
      { h: 'Personnage(s)', k: 'Characters', f: r => `<span class="ellipsis" title="${esc(r.Characters)}">${esc(r.Characters)}</span>` },
      { h: 'Monstres', k: 'Monsters', f: r => `<span class="ellipsis" title="${esc(r.Monsters)}">${esc(r.Monsters)}</span>` },
      { h: 'XP', k: 'Xp', num: true, f: r => fmt(r.Xp) },
      { h: 'Kamas', k: 'Kamas', num: true, f: r => fmt(r.Kamas) },
      { h: 'Drops', k: 'DropValue', num: true, f: r => fmt(r.DropValue) },
      { h: 'Total', k: 'Total', num: true, f: r => `<span class="acc strong">${fmt(r.Total)}</span>` },
    ], { select: false, empty: 'Démarrez le chronomètre : chaque fin de combat s\'ajoute ici.' })}</div>` : ''}`;
}

const seenCol = { h: 'Relevé', k: 'SeenAt', f: r => `<span class="muted">${esc(r.Seen)}</span>` };
const detailHead = (icon, title, sub) => `<div class="head">${icon}<div><h2>${esc(T(title))}</h2></div></div>
  ${T(sub) ? `<div class="sub">${esc(T(sub))}</div>` : ''}`;

Object.assign(views, {
  brisage() {
    // Un bouton compact par caractéristique (icône de sa rune + abréviation, nom au survol) ; seuils affichés une fois cochée
    const stats = (S.BrisageStats?.items || []).map((s, i) => `<span class="chip stat-filter ${s.on ? 'on' : ''}" title="${esc(s.t)}">
      <label class="check"><input type="checkbox" data-stat="${i}" data-field="on" ${s.on ? 'checked' : ''}>${s.r ? itemImg(s.r, 'xs') : ''}${esc(s.s || s.t)}</label>
      ${s.on ? `<input type="text" data-stat="${i}" data-field="min" data-fk="stat-${i}-min" value="${esc(s.min)}" placeholder="min" title="${esc(s.t)} minimum">
      <span class="muted">à</span>
      <input type="text" data-stat="${i}" data-field="max" data-fk="stat-${i}-max" value="${esc(s.max)}" placeholder="max" title="${esc(s.t)} maximum (vide = pas de limite)">` : ''}</span>`).join('');
    const sel = rows('BrisageGrid')[S.BrisageGrid?.sel ?? -1];
    return `<div class="filters">${input('BrisageSearch', 'Rechercher un objet…', '220px')}${select('BrisageSource')}${select('BrisageTypeFilter')}
        <span class="lbl">Niveau</span>${input('BrisageLevelMin', '', '60px')}<span class="lbl">à</span>${input('BrisageLevelMax', '', '60px')}
        ${btn('BrisageReset', 'Réinitialiser les stats', 'small')}<span class="chip">${esc(T('BrisageCount'))}</span></div>
      <div class="filters stat-filters">${stats}</div>
      <div class="split">
        ${table('BrisageGrid', [
          { h: '', f: r => itemImg(r.ItemId) },
          { h: 'Objet', k: 'Name', f: r => `<a class="strong item-link" data-act="item-recipe" data-id="${r.ItemId}" title="Voir la recette de cet objet">${esc(r.Name)}</a><div class="muted">${esc(r.Type)} · niv. ${r.Level}</div>` },
          // Caractéristiques en entier (sur plusieurs lignes si besoin) ; lieu au survol du nombre possédé ; runes sous leur valeur
          { h: 'Caractéristiques', k: 'Score', f: r => `<span class="stat-chips">${(r.Chips || []).map(c => `<span class="stat-chip ${c.On ? 'on' : ''} ${c.Neg ? 'neg' : ''}" title="${esc(c.Tip)}">${c.Rune ? itemImg(c.Rune, 'xs') : ''}<i>${esc(c.Short)}</i>${esc(c.Value)}</span>`).join('')}</span>` },
          { h: 'Poss.', k: 'Owned', num: true, f: r => `<span class="strong ${r.Owned > 0 ? 'acc' : 'muted'}" title="${esc(r.Where)}">${r.Owned}</span>` },
          { h: 'Faisable', k: 'Craftable', num: true, f: r => r.Craftable > 0 ? `<span class="ok strong" title="Faisable ${r.Craftable} fois avec vos ressources">×${r.Craftable}</span>` : r.Missing ? `<span class="bad strong" title="Manque : ${esc(r.Missing)}">✗</span>` : '' },
          { h: 'Prix', k: 'HdvPrice', num: true, f: r => `<span class="acc strong" title="Prix HDV">${fmt(r.HdvPrice)}</span><div class="muted small" title="Coût de fabrication (ingrédients au prix HDV)">${r.CraftCost != null ? '⚒ ' + fmt(r.CraftCost) : ''}</div>` },
          { h: 'Runes ≈', k: 'RunesValue', num: true, f: r => `<span class="strong" title="${esc(r.RunesText)}">${fmt(r.RunesValue)}</span>` },
          { h: 'Gain', k: 'Gain', num: true, f: r => r.Gain == null ? '—' : `<span title="${esc(S.BrisageGainColumn?.t || 'Gain')} : valeur des runes moins le prix de référence" class="strong ${r.GainSign < 0 ? 'bad' : 'ok'}">${r.Gain > 0 ? '+' : ''}${fmt(r.Gain)}</span>` },
        ], { empty: T('BrisageEmpty') || 'Aucun objet ne correspond à ces critères.' })}
        <aside class="detail glass">${sel ? `${detailHead(itemImg(sel.ItemId, 'lg'), 'BrisageDetailTitle', 'BrisageDetailSub')}
          ${btn('BrisageGoRecipe', 'Voir la recette', 'small')}
          <div class="filters"><span class="lbl">Quantité à briser</span>${input('BrisageBreakCount', '', '80px')}${btn('BrisageBreakMax', 'Max', 'small')}</div>
          ${T('BrisageDetailTotal') ? `<div class="card strong lines" style="color:${S.BrisageDetailTotal?.c || 'inherit'}">${esc(T('BrisageDetailTotal'))}</div>` : ''}
          <h3>Runes estimées</h3>
          ${rows('BrisageRunes').map(r => `<div class="ing rune">${itemImg(r.ItemId)}<div><div class="name">${esc(r.Quantity)} × ${esc(r.Name)}</div>
            <div class="price">${r.UnitPrice == null ? 'pas de prix HDV' : fmt(r.UnitPrice) + ' k / u'}</div></div><span class="ratio acc">${r.Total == null ? '—' : fmt(r.Total) + ' k'}</span></div>`).join('')
            || '<div class="muted">Aucune rune.</div>'}
          <div class="muted" style="font-size:11.5px">Estimation : aucune formule officielle pour Retro (Coiffe du Kitsou, niveau 61, 150 Vi ≈ 1 Ra Vi).</div>`
          : '<div class="empty">Sélectionnez un objet.</div>'}</aside>
      </div>`;
  },

  ttg() {
    const sel = rows('TtgGrid')[S.TtgGrid?.sel ?? -1];
    return `<div class="filters">${input('TtgSearch', 'Rechercher une carte…', '220px')}<span class="lbl">Rareté</span>${select('TtgRarity')}<span class="lbl">Personnage</span>${select('TtgCharacter')}
        <span class="lbl">Afficher</span>${select('TtgShow')}${check('TtgShowAll', 'Toutes les cartes du jeu')}
        <span class="lbl">Période</span>${input('TtgDaysBox', '', '56px')}<span class="lbl">jours</span>
        <button class="small" data-act="book" title="Votre collection présentée comme le livre du jeu : familles, raretés, cartes rangées, possédées et manquantes (personnage choisi)">📖 Classeur</button>
        <span class="chip">${esc(T('TtgCount'))}</span>${T('TtgCollection') ? `<span class="chip accent">${esc(T('TtgCollection'))}</span>` : ''}${T('TtgMissing') ? `<span class="chip accent">${esc(T('TtgMissing'))}</span>` : ''}</div>
      <div class="split">
        ${table('TtgGrid', [
          { h: 'No.', k: 'Number', num: true, f: r => `<span class="muted">${r.Number == null ? '' : String(r.Number).padStart(3, '0')}</span>` },
          { h: '', f: r => itemImg(r.ItemId) },
          { h: 'Carte', k: 'Name', f: r => `<span class="strong">${esc(r.Name)}</span>` },
          { h: 'Rareté', k: 'Rarity' },
          { h: 'Poss.', k: 'Owned', num: true, f: r => `<span class="strong ${r.Owned > 0 ? 'ok' : 'muted'}">${r.Owned}</span>` },
          { h: 'Classeur', k: 'InBinder', f: r => r.InBinder ? '<span class="ok strong" title="Carte rangée dans le classeur">✓</span>' : '' },
          { h: 'Marchand', k: 'MerchantPrice', num: true, f: r => `<span class="acc strong">${fmt(r.MerchantPrice)}</span>` },
          { h: S.TtgBestColumn?.t || 'Meilleur', k: 'BestPrice', num: true, f: r => fmt(r.BestPrice) },
          { h: 'Chez', k: 'BestMerchant', f: r => `<span class="strong">${esc(r.BestMerchant)}</span>` },
          { h: 'Position', k: 'BestWhere', f: r => posLink(r.BestWhere, r.Goto, 'ellipsis') },
          { h: 'Vu', k: 'SeenAt', f: r => `<span class="muted">${esc(r.Seen)}</span>` },
        ], { empty: T('TtgEmpty') || 'Aucune carte à afficher.' })}
        <aside class="detail glass">${detailHead(sel ? itemImg(sel.ItemId, 'lg') : '', 'TtgDetailTitle', 'TtgDetailSub')}
          ${sel ? `${select('TtgOffersMode')}<h3>${esc(T('TtgOffersTitle'))}</h3>
          ${rows('TtgOffers').map(o => `<div class="card"><div class="filters" style="justify-content:space-between"><b class="acc">${esc(o.Price)}<span class="muted"> · ${o.Quantity} en vente</span></b>
            <span class="muted" style="font-size:11px">${esc(o.Seen)}</span></div><div class="strong">${esc(o.Merchant)}</div>
            ${o.Where ? `<div style="font-size:11.5px">${posLink(o.Where, o.Goto)}</div>` : ''}</div>`).join('') || '<div class="muted">Aucune offre.</div>'}
          <h3>Prix chez les marchands</h3><div class="card">${chart(S.TtgMerchantChart?.pts)}</div>
          <h3>Prix unitaire à l'HDV</h3><div class="card">${chart(S.TtgHdvChart?.pts)}</div>
          <button class="ghost" data-act="click" data-name="TtgForget">Oublier les prix marchands</button>` : ''}</aside>
      </div>`;
  },

  pda() {
    const sel = rows('PdaGrid')[S.PdaGrid?.sel ?? -1];
    return `<div class="filters">${input('PdaSearch', 'Monstre ou race…', '220px')}<span class="lbl">Afficher</span>${select('PdaSource')}${shown('PdaCharacter') ? `<span class="lbl">Personnage</span>${select('PdaCharacter')}` : ''}
        <span class="lbl">Catégorie</span>${select('PdaCategory')}${check('PdaShowAll', 'Tous les monstres du jeu')}
        <span class="chip">${esc(T('PdaCount'))}</span>${T('PdaValue') ? `<span class="chip accent">${esc(T('PdaValue'))}</span>` : ''}</div>
      <div class="split">
        ${table('PdaGrid', [
          { h: '', f: r => itemImg(r.StoneTemplate) },
          { h: 'Monstre(s)', k: 'Name', f: r => `<span class="strong ellipsis" title="${esc(r.Name)}">${esc(r.Name)}</span>` },
          { h: 'Niv.', k: 'LevelSort', num: true, f: r => esc(r.Level) },
          { h: 'Race', k: 'Race' },
          { h: 'Catégorie', k: 'Category' },
          { h: 'Prix', k: 'Price', num: true, f: r => `<span class="acc strong">${fmt(r.Price)}</span>` },
          { h: 'Moyen', k: 'Average', num: true, f: r => fmt(r.Average) },
          { h: 'Offres', k: 'Offers', num: true, f: r => r.Offers ?? '' },
          seenCol,
          { h: 'Poss.', k: 'Owned', num: true, f: r => `<span class="strong ${r.Owned > 0 ? 'ok' : 'muted'}">${r.Owned}</span>` },
          { h: 'Où', k: 'Where' },
        ], { empty: T('PdaEmpty') || 'Rien à afficher.' })}
        <aside class="detail glass">${sel ? `${detailHead(itemImg(sel.StoneTemplate, 'lg'), 'PdaDetailTitle', 'PdaDetailSub')}
          <h3>Évolution du prix</h3><div class="card">${chart(S.PdaChart?.pts)}</div>
          <button class="ghost" data-act="click" data-name="PdaForget">Oublier ce prix</button>` : '<div class="empty">Sélectionnez un monstre.</div>'}</aside>
      </div>`;
  },

  ocre() {
    const sel = rows('OcreGrid')[S.OcreGrid?.sel ?? -1];
    return `<div class="filters"><span class="lbl">Personnage</span>${select('OcreCharacter')}
        <span class="lbl">Étape atteinte</span>${select('OcreStep')}<span class="lbl">Coût de</span>${select('OcreKindFilter')}</div>
      <div class="strong acc" style="margin-bottom:10px">${esc(T('OcreSummary'))}</div>
      <div class="split">
        ${table('OcreGrid', [
          { h: 'Étape', k: 'Step', num: true, f: r => `<span class="strong">${r.Step}</span>` },
          { h: 'Âmes', k: 'Kind' },
          { h: 'Total', k: 'Total', num: true },
          { h: 'Possédées', k: 'Owned', num: true },
          { h: 'Manquantes', k: 'Missing', num: true },
          { h: 'dont boss', k: 'MissingBoss', num: true },
          { h: 'dont archi', k: 'MissingArchi', num: true },
          { h: 'Coût', k: 'Cost', num: true, f: r => `<span class="acc strong">${esc(r.CostText)}</span>` },
          { h: 'État', k: 'State', f: r => `<span class="${r.Validable ? 'ok strong' : r.Done ? 'muted' : ''}">${esc(r.State)}</span>` },
        ], { empty: T('OcreSummary') || 'Aucune étape.' })}
        <aside class="detail glass">${sel ? `<h2 class="acc">${esc(T('OcreDetailTitle'))}</h2>
          <div class="muted" style="white-space:pre-line">${esc(T('OcreDetailSub'))}</div>
          <div class="lines">${rows('OcreSouls').map(s => `<div class="card" style="display:flex;gap:8px;align-items:center;padding:6px 8px;margin-top:6px">
            ${itemImg(s.StoneTemplate, 'sm')}<div style="flex:1;min-width:0"><b class="${s.Owned ? 'ok' : ''}">${esc(s.Name)}</b>
            <div class="muted small">${esc(s.Kind)} · niv. ${esc(s.Level)}${s.Where ? ' · ' + esc(s.Where) : ''}</div></div>
            <span class="acc strong">${esc(s.PriceText)}</span></div>`).join('')}</div>` : '<div class="empty">Sélectionnez une étape.</div>'}</aside>
      </div>`;
  },

  fm() {
    return `<div class="filters"><span class="lbl">Objet</span>${select('FmItemCombo', 'style="min-width:340px"')}
        <span class="muted">Le reliquat est une estimation : corrigez-le si besoin.</span></div>
      <div class="split fm">
        <aside class="detail glass">
          <h2 class="acc">${esc(T('FmItemName'))}</h2>
          <div class="card"><h3>Reliquat estimé</h3><div class="huge">${esc(T('FmReliquat'))}</div>
            <div class="filters">${input('FmReliquatInput', 'Valeur', '90px')}<button class="small" data-act="click" data-name="FmSetReliquat">Corriger</button></div></div>
          <div class="filters"><button class="small" data-act="confirm" data-name="FmClear" data-text="Effacer l'historique de cet objet et remettre son reliquat à 0 ?">Effacer l'historique</button>
            <button class="small" data-act="confirm" data-name="FmForget" data-text="Retirer cet objet et tout son historique de la liste ?">Oublier l'objet</button>
            ${btn('FmWeights', 'Poids des stats', 'small')}</div>
          <h3>Caractéristiques actuelles</h3><div class="stats">${rows('FmStats').map(s => `<span class="chip">${esc(s)}</span>`).join('') || '<span class="muted">—</span>'}</div>
        </aside>
        ${shown('FmWeightsPanel') ? fmWeights() : table('FmHistoryGrid', [
          { h: '#', k: 'Index', num: true, f: r => `<span class="muted">${r.Index}</span>` },
          { h: 'Heure', k: 'Time' },
          { h: '', f: r => itemImg(r.RuneId, 'sm') },
          { h: 'Rune', k: 'Rune', f: r => `<span class="strong">${esc(r.Rune)}</span>` },
          { h: 'Résultat', k: 'Outcome', f: r => `<span class="strong" style="color:${r.OutcomeBrush || 'inherit'}">${esc(r.Outcome)}</span>` },
          { h: 'Gains', k: 'Gains', f: r => `<span class="ok ellipsis" title="${esc(r.Gains)}">${esc(r.Gains)}</span>` },
          { h: 'Pertes', k: 'Losses', f: r => `<span class="bad ellipsis" title="${esc(r.Losses)}">${esc(r.Losses)}</span>` },
          { h: 'Poids', k: 'Weight' },
          { h: 'Reliquat', k: 'Reliquat', f: r => `<span class="acc strong">${esc(r.Reliquat)}</span>` },
        ], { select: false, empty: T('FmEmpty') || 'Aucun passage de rune.' })}
      </div>`;
  },

  windows() {
    const on = n => S[n]?.on ? 'checked' : '';
    const hotkey = (name, icon, label) => `<label class="org-key"><span class="k-ico">${icon}</span><span>${label}</span>
      <input type="text" class="hotkey" readonly data-hotkey="${name}" value="${esc(T(name))}" placeholder="aucun"
        title="Cliquez puis appuyez sur la combinaison voulue (Suppr : aucun raccourci)"></label>`;
    // Interrupteur avec explication, et pastille cochable (choix multiples)
    const sw = (name, label, desc = '') => `<label class="switch"><input type="checkbox" data-set="${name}" ${on(name)}><span class="track"></span>
      <span class="sw-txt"><b>${esc(label)}</b>${desc ? `<small>${esc(desc)}</small>` : ''}</span></label>`;
    const pill = (name, label) => `<label class="tog"><input type="checkbox" data-set="${name}" ${on(name)}><span>${esc(label)}</span></label>`;
    const enabled = !!S.OrganizerEnabled?.on;
    const opacity = S.OverlayOpacity?.val ?? 92;
    return `<div class="org-head card">${sw('OrganizerEnabled', 'Switcheur activé', 'Raccourcis, notifications et bascules automatiques. Désactivé, la liste reste affichée.')}
        ${T('OrganizerCount') ? `<span class="chip accent">${esc(T('OrganizerCount'))}</span>` : ''}
        <button class="small ghost" data-act="org-settings">${local.orgHidden ? 'Afficher les réglages ▾' : 'Masquer les réglages ▴'}</button></div>
      ${local.orgHidden ? '' : `<div class="org-grid ${enabled ? '' : 'off'}">
        <section class="card org-card"><h3><span class="ico">⌨</span>Raccourcis</h3>
          ${hotkey('HotkeyNext', '→', 'Suivant')}${hotkey('HotkeyPrevious', '←', 'Précédent')}${hotkey('HotkeyMain', '★', 'Principal')}
          <div class="org-key sadida" title="Mode Sadida : affiche le Sadida en arbre (Puissance Sylvestre) dont c'est le tour, celui qui a le moins de temps restant. Passez son tour vous-même : la fenêtre d'avant revient. Sans Sadida en arbre à son tour, rien ne se passe.">${hotkey('HotkeySadida', '🌳', 'Sadida')}<span title="Retour à la fenêtre d'avant après le tour du Sadida">${pill('SadidaReturn', '↩')}</span></div>
          <label class="org-key"><span class="k-ico">⏱</span><span>Anti-rebond</span><span class="unit">${input('HotkeyDebounce', '', '70px')} ms</span></label>
          <div class="org-key click" title="Cliquez dans le jeu en gardant cette touche enfoncée : votre clic arrive au jeu, puis la fenêtre suivante (ordre de jeu) s'affiche. Rien n'est cliqué à votre place.">
            <span class="k-ico">🖱</span><label class="check"><input type="checkbox" data-set="OrganizerClickSwitch" ${on('OrganizerClickSwitch')}>Clic +</label>
            <span class="unit">${select('OrganizerClickKey')}</span>${pill('OrganizerClickLeft', 'Gauche')}${pill('OrganizerClickRight', 'Droit')}<span class="unit">: suivant</span></div>
          <div title="Si Windows rend aussitôt la main à la fenêtre quittée, la fenêtre demandée est réaffichée. Rien n'est cliqué ni tapé à votre place.">${check('OrganizerFixBounce', 'Anti-rebond fenêtre')}</div>
          ${T('HotkeyStatus') ? `<div class="bad strong small">${esc(T('HotkeyStatus'))}</div>` : ''}
          <small class="muted">Ils marchent aussi en jeu ; ils ne font qu'afficher la fenêtre.</small></section>
        <section class="card org-card"><h3><span class="ico">🔔</span>Notifications</h3>
          <div class="sub">Quand</div><div class="togs">${pill('NotifyFight', 'Début de combat')}${pill('NotifyTurn', 'Début de votre tour')}
            ${pill('NotifyExchange', "Demande d'échange")}${pill('NotifyParty', 'Invitation de groupe')}</div>
          <div class="sub">Comment</div><div class="togs">${pill('NotifyToast', 'Bulle')}${pill('NotifyFlash', 'Clignotement')}${pill('NotifySound', 'Son')}</div>
          <small class="muted">Rien n'est notifié pour la fenêtre déjà au premier plan.</small></section>
        <section class="card org-card"><h3><span class="ico">⇄</span>Bascules automatiques</h3>
          ${sw('NotifyAutoSwitch', 'Sur la fenêtre invitée', "Invitation de groupe ou demande d'échange : vous n'avez plus qu'à accepter.")}
          ${sw('NotifyAutoSwitchTurn', 'Au début du tour', 'En combat, la fenêtre du personnage qui doit jouer.')}
          ${sw('OrganizerSplitMode', 'Mode split', 'Fenêtres côte à côte : pas de bascule au début du tour, la ligne du personnage clignote dans la superposition avec le temps du tour.')}
          ${sw('OrganizerMaximize', 'Agrandir au démarrage', 'Chaque nouvelle fenêtre du jeu est agrandie dès qu’elle apparaît.')}
          ${sw('OrganizerLockOrder', "Figer l'ordre en groupe", "L'ordre de jeu est enregistré à l'entrée en groupe.")}</section>
        <section class="card org-card"><h3><span class="ico">▣</span>Superposition en jeu</h3>
          ${sw('OverlayEnabled', 'Afficher au-dessus du jeu', 'Laisse passer les clics vers le jeu, sauf sur l’ordre de jeu : un clic sur un personnage affiche sa fenêtre.')}
          <div class="sub">Blocs</div><div class="togs">${pill('OverlayEconome', 'Économe')}${pill('OverlayGroups', 'Groupes de la map')}${pill('OverlayHarvest', 'Récolte')}${pill('OverlayRotation', 'Ordre de jeu')}</div>
          <label class="volume">Opacité<input type="range" data-set="OverlayOpacity" data-fk="OverlayOpacity" min="${S.OverlayOpacity?.min ?? 40}"
            max="${S.OverlayOpacity?.max ?? 100}" step="1" value="${opacity}" style="--p:${((opacity - 40) / 60 * 100).toFixed(0)}%">
            <span class="muted">${esc(T('OverlayOpacityText'))}</span></label>
          <div>${btn('OverlayMove', 'Déplacer', 'small')}</div></section>
      </div>`}
      <div class="filters org-order"><b>Ordre de jeu</b>
        <span class="strong" style="color:${S.OrganizerOrderStatus?.c || 'inherit'}">${esc(T('OrganizerOrderStatus'))}</span>
        <span class="muted">Glissez-déposez les lignes pour le changer.</span><span class="right"></span>
        ${btn('OrganizerSaveOrder', "Enregistrer l'ordre", 'small primary')}${btn('OrganizerResetOrder', 'Ordre par initiative', 'small')}</div>
      ${table('OrganizerGrid', [
        { h: 'Ordre', k: 'Order', num: true, f: r => `<span class="order ${r.Excluded ? 'muted' : ''}">${esc(r.OrderText)}</span>` },
        { h: '★', k: 'Main', f: r => `<button class="star ${r.Main ? 'on' : ''}" title="Personnage principal (raccourci « Principal »)" ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerMain_Click' })}>${r.Main ? '★' : '☆'}</button>` },
        { h: 'Personnage', k: 'Name', f: r => `<div class="char"><span class="head" title="${esc(r.Class)}">${r.Gfx ? `<img src="icon/head/${r.Gfx}.png" alt="">` : ''}</span>
          <div><span class="strong ${r.Active ? 'acc' : ''}">${esc(r.Name)}</span>${r.Active ? ' <span class="chip accent">au premier plan</span>' : ''}
          ${r.Class ? `<div class="muted small">${esc(r.Class)}</div>` : ''}</div></div>` },
        { h: 'Niveau', k: 'Level', num: true },
        { h: 'Initiative', k: 'Initiative', num: true, f: r => `<span class="acc strong">${esc(r.InitiativeText)}</span>` },
        { h: 'État', k: 'State', f: r => `<span class="muted">${esc(r.State)}</span>` },
        { h: 'Exclure', k: 'Excluded', f: r => `<label class="check"><input type="checkbox" ${r.Excluded ? 'checked' : ''} ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerExclude_Click' })}></label>` },
        { h: '', f: r => `<button class="small primary" ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerShow_Click' })}>Afficher</button>` },
      ], { select: false, draggable: r => !r.Excluded && !r.Name.startsWith('('), empty: 'Aucune fenêtre Dofus Retro ouverte. Lancez le jeu : ses fenêtres apparaîtront ici.' })}`;
  },

  carte() {
    const D = S._carte;
    if (!D) return `<div class="empty">Chargement de la carte…</div>`;
    if (!carte.shown) carte.shown = new Set(D.shown);
    const list = carte.list = carteHints(D);
    // Ressources des maps visitées : un métier, puis ses ressources (mêmes filtres que les catégories)
    const R = S._carteRes || { jobs: [], hints: [], maps: 0 };
    const resName = k => R.jobs.flatMap(j => j.res).find(r => r.k === k)?.n;
    const job = R.jobs.find(j => j.n === carte.job);
    const groups = {};
    list.forEach(h => { const n = D.cats.find(c => c.k === h.k)?.n || resName(h.k) || 'Divers'; groups[n] = (groups[n] || 0) + 1; });
    const counts = list.length ? Object.entries(groups).map(([n, c]) => `${n} (${c})`).join(' · ')
      : carte.search ? "Aucun point d'intérêt ne correspond à la recherche." : 'Cochez une catégorie pour afficher ses points.';
    return `<div class="filters">
        <select data-carte="sa"><option value="0" ${carte.sa === 0 ? 'selected' : ''}>Continent amaknien</option>
          <option value="3" ${carte.sa === 3 ? 'selected' : ''}>Incarnam</option></select>
        ${D.cats.map(c => `<button class="pill carte-cat ${carte.shown.has(c.k) ? 'on' : ''}" data-act="carte-cat" data-k="${c.k}">
          ${c.i ? `<img src="${c.i}" alt="">` : ''}${esc(c.n)}</button>`).join('')}
        <label class="check"><input type="checkbox" data-carte="grid" ${carte.grid ? 'checked' : ''}>Grille</label>
        <button class="small" data-act="carte-me">Ma position</button>
        <button class="small" data-act="carte-fit">Vue d'ensemble</button>
      </div>
      <div class="filters"><span class="lbl">Ressources</span>
        <select data-carte="job" title="Métier dont les ressources sont proposées. Elles apparaissent sur les maps déjà visitées avec l'application lancée.">
          <option value="">Aucune</option>${R.jobs.map(j => `<option ${j.n === carte.job ? 'selected' : ''}>${esc(j.n)}</option>`).join('')}</select>
        ${(job?.res || []).map(r => {
          const maps = R.hints.filter(h => h.k === r.k).length;
          return `<button class="pill carte-cat ${carte.shown.has(r.k) ? 'on' : ''}" data-act="carte-cat" data-k="${r.k}"
            title="${esc(r.t)} · ${maps ? `vue sur ${maps} map(s) visitée(s)` : 'pas encore vue sur une map visitée'}"><img src="${r.i}" alt="">${esc(r.n)}</button>`;
        }).join('')}
        <span class="muted">${R.maps > 0 ? `${R.maps} map(s) lue(s)` : 'aucune map lue pour l’instant'}</span>
      </div>
      <div class="split wide">
        <div id="carte-host" class="carte-host"></div>
        <aside class="glass detail carte-side">
          <input type="search" data-carte="search" data-fk="carteSearch" value="${esc(carte.search)}" placeholder="Zaap, Temple Iop, Bouftou, [4,-18]…">
          <div class="card harvest"><b>${esc(T('CarteHarvestTitle'))}</b>${T('CarteHarvest') ? `<div class="muted">${esc(T('CarteHarvest'))}</div>` : ''}
            <div class="harvest-chips">${rows('CarteHarvestList').map(h => `<span class="harvest-chip" title="${esc(h.Tooltip)}">${itemImg(h.ItemId)}<span><b>${esc(h.Count)}</b>${h.State ? `<small class="muted">${esc(h.State)}</small>` : ''}</span></span>`).join('')}</div></div>
          ${shown('CarteCellPanel') ? `<div class="card harvest cell-info"><b class="ellipsis" title="${esc(T('CarteCellTitle'))}">${esc(T('CarteCellTitle'))}</b>
            ${T('CarteCellInfo') ? `<div class="muted ellipsis" title="${esc(T('CarteCellInfo'))}">${esc(T('CarteCellInfo'))}</div>` : ''}
            ${T('CarteCellHarvest') ? `<div class="muted">${esc(T('CarteCellHarvest'))}</div>` : ''}
            <div class="harvest-chips">${rows('CarteCellList').map(h => `<span class="harvest-chip" title="${esc(h.Tooltip)}">${itemImg(h.ItemId)}<span><b>${esc(h.Count)}</b>${h.State ? `<small class="muted">${esc(h.State)}</small>` : ''}</span></span>`).join('')}</div></div>` : ''}
          <div class="muted">${esc(counts)}</div>
          <div class="carte-list">${list.slice(0, 400).map((h, i) => `<div class="carte-row ${carte.sel === h ? 'on' : ''}" data-act="carte-go" data-i="${i}">
            ${h.i ? `<img src="${h.i}" alt="">` : '<span></span>'}<div><b>${esc(h.n)}</b><small>${esc(h.l)}</small></div></div>`).join('')}
            ${list.length > 400 ? `<div class="muted">… ${list.length - 400} autres : affinez la recherche.</div>` : ''}</div>
          <div class="muted small">Carte et icônes lues dans le client Dofus Retro installé. Glissez pour déplacer, molette pour zoomer.</div>
        </aside>
      </div>`;
  },

  journal() {
    return `<div class="filters">${check('LogPacketsBox', 'Journaliser les paquets (banque, inventaire, atelier : packets.log)')}</div>
      <pre class="log glass" id="log">${esc(T('LogBox'))}</pre>`;
  },
});

function recipeDetail(sel) {
  const tree = (list, path) => (list || []).map((g, i) => {
    const key = path + '/' + i, open = !!local.open[key];
    return `<div class="ing"><span>${g.HasChildren ? `<button class="caret ${open ? 'open' : ''}" data-act="open" data-key="${esc(key)}">▶</button>` : ''}</span>
      <a class="item-link" data-act="item-hdv" data-id="${g.ItemId}" title="Voir cet objet à l'HDV">${itemImg(g.ItemId)}</a><div><div class="name"><a class="item-link" data-act="item-hdv" data-id="${g.ItemId}" title="Voir cet objet à l'HDV">${esc(g.Name)}</a></div>${g.Note ? `<div class="note">${esc(g.Note)}</div>` : ''}<div class="price">${esc(g.Price)}</div></div>
      <span class="ratio" style="color:${g.Brush || 'inherit'}">${esc(g.Ratio)}</span></div>
      ${g.HasChildren && open ? `<div class="children">${tree(g.Children, key)}</div>` : ''}`;
  }).join('');
  return `<div class="head">${itemImg(sel.Recipe?.ResultId, 'lg')}<div><h2>${esc(T('DetailTitle'))}</h2><div class="sub">${esc(T('DetailSub'))}</div></div></div>
    ${shown('DetailCostPanel') ? `<div class="card">
      <div class="filters"><span class="lbl">Crafts</span>${input('DetailCraftCount', '', '80px')}${btn('DetailCraftMax', 'Max', 'small')}</div>
      <h3>${esc(T('DetailCostTitle'))}</h3><div class="big">${esc(T('DetailCost'))}</div>
      <div class="lines">${esc(T('DetailCostNote'))}</div>
      ${T('DetailProfit') ? `<div class="strong">${colored('DetailProfit')}</div>` : ''}
      ${T('DetailProfitOwned') ? `<div class="strong">${colored('DetailProfitOwned')}</div>` : ''}</div>` : ''}
    ${shown('DetailStatsPanel') && rows('DetailStats').length ? `<h3>Caractéristiques (max.)</h3><div class="stats">${rows('DetailStats').map(s => `<span class="chip">${esc(s)}</span>`).join('')}</div>` : ''}
    <h3>Ingrédients</h3>${tree(rows('DetailIngredients'), 'r' + sel.Recipe?.ResultId)}`;
}


// --- Carte : vue persistante (les tuiles ne sont pas rechargées à chaque rendu), déplacée et zoomée à la souris ---
// Une map occupe 40 × 23 ; une tuile du client (600 × 345) couvre 15 × 15 maps.
const CW = 40, CH = 23, TW = 600, TH = 345;
const carte = { el: null, built: null, sa: 0, s: 0.2, ox: 0, oy: 0, fitted: false, grid: true, search: '', shown: null, job: '',
  sel: null, list: [], bounds: null, drag: null };

const norm = s => String(s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

function carteHints(D) {
  const q = norm(carte.search.trim());
  return D.hints.concat(S._carteRes?.hints || []).filter(h => h.sa === carte.sa && (q
    ? norm(h.n).includes(q) || norm(h.l).includes(q) || `[${h.x},${h.y}]`.includes(q.replace(/\s/g, ''))
    : carte.shown.has(h.k)))
    .sort((a, b) => a.k - b.k || a.n.localeCompare(b.n));
}

function carteBuild(D) {
  const tiles = D.tiles[carte.sa] || [];
  const el = document.createElement('div');
  el.className = 'carte-view';
  if (!tiles.length) {
    el.innerHTML = `<div class="empty">Carte introuvable : elle est lue dans les fichiers de Dofus Retro, qui doit être installé sur ce PC.</div>`;
  } else {
    const xs = tiles.map(t => t[0]), ys = tiles.map(t => t[1]);
    const b = carte.bounds = { x: Math.min(...xs) * TW, y: Math.min(...ys) * TH, w: (Math.max(...xs) + 1) * TW, h: (Math.max(...ys) + 1) * TH };
    b.w -= b.x; b.h -= b.y;
    el.innerHTML = `<div class="carte-world">
        ${tiles.map(([x, y]) => `<img src="carte/tile/${carte.sa}/${x}_${y}.png" style="left:${x * TW}px;top:${y * TH}px" alt="" draggable="false">`).join('')}
        <div class="carte-grid" style="left:${b.x}px;top:${b.y}px;width:${b.w}px;height:${b.h}px"></div>
        <div class="carte-cell hover"></div><div class="carte-cell sel"></div>
      </div><div class="carte-markers"></div><div class="carte-hover">Survolez la carte pour voir les coordonnées.</div>`;
  }
  carte.el = el;
  carte.built = carte.sa;
  carte.fitted = false;

  el.addEventListener('wheel', e => {
    e.preventDefault();
    const r = el.getBoundingClientRect();
    carteZoom(e.clientX - r.left, e.clientY - r.top, e.deltaY < 0 ? 1.25 : 0.8);
  }, { passive: false });
  el.addEventListener('mousedown', e => {
    if (e.button !== 0) return;
    carte.drag = { x: e.clientX, y: e.clientY, ox: carte.ox, oy: carte.oy, moved: false };
  });
  el.addEventListener('mousemove', e => {
    const r = el.getBoundingClientRect();
    const wx = (e.clientX - r.left - carte.ox) / carte.s, wy = (e.clientY - r.top - carte.oy) / carte.s;
    carteHover(Math.floor(wx / CW), Math.floor(wy / CH));
  });
  el.addEventListener('mouseleave', () => carteHover(null));
}

window.addEventListener('mousemove', e => {
  const d = carte.drag;
  if (!d) return;
  if (!d.moved && Math.hypot(e.clientX - d.x, e.clientY - d.y) > 4) d.moved = true;
  if (d.moved) { carte.ox = d.ox + e.clientX - d.x; carte.oy = d.oy + e.clientY - d.y; carteView(); }
});
window.addEventListener('mouseup', e => {
  const d = carte.drag;
  carte.drag = null;
  if (!d || d.moved) return;
  // Clic sans déplacement sur un marqueur : le point est choisi dans la liste ; ailleurs sur la carte : infos de la case
  const m = e.target.closest?.('.carte-marker[data-i]');
  if (m) { const h = carte.list[+m.dataset.i]; carteSelect(h, false); if (h) post({ type: 'cell', sa: h.sa, x: h.x, y: h.y }); }
  else if (carte.el && carte.el.contains(e.target)) {
    const r = carte.el.getBoundingClientRect();
    const x = Math.floor((e.clientX - r.left - carte.ox) / carte.s / CW), y = Math.floor((e.clientY - r.top - carte.oy) / carte.s / CH);
    if (S._carte?.cells[carte.sa]?.[`${x},${y}`]) { carte.sel = { x, y, sa: carte.sa }; carteView(); post({ type: 'cell', sa: carte.sa, x, y }); }
  }
});

function carteZoom(px, py, f) {
  const wx = (px - carte.ox) / carte.s, wy = (py - carte.oy) / carte.s;
  carte.s = Math.min(4, Math.max(0.08, carte.s * f));
  carte.ox = px - wx * carte.s; carte.oy = py - wy * carte.s;
  carteView();
}

function carteFit() {
  const b = carte.bounds, el = carte.el;
  if (!b || !el || !el.clientWidth) return;
  carte.s = Math.min(el.clientWidth / b.w, el.clientHeight / b.h);
  carte.ox = (el.clientWidth - b.w * carte.s) / 2 - b.x * carte.s;
  carte.oy = (el.clientHeight - b.h * carte.s) / 2 - b.y * carte.s;
  carte.fitted = true;
  carteView();
}

function carteCenter(x, y, minScale = 1) {
  const el = carte.el;
  if (!el) return;
  carte.s = Math.max(carte.s, minScale);
  carte.ox = el.clientWidth / 2 - (x + 0.5) * CW * carte.s;
  carte.oy = el.clientHeight / 2 - (y + 0.5) * CH * carte.s;
  carte.fitted = true;
  carteView();
}

/** Applique la vue : déplacement et zoom du monde, marqueurs replacés (ils gardent leur taille). */
function carteView() {
  const el = carte.el;
  if (!el) return;
  const world = el.querySelector('.carte-world');
  if (world) world.style.transform = `translate(${carte.ox}px,${carte.oy}px) scale(${carte.s})`;
  const grid = el.querySelector('.carte-grid');
  if (grid) grid.style.display = carte.grid && carte.s * CH >= 6 ? '' : 'none';
  el.querySelectorAll('.carte-marker').forEach(m => {
    m.style.left = ((+m.dataset.x + 0.5) * CW * carte.s + carte.ox) + 'px';
    m.style.top = ((+m.dataset.y + 0.5) * CH * carte.s + carte.oy) + 'px';
  });
  const sel = el.querySelector('.carte-cell.sel');
  if (sel) sel.style.cssText = carte.sel ? `display:block;left:${carte.sel.x * CW}px;top:${carte.sel.y * CH}px;border-width:${3 / carte.s}px` : 'display:none';
}

function carteHover(x, y) {
  const el = carte.el, D = S._carte;
  if (!el || !D) return;
  const cell = el.querySelector('.carte-cell.hover'), label = el.querySelector('.carte-hover');
  if (!cell || !label) return;
  if (x == null) { cell.style.display = 'none'; label.textContent = 'Survolez la carte pour voir les coordonnées.'; return; }
  cell.style.cssText = `display:block;left:${x * CW}px;top:${y * CH}px;border-width:${1.5 / carte.s}px`;
  const zone = D.cells[carte.sa]?.[`${x},${y}`];
  const here = [...new Set(D.hints.filter(h => h.sa === carte.sa && h.x === x && h.y === y).map(h => h.n))];
  label.textContent = `[${x},${y}]` + (zone ? `  ${zone}` : '') + (here.length ? '  —  ' + here.slice(0, 4).join(', ') + (here.length > 4 ? '…' : '') : '');
}

function carteSelect(h, center = true) {
  if (!h) return;
  carte.sel = h;
  if (center) carteCenter(h.x, h.y); else carteView();
  carteHover(h.x, h.y);
  schedule();
}

/** Place la vue dans l'onglet (après chaque rendu) et redessine les marqueurs : points filtrés et vos personnages. */
function carteMount() {
  const host = document.getElementById('carte-host'), D = S._carte;
  if (!host || !D) return;
  if (!carte.el || carte.built !== carte.sa) carteBuild(D);
  host.appendChild(carte.el);
  const layer = carte.el.querySelector('.carte-markers');
  if (layer) {
    const players = (S._cartePos || []).filter(p => p.sa === carte.sa);
    layer.innerHTML = carte.list.map((h, i) => `<div class="carte-marker" data-i="${i}" data-x="${h.x}" data-y="${h.y}" title="${esc(h.n + '\n' + h.l)}">
        ${h.i ? `<img src="${h.i}" alt="" draggable="false">` : '<i></i>'}</div>`).join('')
      + players.map(p => `<div class="carte-marker player ${p.me ? 'me' : ''}" data-x="${p.x}" data-y="${p.y}" title="${esc(p.n + '\n' + p.l)}">
        <i></i><span>${esc(p.n)}</span></div>`).join('')
      + (carte.target?.sa === carte.sa ? `<div class="carte-marker player goto" data-x="${carte.target.x}" data-y="${carte.target.y}" title="${esc(carte.target.l)}">
        <i></i><span>${esc(carte.target.l)}</span></div>` : '');
  }
  if (carte.goto) carteGoto(); else if (!carte.fitted) requestAnimationFrame(carteFit); else carteView();
}

/** Centre la carte sur la case demandée (position cliquée), l'encadre et y pose un repère avec son libellé. */
function carteGoto() {
  let g = carte.goto;
  if (g.me) {
    if (!S._cartePos) return; // positions pas encore reçues
    const me = S._cartePos.find(p => p.me) || S._cartePos[0];
    carte.goto = null;
    if (!me) { carteFit(); return; }
    g = { sa: me.sa, x: me.x, y: me.y };
    if (me.sa !== carte.sa) { carte.sa = me.sa; carte.goto = g; schedule(); return; }
  }
  carte.goto = null;
  carte.target = g.l ? g : null;
  carte.sel = { x: g.x, y: g.y };
  carte.fitted = true; // pas de vue d'ensemble au rendu suivant : on centre sur la case
  schedule(); // repère redessiné au rendu suivant
  requestAnimationFrame(() => { carteCenter(g.x, g.y, 1.5); carteHover(g.x, g.y); });
}

// Mise à jour de la page : seuls les éléments qui ont changé sont modifiés (texte, attributs, lignes ajoutées ou retirées).
// Les images déjà chargées, la saisie en cours, le défilement et la carte restent en place.
function morphHtml(root, html) {
  const next = document.createElement('div');
  next.innerHTML = html;
  morphChildren(root, next);
}

function morphChildren(a, b) {
  if (a.id === 'carte-host') return; // vue de la carte, posée par carteMount
  const live = a.childNodes, wanted = [...b.childNodes];
  for (let i = 0; i < wanted.length; i++) {
    const x = live[i], y = wanted[i];
    if (!x) a.appendChild(y);
    else if (x.nodeType !== y.nodeType || x.nodeName !== y.nodeName) a.replaceChild(y, x);
    else if (x.nodeType !== 1) { if (x.nodeValue !== y.nodeValue) x.nodeValue = y.nodeValue; }
    // Bloc identique (comparaison native, rapide) : rien à faire, sauf pour les champs dont la valeur saisie diffère
    else if (x.isEqualNode(y) && !x.querySelector?.('input, select') && x.tagName !== 'INPUT') { }
    else morphElement(x, y);
  }
  while (live.length > wanted.length) a.removeChild(a.lastChild);
}

function morphElement(a, b) {
  for (const { name } of [...a.attributes]) if (!b.hasAttribute(name)) a.removeAttribute(name);
  for (const { name, value } of b.attributes) if (a.getAttribute(name) !== value) a.setAttribute(name, value);
  // État des champs : la valeur affichée n'est pas un attribut (sauf pour le champ en cours de saisie)
  if (a.tagName === 'INPUT') {
    if (a !== document.activeElement && a.value !== b.value) a.value = b.value;
    if (a.checked !== b.checked) a.checked = b.checked;
  } else if (a.tagName === 'OPTION') { if (a.selected !== b.selected) a.selected = b.selected; }
  morphChildren(a, b);
}

let pending = false;
function render() {
  pending = false;
  document.documentElement.dataset.theme = (S._theme || 'Crepuscule').toLowerCase();
  document.documentElement.dataset.style = (S._style || 'Moderne').toLowerCase();
  const active = S.MainTabs?.i ?? 0;
  const tab = TABS[active] || TABS[0];
  watchTab(active);

  // Garder la saisie en cours et le défilement des tableaux pendant le rendu
  const focus = document.activeElement?.dataset?.fk ? { key: document.activeElement.dataset.fk, value: document.activeElement.value,
    start: document.activeElement.selectionStart, end: document.activeElement.selectionEnd } : null;
  const scroll = [...document.querySelectorAll('.table-wrap, .detail, .modal-body')].map(e => e.scrollTop);

  const html = header() + banner() + tabs(active)
    + `<main class="glass">${views[tab.key]()}</main>` + changelog() + confirmBox() + ttgBook();
  const app = document.getElementById('app');
  if (local.noMorph) app.innerHTML = html; else morphHtml(app, html);

  if (tab.key === 'carte') carteMount();
  document.querySelectorAll('.table-wrap, .detail, .modal-body').forEach((e, i) => { if (scroll[i]) e.scrollTop = scroll[i]; });
  // Journal : toujours sur les derniers messages
  const log = document.getElementById('log');
  if (log) log.scrollTop = log.scrollHeight;
  if (focus) {
    const el = document.querySelector(`[data-fk="${focus.key}"]`);
    if (el) {
      el.focus();
      if (el.value !== focus.value) el.value = focus.value;
      try { el.setSelectionRange(focus.start, focus.end); } catch { }
    }
  }
}
const schedule = () => { if (!pending) { pending = true; requestAnimationFrame(render); } };

// --- Événements ---
window.chrome.webview.addEventListener('message', e => {
  if (e.data?.type !== 'update') return;
  Object.assign(S, e.data.data);
  // Nouveautés demandées par l'application (après une mise à jour) : la fenêtre s'ouvre une fois par demande
  if ((S._changelog || 0) > (local.changelogSeen || 0)) { local.changelogSeen = S._changelog; local.changelog = true; }
  schedule();
});

document.addEventListener('click', e => {
  const a = e.target.closest('[data-act]');
  if (!a) return;
  const d = a.dataset;
  switch (d.act) {
    case 'click': post({ type: 'click', name: d.name }); break;
    case 'tab': post({ type: 'set', name: 'MainTabs', value: +d.i }); S.MainTabs = { i: +d.i }; schedule(); break;
    case 'changelog': local.changelog = true; schedule(); break;
    case 'changelog-close': local.changelog = false; schedule(); break;
    case 'book': local.book = { fam: null, shiny: false }; schedule(); break;
    case 'trades': local.trades = !local.trades; schedule(); break;
    case 'trade-del': post({ type: 'trade-del', index: +d.i }); break;
    case 'trade-kind': local.tf.k = d.k; schedule(); break;
    case 'book-close': local.book = null; schedule(); break;
    case 'book-fam': local.book.fam = +d.id; schedule(); break;
    case 'book-shiny': local.book.shiny = !local.book.shiny; schedule(); break;
    case 'confirm': local.confirm = { name: d.name, text: d.text }; schedule(); break;
    case 'confirm-close': local.confirm = null; schedule(); break;
    case 'org-settings': local.orgHidden = !local.orgHidden; schedule(); break;
    case 'confirm-ok': post({ type: 'click', name: local.confirm.name, confirmed: true }); local.confirm = null; schedule(); break;
    case 'carte-cat': {
      const k = +d.k;
      if (carte.shown.has(k)) carte.shown.delete(k); else carte.shown.add(k);
      schedule();
      break;
    }
    case 'carte-go': { const h = carte.list[+d.i]; carteSelect(h); if (h) post({ type: 'cell', sa: h.sa, x: h.x, y: h.y }); break; }
    case 'carte-fit': carteFit(); break;
    case 'item-hdv': post({ type: 'show', tab: 'hdv', id: +d.id }); break;
    case 'item-recipe': post({ type: 'show', tab: 'recipe', id: +d.id }); break;
    case 'goto': case 'goto-me': {
      // Repère à montrer une fois la carte chargée (carteMount) ; vos personnages sont déjà marqués
      const g = d.act === 'goto' ? JSON.parse(d.g) : null;
      carte.goto = g ? { sa: g.SuperArea, x: g.X, y: g.Y, l: g.Label } : { me: true };
      if (g && g.SuperArea !== carte.sa) { carte.sa = g.SuperArea; carte.sel = null; }
      const i = TABS.findIndex(t => t.key === 'carte');
      post({ type: 'set', name: 'MainTabs', value: i }); S.MainTabs = { i }; schedule();
      break;
    }
    case 'carte-me': {
      const me = (S._cartePos || []).find(p => p.me) || (S._cartePos || [])[0];
      if (!me) break;
      if (me.sa !== carte.sa) { carte.sa = me.sa; carte.sel = null; schedule(); requestAnimationFrame(() => requestAnimationFrame(() => carteCenter(me.x, me.y, 1.5))); }
      else carteCenter(me.x, me.y, 1.5);
      break;
    }
    case 'theme': post({ type: 'theme', value: d.v }); S._theme = d.v; schedule(); break;
    case 'style': post({ type: 'style', value: d.v }); S._style = d.v; schedule(); break;
    case 'select': post({ type: 'select', name: d.grid, index: +d.i }); break;
    case 'row': post({ type: 'row', name: d.grid, index: +d.i, handler: d.handler }); break;
    case 'toggle': post({ type: 'toggle', name: d.name, index: +d.i }); break;
    case 'open': local.open[d.key] = !local.open[d.key]; schedule(); break;
    case 'more': local.limit[d.grid] = (local.limit[d.grid] || 300) + 300; schedule(); break;
    case 'sort': {
      const s = local.sort[d.grid];
      local.sort[d.grid] = s && s.k === d.k ? (s.dir > 0 ? { k: d.k, dir: -1 } : null) : { k: d.k, dir: 1 };
      schedule();
      break;
    }
  }
});

// Glisser-déposer d'une ligne (ordre de jeu du Switcheur)
let dragFrom = null;
document.addEventListener('dragstart', e => {
  const tr = e.target.closest?.('tr[data-drag-grid]');
  if (!tr) return;
  dragFrom = { grid: tr.dataset.dragGrid, i: +tr.dataset.dragI };
  e.dataTransfer.effectAllowed = 'move';
  tr.classList.add('dragging');
});
document.addEventListener('dragover', e => {
  const tr = e.target.closest?.('tr[data-drag-grid]');
  if (!dragFrom || !tr || tr.dataset.dragGrid !== dragFrom.grid) return;
  e.preventDefault();
  document.querySelectorAll('tr.drop-target').forEach(x => x.classList.remove('drop-target'));
  tr.classList.add('drop-target');
});
document.addEventListener('drop', e => {
  const tr = e.target.closest?.('tr[data-drag-grid]');
  if (dragFrom && tr && tr.dataset.dragGrid === dragFrom.grid) {
    e.preventDefault();
    post({ type: 'reorder', name: dragFrom.grid, index: dragFrom.i, to: +tr.dataset.dragI });
  }
});
document.addEventListener('dragend', () => {
  dragFrom = null;
  document.querySelectorAll('tr.dragging, tr.drop-target').forEach(x => x.classList.remove('dragging', 'drop-target'));
});
// Raccourci : la combinaison pressée remplace la valeur (« Ctrl+Alt+Right ») ; Suppr ou Retour arrière l'efface
const KEYS = { ArrowRight: 'Right', ArrowLeft: 'Left', ArrowUp: 'Up', ArrowDown: 'Down', ' ': 'Space', Escape: 'Escape',
  PageUp: 'PageUp', PageDown: 'Next', Enter: 'Return' };
document.addEventListener('keydown', e => {
  if (e.key === 'Escape' && local.confirm) { local.confirm = null; schedule(); return; }
  if (e.key === 'Escape' && local.changelog) { local.changelog = false; schedule(); return; }
  if (e.key === 'Escape' && local.book) { local.book = null; schedule(); return; }
  const name = e.target.dataset?.hotkey;
  if (!name) return;
  e.preventDefault();
  if (e.key === 'Delete' || e.key === 'Backspace') { post({ type: 'set', name, value: '' }); return; }
  if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) return;
  const key = KEYS[e.key] || (/^Digit\d$/.test(e.code) ? 'D' + e.code.slice(5) : /^Key[A-Z]$/.test(e.code) ? e.code.slice(3)
    : /^F\d+$/.test(e.key) ? e.key : /^Numpad\d$/.test(e.code) ? 'NumPad' + e.code.slice(6) : null);
  if (!key) return;
  const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean);
  post({ type: 'set', name, value: mods.concat(key).join('+') });
});

// Saisie : envoyée après une courte pause ; listes et cases : tout de suite
const timers = {};
document.addEventListener('input', e => {
  const el = e.target;
  // Carte : recherche propre à la page
  if (el.dataset?.carte === 'search') { carte.search = el.value; schedule(); return; }
  // Seuil du Brisage (minimum ou maximum d'une caractéristique)
  if (el.dataset?.stat != null && el.type !== 'checkbox') {
    const key = 'stat' + el.dataset.stat + el.dataset.field;
    clearTimeout(timers[key]);
    timers[key] = setTimeout(() => post({ type: 'stat', index: +el.dataset.stat, field: el.dataset.field, value: el.value }), 250);
    return;
  }
  if (!el.dataset?.set || el.type === 'checkbox' || el.tagName === 'SELECT') return;
  if (el.type === 'range') {
    el.style.setProperty('--p', ((el.value - el.min) / (el.max - el.min) * 100) + '%');
    clearTimeout(timers[el.dataset.set]);
    timers[el.dataset.set] = setTimeout(() => post({ type: 'set', name: el.dataset.set, value: +el.value }), 120);
    return;
  }
  clearTimeout(timers[el.dataset.set]);
  timers[el.dataset.set] = setTimeout(() => post({ type: 'set', name: el.dataset.set, value: el.value }), 250);
});
document.addEventListener('change', e => {
  const el = e.target;
  if (el.dataset?.tf) { local.tf[el.dataset.tf] = el.dataset.tf === 'd' ? +el.value : el.value; schedule(); return; }
  if (el.dataset?.carte === 'sa') { carte.sa = +el.value; carte.sel = null; schedule(); return; }
  if (el.dataset?.carte === 'job') { carte.job = el.value; schedule(); return; }
  if (el.dataset?.carte === 'grid') { carte.grid = el.checked; carteView(); return; }
  if (el.dataset?.fmweight != null) { post({ type: 'fmweight', index: +el.dataset.fmweight, value: el.value }); return; }
  if (el.dataset?.stat != null && el.type === 'checkbox') { post({ type: 'stat', index: +el.dataset.stat, field: 'on', value: el.checked }); return; }
  if (!el.dataset?.set) return;
  if (el.type === 'checkbox') post({ type: 'set', name: el.dataset.set, value: el.checked });
  // Volume relâché : nouvelle valeur, puis le son pour l'entendre
  else if (el.type === 'range') {
    post({ type: 'set', name: el.dataset.set, value: +el.value });
    if (el.dataset.set === 'HuntVolume') post({ type: 'click', name: 'HuntTestSound' });
  }
  else if (el.tagName === 'SELECT') post({ type: 'set', name: el.dataset.set, value: +el.value });
});

// Icône introuvable : masquée (pour un sort, son nom reste affiché dessous)
document.addEventListener('error', e => { if (e.target.tagName === 'IMG') e.target.style.visibility = 'hidden'; }, true);

render();

// --- Survol immédiat : éléments [data-tip] (nom, état, prix…), bulle qui suit la souris ---
(() => {
  const tip = document.createElement('div');
  tip.className = 'hover-tip';
  document.body.appendChild(tip);
  let cur = null;
  document.addEventListener('mouseover', e => {
    const el = e.target.closest?.('[data-tip]');
    if (el === cur) return;
    cur = el;
    if (!el || !el.dataset.tip) { tip.style.display = 'none'; return; }
    tip.textContent = el.dataset.tip;
    tip.style.display = 'block';
  });
  document.addEventListener('mousemove', e => {
    if (!cur) return;
    const w = tip.offsetWidth, h = tip.offsetHeight;
    tip.style.left = Math.min(e.clientX + 14, innerWidth - w - 6) + 'px';
    tip.style.top = (e.clientY + 18 + h > innerHeight ? e.clientY - h - 10 : e.clientY + 18) + 'px';
  });
  // La page est redessinée : l'élément survolé a pu disparaître
  new MutationObserver(() => { if (cur && !cur.isConnected) { cur = null; tip.style.display = 'none'; } })
    .observe(document.body, { childList: true, subtree: true });
})();
