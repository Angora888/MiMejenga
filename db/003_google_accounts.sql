-- 003: Google accounts and personal financial dashboards.
-- Fresh deployment: run 001, 002, then 003; no old WhatsApp records imported.
CREATE TABLE IF NOT EXISTS google_player_accounts (
  google_sub text PRIMARY KEY,
  player_id uuid NOT NULL UNIQUE REFERENCES players(id) ON DELETE CASCADE,
  email text NOT NULL,
  picture_url text,
  created_at timestamptz NOT NULL DEFAULT now(),
  last_login_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_google_players_email ON google_player_accounts(lower(email));
-- The prior email/password table is unused by Google sign-in.
-- Leave it unpopulated; do not use its endpoints on the public web.
-- Existing payments remain the authoritative recorded amounts.
-- A new player becomes a seasonal member from the first match they sign up for.
