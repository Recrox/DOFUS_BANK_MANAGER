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
