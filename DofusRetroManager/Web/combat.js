'use strict';
// Onglet Combat, et son chronomètre (ancien onglet Arène)

// Chronomètre de rentabilité et challenges (ancien onglet Arène), en bandeau compact de l'onglet Combat
function chronoBlock() {
  const challenges = rows('AreneChallenges');
  const state = c => c.State === 1 ? 'ok' : c.State === -1 ? 'bad' : 'warn';
  const open = local.open.chrono;
  return `<div class="glass card chrono">
      <span class="time acc" title="Chronomètre : compte les fins de combat des personnages choisis (un combat à plusieurs compte une fois)">⏱ ${esc(T('ChronoTime'))}</span>
      ${btn('ChronoStart', '▶ Démarrer', 'small primary')}${btn('ChronoReset', 'Remettre à zéro', 'small')}
      <span title="Personnages comptés">${select('AreneGroup')}</span><label class="icheck" data-tip="Gérer les groupes de personnages comptés"><input type="checkbox" data-set="AreneGroupEdit" ${S.AreneGroupEdit?.on ? 'checked' : ''}><span>👥</span></label>
      <div class="chrono-stats" title="Gains : kamas + drops au prix HDV (sinon prix marchand)">
        <b>${esc(T('ChronoFights'))}</b><b class="acc">${esc(T('ChronoGains'))}</b><span class="muted ellipsis" title="${esc(T('ChronoRate'))}">${esc(T('ChronoRate'))}</span></div>
      <div class="chrono-chal">${challenges.map(c => `<span class="chall ${state(c)}" data-tip="${esc(c.Name + ' · ' + c.Status + ' · ' + c.Character + (c.Rule ? ' — ' + c.Rule : '') + (c.Announced ? ' (annoncé par le jeu)' : ''))}">
          ${c.Gfx >= 0 ? `<img src="icon/chall/${c.Gfx}.png" alt="">` : ''}<span class="fallback">${esc(c.Name)}</span></span>`).join('')}
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

Object.assign(views, {
  combat() {
    const spells = rows('EconomeSpells').map(s => `<div class="spell ${s.State === 0 ? 'used' : s.State < 0 ? 'twice' : ''}" title="${esc(s.Name + ' — ' + s.Status)}">
      <img src="icon/spell/${s.Id}.png" alt=""><span class="fallback">${esc(s.Name)}</span></div>`).join('');
    const char = S.HuntCharacterBox;
    // Caractéristiques en icônes du jeu (tête du personnage, sagesse et prospection de sa fiche) ; le libellé et le détail au survol
    const head = Number(T('HuntIcons').split('|')[0]);
    const headImg = head ? `<img class="ico" src="icon/head/${head}.png" alt="">` : '<span class="glyph">Niv</span>';
    const group = img => `<span class="grp">${img}<i>👥</i></span>`;
    const ist = (img, name, label, cls = '') => `<div class="istat" data-tip="${esc(label + (S[name]?.tt ? ' — ' + S[name].tt : ''))}">${img}<b class="${cls}">${esc(T(name))}</b></div>`;
    const statImg = n => `<img class="ico" src="icon/stat/${n}.png" alt="">`;
    const saImg = statImg('Wisdom'), ppImg = statImg('PP');
    // Cases et boutons en icônes : le texte au survol
    const icheck = (name, icon, label) => `<label class="icheck" data-tip="${esc(S[name]?.t || label)}"><input type="checkbox" data-set="${name}" ${S[name]?.on ? 'checked' : ''}><span>${icon}</span></label>`;
    const ibtn = (name, icon, label) => `<button class="small ibtn" data-act="click" data-name="${name}" data-tip="${esc(label)}">${icon}</button>`;
    const pause = T('HuntPauseSession') || '⏸ Pause';
    return `<div class="stat-row">
        ${char?.items?.length ? `<span data-tip="Personnage affiché">${select('HuntCharacterBox')}</span>` : ''}
        ${ist(headImg, 'HuntLevel', 'Niveau')}${ist(group(headImg), 'HuntGroupLevel', 'Niveau du groupe', 'acc')}
        ${ist(saImg, 'HuntWisdom', 'Sagesse')}${ist(ppImg, 'HuntProspection', 'Prospection')}${ist(group(ppImg), 'HuntGroupProspection', 'Prospection du groupe', 'acc')}
        <span class="sep"></span>
        ${icheck('HuntSound', '🔔', 'Alerte sonore archi-monstre')}
        <label class="volume" title="Volume de l'alerte archi-monstre">🔊<input type="range" data-set="HuntVolume" data-fk="HuntVolume"
          min="${S.HuntVolume?.min ?? 0}" max="${S.HuntVolume?.max ?? 100}" step="1" value="${S.HuntVolume?.val ?? 70}"
          style="--p:${S.HuntVolume?.val ?? 70}%"><span class="muted">${esc(T('HuntVolumeText'))}</span></label>
        ${ibtn('HuntTestSound', '▶', 'Tester le son (et la bulle archi-monstre)')}
        ${icheck('HuntArchiPopup', '💬', 'Pop-up archi-monstre (bulle jusqu’à sa croix)')}${icheck('HuntAlertsToggle', '🎯', 'Alertes monstres')}
        <span class="sep"></span>
        ${ibtn('HuntNewSession', '↺', 'Nouvelle session : remet à zéro la rentabilité')}
        ${ibtn('HuntPauseSession', esc(pause.split(' ')[0]), pause.slice(pause.indexOf(' ') + 1) + ' : le temps ne compte plus dans la rentabilité, un combat la relance')}
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
              // Colonne étroite : kamas + drops en un seul gain, le détail au survol
              { h: 'Gain', k: 'DropValue', num: true, f: r => `<span class="acc" data-tip="${esc(`Kamas : ${fmt(r.Kamas)} · Drops : ${fmt(r.DropValue)}${r.Drops ? ' — ' + r.Drops : ''}`)}">${fmt((r.Kamas || 0) + (r.DropValue || 0))}</span>` },
            ], { empty: 'Aucun combat sur cette session.' })}</div>
          <div class="mini"><p class="section-title">${esc(T('HuntDropsTitle'))}</p>
            ${table('HuntDropGrid', [
              { h: '', f: r => itemImg(r.ItemId, 'sm') },
              { h: 'Objet', k: 'Name', f: r => `<span class="strong ellipsis" title="${esc(r.Name)}">${esc(r.Name)}</span>` },
              { h: 'Qté', k: 'Quantity', num: true, f: r => fmt(r.Quantity) },
              { h: 'Total', k: 'Total', num: true, f: r => `<span class="acc strong" data-tip="${esc('Prix unitaire : ' + fmt(r.Unit) + ' k')}">${fmt(r.Total)}</span>` },
            ], { select: false, empty: 'Aucun objet obtenu.' })}</div>
        </div>
      </div>`;
  },
});
