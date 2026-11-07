'use strict';
// Onglet Ocre (quête du Dofus Ocre)

Object.assign(views, {
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
});
