import React,{useCallback,useEffect,useState} from 'react';
import {GoogleSignIn} from './GoogleSignIn';
import './community.css';
const API=import.meta.env.VITE_API_URL||'http://localhost:5080';
type Summary={year:number;matches:number;played:number;missed:number;credits:number;chargeable:number;due:number;contributed:number;laundry:number};
type Payment={date:string;amount:number;method:string;notes:string};
type Played={date:string;laundry:boolean};
type Dashboard={seasons:Summary[];payments:Payment[];played:Played[]};
const money=(n:number)=>'₡'+n.toLocaleString('es-CR');
export default function PlayerDashboard(){
 const [token,setToken]=useState(sessionStorage.getItem('mm_player_token')||'');
 const [profile,setProfile]=useState<{name:string;email:string;picture:string}|null>(null);
 const [dashboard,setDashboard]=useState<Dashboard|null>(null);
 const [error,setError]=useState('');
 const [busy,setBusy]=useState(false);
 async function request(path:string,t=token,options:RequestInit={}){
  const response=await fetch(API+path,{...options,headers:{'Content-Type':'application/json',...(t?{Authorization:'Bearer '+t}:{}),...options.headers}});
  let data:any={};try{data=await response.json();}catch{}
  if(!response.ok)throw Error(data.error||'Error '+response.status);
  return data;
 }
 useEffect(()=>{if(!token){setProfile(null);setDashboard(null);return;}
  Promise.all([request('/community/me'),request('/community/dashboard')]).then(([p,d])=>{setProfile(p);setDashboard(d);}).catch(e=>{setError(e.message);setToken('');sessionStorage.removeItem('mm_player_token');});
 },[token]);
 const google=useCallback(async(credential:string)=>{
  setBusy(true);setError('');
  try{const r=await request('/community/google','',{method:'POST',body:JSON.stringify({credential})});sessionStorage.setItem('mm_player_token',r.token);setToken(r.token);}
  catch(e:any){setError(e.message);}finally{setBusy(false);}
 },[]);
 const logout=()=>{sessionStorage.removeItem('mm_player_token');setToken('');window.google?.accounts.id.disableAutoSelect();};
 const latest=dashboard?.seasons[0];
 return <div className="community-page">
  <header className="community-header"><span className="community-ball">⚽</span><div><h1>MI MEJENGA</h1><p>Mi perfil · Directiva SM</p></div><a href="/" className="community-header-link">Directiva</a></header>
  <main className="community-main">
  {error&&<p className="community-notice">{error}</p>}
  {!token?<section className="community-card player-welcome">
    <h2>Bienvenido a tu mejenga ⚽</h2><p>Entrá con tu cuenta de Google, revisá cuánto llevás abonado para la fiesta, tus partidos jugados y tu saldo.</p>
    <GoogleSignIn onCredential={google}/>{busy&&<p>Iniciando sesión...</p>}
   </section>:<>
    <section className="community-card player-identity"><div><small>MI PERFIL</small><h2>{profile?.name||'Cargando...'}</h2><span>{profile?.email||''}</span></div><button className="community-plain" onClick={logout}>Salir</button></section>
    {!dashboard?<section className="community-card">Cargando tu historial...</section>:<>
      {!latest&&<section className="community-card"><h2>Tu temporada empieza aquí 🤗</h2><p>Cuando la directiva comparta la próxima mejenga, abrí el enlace e inscribite; desde entonces empezará tu historial.</p></section>}
      {latest&&<><h2 className="player-section">Temporada {latest.year}</h2>
       <div className="player-stats">
        <article><small>🏃 Partidos jugados</small><strong>{latest.played}</strong></article>
        <article><small>🎉 Abonado para la fiesta</small><strong>{money(latest.contributed)}</strong></article>
        <article><small>⚽ Créditos extras</small><strong>{latest.credits}</strong></article>
        <article><small>💰 Saldo pendiente</small><strong>{money(latest.due)}</strong></article>
       </div>
       <section className="community-card"><h2>Tu control anual</h2><div className="player-line"><span>Mejengas desde tu ingreso</span><strong>{latest.matches}</strong></div><div className="player-line"><span>Asistencias confirmadas</span><strong>{latest.played}</strong></div><div className="player-line"><span>Ausencias</span><strong>{latest.missed}</strong></div><div className="player-line"><span>Créditos por pagos adicionales</span><strong>{latest.credits}</strong></div><div className="player-line"><span>Ausencias pendientes de cobro</span><strong>{latest.chargeable}</strong></div><div className="player-line"><span>Lavadas registradas</span><strong>{latest.laundry}</strong></div></section></>}
      <section className="community-card"><h2>💳 Historial de aportes</h2>{!dashboard.payments.length?<p className="muted">Todavía no tenés pagos registrados.</p>:dashboard.payments.map((p,i)=><div className="player-line" key={i}><div><b>{p.date}</b><small>{p.method.toUpperCase()}{p.notes?' · '+p.notes:''}</small></div><strong>{money(p.amount)}</strong></div>)}</section>
      <section className="community-card"><h2>📆 Mejengas jugadas</h2>{!dashboard.played.length?<p className="muted">Tus partidos aparecerán cuando la directiva confirme la asistencia.</p>:dashboard.played.map((m,i)=><div className="player-line" key={i}><span>{m.date}{m.laundry?' · 🧺 Lavada':''}</span><strong>✓ Jugó</strong></div>)}</section>
    </>}
   </>}
  </main>
 </div>;
}
