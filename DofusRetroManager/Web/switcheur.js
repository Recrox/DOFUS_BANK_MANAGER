'use strict';
// Onglet Switcheur (multi-compte)

Object.assign(views, {
  windows() {
    const on = n => S[n]?.on ? 'checked' : '';
    const hotkey = (name, icon, label) => `<label class="org-key"><span class="k-ico">${icon}</span><span>${label}</span>
      <input type="text" class="hotkey" readonly data-hotkey="${name}" value="${esc(T(name))}" placeholder="aucun"
        title="Cliquez puis appuyez sur la combinaison voulue (Suppr : aucun raccourci)"></label>`;
    // Interrupteur avec explication, et pastille cochable (choix multiples)
    const sw = (name, label, desc = '') => `<label class="switch"><input type="checkbox" data-set="${name}" ${on(name)}><span class="track"></span>
      <span class="sw-txt"><b>${esc(label)}</b>${desc ? `<small>${esc(desc)}</small>` : ''}</span></label>`;
    const pill = (name, label) => `<label class="tog"><input type="checkbox" data-set="${name}" ${on(name)}><span>${esc(label)}</span></label>`;
    const enabled = !!S.OrganizerEnabled?.on;
    const opacity = S.OverlayOpacity?.val ?? 92;
    return `<div class="org-head card">${sw('OrganizerEnabled', 'Switcheur activé', 'Raccourcis, notifications et bascules automatiques. Désactivé, la liste reste affichée.')}
        ${T('OrganizerCount') ? `<span class="chip accent">${esc(T('OrganizerCount'))}</span>` : ''}
        <button class="small ghost" data-act="org-settings">${local.orgHidden ? 'Afficher les réglages ▾' : 'Masquer les réglages ▴'}</button></div>
      ${local.orgHidden ? '' : `<div class="org-grid ${enabled ? '' : 'off'}">
        <section class="card org-card"><h3><span class="ico">⌨</span>Raccourcis</h3>
          ${hotkey('HotkeyNext', '→', 'Suivant')}${hotkey('HotkeyPrevious', '←', 'Précédent')}${hotkey('HotkeyMain', '★', 'Principal')}
          <div class="org-key sadida" title="Mode Sadida : affiche le Sadida en arbre (Puissance Sylvestre) dont c'est le tour, celui qui a le moins de temps restant. Passez son tour vous-même : la fenêtre d'avant revient. Sans Sadida en arbre à son tour, rien ne se passe.">${hotkey('HotkeySadida', '🌳', 'Sadida')}<span title="Retour à la fenêtre d'avant après le tour du Sadida">${pill('SadidaReturn', '↩')}</span></div>
          <label class="org-key"><span class="k-ico">⏱</span><span>Anti-rebond</span><span class="unit">${input('HotkeyDebounce', '', '70px')} ms</span></label>
          <div class="org-key click" title="Cliquez dans le jeu en gardant cette touche enfoncée : votre clic arrive au jeu, puis la fenêtre suivante (ordre de jeu) s'affiche. Rien n'est cliqué à votre place.">
            <span class="k-ico">🖱</span><label class="check"><input type="checkbox" data-set="OrganizerClickSwitch" ${on('OrganizerClickSwitch')}>Clic +</label>
            <span class="unit">${select('OrganizerClickKey')}</span>${pill('OrganizerClickLeft', 'Gauche')}${pill('OrganizerClickRight', 'Droit')}<span class="unit">: suivant</span></div>
          <div title="Si Windows rend aussitôt la main à la fenêtre quittée, la fenêtre demandée est réaffichée. Rien n'est cliqué ni tapé à votre place.">${check('OrganizerFixBounce', 'Anti-rebond fenêtre')}</div>
          ${T('HotkeyStatus') ? `<div class="bad strong small">${esc(T('HotkeyStatus'))}</div>` : ''}
          <small class="muted">Ils marchent aussi en jeu ; ils ne font qu'afficher la fenêtre.</small></section>
        <section class="card org-card"><h3><span class="ico">🔔</span>Notifications</h3>
          <div class="sub">Quand</div><div class="togs">${pill('NotifyFight', 'Début de combat')}${pill('NotifyTurn', 'Début de votre tour')}
            ${pill('NotifyExchange', "Demande d'échange")}${pill('NotifyParty', 'Invitation de groupe')}</div>
          <div class="sub">Comment</div><div class="togs">${pill('NotifyToast', 'Bulle')}${pill('NotifyFlash', 'Clignotement')}${pill('NotifySound', 'Son')}</div>
          <small class="muted">Rien n'est notifié pour la fenêtre déjà au premier plan.</small></section>
        <section class="card org-card"><h3><span class="ico">⇄</span>Bascules automatiques</h3>
          ${sw('NotifyAutoSwitch', 'Sur la fenêtre invitée', "Invitation de groupe ou demande d'échange : vous n'avez plus qu'à accepter.")}
          ${sw('NotifyAutoSwitchTurn', 'Au début du tour', 'En combat, la fenêtre du personnage qui doit jouer.')}
          ${sw('OrganizerSplitMode', 'Mode split', 'Fenêtres côte à côte : pas de bascule au début du tour, la ligne du personnage clignote dans la superposition avec le temps du tour.')}
          ${sw('OrganizerMaximize', 'Agrandir au démarrage', 'Chaque nouvelle fenêtre du jeu est agrandie dès qu’elle apparaît.')}
          ${sw('OrganizerLockOrder', "Figer l'ordre en groupe", "L'ordre de jeu est enregistré à l'entrée en groupe.")}</section>
        <section class="card org-card"><h3><span class="ico">▣</span>Superposition en jeu</h3>
          ${sw('OverlayEnabled', 'Afficher au-dessus du jeu', 'Laisse passer les clics vers le jeu, sauf sur l’ordre de jeu : un clic sur un personnage affiche sa fenêtre.')}
          <div class="sub">Blocs</div><div class="togs">${pill('OverlayEconome', 'Challenges')}${pill('OverlayGroups', 'Groupes de la map')}${pill('OverlayHarvest', 'Récolte')}${pill('OverlayRotation', 'Ordre de jeu')}</div>
          <label class="volume">Opacité<input type="range" data-set="OverlayOpacity" data-fk="OverlayOpacity" min="${S.OverlayOpacity?.min ?? 40}"
            max="${S.OverlayOpacity?.max ?? 100}" step="1" value="${opacity}" style="--p:${((opacity - 40) / 60 * 100).toFixed(0)}%">
            <span class="muted">${esc(T('OverlayOpacityText'))}</span></label>
          <div>${btn('OverlayMove', 'Déplacer', 'small')}</div></section>
      </div>`}
      <div class="filters org-order"><b>Ordre de jeu</b>
        <span class="strong" style="color:${S.OrganizerOrderStatus?.c || 'inherit'}">${esc(T('OrganizerOrderStatus'))}</span>
        <span class="muted">Glissez-déposez les lignes pour le changer.</span><span class="right"></span>
        ${btn('OrganizerSaveOrder', "Enregistrer l'ordre", 'small primary')}${btn('OrganizerResetOrder', 'Ordre par initiative', 'small')}</div>
      ${table('OrganizerGrid', [
        { h: 'Ordre', k: 'Order', num: true, f: r => `<span class="order ${r.Excluded ? 'muted' : ''}">${esc(r.OrderText)}</span>` },
        { h: '★', k: 'Main', f: r => `<button class="star ${r.Main ? 'on' : ''}" title="Personnage principal (raccourci « Principal »)" ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerMain_Click' })}>${r.Main ? '★' : '☆'}</button>` },
        { h: 'Personnage', k: 'Name', f: r => `<div class="char"><span class="head" title="${esc(r.Class)}">${r.Gfx ? `<img src="icon/head/${r.Gfx}.png" alt="">` : ''}</span>
          <div><span class="strong ${r.Active ? 'acc' : ''}">${esc(r.Name)}</span>${r.Active ? ' <span class="chip accent">au premier plan</span>' : ''}
          ${r.Class ? `<div class="muted small">${esc(r.Class)}</div>` : ''}</div></div>` },
        { h: 'Niveau', k: 'Level', num: true },
        { h: 'Initiative', k: 'Initiative', num: true, f: r => `<span class="acc strong">${esc(r.InitiativeText)}</span>` },
        { h: 'État', k: 'State', f: r => `<span class="muted">${esc(r.State)}</span>` },
        { h: 'Exclure', k: 'Excluded', f: r => `<label class="check"><input type="checkbox" ${r.Excluded ? 'checked' : ''} ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerExclude_Click' })}></label>` },
        { h: '', f: r => `<button class="small primary" ${attrs({ act: 'row', grid: 'OrganizerGrid', i: r._i, handler: 'OrganizerShow_Click' })}>Afficher</button>` },
      ], { select: false, draggable: r => !r.Excluded && !r.Name.startsWith('('), empty: 'Aucune fenêtre Dofus Retro ouverte. Lancez le jeu : ses fenêtres apparaîtront ici.' })}`;
  },
});
