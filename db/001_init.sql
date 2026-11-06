CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE IF NOT EXISTS seasons (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), year int UNIQUE NOT NULL, weekly_fee integer NOT NULL DEFAULT 1500 CHECK(weekly_fee>0), absence_fee integer NOT NULL DEFAULT 500 CHECK(absence_fee>0));
CREATE TABLE IF NOT EXISTS players (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text UNIQUE NOT NULL, active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS aliases (alias text PRIMARY KEY, player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS memberships (season_id uuid NOT NULL REFERENCES seasons(id), player_id uuid NOT NULL REFERENCES players(id), starts_on date NOT NULL, charge_absences boolean NOT NULL DEFAULT true, PRIMARY KEY(season_id,player_id));
CREATE TABLE IF NOT EXISTS matches (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), season_id uuid NOT NULL REFERENCES seasons(id), match_date date NOT NULL, status text NOT NULL CHECK(status IN ('scheduled','played','cancelled')), notes text NOT NULL DEFAULT '', UNIQUE(season_id,match_date));
CREATE TABLE IF NOT EXISTS attendances (match_id uuid NOT NULL REFERENCES matches(id) ON DELETE CASCADE, player_id uuid NOT NULL REFERENCES players(id), present boolean NOT NULL DEFAULT false, laundry boolean NOT NULL DEFAULT false, PRIMARY KEY(match_id,player_id));
CREATE TABLE IF NOT EXISTS payments (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), match_id uuid NOT NULL REFERENCES matches(id) ON DELETE CASCADE, player_id uuid NOT NULL REFERENCES players(id), amount integer NOT NULL CHECK(amount>0), method text NOT NULL CHECK(method IN ('sinpe','efectivo','otro')), notes text NOT NULL DEFAULT '', created_at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS idx_matches_season ON matches(season_id,match_date);
CREATE OR REPLACE VIEW season_balances AS
WITH counts AS (
 SELECT mb.season_id,mb.player_id,mb.charge_absences,
 COUNT(m.id) FILTER(WHERE m.status='played' AND m.match_date>=mb.starts_on)::int AS games,
 COUNT(m.id) FILTER(WHERE m.status='played' AND m.match_date>=mb.starts_on AND a.present)::int AS attended
 FROM memberships mb LEFT JOIN matches m ON m.season_id=mb.season_id
 LEFT JOIN attendances a ON a.match_id=m.id AND a.player_id=mb.player_id
 GROUP BY mb.season_id,mb.player_id,mb.charge_absences
), paid AS (
 SELECT match_id,player_id,SUM(amount)::int AS amount FROM payments GROUP BY match_id,player_id
), credits AS (
 SELECT m.season_id,p.player_id,
 SUM(GREATEST(0,FLOOR((p.amount-s.weekly_fee)::numeric/s.absence_fee)))::int AS extra
 FROM paid p JOIN matches m ON m.id=p.match_id JOIN seasons s ON s.id=m.season_id
 JOIN attendances a ON a.match_id=m.id AND a.player_id=p.player_id AND a.present=true
 WHERE m.status='played' GROUP BY m.season_id,p.player_id
)
SELECT c.season_id,c.player_id,p.name,c.games,c.attended,c.games-c.attended AS missed,COALESCE(cr.extra,0) AS credits,
CASE WHEN c.charge_absences THEN GREATEST(0,c.games-c.attended-COALESCE(cr.extra,0)) ELSE 0 END AS chargeable,
CASE WHEN c.charge_absences THEN GREATEST(0,c.games-c.attended-COALESCE(cr.extra,0))*s.absence_fee ELSE 0 END AS due
FROM counts c JOIN players p ON p.id=c.player_id JOIN seasons s ON s.id=c.season_id
LEFT JOIN credits cr ON cr.season_id=c.season_id AND cr.player_id=c.player_id;
