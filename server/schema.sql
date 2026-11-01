-- Un prix par objet : le relevé le plus récent (seen_at = date du relevé en jeu, received_at = date de réception)
CREATE TABLE IF NOT EXISTS prices (
  tpl         INTEGER PRIMARY KEY,
  lot1        INTEGER,
  lot10       INTEGER,
  lot100      INTEGER,
  avg         INTEGER,
  seen_at     INTEGER NOT NULL,
  received_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_prices_received ON prices (received_at);

-- Cartes TTG vues chez les marchands : chaque relevé est gardé (un par boutique consultée), pour l'historique
CREATE TABLE IF NOT EXISTS merchant_prices (
  tpl         INTEGER NOT NULL,
  price       INTEGER NOT NULL,
  qty         INTEGER NOT NULL,
  seen_at     INTEGER NOT NULL,
  received_at INTEGER NOT NULL,
  PRIMARY KEY (tpl, seen_at, price)
);
CREATE INDEX IF NOT EXISTS idx_merchant_received ON merchant_prices (received_at);

-- Pierres d'âme : un prix par monstre et par catégorie (0 = monstre, 1 = donjon, 2 = archi-monstre), le plus récent
CREATE TABLE IF NOT EXISTS pda_prices (
  monster     INTEGER NOT NULL,
  category    INTEGER NOT NULL,
  price       INTEGER,
  offers      INTEGER NOT NULL,
  avg         INTEGER,
  seen_at     INTEGER NOT NULL,
  received_at INTEGER NOT NULL,
  PRIMARY KEY (monster, category)
);
CREATE INDEX IF NOT EXISTS idx_pda_received ON pda_prices (received_at);
