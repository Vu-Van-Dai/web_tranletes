import type { AdSlot, AdUnit } from '@/types/api';
export function dimensions(u:AdUnit):{width:number;height:number}|null {
  if(u.width&&u.height)return {width:u.width,height:u.height};
  const h=u.html??'',w=h.match(/['"]?width['"]?\s*:\s*(\d{2,4})\b/i),y=h.match(/['"]?height['"]?\s*:\s*(\d{2,4})\b/i);
  return w&&y?{width:Number(w[1]),height:Number(y[1])}:null;
}
export function fitsSlot(u:AdUnit,slot:AdSlot,width:number,viewport:number):boolean {
  if(!u.enabled||u.format!=='banner'||!u.slots?.includes(slot)||width<=0)return false;
  if(u.device==='mobile'&&viewport>=768||u.device==='desktop'&&viewport<768)return false;
  const d=dimensions(u);
  if(slot==='side_left'||slot==='side_right')return viewport>=1700&&!!d&&d.width<=width&&d.height<=600;
  return !d||d.width<=width;
}
export function weightedPick<T extends {weight?:number|null}>(units:T[],seed:number):T|undefined {
  const weights=units.map(u=>Math.max(1,Math.min(100,u.weight??1)));
  let x=Math.max(0,Math.min(0.999999999,seed))*weights.reduce((a,b)=>a+b,0);
  for(let i=0;i<units.length;i++){x-=weights[i]!;if(x<0)return units[i];}
  return units[0];
}
export function standaloneLink(html:string|null):{href:string;label:string}|null {
  if(!html)return null;
  const t=document.createElement('template');t.innerHTML=html.trim();
  const a=t.content.firstElementChild;
  if(t.content.childNodes.length!==1||!(a instanceof HTMLAnchorElement)||a.children.length||!/^https:\/\//i.test(a.getAttribute('href')??''))return null;
  if([...a.attributes].some(x=>!['href','target','rel','title','class'].includes(x.name)))return null;
  return {href:a.href,label:a.textContent?.trim()||'Xem quảng cáo'};
}
type State='loading'|'element'|'no-element'|'script-error'|'injected';
export type AdDiagnostic={state:State;at:number;slot:string};
const KEY='rn_ad_diagnostics_v1';
export function readDiagnostics():Record<string,AdDiagnostic>{
  try{const value=JSON.parse(localStorage.getItem(KEY)??'{}');return value&&typeof value==='object'&&!Array.isArray(value)?value:{};}catch{return {};}
}
export function noteAd(id:string,slot:string,state:State){
  try{const data=readDiagnostics();data[id]={slot,state,at:Date.now()};localStorage.setItem(KEY,JSON.stringify(Object.fromEntries(Object.entries(data).sort((a,b)=>b[1].at-a[1].at).slice(0,50))));}catch{/* optional local diagnostics */}
}
export function suggestedAdsterraLayout(u:AdUnit):AdUnit{
  if(u.provider!=='adsterra')return u;
  const d=dimensions(u);
  if(u.format==='banner'&&d){
    const choices:Record<string,{slot:AdSlot;device:'mobile'|'desktop'|'all'}>={
      '728x90':{slot:'site_top',device:'desktop'},'320x50':{slot:'site_top',device:'mobile'},
      '300x250':{slot:'home_extra',device:'all'},'468x60':{slot:'comic_chapters',device:'all'},
      '160x600':{slot:'side_left',device:'desktop'},'160x300':{slot:'side_right',device:'desktop'}
    };
    const c=choices[d.width+'x'+d.height];
    if(c)return {...u,slots:[c.slot],device:c.device,width:d.width,height:d.height};
  }
  if(u.format==='banner'&&/native/i.test(u.name))return {...u,slots:['home_middle']};
  if(u.format==='banner'&&/smartlink/i.test(u.name))return {...u,slots:['site_bottom']};
  return u;
}
