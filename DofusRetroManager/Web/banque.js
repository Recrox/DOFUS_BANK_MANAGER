'use strict';
// Onglet Banque

Object.assign(views, {
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
});
