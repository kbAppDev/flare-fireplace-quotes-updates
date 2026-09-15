/* Preview-only tests. Drive the editor's real DOM; do not add production test hooks. */
const frame=document.querySelector('#device'),report=document.querySelector('#report');
let entries=[];
const win=()=>frame.contentWindow,doc=()=>frame.contentDocument,$=id=>doc().getElementById(id);
const wait=ms=>new Promise(r=>setTimeout(r,ms));
async function until(test,label){for(let i=0;i<100;i++){if(test())return;await wait(30);}throw Error('Timed out: '+label);}
function ok(condition,label){if(!condition)throw Error(label);entries.push('PASS '+label);report.textContent=entries.join('\n');}
function fill(id,value){$(id).value=value;$(id).dispatchEvent(new (win().Event)('input',{bubbles:true}));}
function click(id){$(id).click();}
function choose(id,value){click(id+'Choice');const select=$(id);const index=Array.from(select.options).findIndex(o=>o.value===value);$('choiceOptions').children[index].click();}
function closeAll(){doc().querySelectorAll('dialog[open]').forEach(d=>d.close());}
async function reset(){closeAll();click('backHit');const old=win().confirm;win().confirm=()=>true;click('resetBtn');win().confirm=old;await wait(80);}
async function add(text,sender='me',kind='text',extras={}){
  click('composerHit');doc().querySelector('[data-sender="'+sender+'"]').click();doc().querySelector('[data-kind="'+kind+'"]').click();fill('messageText',text);fill('messageDate',extras.date||'2026-09-16T09:00');
  if(extras.delivery)choose('messageDelivery',extras.delivery);if(extras.reaction)choose('messageReaction',extras.reaction);
  if(kind==='link')fill('messageUrl','https://example.com/specification');if(kind==='file')fill('messageFileName','Example.pdf');
  if(extras.file){const transfer=new (win().DataTransfer)();transfer.items.add(extras.file);$('messageMedia').files=transfer.files;}
  click('saveMessageBtn');await until(() => !$('messageDialog').open,'save message');await wait(60);
}
function hits(){return Array.from(doc().querySelectorAll('.message-hit'));}
function bounds(){const c=$('previewCanvas'),r=c.getBoundingClientRect();return {width:r.width,height:r.height,pixels:[c.width,c.height]};}
function noOverflow(){const card=$('messageDialog').querySelector('.sheet-card');return Array.from(card.querySelectorAll('input,textarea,button')).filter(e=>e.getClientRects().length).every(e=>e.getBoundingClientRect().left>=card.getBoundingClientRect().left-1&&e.getBoundingClientRect().right<=card.getBoundingClientRect().right+1);}
async function sample(){await reset();click('headerHit');fill('contactName','Alex');fill('contactInitials','AL');click('saveContactBtn');await add('The plans look great. Shall we review them tomorrow?','me','text',{delivery:'Delivered'});await add('Absolutely. Morning works for me.','them');await add('I’ll bring the updated drawings.','them');await add('Perfect — see you at 10.','me','text',{reaction:'👍',delivery:'Read'});}
async function run(){
  entries=[];document.querySelector('#run').disabled=true;report.textContent='Running…';
  try{
    await reset();ok($('appVersion').textContent==='0.4.0','version 0.4.0');
    for(const [w,h]of [[320,568],[375,667],[402,874],[768,1024]]){
      frame.style.width=w+'px';frame.style.height=h+'px';await wait(120);const b=bounds();ok(Math.abs(b.width/b.height-402/874)<.00001,`${w}×${h}: uniform preview scaling`);ok(b.pixels.join('×')==='1206×2622',`${w}×${h}: fixed canvas pixels`);
      click('composerHit');ok(noOverflow(),`${w}×${h}: sheet fields inside bounds`);const buttons=Array.from($('kindSegment').children);ok(buttons.every(b=>b.clientWidth>=50&&b.clientHeight>=44),`${w}×${h}: all seven type buttons fit`);ok(buttons[4].offsetTop>buttons[0].offsetTop,`${w}×${h}: type grid wraps into two rows`);closeAll();
    }
    frame.style.width='402px';frame.style.height='874px';await wait(100);
    await add('First grouped message.');await add('Second grouped message.');ok(hits().length===2,'adding messages');const hs=hits();ok(parseFloat(hs[1].style.top)-parseFloat(hs[0].style.top)-parseFloat(hs[0].style.height)===4,'same-sender 4pt grouping gap');
    await add('Incoming reply.','them');ok(parseFloat(hits()[2].style.top)-parseFloat(hits()[1].style.top)-parseFloat(hits()[1].style.height)===10,'sender-change 10pt gap');
    hits()[0].click();fill('messageText','Edited first message.');$('messageEdited').checked=true;choose('messageReaction','🔥');choose('messageDelivery','Read');click('saveMessageBtn');await wait(100);hits()[0].click();ok($('messageText').value==='Edited first message.'&&$('messageEdited').checked&&$('messageReaction').value==='🔥','editing, edited indicator and reactions retained');closeAll();
    hits()[0].click();click('moveDownBtn');await wait(100);hits()[1].click();ok($('messageText').value==='Edited first message.','reorder moves the correct message');click('deleteMessageBtn');await wait(100);ok(hits().length===2,'delete removes one message');
    const target=hits()[0].dataset.id;click('composerHit');fill('messageText','Inline reply test');fill('messageDate','2026-09-16T09:01');choose('messageReplyTo',target);click('saveMessageBtn');await wait(100);hits().at(-1).click();ok($('messageReplyTo').value===target,'inline reply persists');$('messageDeleted').checked=true;click('saveMessageBtn');await wait(100);hits().at(-1).click();ok($('messageDeleted').checked,'message removed flag persists');closeAll();
    await reset();await add('Line one\nLine two\n\nFinal line');ok(parseFloat(hits()[0].style.height)===100,'multiline and blank-line height');await add('abcdefghijklmnopqrstuvwxyz'.repeat(8),'them');ok(parseFloat(hits()[1].style.width)<=280&&parseFloat(hits()[1].style.height)>40,'long unbroken text wraps within bubble');
    await add('🙂','me');ok(parseFloat(hits().at(-1).style.height)===45,'emoji-only large rendering');await add('!!!','them');ok(parseFloat(hits().at(-1).style.height)===40,'punctuation is not misclassified as emoji');
    const media=doc().createElement('canvas');media.width=240;media.height=160;const mc=media.getContext('2d');mc.fillStyle='#e4b24d';mc.fillRect(0,0,240,160);mc.fillStyle='#375362';mc.fillRect(20,20,200,120);const blob=await new Promise(r=>media.toBlob(r));const file=new (win().File)([blob],'synthetic.png',{type:'image/png'});
    for(const kind of ['photo','video','link','file','voice','sticker']){await add(kind==='voice'?'0:12':kind==='sticker'?'⭐️':'Example '+kind,'them',kind,kind==='photo'?{file}:{});hits().at(-1).click();ok(doc().querySelector('[data-kind="'+kind+'"]').classList.contains('active'),kind+' message preserved');closeAll();}
    click('headerHit');fill('contactName','Alex');fill('contactInitials','AL');choose('avatarStyle','emoji');fill('contactEmoji','🔥');click('saveContactBtn');await wait(80);click('headerHit');ok($('avatarStyle').value==='emoji'&&$('contactEmoji').value==='🔥','emoji avatar');choose('avatarStyle','photo');const dt=new (win().DataTransfer)();dt.items.add(file);$('avatarPhoto').files=dt.files;click('saveContactBtn');await wait(150);click('headerHit');ok($('avatarStyle').value==='photo','photo avatar retained');choose('avatarStyle','initials');click('saveContactBtn');await wait(80);
    for(const theme of ['dark','light']){click('backHit');doc().querySelector('[data-theme="'+theme+'"]').click();await wait(80);ok(doc().documentElement.dataset.theme===theme,theme+' renderer and sheet theme');closeAll();}
    const last=hits().at(-1);last.dispatchEvent(new (win().PointerEvent)('pointerdown',{clientX:200,clientY:300,bubbles:true}));await wait(600);ok($('messageDialog').open,'long press opens editor');last.dispatchEvent(new (win().PointerEvent)('pointerup',{bubbles:true}));closeAll();
    $('messageScroller').scrollTop=170;await wait(150);const before=$('previewCanvas').toDataURL();click('backHit');let exportUrl;const original=win().HTMLAnchorElement.prototype.click;win().HTMLAnchorElement.prototype.click=function(){if(this.download)exportUrl=this.href;else original.call(this);};click('exportBtn');await until(()=>!!exportUrl,'export');win().HTMLAnchorElement.prototype.click=original;
    const out=await win().fetch(exportUrl).then(r=>r.blob()),bytes=new Uint8Array(await out.arrayBuffer());const dv=new DataView(bytes.buffer);ok(out.type==='image/png'&&dv.getUint32(16)===1206&&dv.getUint32(20)===2622,'export PNG has exact 1206×2622 dimensions');
    const exported=await new Promise(resolve=>{const reader=new (win().FileReader)();reader.onload=()=>resolve(reader.result);reader.readAsDataURL(out);});
    async function pixels(src){const im=new (win().Image)();im.src=src;await im.decode();const c=doc().createElement('canvas');c.width=1206;c.height=2622;const x=c.getContext('2d');x.drawImage(im,0,0);return x.getImageData(0,0,1206,2622).data;}
    const a=await pixels(before),b=await pixels(exported);let differences=0;for(let i=0;i<a.length;i++)if(a[i]!==b[i])differences++;ok(!differences,'export pixel match at scroll 170: no tools or browser chrome ('+differences+' channel differences, current scroll '+$('messageScroller').scrollTop+')');closeAll();
    const caches=await win().caches.keys();ok(caches.includes('bubble-lab-v0.4.0'),'versioned PWA cache installed');const keys=await (await win().caches.open('bubble-lab-v0.4.0')).keys();ok(keys.length===8&&keys.every(r=>!r.url.includes('qa.')),'offline shell contains only eight static assets, no conversation data');
    await sample();entries.push('\nCOMPLETE — '+entries.length+' checks passed. Physical iPhone / Safari share sheet still requires device verification.');report.textContent=entries.join('\n');
  }catch(e){entries.push('FAIL '+e.message);report.textContent=entries.join('\n');}finally{document.querySelector('#run').disabled=false;}
}
document.querySelector('#run').onclick=run;document.querySelector('#blank').onclick=reset;document.querySelector('#sample').onclick=sample;
document.querySelectorAll('[data-size]').forEach(b=>b.onclick=()=>{const [w,h]=b.dataset.size.split(',');frame.style.width=w+'px';frame.style.height=h+'px';});
