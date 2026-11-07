'use strict';
// Onglet Recettes

Object.assign(views, {
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
