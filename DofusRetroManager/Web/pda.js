'use strict';
// Onglet PDA (pierres d'âme)

Object.assign(views, {
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
});
