// Base de prix HDV partagée de Dofus Bank Manager (Cloudflare Worker + D1).
//
// Ne contient que des prix d'objets : identifiant d'objet, lots ×1/×10/×100, prix moyen, date du relevé.
// Aucune donnée de joueur (README, règle 4). L'accès exige le code de groupe (secret GROUP_KEY).
//
//   GET  /prices?since=<ms>   prix reçus par le serveur depuis cette date -> { now, prices: [...] }
//   POST /prices              [{ tpl, lot1, lot10, lot100, avg, seenAt }, ...] -> { accepted }
//   GET  /health              vérifie le code de groupe

const MAX_BATCH = 1000;
const MAX_PRICE = 2_000_000_000;
const MAX_AGE_MS = 30 * 24 * 3600 * 1000; // relevés de plus de 30 jours refusés
const MAX_FUTURE_MS = 10 * 60 * 1000;     // tolérance d'horloge

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (!env.GROUP_KEY || request.headers.get('X-Group-Key') !== env.GROUP_KEY)
      return json({ error: 'Code de groupe invalide.' }, 401);

    if (url.pathname === '/health') return json({ ok: true });
    if (url.pathname !== '/prices') return json({ error: 'Introuvable.' }, 404);

    if (request.method === 'GET') {
      const since = Number(url.searchParams.get('since')) || 0;
      const { results } = await env.DB.prepare(
        `SELECT tpl, lot1, lot10, lot100, avg, seen_at AS seenAt
           FROM prices WHERE received_at > ?1 ORDER BY received_at LIMIT 50000`)
        .bind(since).all();
      return json({ now: Date.now(), prices: results });
    }

    if (request.method === 'POST') {
      let items;
      try { items = await request.json(); } catch { return json({ error: 'JSON invalide.' }, 400); }
      if (!Array.isArray(items) || items.length > MAX_BATCH) return json({ error: 'Lot invalide.' }, 400);

      const now = Date.now();
      const valid = items.filter(it => isValid(it, now));
      // Le relevé le plus récent gagne ; un prix moyen absent ne remplace pas celui connu
      const upsert = env.DB.prepare(
        `INSERT INTO prices (tpl, lot1, lot10, lot100, avg, seen_at, received_at)
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)
         ON CONFLICT(tpl) DO UPDATE SET
           lot1 = excluded.lot1, lot10 = excluded.lot10, lot100 = excluded.lot100,
           avg = COALESCE(excluded.avg, prices.avg),
           seen_at = excluded.seen_at, received_at = excluded.received_at
         WHERE excluded.seen_at > prices.seen_at`);
      if (valid.length > 0)
        await env.DB.batch(valid.map(it =>
          upsert.bind(it.tpl, it.lot1 ?? null, it.lot10 ?? null, it.lot100 ?? null, it.avg ?? null, it.seenAt, now)));
      return json({ accepted: valid.length });
    }

    return json({ error: 'Méthode non autorisée.' }, 405);
  },
};

function isValid(it, now) {
  if (!it || !Number.isInteger(it.tpl) || it.tpl < 1 || it.tpl > 1_000_000) return false;
  if (!Number.isInteger(it.seenAt) || it.seenAt < now - MAX_AGE_MS || it.seenAt > now + MAX_FUTURE_MS) return false;
  const prices = [it.lot1, it.lot10, it.lot100, it.avg];
  if (!prices.every(p => p == null || (Number.isInteger(p) && p > 0 && p <= MAX_PRICE))) return false;
  return prices.some(p => p != null);
}

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json; charset=utf-8' },
  });
}
