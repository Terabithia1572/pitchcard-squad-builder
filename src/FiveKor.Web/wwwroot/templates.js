import catalog from './template-catalog.js';
export const CATALOG=catalog;
let custom=[];
export function setTemplateLibrary(items=[]){custom=items}
export function allTemplates(){return [...catalog,...custom]}
export function getTemplate(id){return allTemplates().find(t=>t.id===id)}
export const NATIONS=[['','Ülke gösterme'],['TR','Türkiye'],['FR','Fransa'],['DE','Almanya'],['BR','Brezilya'],['AR','Arjantin'],['PT','Portekiz'],['ES','İspanya'],['GB','Birleşik Krallık'],['IT','İtalya'],['NL','Hollanda']];
export const flag=code=>/^[A-Z]{2}$/.test(code||'')?String.fromCodePoint(...[...code].map(c=>127397+c.charCodeAt(0))):'';
export function presentation(p){const t=getTemplate(p.templateId);return {template:t,color:/^#[0-9a-f]{6}$/i.test(p.textColor||'')?p.textColor:t?.color||'#fff0ba',layout:p.cardLayout||t?.layout||'modern',nameY:p.nameY??65,statsY:p.statsY??71.1,badgesY:p.badgesY??84,filter:p.photoFilter==='mono'?'grayscale(1)':p.photoFilter==='sepia'?'sepia(.8)':'none'}}
