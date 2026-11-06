-- 002: Inscripción semanal por enlace y cuentas individuales de jugadores.
-- Ejecutar DESPUÉS de db/001_init.sql
CREATE TABLE IF NOT EXISTS player_accounts (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 player_id uuid NOT NULL UNIQUE REFERENCES players(id) ON DELETE CASCADE,
 email text NOT NULL UNIQUE,
 password_salt bytea NOT NULL,
 password_hash bytea NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),
 CONSTRAINT account_email_lower CHECK (email=lower(email))
);
ALTER TABLE matches ADD COLUMN IF NOT EXISTS share_token uuid NOT NULL DEFAULT gen_random_uuid();
ALTER TABLE matches ADD COLUMN IF NOT EXISTS goalkeeper_slots smallint NOT NULL DEFAULT 2 CHECK(goalkeeper_slots BETWEEN 0 AND 30);
ALTER TABLE matches ADD COLUMN IF NOT EXISTS field_slots smallint NOT NULL DEFAULT 12 CHECK(field_slots BETWEEN 0 AND 40);
CREATE UNIQUE INDEX IF NOT EXISTS idx_matches_share_token ON matches(share_token);
CREATE TABLE IF NOT EXISTS match_signups (
 match_id uuid NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
 player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 role text NOT NULL CHECK(role IN ('goalkeeper','field')),
 status text NOT NULL DEFAULT 'confirmed' CHECK(status IN ('confirmed','cancelled')),
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(match_id,player_id)
);
CREATE INDEX IF NOT EXISTS idx_match_signups_confirmed ON match_signups(match_id,role) WHERE status='confirmed';
-- Un signup no es una asistencia real: attendances se actualiza aparte al jugarse.
