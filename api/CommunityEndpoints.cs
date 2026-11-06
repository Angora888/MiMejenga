using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

public static class CommunityEndpoints
{
    private const int HashIterations = 210_000;
    private static string NewToken(Guid playerId, string secret)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(playerId + ":" + DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "p:" + payload + "." + Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
    private static Guid? PlayerFrom(HttpContext ctx, string secret)
    {
        try
        {
            var token = ctx.Request.Headers.Authorization.ToString();
            if (!token.StartsWith("Bearer p:", StringComparison.Ordinal)) return null;
            var parts = token[9..].Split('.');
            if (parts.Length != 2) return null;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
            if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(parts[1]))) return null;
            var fields = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])).Split(':');
            if (fields.Length != 2 || !Guid.TryParse(fields[0], out var id)) return null;
            if (long.Parse(fields[1], CultureInfo.InvariantCulture) <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return id;
        }
        catch { return null; }
    }
    private static byte[] Hash(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, HashIterations, HashAlgorithmName.SHA256, 32);

    public static void MapCommunityEndpoints(this WebApplication app, string connection, string secret)
    {
        app.MapGet("/community/matches/{token:guid}", async (Guid token) =>
        {
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT m.id,m.match_date,m.status,m.goalkeeper_slots,m.field_slots,
                       s.year, s.weekly_fee
                FROM matches m JOIN seasons s ON s.id=m.season_id WHERE m.share_token=@token
                """, db);
            cmd.Parameters.AddWithValue("token", token);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return Results.NotFound();
            var matchId = reader.GetGuid(0);
            var date = reader.GetDateTime(1).ToString("yyyy-MM-dd");
            var status = reader.GetString(2);
            var keepers = reader.GetInt16(3);
            var field = reader.GetInt16(4);
            var year = reader.GetInt32(5);
            var fee = reader.GetInt32(6);
            await reader.CloseAsync();
            await using var roster = new NpgsqlCommand("""
                SELECT p.name, ms.role FROM match_signups ms JOIN players p ON p.id=ms.player_id
                WHERE ms.match_id=@id AND ms.status='confirmed'
                ORDER BY ms.created_at, p.name
                """, db);
            roster.Parameters.AddWithValue("id", matchId);
            var users = new List<object>();
            await using var rr = await roster.ExecuteReaderAsync();
            int k = 0, f = 0;
            while (await rr.ReadAsync())
            {
                var role = rr.GetString(1);
                if (role == "goalkeeper") k++; else f++;
                users.Add(new { name = rr.GetString(0), role });
            }
            return Results.Ok(new { date, status, year, fee, goalkeeperSlots = keepers, fieldSlots = field, goalkeeperCount = k, fieldCount = f, players = users });
        });

        app.MapPost("/community/register", async (RegisterPlayer req) =>
        {
            var name = req.Name?.Trim() ?? "";
            var email = req.Email?.Trim().ToLowerInvariant() ?? "";
            if (name.Length < 2 || name.Length > 80 || email.Length > 254 ||
                !System.Net.Mail.MailAddress.TryCreate(email, out var addr) || addr.Address != email ||
                (req.Password?.Length ?? 0) < 12 || req.Password!.Length > 256)
                return Results.BadRequest(new { error = "Usá nombre, correo válido y contraseña de al menos 12 caracteres" });
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var tx = await db.BeginTransactionAsync();
            await using var match = new NpgsqlCommand("""
                SELECT season_id,match_date FROM matches WHERE share_token=@token AND status='scheduled'
                """, db, tx);
            match.Parameters.AddWithValue("token", req.MatchToken);
            Guid season;
            DateTime matchDate;
            await using (var r = await match.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.BadRequest(new { error = "Enlace inválido o mejenga cerrada" });
                season = r.GetGuid(0);
                matchDate = r.GetDateTime(1);
            }
            var salt = RandomNumberGenerator.GetBytes(16);
            try
            {
                await using var cmd = new NpgsqlCommand("""
                    WITH new_player AS (
                      INSERT INTO players(name) VALUES (@name) RETURNING id
                    ), new_account AS (
                      INSERT INTO player_accounts(player_id,email,password_salt,password_hash)
                      SELECT id,@email,@salt,@hash FROM new_player RETURNING player_id
                    )
                    INSERT INTO memberships(season_id,player_id,starts_on,charge_absences)
                    SELECT @season,player_id,@date,true FROM new_account RETURNING player_id
                    """, db, tx);
                cmd.Parameters.AddWithValue("name", name);
                cmd.Parameters.AddWithValue("email", email);
                cmd.Parameters.AddWithValue("salt", salt);
                cmd.Parameters.AddWithValue("hash", Hash(req.Password!, salt));
                cmd.Parameters.AddWithValue("season", season);
                cmd.Parameters.AddWithValue("date", matchDate);
                var result = (Guid)(await cmd.ExecuteScalarAsync())!;
                await tx.CommitAsync();
                return Results.Ok(new { token = NewToken(result, secret), name });
            }
            catch (PostgresException e) when (e.SqlState == "23505")
            {
                await tx.RollbackAsync();
                return Results.Conflict(new { error = "Ese nombre o correo ya está registrado. Iniciá sesión o contactá a la directiva." });
            }
        });

        app.MapPost("/community/login", async (PlayerLogin req) =>
        {
            var email = req.Email?.Trim().ToLowerInvariant() ?? "";
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT player_id,password_salt,password_hash FROM player_accounts WHERE email=@email", db);
            cmd.Parameters.AddWithValue("email", email);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Results.Unauthorized();
            var id = r.GetGuid(0);
            var salt = (byte[])r[1];
            var stored = (byte[])r[2];
            var hash = Hash(req.Password ?? "", salt);
            if (!CryptographicOperations.FixedTimeEquals(hash, stored)) return Results.Unauthorized();
            return Results.Ok(new { token = NewToken(id, secret) });
        });

        app.MapGet("/community/me", async (HttpContext ctx) =>
        {
            var id = PlayerFrom(ctx, secret);
            if (id is null) return Results.Unauthorized();
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT name FROM players WHERE id=@id AND active=true", db);
            cmd.Parameters.AddWithValue("id", id.Value);
            var name = await cmd.ExecuteScalarAsync();
            return name is null ? Results.Unauthorized() : Results.Ok(new { id, name });
        });

        app.MapGet("/community/matches/{token:guid}/mine", async (Guid token, HttpContext ctx) =>
        {
            var player = PlayerFrom(ctx, secret);
            if (player is null) return Results.Unauthorized();
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT ms.role FROM match_signups ms JOIN matches m ON m.id=ms.match_id
                WHERE m.share_token=@token AND ms.player_id=@player AND ms.status='confirmed'
                """, db);
            cmd.Parameters.AddWithValue("token", token);
            cmd.Parameters.AddWithValue("player", player.Value);
            var role = await cmd.ExecuteScalarAsync();
            return Results.Ok(new { role = role?.ToString() });
        });

        app.MapPut("/community/matches/{token:guid}/signup", async (Guid token, SignupRequest req, HttpContext ctx) =>
        {
            var player = PlayerFrom(ctx, secret);
            if (player is null) return Results.Unauthorized();
            if (req.Role != "goalkeeper" && req.Role != "field") return Results.BadRequest(new { error = "Elegí portero o jugador de campo" });
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var tx = await db.BeginTransactionAsync();
            await using var m = new NpgsqlCommand("""
                SELECT m.id,m.status,m.goalkeeper_slots,m.field_slots,m.season_id,m.match_date
                FROM matches m WHERE m.share_token=@token FOR UPDATE OF m
                """, db, tx);
            m.Parameters.AddWithValue("token", token);
            Guid matchId, seasonId;
            int limit;
            DateTime matchDate;
            await using (var r = await m.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound();
                if (r.GetString(1) != "scheduled") return Results.Conflict(new { error = "La inscripción ya está cerrada" });
                matchId = r.GetGuid(0);
                limit = req.Role == "goalkeeper" ? r.GetInt16(2) : r.GetInt16(3);
                seasonId = r.GetGuid(4);
                matchDate = r.GetDateTime(5);
            }
            await using var eligible = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM players p JOIN player_accounts pa ON pa.player_id=p.id
                  JOIN memberships mb ON mb.player_id=p.id AND mb.season_id=@season
                  WHERE p.id=@p AND p.active AND mb.starts_on<=@date)
                """, db, tx);
            eligible.Parameters.AddWithValue("season", seasonId);
            eligible.Parameters.AddWithValue("p", player.Value);
            eligible.Parameters.AddWithValue("date", matchDate);
            if (!(bool)(await eligible.ExecuteScalarAsync())!) return Results.StatusCode(403);
            await using var count = new NpgsqlCommand("""
                SELECT COUNT(*) FROM match_signups
                WHERE match_id=@m AND role=@role AND status='confirmed' AND player_id<>@p
                """, db, tx);
            count.Parameters.AddWithValue("m", matchId);
            count.Parameters.AddWithValue("role", req.Role);
            count.Parameters.AddWithValue("p", player.Value);
            var used = (long)(await count.ExecuteScalarAsync())!;
            if (used >= limit) return Results.Conflict(new { error = req.Role == "goalkeeper" ? "Ya están ocupados los 2 cupos de portero" : "Ya están ocupados los 12 cupos de campo" });
            await using var save = new NpgsqlCommand("""
                INSERT INTO match_signups(match_id,player_id,role,status)
                VALUES (@m,@p,@role,'confirmed')
                ON CONFLICT(match_id,player_id) DO UPDATE
                  SET role=excluded.role,status='confirmed',updated_at=now()
                """, db, tx);
            save.Parameters.AddWithValue("m", matchId);
            save.Parameters.AddWithValue("p", player.Value);
            save.Parameters.AddWithValue("role", req.Role);
            await save.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return Results.Ok(new { role = req.Role });
        });

        app.MapDelete("/community/matches/{token:guid}/signup", async (Guid token, HttpContext ctx) =>
        {
            var player = PlayerFrom(ctx, secret);
            if (player is null) return Results.Unauthorized();
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                UPDATE match_signups s SET status='cancelled',updated_at=now()
                FROM matches m WHERE s.match_id=m.id AND m.share_token=@token
                  AND m.status='scheduled' AND s.player_id=@player
                """, db);
            cmd.Parameters.AddWithValue("token", token);
            cmd.Parameters.AddWithValue("player", player.Value);
            var changed = await cmd.ExecuteNonQueryAsync();
            return changed > 0 ? Results.Ok(new { cancelled = true }) :
                Results.Conflict(new { error = "No hay inscripción activa o la mejenga está cerrada" });
        });
    }
}

public record RegisterPlayer(Guid MatchToken, string Name, string Email, string Password);
public record PlayerLogin(string Email, string Password);
public record SignupRequest(string Role);
