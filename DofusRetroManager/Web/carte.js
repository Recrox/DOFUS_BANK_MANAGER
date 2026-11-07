'use strict';
// Onglet Carte : rendu, puis vue persistante (tuiles, déplacement, zoom)

Object.assign(views, {
  carte() {
    const D = S._carte;
    if (!D) return `<div class="empty">Chargement de la carte…</div>`;
    if (!carte.shown) carte.shown = new Set(D.shown);
    const list = carte.list = carteHints(D);
    // Ressources des maps visitées : un métier, puis ses ressources (mêmes filtres que les catégories)
    const R = S._carteRes || { jobs: [], hints: [], maps: 0 };
    const resName = k => R.jobs.flatMap(j => j.res).find(r => r.k === k)?.n;
    const job = R.jobs.find(j => j.n === carte.job);
    const groups = {};
    list.forEach(h => { const n = D.cats.find(c => c.k === h.k)?.n || resName(h.k) || 'Divers'; groups[n] = (groups[n] || 0) + 1; });
    const counts = list.length ? Object.entries(groups).map(([n, c]) => `${n} (${c})`).join(' · ')
      : carte.search ? "Aucun point d'intérêt ne correspond à la recherche." : 'Cochez une catégorie pour afficher ses points.';
    return `<div class="filters">
        <select data-carte="sa"><option value="0" ${carte.sa === 0 ? 'selected' : ''}>Continent amaknien</option>
          <option value="3" ${carte.sa === 3 ? 'selected' : ''}>Incarnam</option></select>
        ${D.cats.map(c => `<button class="pill carte-cat ${carte.shown.has(c.k) ? 'on' : ''}" data-act="carte-cat" data-k="${c.k}">
          ${c.i ? `<img src="${c.i}" alt="">` : ''}${esc(c.n)}</button>`).join('')}
        <label class="check"><input type="checkbox" data-carte="grid" ${carte.grid ? 'checked' : ''}>Grille</label>
        <button class="small" data-act="carte-me">Ma position</button>
        <button class="small" data-act="carte-fit">Vue d'ensemble</button>
      </div>
      <div class="filters"><span class="lbl">Ressources</span>
        <select data-carte="job" title="Métier dont les ressources sont proposées. Elles apparaissent sur les maps déjà visitées avec l'application lancée.">
          <option value="">Aucune</option>${R.jobs.map(j => `<option ${j.n === carte.job ? 'selected' : ''}>${esc(j.n)}</option>`).join('')}</select>
        ${(job?.res || []).map(r => {
          const maps = R.hints.filter(h => h.k === r.k).length;
          return `<button class="pill carte-cat ${carte.shown.has(r.k) ? 'on' : ''}" data-act="carte-cat" data-k="${r.k}"
            title="${esc(r.t)} · ${maps ? `vue sur ${maps} map(s) visitée(s)` : 'pas encore vue sur une map visitée'}"><img src="${r.i}" alt="">${esc(r.n)}</button>`;
        }).join('')}
        <span class="muted">${R.maps > 0 ? `${R.maps} map(s) lue(s)` : 'aucune map lue pour l’instant'}</span>
      </div>
      <div class="split wide">
        <div id="carte-host" class="carte-host"></div>
        <aside class="glass detail carte-side">
          <input type="search" data-carte="search" data-fk="carteSearch" value="${esc(carte.search)}" placeholder="Zaap, Temple Iop, Bouftou, [4,-18]…">
          <div class="card harvest"><b>${esc(T('CarteHarvestTitle'))}</b>${T('CarteHarvest') ? `<div class="muted">${esc(T('CarteHarvest'))}</div>` : ''}
            <div class="harvest-chips">${rows('CarteHarvestList').map(h => `<span class="harvest-chip" title="${esc(h.Tooltip)}">${itemImg(h.ItemId)}<span><b>${esc(h.Count)}</b>${h.State ? `<small class="muted">${esc(h.State)}</small>` : ''}</span></span>`).join('')}</div></div>
          ${shown('CarteCellPanel') ? `<div class="card harvest cell-info"><b class="ellipsis" title="${esc(T('CarteCellTitle'))}">${esc(T('CarteCellTitle'))}</b>
            ${T('CarteCellInfo') ? `<div class="muted ellipsis" title="${esc(T('CarteCellInfo'))}">${esc(T('CarteCellInfo'))}</div>` : ''}
            ${T('CarteCellHarvest') ? `<div class="muted">${esc(T('CarteCellHarvest'))}</div>` : ''}
            <div class="harvest-chips">${rows('CarteCellList').map(h => `<span class="harvest-chip" title="${esc(h.Tooltip)}">${itemImg(h.ItemId)}<span><b>${esc(h.Count)}</b>${h.State ? `<small class="muted">${esc(h.State)}</small>` : ''}</span></span>`).join('')}</div></div>` : ''}
          <div class="muted">${esc(counts)}</div>
          <div class="carte-list">${list.slice(0, 400).map((h, i) => `<div class="carte-row ${carte.sel === h ? 'on' : ''}" data-act="carte-go" data-i="${i}">
            ${h.i ? `<img src="${h.i}" alt="">` : '<span></span>'}<div><b>${esc(h.n)}</b><small>${esc(h.l)}</small></div></div>`).join('')}
            ${list.length > 400 ? `<div class="muted">… ${list.length - 400} autres : affinez la recherche.</div>` : ''}</div>
          <div class="muted small">Carte et icônes lues dans le client Dofus Retro installé. Glissez pour déplacer, molette pour zoomer.</div>
        </aside>
      </div>`;
  },
});

// --- Carte : vue persistante (les tuiles ne sont pas rechargées à chaque rendu), déplacée et zoomée à la souris ---
// Une map occupe 40 × 23 ; une tuile du client (600 × 345) couvre 15 × 15 maps.
const CW = 40, CH = 23, TW = 600, TH = 345;
const carte = { el: null, built: null, sa: 0, s: 0.2, ox: 0, oy: 0, fitted: false, grid: true, search: '', shown: null, job: '',
  sel: null, list: [], bounds: null, drag: null };

const norm = s => String(s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

function carteHints(D) {
  const q = norm(carte.search.trim());
  return D.hints.concat(S._carteRes?.hints || []).filter(h => h.sa === carte.sa && (q
    ? norm(h.n).includes(q) || norm(h.l).includes(q) || `[${h.x},${h.y}]`.includes(q.replace(/\s/g, ''))
    : carte.shown.has(h.k)))
    .sort((a, b) => a.k - b.k || a.n.localeCompare(b.n));
}

function carteBuild(D) {
  const tiles = D.tiles[carte.sa] || [];
  const el = document.createElement('div');
  el.className = 'carte-view';
  if (!tiles.length) {
    el.innerHTML = `<div class="empty">Carte introuvable : elle est lue dans les fichiers de Dofus Retro, qui doit être installé sur ce PC.</div>`;
  } else {
    const xs = tiles.map(t => t[0]), ys = tiles.map(t => t[1]);
    const b = carte.bounds = { x: Math.min(...xs) * TW, y: Math.min(...ys) * TH, w: (Math.max(...xs) + 1) * TW, h: (Math.max(...ys) + 1) * TH };
    b.w -= b.x; b.h -= b.y;
    el.innerHTML = `<div class="carte-world">
        ${tiles.map(([x, y]) => `<img src="carte/tile/${carte.sa}/${x}_${y}.png" style="left:${x * TW}px;top:${y * TH}px" alt="" draggable="false">`).join('')}
        <div class="carte-grid" style="left:${b.x}px;top:${b.y}px;width:${b.w}px;height:${b.h}px"></div>
        <div class="carte-cell hover"></div><div class="carte-cell sel"></div>
      </div><div class="carte-markers"></div><div class="carte-hover">Survolez la carte pour voir les coordonnées.</div>`;
  }
  carte.el = el;
  carte.built = carte.sa;
  carte.fitted = false;

  el.addEventListener('wheel', e => {
    e.preventDefault();
    const r = el.getBoundingClientRect();
    carteZoom(e.clientX - r.left, e.clientY - r.top, e.deltaY < 0 ? 1.25 : 0.8);
  }, { passive: false });
  el.addEventListener('mousedown', e => {
    if (e.button !== 0) return;
    carte.drag = { x: e.clientX, y: e.clientY, ox: carte.ox, oy: carte.oy, moved: false };
  });
  el.addEventListener('mousemove', e => {
    const r = el.getBoundingClientRect();
    const wx = (e.clientX - r.left - carte.ox) / carte.s, wy = (e.clientY - r.top - carte.oy) / carte.s;
    carteHover(Math.floor(wx / CW), Math.floor(wy / CH));
  });
  el.addEventListener('mouseleave', () => carteHover(null));
}

window.addEventListener('mousemove', e => {
  const d = carte.drag;
  if (!d) return;
  if (!d.moved && Math.hypot(e.clientX - d.x, e.clientY - d.y) > 4) d.moved = true;
  if (d.moved) { carte.ox = d.ox + e.clientX - d.x; carte.oy = d.oy + e.clientY - d.y; carteView(); }
});
window.addEventListener('mouseup', e => {
  const d = carte.drag;
  carte.drag = null;
  if (!d || d.moved) return;
  // Clic sans déplacement sur un marqueur : le point est choisi dans la liste ; ailleurs sur la carte : infos de la case
  const m = e.target.closest?.('.carte-marker[data-i]');
  if (m) { const h = carte.list[+m.dataset.i]; carteSelect(h, false); if (h) post({ type: 'cell', sa: h.sa, x: h.x, y: h.y }); }
  else if (carte.el && carte.el.contains(e.target)) {
    const r = carte.el.getBoundingClientRect();
    const x = Math.floor((e.clientX - r.left - carte.ox) / carte.s / CW), y = Math.floor((e.clientY - r.top - carte.oy) / carte.s / CH);
    if (S._carte?.cells[carte.sa]?.[`${x},${y}`]) { carte.sel = { x, y, sa: carte.sa }; carteView(); post({ type: 'cell', sa: carte.sa, x, y }); }
  }
});

function carteZoom(px, py, f) {
  const wx = (px - carte.ox) / carte.s, wy = (py - carte.oy) / carte.s;
  carte.s = Math.min(4, Math.max(0.08, carte.s * f));
  carte.ox = px - wx * carte.s; carte.oy = py - wy * carte.s;
  carteView();
}

function carteFit() {
  const b = carte.bounds, el = carte.el;
  if (!b || !el || !el.clientWidth) return;
  carte.s = Math.min(el.clientWidth / b.w, el.clientHeight / b.h);
  carte.ox = (el.clientWidth - b.w * carte.s) / 2 - b.x * carte.s;
  carte.oy = (el.clientHeight - b.h * carte.s) / 2 - b.y * carte.s;
  carte.fitted = true;
  carteView();
}

function carteCenter(x, y, minScale = 1) {
  const el = carte.el;
  if (!el) return;
  carte.s = Math.max(carte.s, minScale);
  carte.ox = el.clientWidth / 2 - (x + 0.5) * CW * carte.s;
  carte.oy = el.clientHeight / 2 - (y + 0.5) * CH * carte.s;
  carte.fitted = true;
  carteView();
}

/** Applique la vue : déplacement et zoom du monde, marqueurs replacés (ils gardent leur taille). */
function carteView() {
  const el = carte.el;
  if (!el) return;
  const world = el.querySelector('.carte-world');
  if (world) world.style.transform = `translate(${carte.ox}px,${carte.oy}px) scale(${carte.s})`;
  const grid = el.querySelector('.carte-grid');
  if (grid) grid.style.display = carte.grid && carte.s * CH >= 6 ? '' : 'none';
  el.querySelectorAll('.carte-marker').forEach(m => {
    m.style.left = ((+m.dataset.x + 0.5) * CW * carte.s + carte.ox) + 'px';
    m.style.top = ((+m.dataset.y + 0.5) * CH * carte.s + carte.oy) + 'px';
  });
  const sel = el.querySelector('.carte-cell.sel');
  if (sel) sel.style.cssText = carte.sel ? `display:block;left:${carte.sel.x * CW}px;top:${carte.sel.y * CH}px;border-width:${3 / carte.s}px` : 'display:none';
}

function carteHover(x, y) {
  const el = carte.el, D = S._carte;
  if (!el || !D) return;
  const cell = el.querySelector('.carte-cell.hover'), label = el.querySelector('.carte-hover');
  if (!cell || !label) return;
  if (x == null) { cell.style.display = 'none'; label.textContent = 'Survolez la carte pour voir les coordonnées.'; return; }
  cell.style.cssText = `display:block;left:${x * CW}px;top:${y * CH}px;border-width:${1.5 / carte.s}px`;
  const zone = D.cells[carte.sa]?.[`${x},${y}`];
  const here = [...new Set(D.hints.filter(h => h.sa === carte.sa && h.x === x && h.y === y).map(h => h.n))];
  label.textContent = `[${x},${y}]` + (zone ? `  ${zone}` : '') + (here.length ? '  —  ' + here.slice(0, 4).join(', ') + (here.length > 4 ? '…' : '') : '');
}

function carteSelect(h, center = true) {
  if (!h) return;
  carte.sel = h;
  if (center) carteCenter(h.x, h.y); else carteView();
  carteHover(h.x, h.y);
  schedule();
}

/** Place la vue dans l'onglet (après chaque rendu) et redessine les marqueurs : points filtrés et vos personnages. */
function carteMount() {
  const host = document.getElementById('carte-host'), D = S._carte;
  if (!host || !D) return;
  if (!carte.el || carte.built !== carte.sa) carteBuild(D);
  host.appendChild(carte.el);
  const layer = carte.el.querySelector('.carte-markers');
  if (layer) {
    const players = (S._cartePos || []).filter(p => p.sa === carte.sa);
    layer.innerHTML = carte.list.map((h, i) => `<div class="carte-marker" data-i="${i}" data-x="${h.x}" data-y="${h.y}" title="${esc(h.n + '\n' + h.l)}">
        ${h.i ? `<img src="${h.i}" alt="" draggable="false">` : '<i></i>'}</div>`).join('')
      + players.map(p => `<div class="carte-marker player ${p.me ? 'me' : ''}" data-x="${p.x}" data-y="${p.y}" title="${esc(p.n + '\n' + p.l)}">
        <i></i><span>${esc(p.n)}</span></div>`).join('')
      + (carte.target?.sa === carte.sa ? `<div class="carte-marker player goto" data-x="${carte.target.x}" data-y="${carte.target.y}" title="${esc(carte.target.l)}">
        <i></i><span>${esc(carte.target.l)}</span></div>` : '');
  }
  if (carte.goto) carteGoto(); else if (!carte.fitted) requestAnimationFrame(carteFit); else carteView();
}

/** Centre la carte sur la case demandée (position cliquée), l'encadre et y pose un repère avec son libellé. */
function carteGoto() {
  let g = carte.goto;
  if (g.me) {
    if (!S._cartePos) return; // positions pas encore reçues
    const me = S._cartePos.find(p => p.me) || S._cartePos[0];
    carte.goto = null;
    if (!me) { carteFit(); return; }
    g = { sa: me.sa, x: me.x, y: me.y };
    if (me.sa !== carte.sa) { carte.sa = me.sa; carte.goto = g; schedule(); return; }
  }
  carte.goto = null;
  carte.target = g.l ? g : null;
  carte.sel = { x: g.x, y: g.y };
  carte.fitted = true; // pas de vue d'ensemble au rendu suivant : on centre sur la case
  schedule(); // repère redessiné au rendu suivant
  requestAnimationFrame(() => { carteCenter(g.x, g.y, 1.5); carteHover(g.x, g.y); });
}
