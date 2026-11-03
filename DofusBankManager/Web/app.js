'use strict';
// Interface HTML de Dofus Bank Manager. La page n'a pas de logique métier : elle affiche l'état des contrôles WPF
// (reçu par messages, clé = nom x:Name du contrôle) et leur renvoie les actions de l'utilisateur.

const S = {};                                 // état reçu
const local = { sort: {}, limit: {}, open: {} }; // tri, pagination et arbres dépliés, propres à la page
const post = m => window.chrome.webview.postMessage(m);

// Contrôles suivis, par onglet (les autres onglets s'ouvrent dans l'interface classique)
const COMMON = ['VersionText', 'CaptureDot', 'CaptureStatus', 'BankStatus', 'CaptureButton', 'AdminButton', 'UpdateBanner',
  'UpdateTitle', 'UpdateText', 'UpdateActionButton', 'UpdateLaterButton', 'UpdateNotesButton', 'MainTabs'];
const TABS = [
  { key: 'bank', label: 'Banque', icon: '▣', watch: ['BankOwner', 'BankSearch', 'BankTypeFilter', 'BankCount', 'BankValue', 'BankGrid'] },
  { key: 'recipes', label: 'Recettes', icon: '✦', watch: ['RecipeSearch', 'JobFilter', 'ModeFilter', 'SellMinPanel', 'SellMin',
    'LevelMin', 'LevelMax', 'SlotFilters', 'IncludeInventory', 'InventoryHint', 'RecipeCount', 'IngredientChip', 'IngredientChipText',
    'RecipeGrid', 'DetailTitle', 'DetailSub', 'DetailCostPanel', 'DetailCraftCount', 'DetailCraftMax', 'DetailCostTitle', 'DetailCost',
    'DetailCostNote', 'DetailProfit', 'DetailProfitOwned', 'DetailStatsPanel', 'DetailStats', 'DetailIngredients'] },
  { key: 'brisage', label: 'Brisage', icon: '⚒', watch: ['BrisageSearch', 'BrisageSource', 'BrisageTypeFilter', 'BrisageLevelMin',
    'BrisageLevelMax', 'BrisageCount', 'BrisageStats', 'BrisageGrid', 'BrisageGainColumn', 'BrisageEmpty', 'BrisageDetailTitle',
    'BrisageDetailSub', 'BrisageBreakCount', 'BrisageBreakMax', 'BrisageDetailTotal', 'BrisageRunes'] },
  { key: 'hdv', label: 'HDV', icon: '⚖', watch: ['HdvSearch', 'HdvTypeFilter', 'HdvShowAll', 'HdvCount', 'HdvGrid', 'HdvEmpty',
    'HdvDetailTitle', 'HdvDetailSub', 'HdvChart', 'HdvHistory', 'HdvForget'] },
  { key: 'ttg', label: 'TTG', icon: '❏', watch: ['TtgSearch', 'TtgRarity', 'TtgShow', 'TtgShowAll', 'TtgDaysBox', 'TtgCount',
    'TtgCollection', 'TtgGrid', 'TtgBestColumn', 'TtgEmpty', 'TtgDetailTitle', 'TtgDetailSub', 'TtgOffersMode', 'TtgOffersTitle',
    'TtgOffers', 'TtgMerchantChart', 'TtgHdvChart'] },
  { key: 'pda', label: 'PDA', icon: '◆', watch: ['PdaSearch', 'PdaSource', 'PdaCategory', 'PdaShowAll', 'PdaCount', 'PdaValue',
    'PdaGrid', 'PdaEmpty', 'PdaDetailTitle', 'PdaDetailSub', 'PdaChart'] },
  { key: 'combat', label: 'Combat', icon: '⌖', watch: ['HuntCharacterBox', 'HuntLevel', 'HuntGroupLevel', 'HuntWisdom', 'HuntProspection',
    'HuntGroupProspection', 'HuntSound', 'HuntVolume', 'HuntVolumeText', 'HuntStatus', 'EconomeStatus', 'EconomeSpells', 'HuntMap', 'HuntGroupGrid', 'HuntEmpty',
    'HuntSession', 'HuntProjection', 'HuntLearned', 'HuntArchiGrid', 'HuntFightGrid'] },
  { key: 'windows', label: 'Switcheur', icon: '⧉', watch: ['OrganizerEnabled', 'HotkeyDebounce', 'NotifyAutoSwitchTurn', 'OrganizerLockOrder', 'HotkeyNext', 'HotkeyPrevious', 'HotkeyMain', 'HotkeyStatus', 'NotifyFight', 'NotifyTurn',
    'NotifyExchange', 'NotifyParty', 'NotifyToast', 'NotifyFlash', 'NotifySound', 'NotifyAutoSwitch', 'OrganizerCount', 'OrganizerGrid', 'OverlayEnabled', 'OverlayEconome', 'OverlayGroups',
    'OverlayRotation', 'OverlayOpacity', 'OverlayOpacityText', 'OverlayMove', 'OrganizerSaveOrder', 'OrganizerResetOrder',
    'OrganizerOrderStatus'] },
  { key: 'fm', label: 'FM', icon: '✧', watch: ['FmItemCombo', 'FmItemName', 'FmReliquat', 'FmReliquatInput', 'FmStats',
    'FmHistoryGrid', 'FmEmpty'] },
  { key: 'arene', label: 'Arène', icon: '⏱', watch: ['ChronoTime', 'ChronoStart', 'ChronoReset', 'ChronoFights', 'ChronoGains',
    'ChronoRate', 'ChronoGrid', 'AreneChallenges', 'AreneOtherChallenges'] },
  { key: 'carte', label: 'Carte', icon: '◳', watch: [] },
  { key: 'journal', label: 'Journal', icon: '☰', watch: ['LogBox', 'LogPacketsBox'] },
];
post({ type: 'watch', names: COMMON.concat(...TABS.map(t => t.watch || [])) });

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
    <div class="logo">D</div>
    <div class="brand">
      <h1><span class="grad">Dofus Bank Manager</span><small>${esc(T('VersionText'))}</small></h1>
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
      <button data-act="ui">Interface classique</button>
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

function banner() {
  if (!shown('UpdateBanner')) return '';
  return `<div class="banner glass"><div class="txt"><b>${esc(T('UpdateTitle'))}</b><p>${esc(T('UpdateText'))}</p></div>
    ${btn('UpdateNotesButton', 'Nouveautés')}${btn('UpdateActionButton', 'Télécharger et installer', 'primary')}${btn('UpdateLaterButton', 'Plus tard')}</div>`;
}

function tabs(active) {
  return `<nav class="tabs glass">${TABS.map((t, i) => `<button class="tab ${i === active ? 'on' : ''}" data-act="tab" data-i="${i}">
    <span>${t.icon}</span>${esc(t.label)}</button>`).join('')}</nav>`;
}

const views = {
  bank() {
    return `<div class="filters"><span class="strong acc">${esc(T('BankOwner'))}</span>${input('BankSearch', 'Rechercher un objet…', '260px')}${select('BankTypeFilter')}
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
        <span class="muted">Partage entre amis : dans l'interface classique</span></div>
      <div class="split">
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
          ${rows('HdvHistory').map(h => `<div class="card"><b>${esc(h.When)}</b><div class="muted">${esc(h.Prices)}</div></div>`).join('') || '<div class="muted">Aucun relevé.</div>'}
          ${btn('HdvForget', 'Oublier ce prix', 'ghost')}` : '<div class="empty">Sélectionnez un objet.</div>'}</aside>
      </div>`;
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
      </div>
      ${T('HuntStatus') ? `<div class="alert">${esc(T('HuntStatus'))}</div>` : ''}
      <div class="split wide">
        <div class="stack">
          <div class="glass econome"><div class="bar"><span class="t"><b class="acc">Économe</b> · ${esc(T('EconomeStatus'))}</span>
            <button class="small" data-act="click" data-name="EconomeReset">Remettre à zéro</button></div><div class="spells">${spells}</div></div>
          <div class="muted strong">${esc(T('HuntMap'))}</div>
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
              { h: 'Vu', k: 'TimeSort', f: r => `<span class="muted">${esc(r.Time)}</span>` },
              { h: 'Archi-monstre', k: 'Name', f: r => `<span class="strong">${esc(r.Name)}</span><div class="muted">${esc(r.Map)}</div>` },
              { h: 'Niv.', k: 'Level', num: true },
              { h: 'Pierre', k: 'Price', num: true, f: r => fmt(r.Price) },
            ], { select: false, empty: 'Aucun archi-monstre vu.' })}</div>
          <div class="mini"><p class="section-title">Combats de la session</p>
            ${table('HuntFightGrid', [
              { h: 'Heure', k: 'TimeSort', f: r => `<span class="muted">${esc(r.Time)}</span>` },
              { h: 'Monstres', k: 'Monsters', f: r => `<span class="ellipsis" title="${esc(r.Drops)}">${esc(r.Monsters)}</span>` },
              { h: 'XP', k: 'Xp', num: true, f: r => fmt(r.Xp) },
              { h: 'Kamas', k: 'Kamas', num: true, f: r => `<span class="acc">${fmt(r.Kamas)}</span>` },
            ], { select: false, empty: 'Aucun combat sur cette session.' })}</div>
        </div>
      </div>`;
  },
};

const seenCol = { h: 'Relevé', k: 'SeenAt', f: r => `<span class="muted">${esc(r.Seen)}</span>` };
const detailHead = (icon, title, sub) => `<div class="head">${icon}<div><h2>${esc(T(title))}</h2></div></div>
  ${T(sub) ? `<div class="sub">${esc(T(sub))}</div>` : ''}`;

Object.assign(views, {
  brisage() {
    const stats = (S.BrisageStats?.items || []).map((s, i) => `<span class="chip stat-filter ${s.on ? 'on' : ''}">
      <label class="check"><input type="checkbox" data-stat="${i}" data-field="on" ${s.on ? 'checked' : ''}>${esc(s.t)}</label>
      <input type="text" data-stat="${i}" data-field="min" data-fk="stat-${i}-min" value="${esc(s.min)}" placeholder="min">
      <span class="muted">à</span>
      <input type="text" data-stat="${i}" data-field="max" data-fk="stat-${i}-max" value="${esc(s.max)}" placeholder="max"></span>`).join('');
    const sel = rows('BrisageGrid')[S.BrisageGrid?.sel ?? -1];
    return `<div class="filters">${input('BrisageSearch', 'Rechercher un objet…', '220px')}${select('BrisageSource')}${select('BrisageTypeFilter')}
        <span class="lbl">Niveau</span>${input('BrisageLevelMin', '', '60px')}<span class="lbl">à</span>${input('BrisageLevelMax', '', '60px')}
        ${btn('BrisageReset', 'Réinitialiser les stats', 'small')}<span class="chip">${esc(T('BrisageCount'))}</span></div>
      <div class="filters stat-filters">${stats}</div>
      <div class="split">
        ${table('BrisageGrid', [
          { h: '', f: r => itemImg(r.ItemId) },
          { h: 'Objet', k: 'Name', f: r => `<span class="strong">${esc(r.Name)}</span><div class="muted">${esc(r.Type)} · niv. ${r.Level}</div>` },
          { h: 'Caractéristiques', k: 'Score', f: r => `<span class="ellipsis" title="${esc(r.Stats)}">${esc(r.Stats)}</span>` },
          { h: 'Où', k: 'Where' },
          { h: 'Poss.', k: 'Owned', num: true, f: r => `<span class="strong ${r.Owned > 0 ? 'acc' : 'muted'}">${r.Owned}</span>` },
          { h: 'Faisable', k: 'Craftable', num: true, f: r => r.Craftable > 0 ? `<span class="ok strong">×${r.Craftable}</span>` : r.Missing ? `<span class="bad ellipsis" title="${esc(r.Missing)}">${esc(r.Missing)}</span>` : '' },
          { h: 'Coût craft', k: 'CraftCost', num: true, f: r => fmt(r.CraftCost) },
          { h: 'Prix HDV', k: 'HdvPrice', num: true, f: r => `<span class="acc strong">${fmt(r.HdvPrice)}</span>` },
          { h: 'Runes ≈', k: 'RunesText', f: r => `<span class="ellipsis" title="${esc(r.RunesText)}">${esc(r.RunesText)}</span>` },
          { h: 'Valeur runes', k: 'RunesValue', num: true, f: r => `<span class="strong">${fmt(r.RunesValue)}</span>` },
          { h: S.BrisageGainColumn?.t || 'Gain', k: 'Gain', num: true, f: r => r.Gain == null ? '—' : `<span class="strong ${r.GainSign < 0 ? 'bad' : 'ok'}">${r.Gain > 0 ? '+' : ''}${fmt(r.Gain)}</span>` },
        ], { empty: T('BrisageEmpty') || 'Aucun objet ne correspond à ces critères.' })}
        <aside class="detail glass">${sel ? `${detailHead(itemImg(sel.ItemId, 'lg'), 'BrisageDetailTitle', 'BrisageDetailSub')}
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
    return `<div class="filters">${input('TtgSearch', 'Rechercher une carte…', '220px')}<span class="lbl">Rareté</span>${select('TtgRarity')}
        <span class="lbl">Afficher</span>${select('TtgShow')}${check('TtgShowAll', 'Toutes les cartes du jeu')}
        <span class="lbl">Période</span>${input('TtgDaysBox', '', '56px')}<span class="lbl">jours</span>
        <span class="chip">${esc(T('TtgCount'))}</span>${T('TtgCollection') ? `<span class="chip accent">${esc(T('TtgCollection'))}</span>` : ''}</div>
      <div class="split">
        ${table('TtgGrid', [
          { h: 'No.', k: 'Number', num: true, f: r => `<span class="muted">${r.Number == null ? '' : String(r.Number).padStart(3, '0')}</span>` },
          { h: '', f: r => itemImg(r.ItemId) },
          { h: 'Carte', k: 'Name', f: r => `<span class="strong">${esc(r.Name)}</span>` },
          { h: 'Rareté', k: 'Rarity' },
          { h: 'Poss.', k: 'Owned', num: true, f: r => `<span class="strong ${r.Owned > 0 ? 'ok' : 'muted'}">${r.Owned}</span>` },
          { h: 'Marchand', k: 'MerchantPrice', num: true, f: r => `<span class="acc strong">${fmt(r.MerchantPrice)}</span>` },
          { h: S.TtgBestColumn?.t || 'Meilleur', k: 'BestPrice', num: true, f: r => fmt(r.BestPrice) },
          { h: 'Chez', k: 'BestMerchant', f: r => `<span class="strong">${esc(r.BestMerchant)}</span>` },
          { h: 'Position', k: 'BestWhere', f: r => `<span class="muted ellipsis" title="${esc(r.BestWhere)}">${esc(r.BestWhere)}</span>` },
          { h: 'Vu', k: 'SeenAt', f: r => `<span class="muted">${esc(r.Seen)}</span>` },
        ], { empty: T('TtgEmpty') || 'Aucune carte à afficher.' })}
        <aside class="detail glass">${detailHead(sel ? itemImg(sel.ItemId, 'lg') : '', 'TtgDetailTitle', 'TtgDetailSub')}
          ${sel ? `${select('TtgOffersMode')}<h3>${esc(T('TtgOffersTitle'))}</h3>
          ${rows('TtgOffers').map(o => `<div class="card"><div class="filters" style="justify-content:space-between"><b class="acc">${esc(o.Price)}<span class="muted"> · ${o.Quantity} en vente</span></b>
            <span class="muted" style="font-size:11px">${esc(o.Seen)}</span></div><div class="strong">${esc(o.Merchant)}</div>
            ${o.Where ? `<div class="muted" style="font-size:11.5px">${esc(o.Where)}</div>` : ''}</div>`).join('') || '<div class="muted">Aucune offre.</div>'}
          <h3>Prix chez les marchands</h3><div class="card">${chart(S.TtgMerchantChart?.pts)}</div>
          <h3>Prix unitaire à l'HDV</h3><div class="card">${chart(S.TtgHdvChart?.pts)}</div>
          <button class="ghost" data-act="click" data-name="TtgForget">Oublier les prix marchands</button>` : ''}</aside>
      </div>`;
  },

  pda() {
    const sel = rows('PdaGrid')[S.PdaGrid?.sel ?? -1];
    return `<div class="filters">${input('PdaSearch', 'Monstre ou race…', '220px')}<span class="lbl">Afficher</span>${select('PdaSource')}
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

  fm() {
    return `<div class="filters"><span class="lbl">Objet</span>${select('FmItemCombo', 'style="min-width:340px"')}
        <span class="muted">Le reliquat est une estimation : corrigez-le si besoin.</span></div>
      <div class="split fm">
        <aside class="detail glass">
          <h2 class="acc">${esc(T('FmItemName'))}</h2>
          <div class="card"><h3>Reliquat estimé</h3><div class="huge">${esc(T('FmReliquat'))}</div>
            <div class="filters">${input('FmReliquatInput', 'Valeur', '90px')}<button class="small" data-act="click" data-name="FmSetReliquat">Corriger</button></div></div>
          <div class="filters"><button class="small" data-act="click" data-name="FmClear">Effacer l'historique</button>
            <button class="small" data-act="click" data-name="FmForget">Oublier l'objet</button>
            <button class="small" data-act="click" data-name="FmWeights">Poids des stats</button></div>
          <h3>Caractéristiques actuelles</h3><div class="stats">${rows('FmStats').map(s => `<span class="chip">${esc(s)}</span>`).join('') || '<span class="muted">—</span>'}</div>
        </aside>
        ${table('FmHistoryGrid', [
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
    const hotkey = (name, label) => `<span class="lbl">${label}</span><input type="text" class="hotkey" readonly data-hotkey="${name}"
      value="${esc(T(name))}" placeholder="aucun" title="Cliquez puis appuyez sur la combinaison voulue (Suppr : aucun raccourci)">`;
    return `<div class="filters">${check('OrganizerEnabled', 'Switcheur activé')}</div>
      <div class="filters">${hotkey('HotkeyNext', 'Personnage suivant')}${hotkey('HotkeyPrevious', 'Précédent')}${hotkey('HotkeyMain', 'Principal ★')}
        <span class="lbl">Anti-rebond (ms)</span>${input('HotkeyDebounce', '', '70px')}
        <span class="muted">Les raccourcis marchent aussi en jeu ; ils ne font qu'afficher la fenêtre.</span>
        ${T('HotkeyStatus') ? `<span class="bad strong">${esc(T('HotkeyStatus'))}</span>` : ''}</div>
      <div class="filters card overlay-opts">${check('OverlayEnabled', 'Superposition en jeu')}${check('OverlayEconome', 'Économe')}
        ${check('OverlayGroups', 'Groupes de la map')}${check('OverlayRotation', 'Ordre de jeu')}
        <label class="volume">Opacité<input type="range" data-set="OverlayOpacity" data-fk="OverlayOpacity" min="${S.OverlayOpacity?.min ?? 40}"
          max="${S.OverlayOpacity?.max ?? 100}" step="1" value="${S.OverlayOpacity?.val ?? 92}"
          style="--p:${(((S.OverlayOpacity?.val ?? 92) - 40) / 60 * 100).toFixed(0)}%"><span class="muted">${esc(T('OverlayOpacityText'))}</span></label>
        ${btn('OverlayMove', 'Déplacer', 'small')}</div>
      <div class="filters"><span class="lbl">Notifier</span>${check('NotifyFight', 'Début de combat')}${check('NotifyTurn', 'Début de votre tour')}
        ${check('NotifyExchange', "Demande d'échange")}${check('NotifyParty', 'Invitation de groupe')}
        <span class="lbl">par</span>${check('NotifyToast', 'Bulle')}${check('NotifyFlash', 'Clignotement')}${check('NotifySound', 'Son')}${check('NotifyAutoSwitch', 'Basculer sur la fenêtre invitée')}
        ${check('NotifyAutoSwitchTurn', 'Basculer au début du tour')}${check('OrganizerLockOrder', "Figer l'ordre à l'entrée en groupe")}</div>
      <div class="filters"><span class="muted">Ordre de jeu : par initiative, ou à votre goût en glissant-déposant les lignes.</span>
        <span class="strong" style="color:${S.OrganizerOrderStatus?.c || 'inherit'}">${esc(T('OrganizerOrderStatus'))}</span>
        ${btn('OrganizerSaveOrder', "Enregistrer l'ordre", 'small primary')}${btn('OrganizerResetOrder', 'Ordre par initiative', 'small')}
        ${T('OrganizerCount') ? `<span class="chip accent">${esc(T('OrganizerCount'))}</span>` : ''}</div>
      ${table('OrganizerGrid', [
        { h: 'Ordre', k: 'Order', num: true, f: r => `<span class="order ${r.Excluded ? 'muted' : ''}">${esc(r.OrderText)}</span>` },
        { h: '★', k: 'Main', f: r => `<button class="star ${r.Main ? 'on' : ''}" title="Personnage principal (raccourci « Principal »)" ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerMain_Click' })}>${r.Main ? '★' : '☆'}</button>` },
        { h: 'Personnage', k: 'Name', f: r => `<span class="strong ${r.Active ? 'acc' : ''}">${esc(r.Name)}</span>${r.Active ? ' <span class="chip accent">au premier plan</span>' : ''}` },
        { h: 'Niveau', k: 'Level', num: true },
        { h: 'Initiative', k: 'Initiative', num: true, f: r => `<span class="acc strong">${esc(r.InitiativeText)}</span>` },
        { h: 'État', k: 'State', f: r => `<span class="muted">${esc(r.State)}</span>` },
        { h: 'Exclure', k: 'Excluded', f: r => `<label class="check"><input type="checkbox" ${r.Excluded ? 'checked' : ''} ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerExclude_Click' })}></label>` },
        { h: '', f: r => `<button class="small primary" ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerShow_Click' })}>Afficher</button>` },
      ], { select: false, draggable: r => !r.Excluded && !r.Name.startsWith('('), empty: 'Aucune fenêtre Dofus Retro ouverte. Lancez le jeu : ses fenêtres apparaîtront ici.' })}`;
  },

  arene() {
    const challenges = rows('AreneChallenges');
    const state = c => c.State === 1 ? 'ok' : c.State === -1 ? 'bad' : 'warn';
    return `<div class="card chrono">
        <div class="chrono-clock"><span class="lbl">Chronomètre</span><div class="time acc">${esc(T('ChronoTime'))}</div>
          <div class="filters">${btn('ChronoStart', '▶ Démarrer', 'primary')}${btn('ChronoReset', 'Remettre à zéro')}</div></div>
        <div class="chrono-stats"><div class="strong">${esc(T('ChronoFights'))}</div><div class="strong acc">${esc(T('ChronoGains'))}</div>
          <div class="muted">${esc(T('ChronoRate'))}</div>
          <div class="muted small">Compte les fins de combat de tous vos personnages pendant que le chronomètre tourne (un combat à plusieurs personnages compte une fois). Gains : kamas + drops au prix HDV (sinon prix marchand).</div></div>
      </div>
      <div class="split wide">
        ${table('ChronoGrid', [
          { h: 'Fin', k: 'TimeSort', f: r => esc(r.Time) },
          { h: 'Personnage(s)', k: 'Characters' },
          { h: 'Monstres', k: 'Monsters', f: r => `<span class="ellipsis" title="${esc(r.Monsters)}">${esc(r.Monsters)}</span>` },
          { h: 'XP', k: 'Xp', num: true, f: r => fmt(r.Xp) },
          { h: 'Kamas', k: 'Kamas', num: true, f: r => fmt(r.Kamas) },
          { h: 'Drops', k: 'DropValue', num: true, f: r => fmt(r.DropValue) },
          { h: 'Total', k: 'Total', num: true, f: r => `<span class="acc strong">${fmt(r.Total)}</span>` },
        ], { select: false, empty: 'Aucun combat compté : démarrez le chronomètre, chaque fin de combat sera ajoutée ici.' })}
        <aside class="glass detail">
          <h2>Challenges Économe et Barbare</h2>
          <div class="sub">Annoncés par le jeu au début du combat ; leur état est suivi d'après les sorts et attaques de vos personnages. Le jeu donne le résultat final.</div>
          ${challenges.length ? challenges.map(c => `<div class="card challenge ${c.State === -1 ? 'failed' : ''}">
              <div class="row"><b>${esc(c.Name)}</b>${c.Announced ? '<span class="chip">annoncé</span>' : ''}<span class="muted right">${esc(c.Character)}</span></div>
              <div class="muted small">${esc(c.Rule)}</div>
              <div class="strong ${state(c)}">${esc(c.Status)}</div></div>`).join('')
            : `<div class="empty">Entrez en combat : les challenges de vos personnages s'afficheront ici.</div>`}
          ${T('AreneOtherChallenges') ? `<div class="muted">${esc(T('AreneOtherChallenges'))}</div>` : ''}
        </aside>
      </div>`;
  },

  carte() {
    const D = S._carte;
    if (!D) return `<div class="empty">Chargement de la carte…</div>`;
    if (!carte.shown) carte.shown = new Set(D.shown);
    const list = carte.list = carteHints(D);
    const groups = {};
    list.forEach(h => { const n = D.cats.find(c => c.k === h.k)?.n || 'Divers'; groups[n] = (groups[n] || 0) + 1; });
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
      <div class="split wide">
        <div id="carte-host" class="carte-host"></div>
        <aside class="glass detail carte-side">
          <input type="search" data-carte="search" data-fk="carteSearch" value="${esc(carte.search)}" placeholder="Zaap, Temple Iop, Bouftou, [4,-18]…">
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
      ${itemImg(g.ItemId)}<div><div class="name">${esc(g.Name)}</div>${g.Note ? `<div class="note">${esc(g.Note)}</div>` : ''}<div class="price">${esc(g.Price)}</div></div>
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
const carte = { el: null, built: null, sa: 0, s: 0.2, ox: 0, oy: 0, fitted: false, grid: true, search: '', shown: null,
  sel: null, list: [], bounds: null, drag: null };

const norm = s => String(s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

function carteHints(D) {
  const q = norm(carte.search.trim());
  return D.hints.filter(h => h.sa === carte.sa && (q
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
  // Clic sans déplacement sur un marqueur : le point est choisi dans la liste
  const m = e.target.closest?.('.carte-marker[data-i]');
  if (m) carteSelect(carte.list[+m.dataset.i], false);
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
        <i></i><span>${esc(p.n)}</span></div>`).join('');
  }
  if (!carte.fitted) requestAnimationFrame(carteFit); else carteView();
}

let pending = false;
function render() {
  pending = false;
  document.documentElement.dataset.theme = (S._theme || 'Crepuscule').toLowerCase();
  document.documentElement.dataset.style = (S._style || 'Moderne').toLowerCase();
  const active = S.MainTabs?.i ?? 0;
  const tab = TABS[active] || TABS[0];

  // Garder la saisie en cours et le défilement des tableaux pendant le rendu
  const focus = document.activeElement?.dataset?.fk ? { key: document.activeElement.dataset.fk, value: document.activeElement.value,
    start: document.activeElement.selectionStart, end: document.activeElement.selectionEnd } : null;
  const scroll = [...document.querySelectorAll('.table-wrap, .detail, .modal-body')].map(e => e.scrollTop);

  document.getElementById('app').innerHTML = header() + banner() + tabs(active)
    + `<main class="glass">${views[tab.key]()}</main>` + changelog();

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
  schedule();
});

document.addEventListener('click', e => {
  const a = e.target.closest('[data-act]');
  if (!a) return;
  const d = a.dataset;
  switch (d.act) {
    case 'click': post({ type: 'click', name: d.name }); break;
    case 'tab': post({ type: 'set', name: 'MainTabs', value: +d.i }); S.MainTabs = { i: +d.i }; schedule(); break;
    case 'ui': post({ type: 'ui' }); break;
    case 'changelog': local.changelog = true; schedule(); break;
    case 'changelog-close': local.changelog = false; schedule(); break;
    case 'carte-cat': {
      const k = +d.k;
      if (carte.shown.has(k)) carte.shown.delete(k); else carte.shown.add(k);
      schedule();
      break;
    }
    case 'carte-go': carteSelect(carte.list[+d.i]); break;
    case 'carte-fit': carteFit(); break;
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
  if (e.key === 'Escape' && local.changelog) { local.changelog = false; schedule(); return; }
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
  if (el.dataset?.carte === 'sa') { carte.sa = +el.value; carte.sel = null; schedule(); return; }
  if (el.dataset?.carte === 'grid') { carte.grid = el.checked; carteView(); return; }
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
