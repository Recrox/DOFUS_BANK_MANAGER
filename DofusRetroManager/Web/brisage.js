'use strict';
// Onglet Brisage

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
          { h: 'Objet', k: 'Name', f: r => `<a class="strong item-link brisage-name" data-act="item-recipe" data-id="${r.ItemId}" title="${esc(r.Name)} — voir la recette">${esc(r.Name)}</a><div class="muted brisage-name">${esc(r.Type)} · niv. ${r.Level}</div>` },
          // Caractéristiques en entier (sur plusieurs lignes si besoin) ; lieu au survol du nombre possédé ; runes sous leur valeur
          // Caractéristiques sur toute la largeur libre de la colonne : moins de lignes, donc des lignes moins hautes
          { h: 'Caractéristiques', k: 'Score', f: r => `<span class="stat-chips wide">${(r.Chips || []).map(c => `<span class="stat-chip ${c.On ? 'on' : ''} ${c.Neg ? 'neg' : ''}" title="${esc(c.Tip)}">${c.Rune ? itemImg(c.Rune, 'xs') : ''}<i>${esc(c.Short)}</i>${esc(c.Value)}</span>`).join('')}</span>` },
          { h: 'Fais.', k: 'Craftable', num: true, f: r => r.Craftable > 0 ? `<span class="ok strong" title="Faisable ${r.Craftable} fois avec vos ressources">×${r.Craftable}</span>` : r.Missing ? `<span class="bad strong" title="Manque : ${esc(r.Missing)}">✗</span>` : '' },
          { h: 'Prix', k: 'HdvPrice', num: true, f: r => `<span class="acc strong" title="Prix HDV">${fmt(r.HdvPrice)}</span><div class="muted small" title="Coût de fabrication (ingrédients au prix HDV)">${r.CraftCost != null ? '⚒ ' + fmt(r.CraftCost) : ''}</div>` },
          { h: 'Runes ≈', k: 'RunesValue', num: true, f: r => `<span class="strong" title="${esc(r.RunesText)}">${fmt(r.RunesValue)}</span>` },
          { h: 'Gain', k: 'Gain', num: true, f: r => r.Gain == null ? '—' : `<span title="${esc(S.BrisageGainColumn?.t || 'Gain')} : valeur des runes moins le prix de référence" class="strong ${r.GainSign < 0 ? 'bad' : 'ok'}">${r.Gain > 0 ? '+' : ''}${fmt(r.Gain)}</span>` },
          { h: 'Gain %', k: 'GainPercent', num: true, f: r => r.GainPercent == null ? '—' : `<span title="Gain rapporté au prix de référence (${r.Reference == null ? '—' : fmt(r.Reference) + ' k'})" class="strong ${r.GainSign < 0 ? 'bad' : 'ok'}">${pct(r.GainPercent)}</span>` },
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
});
