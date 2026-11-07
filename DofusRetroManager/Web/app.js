'use strict';
// Mise à jour de la page et événements ; chargé après core.js et les fichiers des onglets

// Mise à jour de la page : seuls les éléments qui ont changé sont modifiés (texte, attributs, lignes ajoutées ou retirées).
// Les images déjà chargées, la saisie en cours, le défilement et la carte restent en place.
function morphHtml(root, html) {
  const next = document.createElement('div');
  next.innerHTML = html;
  morphChildren(root, next);
}

function morphChildren(a, b) {
  if (a.id === 'carte-host') return; // vue de la carte, posée par carteMount
  const live = a.childNodes, wanted = [...b.childNodes];
  for (let i = 0; i < wanted.length; i++) {
    const x = live[i], y = wanted[i];
    if (!x) a.appendChild(y);
    else if (x.nodeType !== y.nodeType || x.nodeName !== y.nodeName) a.replaceChild(y, x);
    else if (x.nodeType !== 1) { if (x.nodeValue !== y.nodeValue) x.nodeValue = y.nodeValue; }
    // Bloc identique (comparaison native, rapide) : rien à faire, sauf pour les champs dont la valeur saisie diffère
    else if (x.isEqualNode(y) && !x.querySelector?.('input, select') && x.tagName !== 'INPUT') { }
    else morphElement(x, y);
  }
  while (live.length > wanted.length) a.removeChild(a.lastChild);
}

function morphElement(a, b) {
  for (const { name } of [...a.attributes]) if (!b.hasAttribute(name)) a.removeAttribute(name);
  for (const { name, value } of b.attributes) if (a.getAttribute(name) !== value) a.setAttribute(name, value);
  // État des champs : la valeur affichée n'est pas un attribut (sauf pour le champ en cours de saisie)
  if (a.tagName === 'INPUT') {
    if (a !== document.activeElement && a.value !== b.value) a.value = b.value;
    if (a.checked !== b.checked) a.checked = b.checked;
  } else if (a.tagName === 'OPTION') { if (a.selected !== b.selected) a.selected = b.selected; }
  morphChildren(a, b);
}

let pending = false;
function render() {
  pending = false;
  document.documentElement.dataset.theme = (S._theme || 'Crepuscule').toLowerCase();
  document.documentElement.dataset.style = (S._style || 'Moderne').toLowerCase();
  const active = S.MainTabs?.i ?? 0;
  const tab = TABS[active] || TABS[0];
  watchTab(active);

  // Garder la saisie en cours et le défilement des tableaux pendant le rendu
  const focus = document.activeElement?.dataset?.fk ? { key: document.activeElement.dataset.fk, value: document.activeElement.value,
    start: document.activeElement.selectionStart, end: document.activeElement.selectionEnd } : null;
  const scroll = [...document.querySelectorAll('.table-wrap, .detail, .modal-body')].map(e => e.scrollTop);

  const html = header() + banner() + tabs(active)
    + `<main class="glass">${views[tab.key]()}</main>` + changelog() + confirmBox() + ttgBook();
  const app = document.getElementById('app');
  if (local.noMorph) app.innerHTML = html; else morphHtml(app, html);

  if (tab.key === 'carte') carteMount();
  document.querySelectorAll('.table-wrap, .detail, .modal-body').forEach((e, i) => { if (scroll[i]) e.scrollTop = scroll[i]; });
  // Journal : toujours sur les derniers messages
  const log = document.getElementById('log');
  if (log) log.scrollTop = log.scrollHeight;
  if (focus) {
    const el = document.querySelector(`[data-fk="${focus.key}"]`);
    if (el) {
      el.focus();
      if (el.value !== focus.value) el.value = focus.value;
      try { el.setSelectionRange(focus.start, focus.end); } catch { }
    }
  }
}
const schedule = () => { if (!pending) { pending = true; requestAnimationFrame(render); } };

// --- Événements ---
window.chrome.webview.addEventListener('message', e => {
  if (e.data?.type !== 'update') return;
  Object.assign(S, e.data.data);
  // Nouveautés demandées par l'application (après une mise à jour) : la fenêtre s'ouvre une fois par demande
  if ((S._changelog || 0) > (local.changelogSeen || 0)) { local.changelogSeen = S._changelog; local.changelog = true; }
  schedule();
});

document.addEventListener('click', e => {
  const a = e.target.closest('[data-act]');
  if (!a) return;
  const d = a.dataset;
  switch (d.act) {
    case 'click': post({ type: 'click', name: d.name }); break;
    case 'tab': post({ type: 'set', name: 'MainTabs', value: +d.i }); S.MainTabs = { i: +d.i }; schedule(); break;
    case 'changelog': local.changelog = true; schedule(); break;
    case 'changelog-close': local.changelog = false; schedule(); break;
    case 'book': local.book = { fam: null, shiny: false }; schedule(); break;
    case 'trades': local.trades = !local.trades; schedule(); break;
    case 'trade-del': post({ type: 'trade-del', index: +d.i }); break;
    case 'trade-kind': local.tf.k = d.k; schedule(); break;
    case 'book-close': local.book = null; schedule(); break;
    case 'book-fam': local.book.fam = +d.id; schedule(); break;
    case 'book-shiny': local.book.shiny = !local.book.shiny; schedule(); break;
    case 'confirm': local.confirm = { name: d.name, text: d.text }; schedule(); break;
    case 'confirm-close': local.confirm = null; schedule(); break;
    case 'org-settings': local.orgHidden = !local.orgHidden; schedule(); break;
    case 'confirm-ok': post({ type: 'click', name: local.confirm.name, confirmed: true }); local.confirm = null; schedule(); break;
    case 'carte-cat': {
      const k = +d.k;
      if (carte.shown.has(k)) carte.shown.delete(k); else carte.shown.add(k);
      schedule();
      break;
    }
    case 'carte-go': { const h = carte.list[+d.i]; carteSelect(h); if (h) post({ type: 'cell', sa: h.sa, x: h.x, y: h.y }); break; }
    case 'carte-fit': carteFit(); break;
    case 'item-hdv': post({ type: 'show', tab: 'hdv', id: +d.id }); break;
    case 'item-recipe': post({ type: 'show', tab: 'recipe', id: +d.id }); break;
    case 'goto': case 'goto-me': {
      // Repère à montrer une fois la carte chargée (carteMount) ; vos personnages sont déjà marqués
      const g = d.act === 'goto' ? JSON.parse(d.g) : null;
      carte.goto = g ? { sa: g.SuperArea, x: g.X, y: g.Y, l: g.Label } : { me: true };
      if (g && g.SuperArea !== carte.sa) { carte.sa = g.SuperArea; carte.sel = null; }
      const i = TABS.findIndex(t => t.key === 'carte');
      post({ type: 'set', name: 'MainTabs', value: i }); S.MainTabs = { i }; schedule();
      break;
    }
    case 'carte-me': {
      const me = (S._cartePos || []).find(p => p.me) || (S._cartePos || [])[0];
      if (!me) break;
      if (me.sa !== carte.sa) { carte.sa = me.sa; carte.sel = null; schedule(); requestAnimationFrame(() => requestAnimationFrame(() => carteCenter(me.x, me.y, 1.5))); }
      else carteCenter(me.x, me.y, 1.5);
      break;
    }
    case 'theme': post({ type: 'theme', value: d.v }); S._theme = d.v; schedule(); break;
    case 'style': post({ type: 'style', value: d.v }); S._style = d.v; schedule(); break;
    case 'select': post({ type: 'select', name: d.grid, index: +d.i }); break;
    case 'row': post({ type: 'row', name: d.grid, index: +d.i, handler: d.handler }); break;
    case 'toggle': post({ type: 'toggle', name: d.name, index: +d.i }); break;
    case 'open': local.open[d.key] = !local.open[d.key]; schedule(); break;
    case 'more': local.limit[d.grid] = (local.limit[d.grid] || 300) + 300; schedule(); break;
    case 'sort': {
      const s = local.sort[d.grid];
      local.sort[d.grid] = s && s.k === d.k ? (s.dir > 0 ? { k: d.k, dir: -1 } : null) : { k: d.k, dir: 1 };
      schedule();
      break;
    }
  }
});

// Glisser-déposer d'une ligne (ordre de jeu du Switcheur)
let dragFrom = null;
document.addEventListener('dragstart', e => {
  const tr = e.target.closest?.('tr[data-drag-grid]');
  if (!tr) return;
  dragFrom = { grid: tr.dataset.dragGrid, i: +tr.dataset.dragI };
  e.dataTransfer.effectAllowed = 'move';
  tr.classList.add('dragging');
});
document.addEventListener('dragover', e => {
  const tr = e.target.closest?.('tr[data-drag-grid]');
  if (!dragFrom || !tr || tr.dataset.dragGrid !== dragFrom.grid) return;
  e.preventDefault();
  document.querySelectorAll('tr.drop-target').forEach(x => x.classList.remove('drop-target'));
  tr.classList.add('drop-target');
});
document.addEventListener('drop', e => {
  const tr = e.target.closest?.('tr[data-drag-grid]');
  if (dragFrom && tr && tr.dataset.dragGrid === dragFrom.grid) {
    e.preventDefault();
    post({ type: 'reorder', name: dragFrom.grid, index: dragFrom.i, to: +tr.dataset.dragI });
  }
});
document.addEventListener('dragend', () => {
  dragFrom = null;
  document.querySelectorAll('tr.dragging, tr.drop-target').forEach(x => x.classList.remove('dragging', 'drop-target'));
});
// Raccourci : la combinaison pressée remplace la valeur (« Ctrl+Alt+Right ») ; Suppr ou Retour arrière l'efface
const KEYS = { ArrowRight: 'Right', ArrowLeft: 'Left', ArrowUp: 'Up', ArrowDown: 'Down', ' ': 'Space', Escape: 'Escape',
  PageUp: 'PageUp', PageDown: 'Next', Enter: 'Return' };
document.addEventListener('keydown', e => {
  if (e.key === 'Escape' && local.confirm) { local.confirm = null; schedule(); return; }
  if (e.key === 'Escape' && local.changelog) { local.changelog = false; schedule(); return; }
  if (e.key === 'Escape' && local.book) { local.book = null; schedule(); return; }
  const name = e.target.dataset?.hotkey;
  if (!name) return;
  e.preventDefault();
  if (e.key === 'Delete' || e.key === 'Backspace') { post({ type: 'set', name, value: '' }); return; }
  if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) return;
  const key = KEYS[e.key] || (/^Digit\d$/.test(e.code) ? 'D' + e.code.slice(5) : /^Key[A-Z]$/.test(e.code) ? e.code.slice(3)
    : /^F\d+$/.test(e.key) ? e.key : /^Numpad\d$/.test(e.code) ? 'NumPad' + e.code.slice(6) : null);
  if (!key) return;
  const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean);
  post({ type: 'set', name, value: mods.concat(key).join('+') });
});

// Saisie : envoyée après une courte pause ; listes et cases : tout de suite
const timers = {};
document.addEventListener('input', e => {
  const el = e.target;
  // Carte : recherche propre à la page
  if (el.dataset?.carte === 'search') { carte.search = el.value; schedule(); return; }
  // Seuil du Brisage (minimum ou maximum d'une caractéristique)
  if (el.dataset?.stat != null && el.type !== 'checkbox') {
    const key = 'stat' + el.dataset.stat + el.dataset.field;
    clearTimeout(timers[key]);
    timers[key] = setTimeout(() => post({ type: 'stat', index: +el.dataset.stat, field: el.dataset.field, value: el.value }), 250);
    return;
  }
  if (!el.dataset?.set || el.type === 'checkbox' || el.tagName === 'SELECT') return;
  if (el.type === 'range') {
    el.style.setProperty('--p', ((el.value - el.min) / (el.max - el.min) * 100) + '%');
    clearTimeout(timers[el.dataset.set]);
    timers[el.dataset.set] = setTimeout(() => post({ type: 'set', name: el.dataset.set, value: +el.value }), 120);
    return;
  }
  clearTimeout(timers[el.dataset.set]);
  timers[el.dataset.set] = setTimeout(() => post({ type: 'set', name: el.dataset.set, value: el.value }), 250);
});
document.addEventListener('change', e => {
  const el = e.target;
  if (el.dataset?.tf) { local.tf[el.dataset.tf] = el.dataset.tf === 'd' ? +el.value : el.value; schedule(); return; }
  if (el.dataset?.carte === 'sa') { carte.sa = +el.value; carte.sel = null; schedule(); return; }
  if (el.dataset?.carte === 'job') { carte.job = el.value; schedule(); return; }
  if (el.dataset?.carte === 'grid') { carte.grid = el.checked; carteView(); return; }
  if (el.dataset?.fmweight != null) { post({ type: 'fmweight', index: +el.dataset.fmweight, value: el.value }); return; }
  if (el.dataset?.stat != null && el.type === 'checkbox') { post({ type: 'stat', index: +el.dataset.stat, field: 'on', value: el.checked }); return; }
  if (!el.dataset?.set) return;
  if (el.type === 'checkbox') post({ type: 'set', name: el.dataset.set, value: el.checked });
  // Volume relâché : nouvelle valeur, puis le son pour l'entendre
  else if (el.type === 'range') {
    post({ type: 'set', name: el.dataset.set, value: +el.value });
    if (el.dataset.set === 'HuntVolume') post({ type: 'click', name: 'HuntTestSound' });
  }
  else if (el.tagName === 'SELECT') post({ type: 'set', name: el.dataset.set, value: +el.value });
});

// Icône introuvable : masquée (pour un sort, son nom reste affiché dessous)
document.addEventListener('error', e => { if (e.target.tagName === 'IMG') e.target.style.visibility = 'hidden'; }, true);

render();

// --- Survol immédiat : éléments [data-tip] (nom, état, prix…), bulle qui suit la souris ---
(() => {
  const tip = document.createElement('div');
  tip.className = 'hover-tip';
  document.body.appendChild(tip);
  let cur = null;
  document.addEventListener('mouseover', e => {
    const el = e.target.closest?.('[data-tip]');
    if (el === cur) return;
    cur = el;
    if (!el || !el.dataset.tip) { tip.style.display = 'none'; return; }
    tip.textContent = el.dataset.tip;
    tip.style.display = 'block';
  });
  document.addEventListener('mousemove', e => {
    if (!cur) return;
    const w = tip.offsetWidth, h = tip.offsetHeight;
    tip.style.left = Math.min(e.clientX + 14, innerWidth - w - 6) + 'px';
    tip.style.top = (e.clientY + 18 + h > innerHeight ? e.clientY - h - 10 : e.clientY + 18) + 'px';
  });
  // La page est redessinée : l'élément survolé a pu disparaître
  new MutationObserver(() => { if (cur && !cur.isConnected) { cur = null; tip.style.display = 'none'; } })
    .observe(document.body, { childList: true, subtree: true });
})();
