export const FORMATIONS = {
 '7-classic':{count:7,label:'3-3-1',hint:'3 hücum · 3 savunma · 1 kaleci',rows:[3,3]},
 '7-231':{count:7,label:'2-3-1',hint:'2 savunma · 3 orta saha · 1 hücum + kaleci',rows:[1,3,2]},
 '7-321':{count:7,label:'3-2-1',hint:'3 savunma · 2 orta saha · 1 hücum + kaleci',rows:[1,2,3]},
 '7-222':{count:7,label:'2-2-2',hint:'2 savunma · 2 orta saha · 2 hücum + kaleci',rows:[2,2,2]},
 '5-211':{count:5,label:'2-1-1',hint:'2 savunma · 1 orta saha · 1 hücum + kaleci',rows:[1,1,2]},
 '5-121':{count:5,label:'1-2-1',hint:'1 savunma · 2 orta saha · 1 hücum + kaleci',rows:[1,2,1]},
 '6-221':{count:6,label:'2-2-1',hint:'2 savunma · 2 orta saha · 1 hücum + kaleci',rows:[1,2,2]},
 '6-212':{count:6,label:'2-1-2',hint:'2 savunma · 1 orta saha · 2 hücum + kaleci',rows:[2,1,2]},
 '8-331':{count:8,label:'3-3-1',hint:'3 savunma · 3 orta saha · 1 hücum + kaleci',rows:[1,3,3]}
};
export function positions(key){const f=FORMATIONS[key]||FORMATIONS['7-classic'];const rows=[...f.rows,1];return rows.flatMap((n,row)=>Array.from({length:n},(_,col)=>({x:n===1?50:n===2?29+col*42:17+col*33,y:(row+.5)/rows.length*100,label:row===rows.length-1?'GK':row===0?'ST':row===rows.length-2?'CB':'CM'})))}
export function ensureSquads(state){if(!state.squads){state.squads={};for(const team of ['blue','red']){const list=state.players.filter(p=>p.visible&&p.team===team).sort((a,b)=>a.order-b.order);const keeper=list.find(p=>p.position==='GK');const field=list.filter(p=>p!==keeper);state.squads[team]={formation:'7-classic',slots:[...Array.from({length:6},(_,i)=>field[i]?.id||null),keeper?.id||null]}}}return state}
export function changeFormation(state,team,key){const squad=state.squads[team],oldPositions=positions(squad.formation),nextPositions=positions(key);const occupied=squad.slots.map((id,i)=>({id,pos:oldPositions[i]})).filter(x=>x.id);const keeper=occupied.find(x=>x.pos.label==='GK');const field=occupied.filter(x=>x!==keeper);const next=Array(nextPositions.length).fill(null);if(keeper)next[next.length-1]=keeper.id;field.forEach((x,i)=>{if(i<next.length-1)next[i]=x.id});squad.formation=key;squad.slots=next;state.settings[team+'Formation']=FORMATIONS[key].label;return Math.max(0,occupied.length-next.filter(Boolean).length)}
export function assignPlayer(state,team,index,id){const squad=state.squads[team];if(!squad||!Number.isInteger(index)||index<0||index>=squad.slots.length)throw Error('Geçersiz saha pozisyonu.');if(!state.players.some(p=>p.id===id&&p.visible))throw Error('Oyuncu bulunamadı veya gizli.');const other=team==='blue'?'red':'blue';if(state.squads[other].slots.includes(id))throw Error('Bu oyuncu diğer takımda. Önce o kadrodan çıkar.');const from=squad.slots.indexOf(id),replaced=squad.slots[index];if(from>=0&&from!==index)squad.slots[from]=replaced;else if(from===index)return;squad.slots[index]=id}
export function clearPlayer(state,id){if(!state.squads)return;for(const team of ['blue','red'])state.squads[team].slots=state.squads[team].slots.map(v=>v===id?null:v)}
