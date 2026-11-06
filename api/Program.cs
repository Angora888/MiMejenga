using System.Security.Cryptography;
using System.Text;
using Npgsql;

var builder=WebApplication.CreateBuilder(args);
var connection=Environment.GetEnvironmentVariable("DATABASE_URL") ?? throw new Exception("DATABASE_URL is required");
var admin=Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? throw new Exception("ADMIN_PASSWORD is required");
var secret=Environment.GetEnvironmentVariable("SESSION_SECRET") ?? throw new Exception("SESSION_SECRET is required");
if(admin.Length<12||secret.Length<32) throw new Exception("Configure secure ADMIN_PASSWORD (12+) and SESSION_SECRET (32+)");
builder.Services.AddCors(x=>x.AddPolicy("web",p=>p.WithOrigins(Environment.GetEnvironmentVariable("WEB_ORIGIN")??"http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
var app=builder.Build();
app.UseCors("web");
bool Valid(string token){
 try{
  var segments=token.Split('.');
  if(segments.Length!=2)return false;
  var signature=Convert.FromBase64String(segments[1]);
  using var h=new HMACSHA256(Encoding.UTF8.GetBytes(secret));
  var expected=h.ComputeHash(Encoding.UTF8.GetBytes(segments[0]));
  return CryptographicOperations.FixedTimeEquals(signature,expected)&&long.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segments[0])))>DateTimeOffset.UtcNow.ToUnixTimeSeconds();
 }catch{return false;}
}
app.Use(async(ctx,next)=>{
 if(ctx.Request.Path.StartsWithSegments("/api")&&!ctx.Request.Path.StartsWithSegments("/api/login")&&!ctx.Request.Path.StartsWithSegments("/api/health")){
 var header=ctx.Request.Headers.Authorization.ToString();
 if(!header.StartsWith("Bearer ")||!Valid(header[7..])){ctx.Response.StatusCode=401;return;}
 }
 await next();
});
app.MapGet("/api/health",()=>Results.Ok(new {status="ok"}));
app.MapPost("/api/login",(Login req)=>{
 var a=Encoding.UTF8.GetBytes(req.Password??"");var b=Encoding.UTF8.GetBytes(admin);
 if(a.Length!=b.Length||!CryptographicOperations.FixedTimeEquals(a,b))return Results.Unauthorized();
 var payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds().ToString()));
 using var h=new HMACSHA256(Encoding.UTF8.GetBytes(secret));
 return Results.Ok(new {token=payload+"."+Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(payload)))});
});
async Task<List<Dictionary<string,object?>>> Query(string sql,params (string Key,object? Value)[] parameters){
 await using var db=new NpgsqlConnection(connection);
 await db.OpenAsync();
 await using var cmd=new NpgsqlCommand(sql,db);
 foreach(var p in parameters)cmd.Parameters.AddWithValue(p.Key,p.Value??DBNull.Value);
 await using var reader=await cmd.ExecuteReaderAsync();
 var output=new List<Dictionary<string,object?>>();
 while(await reader.ReadAsync()){
  var row=new Dictionary<string,object?>();
  for(int i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=await reader.IsDBNullAsync(i)?null:reader.GetValue(i);
  output.Add(row);
 }
 return output;
}
async Task<int> Execute(string sql,params (string Key,object? Value)[] parameters){
 await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
 await using var cmd=new NpgsqlCommand(sql,db);
 foreach(var p in parameters)cmd.Parameters.AddWithValue(p.Key,p.Value??DBNull.Value);
 return await cmd.ExecuteNonQueryAsync();
}
async Task<object?> Scalar(string sql,params (string Key,object? Value)[] parameters){
 await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
 await using var cmd=new NpgsqlCommand(sql,db);
 foreach(var p in parameters)cmd.Parameters.AddWithValue(p.Key,p.Value??DBNull.Value);
 return await cmd.ExecuteScalarAsync();
}
app.MapGet("/api/seasons",async()=>Results.Ok(await Query("SELECT id,year,weekly_fee,absence_fee FROM seasons ORDER BY year DESC")));
app.MapPost("/api/seasons",async(Season req)=>{
 if(req.Year<2020||req.Year>2100||req.WeeklyFee<=0||req.AbsenceFee<=0)return Results.BadRequest();
 try{return Results.Ok(new {id=await Scalar("INSERT INTO seasons(year,weekly_fee,absence_fee) VALUES(@y,@w,@a) RETURNING id",("y",req.Year),("w",req.WeeklyFee),("a",req.AbsenceFee))});}
 catch(PostgresException e)when(e.SqlState=="23505"){return Results.Conflict(new {error="Temporada duplicada"});}
});
app.MapGet("/api/players",async()=>Results.Ok(await Query("SELECT id,name,active FROM players ORDER BY name")));
app.MapPost("/api/players",async(Player req)=>{
 if(string.IsNullOrWhiteSpace(req.Name)||req.Name.Length>100)return Results.BadRequest();
 try{return Results.Ok(new {id=await Scalar("INSERT INTO players(name) VALUES(@n) RETURNING id",("n",req.Name.Trim()))});}
 catch(PostgresException e)when(e.SqlState=="23505"){return Results.Conflict(new {error="Jugador duplicado"});}
});
app.MapPost("/api/players/{id:guid}/aliases",async(Guid id,Alias req)=>{
 if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest();
 await Execute("INSERT INTO aliases(alias,player_id) VALUES(@a,@p)",("a",req.Name.Trim()),("p",id));return Results.Ok();
});
app.MapPost("/api/seasons/{id:guid}/members",async(Guid id,Member req)=>{
 await Execute("INSERT INTO memberships(season_id,player_id,starts_on,charge_absences) VALUES(@s,@p,@d,@c) ON CONFLICT(season_id,player_id) DO UPDATE SET starts_on=excluded.starts_on,charge_absences=excluded.charge_absences",("s",id),("p",req.PlayerId),("d",req.StartsOn.ToDateTime(TimeOnly.MinValue)),("c",req.ChargeAbsences));return Results.Ok();
});
app.MapGet("/api/seasons/{id:guid}/summary",async(Guid id)=>Results.Ok(await Query("SELECT player_id,name,games,attended,missed,credits,chargeable,due FROM season_balances WHERE season_id=@s ORDER BY due,attended DESC,name",("s",id))));
app.MapGet("/api/seasons/{id:guid}/matches",async(Guid id)=>Results.Ok(await Query("SELECT id,match_date,status,notes FROM matches WHERE season_id=@s ORDER BY match_date DESC",("s",id))));
app.MapPost("/api/seasons/{id:guid}/matches",async(Guid id,Match req)=>{
 if(!new[]{"played","scheduled","cancelled"}.Contains(req.Status))return Results.BadRequest();
 try{return Results.Ok(new{id=await Scalar("INSERT INTO matches(season_id,match_date,status,notes) VALUES(@s,@d,@st,@n) RETURNING id",("s",id),("d",req.Date.ToDateTime(TimeOnly.MinValue)),("st",req.Status),("n",req.Notes??""))});}
 catch(PostgresException e)when(e.SqlState=="23505"){return Results.Conflict(new{error="Ya existe una mejenga en esa fecha"});}
});
app.MapPut("/api/matches/{id:guid}/status",async(Guid id,Status req)=>{
 if(!new[]{"played","scheduled","cancelled"}.Contains(req.Value))return Results.BadRequest();
 await Execute("UPDATE matches SET status=@st WHERE id=@id",("st",req.Value),("id",id));return Results.Ok();
});
app.MapGet("/api/matches/{id:guid}/details",async(Guid id)=>Results.Ok(new{
 attendance=await Query("SELECT player_id,present,laundry FROM attendances WHERE match_id=@m",("m",id)),
 payments=await Query("SELECT id,player_id,amount,method,notes FROM payments WHERE match_id=@m",("m",id))
}));
app.MapPut("/api/matches/{id:guid}/attendance/{player:guid}",async(Guid id,Guid player,Attendance req)=>{
 await Execute("INSERT INTO attendances(match_id,player_id,present,laundry) VALUES(@m,@p,@a,@l) ON CONFLICT(match_id,player_id) DO UPDATE SET present=excluded.present,laundry=excluded.laundry",("m",id),("p",player),("a",req.Present),("l",req.Laundry));return Results.Ok();
});
app.MapPost("/api/matches/{id:guid}/payments",async(Guid id,Payment req)=>{
 if(req.Amount<=0||!new[]{"sinpe","efectivo","otro"}.Contains(req.Method))return Results.BadRequest();
 return Results.Ok(new{id=await Scalar("INSERT INTO payments(match_id,player_id,amount,method,notes) VALUES(@m,@p,@a,@method,@n) RETURNING id",("m",id),("p",req.PlayerId),("a",req.Amount),("method",req.Method),("n",req.Notes??""))});
});
app.MapDelete("/api/payments/{id:guid}",async(Guid id)=>{await Execute("DELETE FROM payments WHERE id=@id",("id",id));return Results.NoContent();});
app.Run();

record Login(string Password);
record Season(int Year,int WeeklyFee,int AbsenceFee);
record Player(string Name);
record Alias(string Name);
record Member(Guid PlayerId,DateOnly StartsOn,bool ChargeAbsences=true);
record Match(DateOnly Date,string Status,string? Notes);
record Status(string Value);
record Attendance(bool Present,bool Laundry);
record Payment(Guid PlayerId,int Amount,string Method,string? Notes);
