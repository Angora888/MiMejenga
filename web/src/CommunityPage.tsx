import React,{useCallback,useEffect,useState} from 'react';
import {GoogleSignIn} from './GoogleSignIn';
import './community.css';
const API=import.meta.env.VITE_API_URL||'http://localhost:5080';
type MatchInfo={date:string;status:string;year:number;fee:number;goalkeeperSlots:number;fieldSlots:number;goalkeeperCount:number;fieldCount:number;players:{name:string;role:string}[]};
const matchToken=(window.location.pathname.match(/^\/m\/([0-9a-f-]{36})\/?$/i)||[])[1]||'';
export default function CommunityPage(){
 const [game,setGame]=useState<MatchInfo|null>(null);
 const [auth,setAuth]=useState(sessionStorage.getItem('mm_player_token')||'');
 const [me,setMe]=useState<{name:string;id:string}|null>(null);
 const [role,setRole]=useState<string|null>(null);
 const [notice,setNotice]=useState(''),[busy,setBusy]=useState(false);
 async function request(path:string,options:RequestInit={},session=auth){
  const response=await fetch(API+path,{...options,headers:{'Content-Type':'application/json',...(session?{Authorization:'Bearer '+session}:{}),...options.headers}});
  let data:any={};try{data=await response.json();}catch{}
  if(!response.ok)throw Error(data.error||'Error HTTP '+response.status);
  return data;
 }
 async function refresh(){
  const g=await request('/community/matches/'+matchToken);
  setGame(g);
  if(auth){
   try{
    const [profile,mine]=await Promise.all([request('/community/me'),request('/community/matches/'+matchToken+'/mine')]);
    setMe(profile);setRole(mine.role||null);
   }catch{sessionStorage.removeItem('mm_player_token');setAuth('');setMe(null);setRole(null);}
  }
 }
 useEffect(()=>{refresh().catch(e=>setNotice(e.message));},[auth]);
 async function action(fn:()=>Promise<void>){
  setBusy(true);setNotice('');
  try{await fn();await refresh();}catch(e:any){setNotice(e.message);}finally{setBusy(false);}
 }
 const google=useCallback(async(credential:string)=>{
  setBusy(true);setNotice('');
  try{
   const response=await fetch(API+'/community/google',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({credential})});
   const result=await response.json();
   if(!response.ok)throw Error(result.error||'No se pudo iniciar sesión con Google');
   sessionStorage.setItem('mm_player_token',result.token);setAuth(result.token);
  }catch(e:any){setNotice(e.message);}finally{setBusy(false);}
 },[]);
 async function sign(role:'goalkeeper'|'field'){
  await action(async()=>{await request('/community/matches/'+matchToken+'/signup',{method:'PUT',body:JSON.stringify({role})});setRole(role);});
 }
 async function cancel(){
  await action(async()=>{await request('/community/matches/'+matchToken+'/signup',{method:'DELETE'});setRole(null);});
 }
 const keepers=game?.players.filter(p=>p.role==='goalkeeper')||[];
 const field=game?.players.filter(p=>p.role==='field')||[];
 const open=game?.status==='scheduled';
 return <div className="community-page">
  <header className="community-header"><span className="community-ball">⚽</span><div><h1>MI MEJENGA</h1><p>Directiva SM · Lista semanal</p></div><a href="/mi" className="community-header-link">Mi perfil</a></header>
  <main className="community-main">
   {notice&&<div className="community-notice">{notice}</div>}
   {!game?<section className="community-card">Cargando la convocatoria...</section>:<>
    <div className="community-date"><small>MEJENGA DE LA SEMANA</small><h2>{new Date(game.date+'T12:00:00').toLocaleDateString('es-CR',{weekday:'long',day:'numeric',month:'long',year:'numeric'})}</h2><p>Cuota habitual: ₡{game.fee.toLocaleString('es-CR')}</p></div>
    <div className="community-metrics">
      <div><span>🥅 Porteros</span><strong>{game.goalkeeperCount} / {game.goalkeeperSlots}</strong></div>
      <div><span>⚽ Jugadores</span><strong>{game.fieldCount} / {game.fieldSlots}</strong></div>
    </div>
    {!me?<section className="community-card">
     <h2>Entrá con Google 🤗</h2><p className="muted">Creá tu usuario automáticamente y apuntate. La próxima semana solo tendrás que iniciar sesión.</p>
     <GoogleSignIn onCredential={google}/>{busy&&<p>Iniciando sesión...</p>}
     </section>:<section className="community-card">
     <h2>¡Hola, {me.name}! 👋</h2><p>{role?'Estás apuntado como '+(role==='goalkeeper'?'portero':'jugador de campo'):'Elegí tu posición para apuntarte'}</p>
     {open&&<div className="community-actions">
       <button disabled={busy||(game.goalkeeperCount>=game.goalkeeperSlots&&role!=='goalkeeper')} onClick={()=>sign('goalkeeper')}>{role==='goalkeeper'?'✓ ':'🥅 '}Portero</button>
       <button disabled={busy||(game.fieldCount>=game.fieldSlots&&role!=='field')} onClick={()=>sign('field')}>{role==='field'?'✓ ':'⚽ '}Jugador de campo</button>
       {role&&<button className="secondary" disabled={busy} onClick={cancel}>Desapuntarme</button>}
     </div>}
     <a className="community-profile-link" href="/mi">Ver mi dashboard, pagos e historial</a>
     <button className="community-plain" onClick={()=>{sessionStorage.removeItem('mm_player_token');setAuth('');setMe(null);setRole(null);window.google?.accounts.id.disableAutoSelect();}}>Cerrar sesión</button>
     </section>}
    <div className="community-rosters">
      <section className="community-card"><h2>🥅 Porteros ({keepers.length}/{game.goalkeeperSlots})</h2>{Array.from({length:game.goalkeeperSlots},(_,i)=><div className="community-slot" key={i}><span>{i+1}</span><b>{keepers[i]?.name||'Disponible'}</b></div>)}</section>
      <section className="community-card"><h2>⚽ Jugadores ({field.length}/{game.fieldSlots})</h2>{Array.from({length:game.fieldSlots},(_,i)=><div className="community-slot" key={i}><span>{i+1}</span><b>{field[i]?.name||'Disponible'}</b></div>)}</section>
    </div>
    <p className="community-help">Apuntarse no confirma asistencia ni pago; la directiva registra ambos después de jugar.</p>
   </>}
  </main>
 </div>;
}
