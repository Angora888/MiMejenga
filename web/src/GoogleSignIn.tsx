import React,{useEffect,useRef,useState} from 'react';
const CLIENT_ID=import.meta.env.VITE_GOOGLE_CLIENT_ID||'';
declare global{interface Window{google?:{accounts:{id:{initialize:(args:{client_id:string;callback:(result:{credential:string})=>void;auto_select?:boolean})=>void;renderButton:(el:HTMLElement,opts:Record<string,unknown>)=>void;disableAutoSelect:()=>void}}}}}
export function GoogleSignIn({onCredential}:{onCredential:(credential:string)=>void}){
 const target=useRef<HTMLDivElement>(null);
 const [error,setError]=useState('');
 useEffect(()=>{
  if(!CLIENT_ID){setError('Falta configurar VITE_GOOGLE_CLIENT_ID');return;}
  let removed=false;
  const render=()=>{
   if(removed||!target.current||!window.google)return;
   window.google.accounts.id.initialize({client_id:CLIENT_ID,callback:r=>onCredential(r.credential),auto_select:false});
   target.current.innerHTML='';
   window.google.accounts.id.renderButton(target.current,{theme:'outline',size:'large',type:'standard',text:'signin_with',shape:'pill',width:300});
  };
  if(window.google){render();return()=>{removed=true;};}
  const script=document.createElement('script');script.src='https://accounts.google.com/gsi/client';script.async=true;script.defer=true;
  script.onload=render;script.onerror=()=>setError('No se pudo cargar el acceso con Google');
  document.head.appendChild(script);return()=>{removed=true;script.remove();};
 },[onCredential]);
 return <div className="google-login"><div ref={target}/>{error&&<p className="community-error">{error}</p>}</div>;
}
