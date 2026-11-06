import React, {useEffect,useState} from 'react';
import {createRoot} from 'react-dom/client';
import './style.css';

const API=import.meta.env.VITE_API_URL||'http://localhost:5080';
type Player={id:string;name:string;active:boolean};
type Season={id:string;year:number;weekly_fee:number;absence_fee:number};
type Match={id:string;match_date:string;status:string;notes:string};
type Balance={player_id:string;name:string;games:number;attended:number;missed:number;credits:number;chargeable:number;due:number};
type Detail={attendance:{player_id:string;present:boolean;laundry:boolean}[];payments:{id:string;player_id:string;amount:number;method:string;notes:string}[]};
const money=(n:number)=>'₡'+Number(n||0).toLocaleString('es-CR');
function App(){
 const [token,setToken]=useState(sessionStorage.getItem('mm_token')||'');
 const [password,setPassword]=useState('');
 const [seasons,setSeasons]=useState<Season[]>([]);
 const [season,setSeason]=useState('');
 const [players,setPlayers]=useState<Player[]>([]);
 const [summary,setSummary]=useState<Balance[]>([]);
 const [matches,setMatches]=useState<Match[]>([]);
 const [selectedMatch,setSelectedMatch]=useState('');
 const [detail,setDetail]=useState<Detail>({attendance:[],payments:[]});
 const [newPlayer,setNewPlayer]=useState('');
 const [newAlias,setNewAlias]=useState('');
 const [aliasPlayer,setAliasPlayer]=useState('');
 const [member,setMember]=useState('');
 const [startDate,setStartDate]=useState('2026-01-01');
 const [charge,setCharge]=useState(true);
 const [matchDate,setMatchDate]=useState(new Date().toISOString().slice(0,10));
 const [status,setStatus]=useState('played');
 const [paymentPlayer,setPaymentPlayer]=useState('');
 const [amount,setAmount]=useState('1500');
 const [method,setMethod]=useState('sinpe');
 const [message,setMessage]=useState('');
 const [busy,setBusy]=useState(false);
 async function request(path:string,opts:RequestInit={}):Promise<any>{
  const response=await fetch(API+path,{...opts,headers:{'Content-Type':'application/json',Authorization:'Bearer '+token,...opts.headers}});
  if(response.status===401){if(path!=='/api/login'){sessionStorage.removeItem('mm_token');setToken('');}throw Error('Sesión vencida o contraseña incorrecta');}
  if(!response.ok){let data:any={};try{data=await response.json();}catch{}throw Error(data.error||'Error HTTP '+response.status);}
  if(response.status===204)return null;
  return response.json();
 }
 async function act(job:()=>Promise<any>){
  setBusy(true);setMessage('');
  try{await job();setMessage('✓ Cambios guardados');}catch(e:any){setMessage('⚠ '+e.message);}finally{setBusy(false);}
 }
 async function login(e:React.FormEvent){
  e.preventDefault();setBusy(true);setMessage('');
  try{const r=await request('/api/login',{method:'POST',body:JSON.stringify({password})});sessionStorage.setItem('mm_token',r.token);setToken(r.token);setPassword('');}
  catch(e:any){setMessage(e.message);}finally{setBusy(false);}
 }
 async function loadBase(){const [s,p]=await Promise.all([request('/api/seasons'),request('/api/players')]);setSeasons(s);setPlayers(p);if(s.length&&!season)setSeason(s[0].id);}
 async function loadSeason(){if(!season)return;const [s,m]=await Promise.all([request('/api/seasons/'+season+'/summary'),request('/api/seasons/'+season+'/matches')]);setSummary(s);setMatches(m);}
 async function loadDetail(){if(selectedMatch)setDetail(await request('/api/matches/'+selectedMatch+'/details'));}
 useEffect(()=>{if(token)loadBase().catch(e=>setMessage(e.message));},[token]);
 useEffect(()=>{if(token&&season)loadSeason().catch(e=>setMessage(e.message));},[token,season]);
 useEffect(()=>{if(token&&selectedMatch)loadDetail().catch(e=>setMessage(e.message));},[token,selectedMatch]);
 const s=seasons.find(x=>x.id===season);
 const match=matches.find(x=>x.id===selectedMatch);
 const members=summary.filter(x=>players.some(p=>p.id===x.player_id));
 if(!token)return <main className="login"><div className="login-card"><div className="ball">⚽</div><h1>Mi Mejenga</h1><p>Directiva SM · Acceso privado</p><form onSubmit={login}><label>Contraseña administrativa<input autoFocus type="password" value={password} onChange={e=>setPassword(e.target.value)} required/></label><button disabled={busy}>Entrar</button></form>{message&&<p className="message">{message}</p>}</div></main>;
 return <main className="page">
  <header><div className="brand"><span>⚽</span><div><h1>Mi Mejenga</h1><small>Directiva SM · Control anual</small></div></div><button className="secondary" onClick={()=>{sessionStorage.removeItem('mm_token');setToken('');}}>Salir</button></header>
  {message&&<div className="notice">{message}</div>}
  <section className="toolbar card"><label>Temporada<select value={season} onChange={e=>{setSeason(e.target.value);setSelectedMatch('');}}>{seasons.map(x=><option key={x.id} value={x.id}>{x.year}</option>)}</select></label>
   <button disabled={busy} onClick={()=>act(async()=>{const year=Number(prompt('Año de temporada','2026'));if(!year)return;await request('/api/seasons',{method:'POST',body:JSON.stringify({year,weeklyFee:1500,absenceFee:500})});await loadBase();})}>+ Temporada</button>
   <span className="muted">Cuota: {money(s?.weekly_fee||1500)} · Falta: {money(s?.absence_fee||500)}</span>
  </section>
  <div className="stats"><article><small>Mejengas jugadas</small><strong>{matches.filter(x=>x.status==='played').length}</strong></article><article><small>Jugadores inscritos</small><strong>{summary.length}</strong></article><article><small>Saldo teórico</small><strong>{money(summary.reduce((a,x)=>a+x.due,0))}</strong></article><article><small>Créditos extra</small><strong>{summary.reduce((a,x)=>a+x.credits,0)}</strong></article></div>
  <div className="columns">
   <div className="stack">
    <section className="card"><h2>Jugadores</h2><form className="inline" onSubmit={e=>{e.preventDefault();act(async()=>{await request('/api/players',{method:'POST',body:JSON.stringify({name:newPlayer})});setNewPlayer('');await loadBase();});}}><input placeholder="Nombre del jugador" value={newPlayer} onChange={e=>setNewPlayer(e.target.value)} required/><button disabled={busy}>Agregar</button></form>
    <h3>Inscribir a la temporada</h3><form onSubmit={e=>{e.preventDefault();act(async()=>{await request('/api/seasons/'+season+'/members',{method:'POST',body:JSON.stringify({playerId:member,startsOn:startDate,chargeAbsences:charge})});await loadSeason();});}}><div className="form-grid"><select required value={member} onChange={e=>setMember(e.target.value)}><option value="">Elegir jugador</option>{players.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select><input type="date" value={startDate} onChange={e=>setStartDate(e.target.value)} required/></div><label className="check"><input type="checkbox" checked={charge} onChange={e=>setCharge(e.target.checked)}/> Cobrar ausencias</label><button disabled={!season||busy}>Inscribir / actualizar</button></form>
    <h3>Apodos</h3><form className="inline" onSubmit={e=>{e.preventDefault();act(async()=>{await request('/api/players/'+aliasPlayer+'/aliases',{method:'POST',body:JSON.stringify({name:newAlias})});setNewAlias('');});}}><select required value={aliasPlayer} onChange={e=>setAliasPlayer(e.target.value)}><option value="">Jugador</option>{players.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select><input placeholder="Apodo: Tronco, Pucho..." required value={newAlias} onChange={e=>setNewAlias(e.target.value)}/><button disabled={busy}>Guardar</button></form></section>
    <section className="card"><h2>Crear mejenga</h2><form onSubmit={e=>{e.preventDefault();act(async()=>{await request('/api/seasons/'+season+'/matches',{method:'POST',body:JSON.stringify({date:matchDate,status,notes:''})});await loadSeason();});}}><div className="form-grid"><input type="date" value={matchDate} required onChange={e=>setMatchDate(e.target.value)}/><select value={status} onChange={e=>setStatus(e.target.value)}><option value="played">Jugó</option><option value="scheduled">Programada</option><option value="cancelled">Cancelada</option></select></div><button disabled={!season||busy}>Registrar mejenga</button></form><h3>Fechas registradas</h3><div className="match-list">{matches.map(m=><button key={m.id} className={selectedMatch===m.id?'match active':'match'} onClick={()=>setSelectedMatch(m.id)}>{m.match_date.slice(0,10)} · {m.status==='played'?'Jugó':m.status==='cancelled'?'Cancelada':'Programada'}</button>)}</div></section>
   </div>
   <div className="stack">
    <section className="card"><h2>Ranking · {s?.year||'Temporada'}</h2><p className="muted">Asistencias reales y créditos pagados se muestran por separado.</p><div className="table-wrap"><table><thead><tr><th>Jugador</th><th>Jugó</th><th>Faltó</th><th>Créditos</th><th>Debe</th></tr></thead><tbody>{summary.map(p=><tr key={p.player_id}><td>{p.name}</td><td>{p.attended}</td><td>{p.missed}</td><td>{p.credits}</td><td className="debt">{money(p.due)}</td></tr>)}</tbody></table></div>{!summary.length&&<p className="muted">Inscribí jugadores para ver sus balances.</p>}</section>
    {match&&<section className="card"><h2>Mejenga del {match.match_date.slice(0,10)}</h2><p className="muted">Marcá presentes y lavada; después agregá el pago registrado.</p><div className="attend">{members.map(p=>{const att=detail.attendance.find(a=>a.player_id===p.player_id);return <div className="att-row" key={p.player_id}><b>{p.name}</b><label><input type="checkbox" checked={att?.present||false} onChange={e=>act(async()=>{await request('/api/matches/'+match.id+'/attendance/'+p.player_id,{method:'PUT',body:JSON.stringify({present:e.target.checked,laundry:att?.laundry||false})});await loadDetail();await loadSeason();})}/> Presente</label><label><input type="checkbox" checked={att?.laundry||false} onChange={e=>act(async()=>{await request('/api/matches/'+match.id+'/attendance/'+p.player_id,{method:'PUT',body:JSON.stringify({present:att?.present||false,laundry:e.target.checked})});await loadDetail();})}/> Lavada</label></div>})}</div><h3>Registrar pago</h3><form onSubmit={e=>{e.preventDefault();act(async()=>{await request('/api/matches/'+match.id+'/payments',{method:'POST',body:JSON.stringify({playerId:paymentPlayer,amount:Number(amount),method,notes:''})});await loadDetail();await loadSeason();});}}><div className="form-grid"><select required value={paymentPlayer} onChange={e=>setPaymentPlayer(e.target.value)}><option value="">Jugador</option>{members.map(p=><option key={p.player_id} value={p.player_id}>{p.name}</option>)}</select><input type="number" min="1" step="1" value={amount} onChange={e=>setAmount(e.target.value)}/><select value={method} onChange={e=>setMethod(e.target.value)}><option value="sinpe">SINPE</option><option value="efectivo">Efectivo</option><option value="otro">Otro</option></select></div><button disabled={busy}>Guardar pago</button></form><div className="payments">{detail.payments.map(p=><div key={p.id} className="payment"><span>{players.find(x=>x.id===p.player_id)?.name||'Jugador'} · {money(p.amount)} · {p.method}</span><button className="danger secondary" onClick={()=>act(async()=>{await request('/api/payments/'+p.id,{method:'DELETE'});await loadDetail();await loadSeason();})}>Eliminar</button></div>)}</div></section>}
   </div>
  </div>
  <footer>Directiva SM · MVP privado · Revisar los datos históricos antes de importarlos</footer>
 </main>;
}
createRoot(document.getElementById('root')!).render(<App/>);
