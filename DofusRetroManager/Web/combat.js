'use strict';
// Onglet Combat : en-tête (personnage, caractéristiques, session, réglages), réglages repliables, chronomètre,
// puis à gauche l'Économe, les groupes de la map et la rentabilité (session, historique), à droite les listes.

// Chronomètre de rentabilité et challenges (ancien onglet Arène), en bandeau compact
function chronoBlock() {
  const challenges = rows('AreneChallenges');
  const state = c => c.State === 1 ? 'ok' : c.State === -1 ? 'bad' : 'warn';
  const open = local.open.chrono;
  return `<div class="glass card chrono">
      <span class="time acc" title="Chronomètre : compte les fins de combat des personnages choisis (un combat à plusieurs compte une fois)">⏱ ${esc(T('ChronoTime'))}</span>
      ${btn('ChronoStart', '▶ Démarrer', 'small primary')}${btn('ChronoReset', 'Remettre à zéro', 'small')}
      <span title="Personnages comptés (groupes : ⚙ Réglages)">${select('AreneGroup')}</span>
      <div class="chrono-stats" title="Gains : kamas + drops au prix HDV (sinon prix marchand)">
        <b class="ellipsis" title="${esc(T('ChronoFights'))}">${esc(T('ChronoFights'))}</b><b class="acc ellipsis" title="${esc(T('ChronoGains'))}">${esc(T('ChronoGains'))}</b>
        <span class="muted ellipsis" title="${esc(T('ChronoRate'))}">${esc(T('ChronoRate'))}</span></div>
      <div class="chrono-chal">${challenges.map(c => `<span class="chall ${state(c)}" data-tip="${esc(c.Name + ' · ' + c.Status + ' · ' + c.Character + (c.Rule ? ' — ' + c.Rule : '') + (c.Announced ? ' (annoncé par le jeu)' : ''))}">
          ${c.Gfx >= 0 ? `<img src="icon/chall/${c.Gfx}.png" alt="">` : ''}<span class="fallback">${esc(c.Name)}</span></span>`).join('')}
        ${T('AreneOtherChallenges') ? `<span class="muted ellipsis" title="${esc(T('AreneOtherChallenges'))}">${esc(T('AreneOtherChallenges'))}</span>` : ''}</div>
      <button class="small" data-act="open" data-key="chrono" title="Combats comptés par le chronomètre">Combats ${open ? '▾' : '▸'}</button>
    </div>
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

// Réglages de l'onglet, regroupés dans un panneau repliable (⚙) : alertes archi-monstres, alertes monstres, groupes
function combatSettings() {
  const alerts = `<div class="filters" title="Monstre, nombre minimum dans le groupe, niveaux">${input('HuntAlertMonster', 'Monstre (ex. Craqueleur des plaines)', '170px')}
      <span class="lbl">≥</span>${input('HuntAlertCount', '', '40px')}<span class="lbl">niv.</span>${input('HuntAlertMin', '', '46px')}
      <span class="lbl">à</span>${input('HuntAlertMax', '', '46px')}
      <button class="small primary" data-act="click" data-name="HuntAlertAdd" title="Prévenir (son et bulle) quand un groupe de la map contient au moins ce nombre de ce monstre, à ces niveaux">Ajouter</button>
      <button class="small" data-act="click" data-name="HuntAlertTest">Tester</button></div>
    ${T('HuntAlertStatus') ? `<div class="muted small">${esc(T('HuntAlertStatus'))}</div>` : ''}
    <div class="filters">${rows('HuntAlertList').map((a, i) => `<span class="chip ${a.Enabled ? 'on' : 'dim'}">
      <a class="item-link" ${attrs({ act: 'row', grid: 'HuntAlertList', i, handler: 'HuntAlertToggle_Click' })} title="Activer ou couper cette alerte">${a.Enabled ? '🔔' : '🔕'}</a>
      ${esc(a.Text)} <a class="item-link" ${attrs({ act: 'row', grid: 'HuntAlertList', i, handler: 'HuntAlertDelete_Click' })} title="Supprimer">✕</a></span>`).join('') || '<span class="muted">Aucune alerte.</span>'}</div>`;
  const groups = S.AreneGroupEdit?.on
    ? `<div class="filters"><span class="lbl">Nom</span>${input('AreneGroupName', 'Groupe principal', '160px')}
        <button class="small primary" data-act="click" data-name="AreneGroupSave">Enregistrer</button>
        <button class="small" data-act="click" data-name="AreneGroupDelete">Supprimer</button></div>
      ${T('AreneGroupStatus') ? `<div class="muted small">${esc(T('AreneGroupStatus'))}</div>` : ''}
      <div class="filters">${(S.AreneGroupMembers?.items || []).map((m, i) => `<button class="pill ${m.on ? 'on' : ''}" data-act="toggle" data-name="AreneGroupMembers" data-i="${i}">${esc(m.t)}</button>`).join('') || '<span class="muted">Aucun personnage connu.</span>'}</div>`
    : `<div class="muted small">Les personnages comptés par le chronomètre : tous, ou un groupe enregistré.</div>`;
  return `<div class="glass card combat-settings">
      <section><p class="section-title">🔔 Archi-monstres</p>
        <div class="filters">${check('HuntSound', 'Son')}${check('HuntArchiPopup', 'Pop-up')}
          <button class="small ibtn" data-act="click" data-name="HuntTestSound" data-tip="Tester le son (et la bulle archi-monstre)">▶</button></div>
        <label class="volume" title="Volume des alertes (archi-monstres, alertes monstres, Switcheur)">🔊<input type="range" data-set="HuntVolume" data-fk="HuntVolume"
          min="${S.HuntVolume?.min ?? 0}" max="${S.HuntVolume?.max ?? 100}" step="1" value="${S.HuntVolume?.val ?? 70}"
          style="--p:${S.HuntVolume?.val ?? 70}%"><span class="muted">${esc(T('HuntVolumeText'))}</span></label></section>
      <section><p class="section-title">🎯 ${esc(T('HuntAlertsToggle') || 'Alertes monstres')}</p>${alerts}</section>
      <section><p class="section-title" style="display:flex;justify-content:space-between;align-items:center">👥 Groupes du chronomètre
        <label class="check small"><input type="checkbox" data-set="AreneGroupEdit" ${S.AreneGroupEdit?.on ? 'checked' : ''}>Modifier</label></p>${groups}</section>
    </div>`;
}

// Rentabilité : session en cours (et projection), ou historique de toutes les sessions
function sessionCard() {
  const hist = local.open.sessHist;
  const n = rows('HuntSessionGrid').filter(r => !r.Current).length;
  const tabsHtml = `<div class="seg"><button class="pill ${hist ? '' : 'on'}" data-act="${hist ? 'open' : ''}" data-key="sessHist">Session</button>
    <button class="pill ${hist ? 'on' : ''}" data-act="${hist ? '' : 'open'}" data-key="sessHist">Historique (${n})</button></div>`;
  if (hist) return `<div class="glass card session"><div class="bar"><p class="section-title">Rentabilité</p>${tabsHtml}</div>
    <div class="session-hist">${table('HuntSessionGrid', [
      { h: 'Début', k: 'StartSort', f: r => `<span class="${r.Current ? 'acc strong' : ''}" data-tip="${esc(r.Tip)}">${esc(r.Date)}${r.Rebuilt ? ' ≈' : ''}</span>` },
      { h: 'Durée', k: 'Minutes', num: true, f: r => esc(r.Duration) },
      { h: 'Combats', k: 'Fights', num: true, f: r => `<span data-tip="${esc(r.Characters + ' personnage(s)')}">${fmt(r.Fights)}</span>` },
      { h: 'XP', k: 'Xp', num: true, f: r => fmt(r.Xp) },
      // Kamas + drops : le détail au survol (tableau étroit)
      { h: 'Total', k: 'Total', num: true, f: r => `<span class="acc strong" data-tip="${esc('Kamas : ' + fmt(r.Kamas) + ' · Drops : ' + fmt(r.Drops))}">${fmt(r.Total)}</span>` },
      { h: '/ h', k: 'PerHour', num: true, f: r => `<span data-tip="${esc('XP / h : ' + fmt(r.XpPerHour))}">${fmt(r.PerHour)}</span>` },
      { h: '', f: r => r.Current ? '' : `<a class="item-link muted" ${attrs({ act: 'row', grid: 'HuntSessionGrid', i: r._i, handler: 'HuntSessionForget_Click' })} title="Oublier cette session">✕</a>` },
    ], { select: false, empty: 'Aucune session : chaque fin de combat s\'ajoute à la session en cours.' })}</div>
    <div class="muted small">Tous les personnages · drops au prix HDV actuel · détail par personnage au survol de la date · ≈ : reconstituée (pauses inconnues).</div></div>`;
  return `<div class="glass card session"><div class="bar"><p class="section-title">Rentabilité</p>${tabsHtml}</div>
    <div class="lines">${esc(T('HuntSession'))}</div>
    ${rows('HuntProjection').length ? `<table class="proj"><thead><tr><th>Sur</th><th class="num">XP</th><th class="num">Kamas</th><th class="num">Drops (HDV)</th><th class="num">Total kamas</th></tr></thead><tbody>
      ${rows('HuntProjection').map(p => `<tr><td class="strong">${esc(p.Span)}</td><td class="num">${fmt(p.Xp)}</td><td class="num">${fmt(p.Kamas)}</td><td class="num">${fmt(p.Drops)}</td><td class="num acc strong">${fmt(p.Total)}</td></tr>`).join('')}</tbody></table>` : ''}
    <div class="muted small" style="margin-top:6px">${esc(T('HuntLearned'))}</div></div>`;
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
    const ibtn = (name, icon, label) => `<button class="small ibtn" data-act="click" data-name="${name}" data-tip="${esc(label)}">${icon}</button>`;
    const pause = T('HuntPauseSession') || '⏸ Pause';
    const settings = local.open.combatSettings;
    // État des alertes, rappelé sur le bouton des réglages (son coupé, pop-up, alertes monstres actives)
    const flags = (S.HuntSound?.on ? '🔔' : '🔕') + (S.HuntArchiPopup?.on ? '💬' : '') + (rows('HuntAlertList').some(a => a.Enabled) ? '🎯' : '');
    return `<div class="combat-top">
        <div class="stat-row">
          ${char?.items?.length ? `<span data-tip="Personnage affiché">${select('HuntCharacterBox')}</span>` : ''}
          ${ist(headImg, 'HuntLevel', 'Niveau')}${ist(group(headImg), 'HuntGroupLevel', 'Niveau du groupe', 'acc')}
          ${ist(saImg, 'HuntWisdom', 'Sagesse')}${ist(ppImg, 'HuntProspection', 'Prospection')}${ist(group(ppImg), 'HuntGroupProspection', 'Prospection du groupe', 'acc')}
        </div>
        <div class="combat-actions">
          <span class="lbl">Session</span>
          ${ibtn('HuntNewSession', '↺', 'Nouvelle session : la session en cours passe dans l\'historique')}
          ${ibtn('HuntPauseSession', esc(pause.split(' ')[0]), pause.slice(pause.indexOf(' ') + 1) + ' : le temps ne compte plus dans la rentabilité, un combat la relance')}
          <button class="small ${settings ? 'primary' : ''}" data-act="open" data-key="combatSettings" data-tip="Alertes archi-monstres, alertes monstres, groupes du chronomètre">⚙ Réglages <span class="flags">${flags}</span></button>
        </div>
      </div>
      ${settings ? combatSettings() : ''}
      ${T('HuntStatus') ? `<div class="alert">${esc(T('HuntStatus'))}</div>` : ''}
      ${chronoBlock()}
      <div class="split wide combat-split">
        <div class="stack">
          <div class="glass econome"><div class="bar"><span class="t"><b class="acc">Économe</b> · ${esc(T('EconomeStatus'))}</span>
            <button class="small" data-act="click" data-name="EconomeReset">Remettre à zéro</button></div><div class="spells">${spells}</div></div>
          <div class="strong"><a class="pos-link" data-act="goto-me" title="Voir sur la carte">${esc(T('HuntMap'))}</a></div>
          ${table('HuntGroupGrid', [
            { h: 'Monstres', k: 'Monsters', f: r => `<span class="strong">${esc(r.Monsters)}</span>` },
            { h: 'Niv.', k: 'TotalLevel', num: true, f: r => `<span class="acc strong">${fmt(r.TotalLevel)}</span>` },
            { h: '★', k: 'Stars' },
            { h: 'XP ≈', k: 'Xp', num: true, f: r => `<span class="strong">${esc(r.XpText)}</span>` },
            { h: 'Vus', k: 'Drops', f: r => `<span class="muted ellipsis" title="${esc(r.Drops)}">${esc(r.Drops)}</span>` },
            { h: 'Drops (jeu)', k: 'OfficialDrops', f: r => r.HasDropDetails
              ? `<button class="caret ${r.Expanded ? 'open' : ''}" ${attrs({ act: 'row', grid: 'HuntGroupGrid', i: r._i, handler: 'HuntDropsToggle_Click' })}>▶</button> <span class="ellipsis">${esc(r.OfficialDrops)}</span>` : '' },
            { h: 'Drops ≈', k: 'DropValue', num: true, f: r => `<span class="acc strong">${fmt(r.DropValue)}</span>` },
            { h: 'Archi', k: 'Archi', f: r => r.Archi ? `<span class="bad strong">${esc(r.Archi)}</span>` : '' },
            { h: 'Pierre', k: 'ArchiPrice', num: true, f: r => fmt(r.ArchiPrice) },
          ], { select: false, empty: 'Aucun groupe de monstres sur la map.', after: (r, n) => r.Expanded && r.DropDetails?.length ? `<tr><td colspan="${n}" class="drops">
              <table><thead><tr><th></th><th>Objet</th><th class="num">Chance</th><th class="num">Monstres</th><th class="num">PP min.</th><th class="num">Max / combat</th><th class="num">Prix u.</th><th class="num">Valeur / combat</th><th>État</th></tr></thead>
              <tbody>${r.DropDetails.map(d => `<tr class="${d.Muted ? 'dim' : ''}"><td>${itemImg(d.ItemId, 'sm')}</td><td class="strong">${esc(d.Name)}</td>
                <td class="num acc strong">${esc(d.Chance)}</td><td class="num">${esc(d.Monsters)}</td><td class="num">${esc(d.Prospection)}</td>
                <td class="num">${esc(d.MaxPerFight)}</td><td class="num">${esc(d.Price)}</td><td class="num strong">${esc(d.Value)}</td><td>${esc(d.State)}</td></tr>`).join('')}</tbody></table></td></tr>` : '' })}
          ${sessionCard()}
        </div>
        <div class="side-tables">
          <div class="mini"><div class="filters" style="justify-content:space-between"><p class="section-title">Derniers archi-monstres vus</p>
            <button class="small" data-act="click" data-name="HuntClearArchis">Vider</button></div>
            ${table('HuntArchiGrid', [
              // Date courte, niveau sous le nom, position réduite à ses coordonnées et sa zone (texte complet au survol)
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
              // Kamas + drops en un seul gain, le détail au survol
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
