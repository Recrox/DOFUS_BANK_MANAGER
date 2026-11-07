'use strict';
// Onglet FM (forgemagie)

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

Object.assign(views, {
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
});
