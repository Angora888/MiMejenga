-- 004: independent yearly party fund, separate from game fees
CREATE TABLE IF NOT EXISTS party_fund_entries (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  season_id uuid NOT NULL REFERENCES seasons(id),
  player_id uuid NOT NULL REFERENCES players(id),
  amount integer NOT NULL CHECK(amount > 0),
  method text NOT NULL CHECK(method IN ('sinpe','efectivo','otro')),
  notes text NOT NULL DEFAULT '',
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_party_fund_entries_player ON party_fund_entries(player_id,season_id,created_at);
