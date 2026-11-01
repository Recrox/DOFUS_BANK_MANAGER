// Base de prix partagée de Dofus Bank Manager (Cloudflare Worker + D1).
//
// Ne contient que des prix : objets de l'HDV, cartes TTG vues chez les marchands, pierres d'âme par monstre.
// Aucune donnée de joueur ni de marchand (README, règle 4). L'accès exige le code de groupe (secret GROUP_KEY).
//
//   GET  /prices?since=<ms>   relevés reçus par le serveur depuis cette date -> { now, prices, merchant, pda }
//   POST /prices              [{ tpl, lot1, lot10, lot100, avg, seenAt }, ...] -> { accepted }
//   POST /merchant            [{ tpl, price, qty, seenAt }, ...] -> { accepted }
//   POST /pda                 [{ monster, category, price, offers, avg, seenAt }, ...] -> { accepted }
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

    if (url.pathname === '/prices' && request.method === 'GET') {
      const since = Number(url.searchParams.get('since')) || 0;
      // Une seule lecture pour tout : les anciennes versions de l'application ignorent merchant et pda
      const [prices, merchant, pda] = await env.DB.batch([
        env.DB.prepare(
          `SELECT tpl, lot1, lot10, lot100, avg, seen_at AS seenAt
             FROM prices WHERE received_at > ?1 ORDER BY received_at LIMIT 50000`).bind(since),
        env.DB.prepare(
          `SELECT tpl, price, qty, seen_at AS seenAt
             FROM merchant_prices WHERE received_at > ?1 ORDER BY received_at LIMIT 50000`).bind(since),
        env.DB.prepare(
          `SELECT monster, category, price, offers, avg, seen_at AS seenAt
             FROM pda_prices WHERE received_at > ?1 ORDER BY received_at LIMIT 50000`).bind(since),
      ]);
      return json({ now: Date.now(), prices: prices.results, merchant: merchant.results, pda: pda.results });
    }

    if (request.method !== 'POST') return json({ error: 'Introuvable.' }, 404);
    let items;
    try { items = await request.json(); } catch { return json({ error: 'JSON invalide.' }, 400); }
    if (!Array.isArray(items) || items.length > MAX_BATCH) return json({ error: 'Lot invalide.' }, 400);
    const now = Date.now();

    if (url.pathname === '/prices') {
      const valid = items.filter(it => isValidHdv(it, now));
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

    if (url.pathname === '/merchant') {
      const valid = items.filter(it => isValidMerchant(it, now));
      // Chaque relevé est conservé ; un relevé déjà reçu (même carte, date et prix) est ignoré
      const insert = env.DB.prepare(
        `INSERT OR IGNORE INTO merchant_prices (tpl, price, qty, seen_at, received_at) VALUES (?1, ?2, ?3, ?4, ?5)`);
      if (valid.length > 0)
        await env.DB.batch([
          ...valid.map(it => insert.bind(it.tpl, it.price, it.qty, it.seenAt, now)),
          env.DB.prepare(`DELETE FROM merchant_prices WHERE seen_at < ?1`).bind(now - MAX_AGE_MS),
        ]);
      return json({ accepted: valid.length });
    }

    if (url.pathname === '/pda') {
      const valid = items.filter(it => isValidPda(it, now));
      const upsert = env.DB.prepare(
        `INSERT INTO pda_prices (monster, category, price, offers, avg, seen_at, received_at)
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)
         ON CONFLICT(monster, category) DO UPDATE SET
           price = excluded.price, offers = excluded.offers,
           avg = COALESCE(excluded.avg, pda_prices.avg),
           seen_at = excluded.seen_at, received_at = excluded.received_at
         WHERE excluded.seen_at > pda_prices.seen_at`);
      if (valid.length > 0)
        await env.DB.batch(valid.map(it =>
          upsert.bind(it.monster, it.category, it.price ?? null, it.offers, it.avg ?? null, it.seenAt, now)));
      return json({ accepted: valid.length });
    }

    return json({ error: 'Introuvable.' }, 404);
  },
};

const isPrice = p => p == null || (Number.isInteger(p) && p > 0 && p <= MAX_PRICE);
const isId = id => Number.isInteger(id) && id >= 1 && id <= 1_000_000;
const isDate = (t, now) => Number.isInteger(t) && t >= now - MAX_AGE_MS && t <= now + MAX_FUTURE_MS;

function isValidHdv(it, now) {
  if (!it || !isId(it.tpl) || !isDate(it.seenAt, now)) return false;
  const prices = [it.lot1, it.lot10, it.lot100, it.avg];
  return prices.every(isPrice) && prices.some(p => p != null);
}

function isValidMerchant(it, now) {
  return !!it && isId(it.tpl) && isDate(it.seenAt, now) && it.price != null && isPrice(it.price)
    && Number.isInteger(it.qty) && it.qty >= 0 && it.qty <= 1_000_000;
}

function isValidPda(it, now) {
  return !!it && isId(it.monster) && [0, 1, 2].includes(it.category) && isDate(it.seenAt, now)
    && isPrice(it.price) && isPrice(it.avg) && Number.isInteger(it.offers) && it.offers >= 0 && it.offers <= 100_000;
}

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json; charset=utf-8' },
  });
}
