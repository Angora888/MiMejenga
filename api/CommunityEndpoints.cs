using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Npgsql;

public static class CommunityEndpoints
{
    private static string NewToken(Guid playerId, string secret)
    {
        var body=Convert.ToBase64String(Encoding.UTF8.GetBytes(playerId+":"+DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()));
        using var h=new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "p:"+body+"."+Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }
    private static Guid? PlayerFrom(HttpContext ctx,string secret)
    {
        try{
            var header=ctx.Request.Headers.Authorization.ToString();
            if(!header.StartsWith("Bearer p:"))return null;
            var pieces=header[9..].Split('.');
            if(pieces.Length!=2)return null;
            using var h=new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            if(!CryptographicOperations.FixedTimeEquals(h.ComputeHash(Encoding.UTF8.GetBytes(pieces[0])),Convert.FromBase64String(pieces[1])))return null;
            var values=Encoding.UTF8.GetString(Convert.FromBase64String(pieces[0])).Split(':');
            return values.Length==2 && long.Parse(values[1],CultureInfo.InvariantCulture)>DateTimeOffset.UtcNow.ToUnixTimeSeconds() && Guid.TryParse(values[0],out var id)?id:null;
        }catch{return null;}
    }
    public static void MapCommunityEndpoints(this WebApplication app,string connection,string secret)
    {
        var googleClientId=Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? "";
        app.MapPost("/community/google",async(GoogleLogin req)=>{
            if(string.IsNullOrWhiteSpace(googleClientId)||string.IsNullOrWhiteSpace(req.Credential)||req.Credential.Length>12000)
                return Results.BadRequest(new{error="Google Sign-In no configurado"});
            GoogleJsonWebSignature.Payload jwt;
            try{
                jwt=await GoogleJsonWebSignature.ValidateAsync(req.Credential,new GoogleJsonWebSignature.ValidationSettings{Audience=new[]{googleClientId}});
            }catch(Exception){return Results.Unauthorized();}
            if(string.IsNullOrWhiteSpace(jwt.Subject)||jwt.EmailVerified!=true)return Results.Unauthorized();
            var email=(jwt.Email??"").Trim().ToLowerInvariant();
            var displayName=(jwt.Name??email.Split('@')[0]).Trim();
            if(displayName.Length>80)displayName=displayName[..80];
            if(displayName.Length<2)displayName="Jugador";
            await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
            await using var tx=(NpgsqlTransaction)await db.BeginTransactionAsync();
            await using var find=new NpgsqlCommand("SELECT player_id FROM google_player_accounts WHERE google_sub=@sub FOR UPDATE",db,tx);
            find.Parameters.AddWithValue("sub",jwt.Subject);
            Guid playerId;
            var existing=await find.ExecuteScalarAsync();
            if(existing is Guid id){playerId=id;}
            else{
                var reserveId=Guid.NewGuid();
                await using var create=new NpgsqlCommand("""
                    INSERT INTO players(id,name) VALUES(@id,@name)
                    ON CONFLICT(name) DO NOTHING RETURNING id
                    """,db,tx);
                create.Parameters.AddWithValue("id",reserveId);create.Parameters.AddWithValue("name",displayName);
                var made=await create.ExecuteScalarAsync();
                if(made is Guid created)playerId=created;
                else{
                    playerId=reserveId;
                    await using var fallback=new NpgsqlCommand("INSERT INTO players(id,name) VALUES(@id,@name)",db,tx);
                    fallback.Parameters.AddWithValue("id",playerId);
                    fallback.Parameters.AddWithValue("name",displayName+" ("+playerId.ToString()[..6]+")");
                    await fallback.ExecuteNonQueryAsync();
                }
                await using var account=new NpgsqlCommand("""
                    INSERT INTO google_player_accounts(google_sub,player_id,email,picture_url)
                    VALUES(@sub,@player,@email,@picture)
                    """,db,tx);
                account.Parameters.AddWithValue("sub",jwt.Subject);account.Parameters.AddWithValue("player",playerId);
                account.Parameters.AddWithValue("email",email);account.Parameters.AddWithValue("picture",jwt.Picture??"");
                await account.ExecuteNonQueryAsync();
            }
            await using(var update=new NpgsqlCommand("""
                UPDATE google_player_accounts SET email=@email,picture_url=@picture,last_login_at=now() WHERE google_sub=@sub
                """,db,tx)){
                update.Parameters.AddWithValue("sub",jwt.Subject);update.Parameters.AddWithValue("email",email);update.Parameters.AddWithValue("picture",jwt.Picture??"");
                await update.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return Results.Ok(new{token=NewToken(playerId,secret)});
        });

        app.MapGet("/community/me",async(HttpContext ctx)=>{
            var id=PlayerFrom(ctx,secret);
            if(id is null)return Results.Unauthorized();
            await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
            await using var cmd=new NpgsqlCommand("""
                SELECT p.name,g.email,g.picture_url FROM players p
                JOIN google_player_accounts g ON g.player_id=p.id
                WHERE p.id=@id AND p.active=true
                """,db);
            cmd.Parameters.AddWithValue("id",id.Value);
            await using var r=await cmd.ExecuteReaderAsync();
            return await r.ReadAsync()?Results.Ok(new{id,name=r.GetString(0),email=r.GetString(1),picture=r.GetString(2)}):Results.Unauthorized();
        });

        app.MapGet("/community/dashboard",async(HttpContext ctx)=>{
            var id=PlayerFrom(ctx,secret);if(id is null)return Results.Unauthorized();
            await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
            var histories=new List<object>();
            await using(var cmd=new NpgsqlCommand("""
                SELECT s.year,b.games,b.attended,b.missed,b.credits,b.chargeable,b.due,
                COALESCE((SELECT sum(p.amount)::bigint FROM payments p JOIN matches m ON m.id=p.match_id
                    WHERE p.player_id=@player AND m.season_id=s.id),0) AS contributed,
                COALESCE((SELECT count(*)::int FROM attendances a JOIN matches m ON m.id=a.match_id
                    WHERE a.player_id=@player AND a.laundry=true AND m.season_id=s.id),0) AS laundry_count
                FROM memberships mb JOIN seasons s ON s.id=mb.season_id
                JOIN season_balances b ON b.season_id=mb.season_id AND b.player_id=mb.player_id
                WHERE mb.player_id=@player ORDER BY s.year DESC
                """,db)){
                cmd.Parameters.AddWithValue("player",id.Value);
                await using var r=await cmd.ExecuteReaderAsync();
                while(await r.ReadAsync())histories.Add(new{year=r.GetInt32(0),matches=r.GetInt32(1),played=r.GetInt32(2),missed=r.GetInt32(3),credits=r.GetInt32(4),chargeable=r.GetInt32(5),due=r.GetInt32(6),contributed=r.GetInt64(7),laundry=r.GetInt32(8)});
            }
            var payments=new List<object>();
            await using(var cmd=new NpgsqlCommand("""
                SELECT m.match_date,p.amount,p.method,p.notes
                FROM payments p JOIN matches m ON m.id=p.match_id
                WHERE p.player_id=@player ORDER BY m.match_date DESC,p.created_at DESC LIMIT 100
                """,db)){
                cmd.Parameters.AddWithValue("player",id.Value);
                await using var r=await cmd.ExecuteReaderAsync();
                while(await r.ReadAsync())payments.Add(new{date=r.GetDateTime(0).ToString("yyyy-MM-dd"),amount=r.GetInt32(1),method=r.GetString(2),notes=r.GetString(3)});
            }
            var played=new List<object>();
            await using(var cmd=new NpgsqlCommand("""
                SELECT m.match_date,a.laundry FROM attendances a JOIN matches m ON m.id=a.match_id
                WHERE a.player_id=@player AND a.present=true AND m.status='played'
                ORDER BY m.match_date DESC LIMIT 100
                """,db)){
                cmd.Parameters.AddWithValue("player",id.Value);
                await using var r=await cmd.ExecuteReaderAsync();
                while(await r.ReadAsync())played.Add(new{date=r.GetDateTime(0).ToString("yyyy-MM-dd"),laundry=r.GetBoolean(1)});
            }
            return Results.Ok(new{seasons=histories,payments,played});
        });

        app.MapGet("/community/matches/{token:guid}",async(Guid token)=>{
            await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
            await using var cmd=new NpgsqlCommand("""
                SELECT m.id,m.match_date,m.status,m.goalkeeper_slots,m.field_slots,s.year,s.weekly_fee
                FROM matches m JOIN seasons s ON s.id=m.season_id WHERE m.share_token=@token
                """,db);
            cmd.Parameters.AddWithValue("token",token);
            Guid matchId;DateTime date;string status;short keepers,field;int year,fee;
            await using(var r=await cmd.ExecuteReaderAsync()){
                if(!await r.ReadAsync())return Results.NotFound();
                matchId=r.GetGuid(0);date=r.GetDateTime(1);status=r.GetString(2);
                keepers=r.GetInt16(3);field=r.GetInt16(4);year=r.GetInt32(5);fee=r.GetInt32(6);
            }
            var players=new List<object>();int countK=0,countF=0;
            await using(var rcmd=new NpgsqlCommand("""
                SELECT p.name,ms.role FROM match_signups ms JOIN players p ON p.id=ms.player_id
                WHERE ms.match_id=@id AND ms.status='confirmed' ORDER BY ms.created_at,p.name
                """,db)){
                rcmd.Parameters.AddWithValue("id",matchId);
                await using var r=await rcmd.ExecuteReaderAsync();
                while(await r.ReadAsync()){
                    var role=r.GetString(1);
                    if(role=="goalkeeper")countK++;else countF++;
                    players.Add(new{name=r.GetString(0),role});
                }
            }
            return Results.Ok(new{date=date.ToString("yyyy-MM-dd"),status,year,fee,goalkeeperSlots=keepers,fieldSlots=field,goalkeeperCount=countK,fieldCount=countF,players});
        });

        app.MapGet("/community/matches/{token:guid}/mine",async(Guid token,HttpContext ctx)=>{
            var player=PlayerFrom(ctx,secret);if(player is null)return Results.Unauthorized();
            await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
            await using var cmd=new NpgsqlCommand("""
                SELECT ms.role FROM match_signups ms JOIN matches m ON m.id=ms.match_id
                WHERE m.share_token=@t AND ms.player_id=@p AND ms.status='confirmed'
                """,db);
            cmd.Parameters.AddWithValue("t",token);cmd.Parameters.AddWithValue("p",player.Value);
            return Results.Ok(new{role=(await cmd.ExecuteScalarAsync())?.ToString()});
        });

        app.MapPut("/community/matches/{token:guid}/signup",async(Guid token,SignupRequest req,HttpContext ctx)=>{
            var player=PlayerFrom(ctx,secret);if(player is null)return Results.Unauthorized();
            if(req.Role!="goalkeeper"&&req.Role!="field")return Results.BadRequest(new{error="Elegí portero o jugador"});
            await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
            await using var tx=(NpgsqlTransaction)await db.BeginTransactionAsync();
            Guid matchId,seasonId;DateTime matchDate;int limit;
            await using(var lockMatch=new NpgsqlCommand("""
                SELECT id,season_id,match_date,status,goalkeeper_slots,field_slots
                FROM matches WHERE share_token=@t FOR UPDATE
                """,db,tx)){
                lockMatch.Parameters.AddWithValue("t",token);
                await using var r=await lockMatch.ExecuteReaderAsync();
                if(!await r.ReadAsync())return Results.NotFound();
                if(r.GetString(3)!="scheduled")return Results.Conflict(new{error="La inscripción está cerrada"});
                matchId=r.GetGuid(0);seasonId=r.GetGuid(1);matchDate=r.GetDateTime(2);
                limit=req.Role=="goalkeeper"?r.GetInt16(4):r.GetInt16(5);
            }
            await using(var insertMembership=new NpgsqlCommand("""
                INSERT INTO memberships(season_id,player_id,starts_on,charge_absences)
                VALUES(@s,@p,@d,true)
                ON CONFLICT(season_id,player_id) DO NOTHING
                """,db,tx)){
                insertMembership.Parameters.AddWithValue("s",seasonId);insertMembership.Parameters.AddWithValue("p",player.Value);insertMembership.Parameters.AddWithValue("d",matchDate);
                await insertMembership.ExecuteNonQueryAsync();
            }
            await using(var count=new NpgsqlCommand("""
                SELECT count(*) FROM match_signups WHERE match_id=@m AND role=@role
                AND status='confirmed' AND player_id<>@p
                """,db,tx)){
                count.Parameters.AddWithValue("m",matchId);count.Parameters.AddWithValue("role",req.Role);count.Parameters.AddWithValue("p",player.Value);
                if((long)(await count.ExecuteScalarAsync())! >= limit)return Results.Conflict(new{error="Cupos completos para esa posición"});
            }
            await using(var save=new NpgsqlCommand("""
                INSERT INTO match_signups(match_id,player_id,role,status)
                VALUES(@m,@p,@role,'confirmed')
                ON CONFLICT(match_id,player_id) DO UPDATE SET role=excluded.role,status='confirmed',updated_at=now()
                """,db,tx)){
                save.Parameters.AddWithValue("m",matchId);save.Parameters.AddWithValue("p",player.Value);save.Parameters.AddWithValue("role",req.Role);
                await save.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return Results.Ok(new{role=req.Role});
        });

        app.MapDelete("/community/matches/{token:guid}/signup",async(Guid token,HttpContext ctx)=>{
            var player=PlayerFrom(ctx,secret);if(player is null)return Results.Unauthorized();
            await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
            await using var cmd=new NpgsqlCommand("""
                UPDATE match_signups ms SET status='cancelled',updated_at=now()
                FROM matches m WHERE ms.match_id=m.id AND m.share_token=@t
                AND m.status='scheduled' AND ms.player_id=@p
                """,db);
            cmd.Parameters.AddWithValue("t",token);cmd.Parameters.AddWithValue("p",player.Value);
            return await cmd.ExecuteNonQueryAsync()>0?Results.Ok(new{cancelled=true}):Results.Conflict(new{error="No se puede cancelar la inscripción"});
        });
    }
}
public record GoogleLogin(string Credential);
public record SignupRequest(string Role);
