'use strict';
// Onglet HDV, et « Mes échanges »

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

Object.assign(views, {
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
});
