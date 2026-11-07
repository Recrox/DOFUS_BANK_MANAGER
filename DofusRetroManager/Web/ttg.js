'use strict';
// Onglet TTG, et le classeur

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

Object.assign(views, {
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
});
