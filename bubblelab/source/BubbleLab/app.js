(() => {
  'use strict';

  const LOGICAL_W = 402;
  const LOGICAL_H = 874;
  const EXPORT_SCALE = 3;
  const APP_VERSION = '0.4.0';
  const COMPOSER_TOP = 806;
  const CONTENT_TOP = 172;
  const CONTENT_BOTTOM_PAD = 105;
  const BLUE = '#299dff';
  const GREEN = '#34c759';

  const $ = (id) => document.getElementById(id);
  const canvas = $('previewCanvas');
  const scroller = $('messageScroller');
  const spacer = $('scrollSpacer');
  const hitLayer = $('hitLayer');
  const measureCanvas = document.createElement('canvas');
  const mctx = measureCanvas.getContext('2d');

  const defaultState = () => ({
    theme: 'light',
    backgroundEffect: 'plain',
    contact: {
      name: 'Contact',
      initials: 'C',
      avatarStyle: 'initials',
      emoji: '🙂',
      color: '#879bd1',
      photo: ''
    },
    status: {
      time: '9:41',
      battery: 82,
      signal: 4,
      wifi: 3,
      charging: false,
      silent: true,
      focus: false,
      batteryPct: false
    },
    messages: []
  });

  let state = defaultState();
  let layoutCache = null;
  let editingMessageId = null;
  let longPressTimer = null;
  const imageCache = new Map();
  const decodedImages = new Map();
  let renderRequested=0, previewBusy=false;
  const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  const isStandalone = window.matchMedia?.('(display-mode: standalone)').matches || navigator.standalone === true;
  if (isIOS && isStandalone) document.documentElement.classList.add('ios-standalone');

  function fitEditor() {
    const stage = $('phoneStage');
    const viewport = $('phoneViewport');
    const vv = window.visualViewport;
    const availableW = Math.max(1, vv?.width || document.documentElement.clientWidth || window.innerWidth || LOGICAL_W);
    const availableH = Math.max(1, vv?.height || document.documentElement.clientHeight || window.innerHeight || LOGICAL_H);
    document.documentElement.style.setProperty('--sheet-height',`${Math.max(160,availableH-20)}px`);

    // Never independently size width and height. The Messages viewport is a
    // fixed 402×874 coordinate system; the live editor may only scale uniformly.
    // Keep the complete frame visible in Safari and standalone mode alike.
    const cropY = 0;
    const visibleH = LOGICAL_H - cropY;
    const scale = Math.min(1, availableW / LOGICAL_W, availableH / visibleH);

    const stageW = LOGICAL_W * scale;
    const stageH = visibleH * scale;
    stage.style.width = `${stageW}px`;
    stage.style.height = `${stageH}px`;
    viewport.style.left = '0px';
    viewport.style.top = `${-cropY * scale}px`;
    viewport.style.width = `${LOGICAL_W}px`;
    viewport.style.height = `${LOGICAL_H}px`;
    viewport.style.transformOrigin = 'top left';
    viewport.style.transform = `scale(${scale})`;
    stage.dataset.previewScale = scale.toFixed(5);
    stage.dataset.previewCrop = String(cropY);
  }

  function uid() {
    return (crypto.randomUUID ? crypto.randomUUID() : Math.random().toString(36).slice(2) + Date.now());
  }

  function nowLocalInput(offsetMinutes = 0) {
    const d = new Date(Date.now() + offsetMinutes * 60000);
    const z = n => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${z(d.getMonth()+1)}-${z(d.getDate())}T${z(d.getHours())}:${z(d.getMinutes())}`;
  }

  function parseLocalDate(v) {
    if (!v) return new Date();
    const d = new Date(v);
    return isNaN(d) ? new Date() : d;
  }

  function formatClock(d) {
    return new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(d);
  }

  function sameDay(a,b) {
    return a && b && a.getFullYear()===b.getFullYear() && a.getMonth()===b.getMonth() && a.getDate()===b.getDate();
  }

  function startOfDay(d) { return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }

  function timestampLabel(date) {
    const now = new Date();
    const days = Math.round((startOfDay(now) - startOfDay(date)) / 86400000);
    const time = formatClock(date);
    if (days === 0) return `Today ${time}`;
    if (days === 1) return `Yesterday ${time}`;
    if (days > 1 && days < 7) {
      return `${new Intl.DateTimeFormat(undefined,{weekday:'long'}).format(date)} ${time}`;
    }
    const datePart = new Intl.DateTimeFormat(undefined,{weekday:'short',month:'short',day:'numeric'}).format(date);
    return `${datePart} at ${time}`;
  }

  function showTimestamp(prev, cur) {
    if (!prev) return true;
    const a = parseLocalDate(prev.datetime), b = parseLocalDate(cur.datetime);
    if (!sameDay(a,b)) return true;
    return (b-a) >= 45*60000;
  }

  function isEmojiOnly(text) {
    const t = (text || '').trim();
    if (!t) return false;
    if (!/\p{Extended_Pictographic}|\p{Regional_Indicator}/u.test(t) || /[\p{L}\p{N}]/u.test(t)) return false;
    try {
      const count = [...new Intl.Segmenter(undefined,{granularity:'grapheme'}).segment(t)].length;
      return count <= 3;
    } catch { return [...t].length <= 8; }
  }

  function setFont(ctx, size, weight='400') {
    ctx.font = `${weight} ${size}px -apple-system, BlinkMacSystemFont, "SF Pro Text", "SF Pro Display", Helvetica, Arial, sans-serif`;
  }

  function wrapText(ctx, text, maxWidth, fontSize=17, weight='400') {
    setFont(ctx, fontSize, weight);
    const paragraphs = String(text ?? '').split('\n');
    const lines = [];
    for (const p of paragraphs) {
      if (!p) { lines.push(''); continue; }
      const words = p.match(/\S+\s*|\s+/g) || [''];
      let line = '';
      for (const word of words) {
        if (ctx.measureText(line + word.trimEnd()).width <= maxWidth) { line += word; continue; }
        if (line) { lines.push(line.trimEnd()); line = ''; }
        const chars = typeof Intl.Segmenter === 'function' ? [...new Intl.Segmenter(undefined,{granularity:'grapheme'}).segment(word)].map(s=>s.segment) : Array.from(word);
        for (const ch of chars) {
          if (line && ctx.measureText(line + ch).width > maxWidth) { lines.push(line.trimEnd()); line=''; }
          line += ch;
        }
      }
      if (line) lines.push(line.trimEnd());
    }
    return lines.length ? lines : [''];
  }

  function roundedRectPath(ctx,x,y,w,h,r) {
    const rr = Math.min(r,w/2,h/2);
    ctx.beginPath();
    ctx.moveTo(x+rr,y);
    ctx.arcTo(x+w,y,x+w,y+h,rr);
    ctx.arcTo(x+w,y+h,x,y+h,rr);
    ctx.arcTo(x,y+h,x,y,rr);
    ctx.arcTo(x,y,x+w,y,rr);
    ctx.closePath();
  }

  // One continuous silhouette, reflected for incoming messages. The tail is
  // part of the lower corner, never a separate wedge pasted below the body.
  function bubblePath(ctx,x,y,w,h,r,side,tail=true,groupedTop=false,groupedBottom=false) {
    const rr=Math.min(r,w/2,h/2), rt=groupedTop?7:rr, rb=groupedBottom?7:rr;
    ctx.save();ctx.translate(side==='left'?x+w:x,y);if(side==='left')ctx.scale(-1,1);
    ctx.beginPath();ctx.moveTo(rr,0);ctx.lineTo(w-rt,0);
    ctx.quadraticCurveTo(w,0,w,rt);
    if(tail){
      ctx.lineTo(w,h-20);
      ctx.bezierCurveTo(w,h-10,w-10,h-8,w-10,h-3);
      ctx.bezierCurveTo(w-10,h+1,w-6,h+5,w-8,h+6);
      ctx.bezierCurveTo(w-11,h+7,w-18,h,w-23,h);
    }else{ctx.lineTo(w,h-rb);ctx.quadraticCurveTo(w,h,w-rb,h);}
    ctx.lineTo(rr,h);ctx.quadraticCurveTo(0,h,0,h-rr);
    ctx.lineTo(0,rr);ctx.quadraticCurveTo(0,0,rr,0);ctx.closePath();ctx.restore();
  }

  function grouped(a,b){
    return !!(a && b && !a.deleted && !b.deleted && a.sender===b.sender && a.channel===b.channel && !a.delivery && !a.edited && !showTimestamp(a,b) && Math.abs(parseLocalDate(b.datetime)-parseLocalDate(a.datetime))<5*60000);
  }

  function messageTextColor(msg, theme) {
    if (msg.sender === 'me') return '#fff';
    return theme === 'dark' ? '#fff' : '#000';
  }

  function incomingFill(theme) { return theme === 'dark' ? '#2c2c2e' : '#e9e9eb'; }

  function getReplyText(msg) {
    if (!msg.replyTo) return '';
    const target = state.messages.find(m => m.id === msg.replyTo);
    if (!target) return '';
    const t = target.deleted ? 'Message removed' : (target.text || target.fileName || target.url || target.kind);
    return String(t).replace(/\s+/g,' ').slice(0,80);
  }

  function measureMessage(msg, prev, y) {
    const senderRight = msg.sender === 'me';
    const maxBubbleW = 280;
    const minBubbleW = 40;
    let w = 180, h = 40, textLines = [], fontSize = 17, lineHeight = 20;
    let mediaW=0,mediaH=0;
    const emojiOnly = msg.kind === 'text' && isEmojiOnly(msg.text) && !msg.deleted;
    const replyText = getReplyText(msg);
    const replyH = replyText ? 31 : 0;

    if (msg.deleted) {
      fontSize = 14;
      textLines = wrapText(mctx, msg.sender==='me' ? 'You unsent a message' : 'Message unsent', maxBubbleW-26, fontSize);
      w = Math.min(maxBubbleW, Math.max(155, Math.max(...textLines.map(l=>{setFont(mctx,fontSize);return mctx.measureText(l).width;}))+26));
      h = textLines.length*19 + 18;
    } else if (msg.kind === 'text') {
      fontSize = emojiOnly ? 38 : 17;
      lineHeight = emojiOnly ? 43 : 20;
      const maxTextW = emojiOnly ? 180 : maxBubbleW - 26;
      textLines = wrapText(mctx, msg.text || '', maxTextW, fontSize);
      let tw = 0;
      setFont(mctx,fontSize);
      for (const line of textLines) tw = Math.max(tw, mctx.measureText(line).width);
      w = Math.min(maxBubbleW, Math.max(minBubbleW, tw + (emojiOnly?0:26)));
      h = textLines.length*lineHeight + (emojiOnly?2:20) + replyH;
      if (replyText) w = Math.max(w, 190);
    } else if (msg.kind === 'photo' || msg.kind === 'video') {
      mediaW = 262; mediaH = 174; w=mediaW; h=mediaH;
      if (msg.text) {
        textLines = wrapText(mctx,msg.text,mediaW-24,16);
        h += textLines.length*20 + 18;
      }
      if (replyText) h += 31;
    } else if (msg.kind === 'link') {
      w=274; h=112 + replyH;
      textLines = wrapText(mctx,msg.text || msg.url || 'Link',w-26,16,'600').slice(0,2);
    } else if (msg.kind === 'file') {
      w=270; h=70 + replyH;
      textLines = wrapText(mctx,msg.fileName || msg.text || 'Attachment',w-70,15,'600').slice(0,2);
    } else if (msg.kind === 'voice') {
      w=248; h=54 + replyH;
    } else if (msg.kind === 'sticker') {
      w=118; h=118 + replyH;
    }

    const timestamp = showTimestamp(prev,msg);
    let topExtra = 0;
    if (timestamp) topExtra += 32;
    else if (prev) topExtra += grouped(prev,msg) ? 4 : 10;
    if (msg.reaction) topExtra += 12;

    const x = senderRight ? LOGICAL_W - 16 - w : 16;
    const bodyY = y + topExtra;
    let bottomExtra = 0;
    if (msg.edited) bottomExtra += 17;
    if (msg.sender === 'me' && msg.delivery) bottomExtra += 18;

    return {msg,x,y:bodyY,w,h,topExtra,bottomExtra,totalH:topExtra+h+bottomExtra, timestamp, timestampText: timestampLabel(parseLocalDate(msg.datetime)), textLines,fontSize,lineHeight,emojiOnly,replyText,replyH,mediaW,mediaH};
  }

  function computeLayout() {
    const items=[];
    let y=200;
    let prev=null;
    for (const msg of state.messages) {
      const item=measureMessage(msg,prev,y);
      items.push(item);
      y += item.totalH;
      prev=msg;
    }
    items.forEach((item,i)=>{item.groupedTop=grouped(state.messages[i-1],item.msg);item.groupedBottom=grouped(item.msg,state.messages[i+1]);item.tail=!item.groupedBottom;});
    const totalContentHeight = Math.max(LOGICAL_H, y + CONTENT_BOTTOM_PAD);
    return {items,totalContentHeight,worldEnd:y};
  }

  function backgroundColors(theme,effect) {
    const dark = theme==='dark';
    return [dark?'#000000':'#ffffff'];
  }

  function drawBackground(ctx,theme,effect) {
    const cs=backgroundColors(theme,effect);
    if (cs.length===1) {ctx.fillStyle=cs[0];ctx.fillRect(0,0,LOGICAL_W,LOGICAL_H);return;}
    const g=ctx.createLinearGradient(0,0,LOGICAL_W,LOGICAL_H);
    g.addColorStop(0,cs[0]);g.addColorStop(.5,cs[1]);g.addColorStop(1,cs[2]);
    ctx.fillStyle=g;ctx.fillRect(0,0,LOGICAL_W,LOGICAL_H);
  }

  async function loadImage(src) {
    if (!src) return null;
    if (imageCache.has(src)) return imageCache.get(src);
    const p = new Promise(resolve=>{const im=new Image();im.onload=()=>{decodedImages.set(src,im);resolve(im);};im.onerror=()=>resolve(null);im.src=src;});
    imageCache.set(src,p);return p;
  }

  function drawImageCover(ctx,img,x,y,w,h,r=16) {
    ctx.save();roundedRectPath(ctx,x,y,w,h,r);ctx.clip();
    const ir=img.width/img.height, tr=w/h;let sx=0,sy=0,sw=img.width,sh=img.height;
    if (ir>tr){sw=img.height*tr;sx=(img.width-sw)/2;}else{sh=img.width/tr;sy=(img.height-sh)/2;}
    ctx.drawImage(img,sx,sy,sw,sh,x,y,w,h);ctx.restore();
  }

  function drawTimestamp(ctx,item,screenY,theme) {
    if (!item.timestamp) return;
    const y=screenY-22;
    ctx.fillStyle='#8e8e93';setFont(ctx,11,'500');ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(item.timestampText,LOGICAL_W/2,screenY-14-(item.msg.reaction?12:0));
  }

  function drawReply(ctx,item,x,y,w,theme,msg) {
    if (!item.replyText) return 0;
    const muted = msg.sender==='me' ? 'rgba(255,255,255,.72)' : (theme==='dark'?'#a4a4aa':'#74747a');
    ctx.fillStyle=muted;ctx.fillRect(x+12,y+9,2,20);
    ctx.fillStyle=muted;setFont(ctx,11,'600');ctx.textAlign='left';ctx.textBaseline='top';
    const lines=wrapText(ctx,item.replyText,w-38,11,'600').slice(0,1);ctx.fillText(lines[0]||'',x+20,y+10);
    ctx.globalAlpha=.65;setFont(ctx,10,'400');ctx.fillText('Reply',x+20,y+23);ctx.globalAlpha=1;
    return 31;
  }

  function drawMessage(ctx,item,screenY,theme) {
    const msg=item.msg; const side=msg.sender==='me'?'right':'left'; const x=item.x,y=screenY,w=item.w,h=item.h;
    let fill = msg.sender==='me' ? (msg.channel==='sms'?GREEN:BLUE) : incomingFill(theme);
    if(msg.sender==='me' && msg.channel!=='sms') {fill=ctx.createLinearGradient(0,y,0,y+h);fill.addColorStop(0,theme==='dark'?'#168dff':'#42b5f7');fill.addColorStop(1,theme==='dark'?'#087aff':'#299dff');}
    const textColor=messageTextColor(msg,theme);

    if (msg.deleted) {
      ctx.save();ctx.setLineDash([4,4]);ctx.strokeStyle=theme==='dark'?'#66666b':'#c5c5ca';ctx.lineWidth=1;
      roundedRectPath(ctx,x,y,w,h,18);ctx.stroke();ctx.setLineDash([]);
      ctx.fillStyle=theme==='dark'?'#9a9aa0':'#7c7c82';setFont(ctx,14,'400');ctx.textBaseline='top';ctx.textAlign='left';
      let yy=y+9;for(const line of item.textLines){ctx.fillText(line,x+13,yy);yy+=19;}ctx.restore();
    } else if (msg.kind==='text') {
      if (!item.emojiOnly) {ctx.fillStyle=fill;bubblePath(ctx,x,y,w,h,22,side,item.tail,item.groupedTop,item.groupedBottom);ctx.fill();}
      const contentX=x+(item.emojiOnly?0:13);let yy=y+(item.emojiOnly?0:10);
      if (item.replyText) yy += drawReply(ctx,item,x,y,w,theme,msg);
      ctx.fillStyle=item.emojiOnly?(theme==='dark'?'#fff':'#000'):textColor;
      setFont(ctx,item.fontSize,'400');ctx.textBaseline='top';ctx.textAlign='left';
      for(const line of item.textLines){ctx.fillText(line,contentX,yy);yy+=item.lineHeight;}
    } else if (msg.kind==='photo' || msg.kind==='video') {
      const img=decodedImages.get(msg.mediaData);
      ctx.save();bubblePath(ctx,x,y,w,h,22,side,item.tail,item.groupedTop,item.groupedBottom);ctx.clip();
      if(img) drawImageCover(ctx,img,x,y,w,item.mediaH,0); else {ctx.fillStyle=theme==='dark'?'#333':'#d9d9de';ctx.fillRect(x,y,w,item.mediaH);ctx.fillStyle=theme==='dark'?'#aaa':'#777';setFont(ctx,14,'600');ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(msg.kind==='video'?'Video':'Photo',x+w/2,y+item.mediaH/2);}
      if(msg.kind==='video') {ctx.fillStyle='rgba(0,0,0,.45)';ctx.beginPath();ctx.arc(x+w/2,y+item.mediaH/2,24,0,Math.PI*2);ctx.fill();ctx.fillStyle='#fff';ctx.beginPath();ctx.moveTo(x+w/2-6,y+item.mediaH/2-10);ctx.lineTo(x+w/2+12,y+item.mediaH/2);ctx.lineTo(x+w/2-6,y+item.mediaH/2+10);ctx.closePath();ctx.fill();}
      if(msg.text){ctx.fillStyle=fill;roundedRectPath(ctx,x,y+item.mediaH-2,w,h-item.mediaH+2,0);ctx.fill();ctx.fillStyle=textColor;setFont(ctx,16);ctx.textAlign='left';ctx.textBaseline='top';let yy=y+item.mediaH+8;for(const line of item.textLines){ctx.fillText(line,x+12,yy);yy+=20;}}
      if(item.replyText)drawReply(ctx,item,x,y,w,theme,msg);
      ctx.restore();
    } else if (msg.kind==='link') {
      ctx.fillStyle=fill;bubblePath(ctx,x,y,w,h,22,side,item.tail,item.groupedTop,item.groupedBottom);ctx.fill();
      let yy=y+10;if(item.replyText) yy+=drawReply(ctx,item,x,y,w,theme,msg);
      ctx.fillStyle=msg.sender==='me'?'rgba(255,255,255,.75)':(theme==='dark'?'#aaa':'#6e6e73');setFont(ctx,11,'600');ctx.textAlign='left';ctx.textBaseline='top';
      let host='Link';try{host=new URL(msg.url).hostname.replace(/^www\./,'');}catch{}
      ctx.fillText(host,x+13,yy);yy+=17;ctx.fillStyle=textColor;setFont(ctx,16,'600');for(const line of item.textLines){ctx.fillText(line,x+13,yy);yy+=20;}
      ctx.globalAlpha=.75;setFont(ctx,12,'400');ctx.fillText((msg.url||'').slice(0,36),x+13,y+h-23);ctx.globalAlpha=1;
    } else if (msg.kind==='file') {
      ctx.fillStyle=fill;bubblePath(ctx,x,y,w,h,22,side,item.tail,item.groupedTop,item.groupedBottom);ctx.fill();let yy=y+10;if(item.replyText)yy+=drawReply(ctx,item,x,y,w,theme,msg);
      ctx.fillStyle=msg.sender==='me'?'rgba(255,255,255,.22)':(theme==='dark'?'#46464a':'#d4d4d8');roundedRectPath(ctx,x+12,yy,42,42,9);ctx.fill();
      ctx.fillStyle=textColor;setFont(ctx,10,'700');ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText('DOC',x+33,yy+21);
      ctx.textAlign='left';ctx.textBaseline='top';setFont(ctx,15,'600');let ty=yy+4;for(const line of item.textLines){ctx.fillText(line,x+64,ty);ty+=18;}
    } else if(msg.kind==='voice') {
      ctx.fillStyle=fill;bubblePath(ctx,x,y,w,h,22,side,item.tail,item.groupedTop,item.groupedBottom);ctx.fill();let yy=y;if(item.replyText)yy+=drawReply(ctx,item,x,y,w,theme,msg);
      const cy=yy+(h-item.replyH)/2;ctx.fillStyle=textColor;ctx.beginPath();ctx.arc(x+24,cy,12,0,Math.PI*2);ctx.fill();ctx.fillStyle=fill;ctx.beginPath();ctx.moveTo(x+21,cy-6);ctx.lineTo(x+29,cy);ctx.lineTo(x+21,cy+6);ctx.closePath();ctx.fill();
      ctx.strokeStyle=textColor;ctx.lineWidth=2;ctx.lineCap='round';for(let i=0;i<18;i++){const xx=x+48+i*7;const amp=3+Math.abs(Math.sin(i*1.73))*10;ctx.beginPath();ctx.moveTo(xx,cy-amp/2);ctx.lineTo(xx,cy+amp/2);ctx.stroke();}
      ctx.fillStyle=textColor;ctx.globalAlpha=.78;setFont(ctx,11,'600');ctx.textAlign='right';ctx.textBaseline='middle';ctx.fillText(msg.text||'0:12',x+w-12,cy);ctx.globalAlpha=1;
    } else if(msg.kind==='sticker') {
      const img=decodedImages.get(msg.mediaData);let yy=y;if(item.replyText)yy+=drawReply(ctx,item,x,y,w,theme,msg);
      if(img) drawImageCover(ctx,img,x,yy,110,110,18); else {ctx.fillStyle=theme==='dark'?'#fff':'#111';setFont(ctx,58,'400');ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(msg.text||'⭐️',x+w/2,yy+55);}
    }

    if(msg.reaction){
      const rx=msg.sender==='me'?x+12:x+w-12, ry=y-5, dark=theme==='dark';
      ctx.fillStyle=dark?'#424246':'#e9e9ed';ctx.strokeStyle=dark?'#000':'#fff';ctx.lineWidth=2;
      roundedRectPath(ctx,rx-14,ry-13,28,26,13);ctx.fill();ctx.stroke();
      ctx.beginPath();ctx.arc(rx+(msg.sender==='me'?-10:10),ry+12,3.5,0,Math.PI*2);ctx.fill();ctx.stroke();
      setFont(ctx,15);ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillStyle=dark?'#fff':'#111';ctx.fillText(msg.reaction,rx,ry+1);
    }

    let labelY=y+h+3;
    if(msg.edited){ctx.fillStyle='#8e8e93';setFont(ctx,11,'500');ctx.textBaseline='top';ctx.textAlign=msg.sender==='me'?'right':'left';ctx.fillText('Edited',msg.sender==='me'?x+w:x,labelY);labelY+=16;}
    if(msg.sender==='me' && msg.delivery){ctx.fillStyle='#8e8e93';setFont(ctx,11,'500');ctx.textBaseline='top';ctx.textAlign='right';ctx.fillText(msg.delivery,x+w-20,labelY+3);}
  }

  function drawStatusBar(ctx,theme) {
    const fg=theme==='dark'?'#fff':'#000';
    ctx.fillStyle=fg;ctx.textAlign='left';ctx.textBaseline='middle';setFont(ctx,17,'700');ctx.fillText(state.status.time||'9:41',45,32);

    if(state.status.silent){
      // Bell-with-slash, drawn instead of relying on an unsupported glyph.
      ctx.save();ctx.translate(96,32);ctx.strokeStyle=fg;ctx.fillStyle=fg;ctx.lineWidth=1.65;ctx.lineCap='round';ctx.lineJoin='round';
      ctx.beginPath();ctx.moveTo(-4.5,2.8);ctx.lineTo(-3.2,1.1);ctx.lineTo(-3.2,-2.2);ctx.bezierCurveTo(-3.2,-5.1,-1.5,-6.7,0,-6.7);ctx.bezierCurveTo(1.5,-6.7,3.2,-5.1,3.2,-2.2);ctx.lineTo(3.2,1.1);ctx.lineTo(4.5,2.8);ctx.stroke();
      ctx.beginPath();ctx.moveTo(-1.7,4.5);ctx.quadraticCurveTo(0,6.1,1.7,4.5);ctx.stroke();
      ctx.lineWidth=2.15;ctx.beginPath();ctx.moveTo(-5.8,-7.2);ctx.lineTo(5.8,7.2);ctx.stroke();ctx.restore();
    }
    if(state.status.focus){ctx.fillStyle=fg;ctx.beginPath();ctx.arc(108,29,4.5,0,Math.PI*2);ctx.fill();}

    const baseX=284.7,baseY=39.7;ctx.fillStyle=fg;
    const signalHeights=[6.7,8.7,11.3,13.7];
    for(let i=0;i<4;i++){const active=i<state.status.signal;ctx.globalAlpha=active?1:.18;const h=signalHeights[i];roundedRectPath(ctx,baseX+i*5.67,baseY-h,3.67,h,1.84);ctx.fill();}
    ctx.globalAlpha=1;drawWifi(ctx,322.5,29,state.status.wifi,fg);

    const bx=341,by=25.5,bw=26,bh=13;ctx.fillStyle=theme==='dark'?'#5c5c60':'#b5b5b8';roundedRectPath(ctx,bx,by,bw,bh,4);ctx.fill();
    const pct=Math.max(0,Math.min(100,state.status.battery));
    ctx.save();roundedRectPath(ctx,bx,by,bw,bh,4);ctx.clip();ctx.fillStyle=pct<=20?'#ff3b30':fg;ctx.fillRect(bx,by,bw*pct/100,bh);ctx.restore();ctx.fillStyle=theme==='dark'?'#777':'#c5c5c7';roundedRectPath(ctx,bx+bw+1,by+4.5,2,5,1);ctx.fill();
    if(state.status.charging){ctx.fillStyle=theme==='dark'?'#000':'#fff';setFont(ctx,10,'700');ctx.textAlign='center';ctx.fillText('⚡',bx+bw/2,by+bh/2+1);}
    else if(state.status.batteryPct){ctx.fillStyle=theme==='dark'?'#000':'#fff';setFont(ctx,8.5,'700');ctx.textAlign='center';ctx.fillText(String(state.status.battery),bx+bw/2,by+bh/2+1);}
  }

  function drawWifi(ctx,cx,cy,level,color){ctx.save();ctx.strokeStyle=color;ctx.lineWidth=2.2;ctx.lineCap='round';const radii=[10,6.5,3];for(let i=0;i<3;i++){ctx.globalAlpha=(3-i)<=level?1:.18;ctx.beginPath();ctx.arc(cx,cy+7,radii[i],Math.PI*1.18,Math.PI*1.82);ctx.stroke();}ctx.globalAlpha=1;ctx.fillStyle=color;ctx.beginPath();ctx.arc(cx,cy+7,1.8,0,Math.PI*2);ctx.fill();ctx.restore();}

  function drawAvatar(ctx,cx,cy,r) {
    const c=state.contact;
    if(c.avatarStyle==='photo' && c.photo){const img=decodedImages.get(c.photo);if(img){ctx.save();ctx.beginPath();ctx.arc(cx,cy,r,0,Math.PI*2);ctx.clip();const s=Math.min(img.width,img.height),sx=(img.width-s)/2,sy=(img.height-s)/2;ctx.drawImage(img,sx,sy,s,s,cx-r,cy-r,r*2,r*2);ctx.restore();return;}}
    const g=ctx.createLinearGradient(0,cy-r,0,cy+r);g.addColorStop(0,c.color||'#879bd1');g.addColorStop(1,c.color||'#879bd1');ctx.fillStyle=g;ctx.beginPath();ctx.arc(cx,cy,r,0,Math.PI*2);ctx.fill();
    const shade=ctx.createLinearGradient(0,cy-r,0,cy+r);shade.addColorStop(0,'rgba(255,255,255,.23)');shade.addColorStop(1,'rgba(0,0,0,.09)');ctx.fillStyle=shade;ctx.fill();
    ctx.fillStyle='#fff';ctx.textAlign='center';ctx.textBaseline='middle';setFont(ctx,c.avatarStyle==='emoji'?30:27,c.avatarStyle==='emoji'?'400':'600');ctx.fillText(c.avatarStyle==='emoji'?(c.emoji||'🙂'):(c.initials||'C'),cx,cy+1);
  }

  function drawBackButton(ctx,theme) {
    const fg=theme==='dark'?'#fff':'#000';ctx.fillStyle=theme==='dark'?'rgba(58,58,60,.62)':'rgba(248,248,250,.70)';ctx.strokeStyle=theme==='dark'?'rgba(255,255,255,.24)':'rgba(60,60,67,.18)';ctx.lineWidth=.7;ctx.beginPath();ctx.arc(38,84,22.5,0,Math.PI*2);ctx.fill();ctx.stroke();ctx.strokeStyle=fg;ctx.lineWidth=3.2;ctx.lineCap='round';ctx.lineJoin='round';ctx.beginPath();ctx.moveTo(43,75);ctx.lineTo(34,84);ctx.lineTo(43,93);ctx.stroke();
  }

  function drawVideoButton(ctx,theme) {
    const fg=theme==='dark'?'#fff':'#000';ctx.fillStyle=theme==='dark'?'rgba(58,58,60,.62)':'rgba(248,248,250,.70)';ctx.strokeStyle=theme==='dark'?'rgba(255,255,255,.24)':'rgba(60,60,67,.18)';ctx.lineWidth=.7;ctx.beginPath();ctx.arc(364,84,22.5,0,Math.PI*2);ctx.fill();ctx.stroke();ctx.strokeStyle=fg;ctx.lineWidth=1.8;roundedRectPath(ctx,353,78,14,12,3);ctx.stroke();ctx.beginPath();ctx.moveTo(367,81);ctx.lineTo(374,77);ctx.lineTo(374,91);ctx.lineTo(367,87);ctx.closePath();ctx.stroke();
  }

  function drawHeader(ctx,theme) {
    // translucent veil gives the iOS glass effect while allowing messages beneath to faintly show on scroll
    const grad=ctx.createLinearGradient(0,42,0,162);const base=theme==='dark'?'0,0,0':'255,255,255';grad.addColorStop(0,`rgba(${base},.96)`);grad.addColorStop(.72,`rgba(${base},.78)`);grad.addColorStop(1,`rgba(${base},0)`);ctx.fillStyle=grad;ctx.fillRect(0,42,LOGICAL_W,128);
    drawBackButton(ctx,theme);drawVideoButton(ctx,theme);
    let name=state.contact.name||'Contact';setFont(ctx,16.5,'700');while(ctx.measureText(name).width>146 && name.length>1)name=name.slice(0,-2)+'…';const tw=ctx.measureText(name).width;const pw=Math.min(188,Math.max(76,tw+34));const px=(LOGICAL_W-pw)/2,py=117;
    ctx.fillStyle=theme==='dark'?'rgba(58,58,60,.72)':'rgba(248,248,250,.76)';ctx.strokeStyle=theme==='dark'?'rgba(255,255,255,.24)':'rgba(60,60,67,.18)';ctx.lineWidth=.65;roundedRectPath(ctx,px,py,pw,34,17);ctx.fill();ctx.stroke();ctx.fillStyle=theme==='dark'?'#fff':'#000';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(name,LOGICAL_W/2-4,py+17);ctx.strokeStyle=theme==='dark'?'#d1d1d6':'#8e8e93';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(px+pw-18,py+12);ctx.lineTo(px+pw-14,py+17);ctx.lineTo(px+pw-18,py+22);ctx.stroke();
    // Draw last so the avatar occludes the upper five points of the pill.
    drawAvatar(ctx,LOGICAL_W/2,92,30);
  }

  function drawComposer(ctx,theme) {
    const fg=theme==='dark'?'#fff':'#000';const stroke=theme==='dark'?'rgba(255,255,255,.24)':'rgba(60,60,67,.18)';const fill=theme==='dark'?'rgba(38,38,40,.72)':'rgba(250,250,252,.72)';
    ctx.fillStyle=fill;ctx.strokeStyle=stroke;ctx.lineWidth=.75;ctx.beginPath();ctx.arc(48,826,20,0,Math.PI*2);ctx.fill();ctx.stroke();ctx.strokeStyle=fg;ctx.lineWidth=1.8;ctx.beginPath();ctx.moveTo(48,818);ctx.lineTo(48,834);ctx.moveTo(40,826);ctx.lineTo(56,826);ctx.stroke();
    const x=80,y=806,w=294,h=40;ctx.fillStyle=fill;ctx.strokeStyle=stroke;ctx.lineWidth=.55;roundedRectPath(ctx,x,y,w,h,23);ctx.fill();ctx.stroke();ctx.fillStyle=theme==='dark'?'#8e8e93':'#bdbdc2';setFont(ctx,17,'400');ctx.textAlign='left';ctx.textBaseline='middle';ctx.fillText('iMessage',x+16,y+h/2+1);
    // waveform icon
    ctx.strokeStyle=theme==='dark'?'#8e8e93':'#a8a8ae';ctx.lineCap='round';ctx.lineWidth=2;const cx=x+w-22,cy=y+h/2;const hs=[3,7,12,7,4];for(let i=0;i<hs.length;i++){ctx.beginPath();ctx.moveTo(cx+(i-2)*4,cy-hs[i]/2);ctx.lineTo(cx+(i-2)*4,cy+hs[i]/2);ctx.stroke();}
  }

  function drawConversationIntro(ctx,scrollTop,theme) {
    const y=CONTENT_TOP-scrollTop;if(y<-48||y>COMPOSER_TOP)return;
    const muted='#8e8e93';ctx.fillStyle=muted;ctx.textAlign='center';ctx.textBaseline='top';setFont(ctx,11,'500');ctx.fillText('iMessage',LOGICAL_W/2,y);

    setFont(ctx,11,'400');const label='Encrypted';const labelW=ctx.measureText(label).width;const lockW=6, gap=4;const groupW=lockW+gap+labelW;const startX=(LOGICAL_W-groupW)/2;const ly=y+13;
    ctx.strokeStyle=muted;ctx.fillStyle=muted;ctx.lineWidth=1.15;ctx.lineCap='round';
    // tiny native-style lock
    roundedRectPath(ctx,startX,ly+4,5.6,5.4,1);ctx.fill();
    ctx.beginPath();ctx.arc(startX+2.8,ly+4,1.9,Math.PI,0);ctx.stroke();
    ctx.fillStyle=muted;ctx.textAlign='left';ctx.fillText(label,startX+lockW+gap,ly);
  }

  async function renderTo(ctx,scale=1,scrollTop=0) {
    await Promise.all([loadImage(state.contact.photo),...state.messages.map(m=>loadImage(m.mediaData))]);
    ctx.save();ctx.scale(scale,scale);ctx.clearRect(0,0,LOGICAL_W,LOGICAL_H);
    const theme=state.theme;drawBackground(ctx,theme,state.backgroundEffect);
    ctx.save();ctx.beginPath();ctx.rect(0,42,LOGICAL_W,COMPOSER_TOP-42);ctx.clip();
    drawConversationIntro(ctx,scrollTop,theme);
    const layout=layoutCache || computeLayout();
    for(const item of layout.items){const sy=item.y-scrollTop;if(sy+item.h+item.bottomExtra<42||sy>COMPOSER_TOP)continue;drawTimestamp(ctx,item,sy,theme);drawMessage(ctx,item,sy,theme);}
    ctx.restore();
    const base=theme==='dark'?'0,0,0':'255,255,255',fade=ctx.createLinearGradient(0,780,0,806);fade.addColorStop(0,`rgba(${base},0)`);fade.addColorStop(1,`rgba(${base},1)`);ctx.fillStyle=fade;ctx.fillRect(0,780,LOGICAL_W,26);
    drawHeader(ctx,theme);drawStatusBar(ctx,theme);drawComposer(ctx,theme);ctx.restore();
  }

  async function renderPreview() {
    renderRequested++;
    if(previewBusy)return;
    previewBusy=true;
    try{let ticket;do{ticket=renderRequested;const ctx=canvas.getContext('2d');ctx.setTransform(1,0,0,1,0,0);await renderTo(ctx,EXPORT_SCALE,scroller.scrollTop);}while(ticket!==renderRequested);}finally{previewBusy=false;}
  }

  async function refreshRender() {
    document.documentElement.dataset.theme=state.theme;
    document.querySelector('meta[name="theme-color"]').content=state.theme==='dark'?'#111216':'#e8e8ed';
    layoutCache=computeLayout();
    const maxScroll=Math.max(0,layoutCache.totalContentHeight-LOGICAL_H);
    if(scroller.scrollTop>maxScroll) scroller.scrollTop=maxScroll;
    spacer.style.height=`${layoutCache.totalContentHeight}px`;
    rebuildHitLayer();
    await renderPreview();
  }

  function rebuildHitLayer() {
    hitLayer.innerHTML='';if(!layoutCache)return;
    for(const item of layoutCache.items){const b=document.createElement('button');b.className='message-hit';b.style.left=`${item.x}px`;b.style.top=`${item.y}px`;b.style.width=`${item.w}px`;b.style.height=`${item.h+item.bottomExtra}px`;b.dataset.id=item.msg.id;b.setAttribute('aria-label',`Edit ${item.msg.sender==='me'?'sent':'received'} message`);
      let startX=0,startY=0,didLongPress=false;
      b.addEventListener('pointerdown',e=>{startX=e.clientX;startY=e.clientY;didLongPress=false;clearTimeout(longPressTimer);longPressTimer=setTimeout(()=>{didLongPress=true;openMessageDialog(item.msg.id,true);},520);});
      b.addEventListener('pointermove',e=>{if(Math.hypot(e.clientX-startX,e.clientY-startY)>10)clearTimeout(longPressTimer);});
      b.addEventListener('pointerup',()=>clearTimeout(longPressTimer));b.addEventListener('pointercancel',()=>clearTimeout(longPressTimer));
      b.addEventListener('click',()=>{if(didLongPress){didLongPress=false;return;}openMessageDialog(item.msg.id,false);});hitLayer.appendChild(b);
    }
  }

  function toast(msg){const t=$('toast');t.textContent=msg;t.classList.add('show');clearTimeout(t._timer);t._timer=setTimeout(()=>t.classList.remove('show'),2200);}

  function syncChoices(){document.querySelectorAll('select[data-choice]').forEach(sel=>{const b=$(sel.id+'Choice');if(b)b.querySelector('span').textContent=sel.selectedOptions[0]?.textContent||'None';});}
  function openDialog(dialog){syncChoices();if(typeof dialog.showModal==='function')dialog.showModal();else dialog.setAttribute('open','');dialog.querySelector('.sheet-card').scrollTop=0;}
  function closeDialog(dialog){if(typeof dialog.close==='function')dialog.close();else dialog.removeAttribute('open');}

  function setSegment(container,attr,value){container.querySelectorAll('button').forEach(b=>{b.classList.toggle('active',b.dataset[attr]===value);b.setAttribute('aria-pressed',String(b.dataset[attr]===value));});}
  function getSegment(container,attr){return container.querySelector('button.active')?.dataset[attr];}

  function updateKindFields(kind){
    $('mediaFieldWrap').style.display=['photo','video','sticker'].includes(kind)?'grid':'none';
    $('linkFieldWrap').style.display=kind==='link'?'grid':'none';$('fileFieldWrap').style.display=kind==='file'?'grid':'none';
    $('textFieldWrap').style.display=['photo','video','sticker','text','link','voice'].includes(kind)?'grid':'none';
    $('messageText').placeholder=kind==='voice'?'Duration, e.g. 0:12':kind==='sticker'?'Emoji if no image is selected':'Message';
  }

  function rebuildReplyOptions(currentId=''){
    const sel=$('messageReplyTo');sel.innerHTML='<option value="">No reply</option>';
    state.messages.forEach((m,i)=>{if(m.id===currentId)return;const o=document.createElement('option');o.value=m.id;o.textContent=`${i+1}. ${m.sender==='me'?'Me':'Them'} — ${(m.text||m.fileName||m.url||m.kind).slice(0,42)}`;sel.appendChild(o);});
  }

  function openMessageDialog(id=null,longPress=false){
    editingMessageId=id;const msg=id?state.messages.find(m=>m.id===id):null;$('messageDialogTitle').textContent=msg?'Edit Message':'Add Message';$('editActions').classList.toggle('hidden',!msg);
    rebuildReplyOptions(id||'');const sender=msg?.sender||'them',kind=msg?.kind||'text';setSegment($('senderSegment'),'sender',sender);setSegment($('kindSegment'),'kind',kind);updateKindFields(kind);
    $('messageText').value=msg?.text||'';$('messageUrl').value=msg?.url||'';$('messageFileName').value=msg?.fileName||'';$('messageDate').value=msg?.datetime||nowLocalInput();$('messageDelivery').value=msg?.delivery||'';$('messageReaction').value=msg?.reaction||'';$('messageChannel').value=msg?.channel||'imessage';$('messageEdited').checked=!!msg?.edited;$('messageDeleted').checked=!!msg?.deleted;$('messageReplyTo').value=msg?.replyTo||'';$('messageMedia').value='';
    $('saveMessageBtn').textContent=msg?'Save':'Add';
    const index=state.messages.findIndex(m=>m.id===id);$('moveUpBtn').disabled=index<=0;$('moveDownBtn').disabled=index<0||index>=state.messages.length-1;
    openDialog($('messageDialog'));if(longPress)toast('Message actions opened');
  }

  async function saveMessage(){
    const sender=getSegment($('senderSegment'),'sender')||'them';const kind=getSegment($('kindSegment'),'kind')||'text';
    const data={sender,kind,text:$('messageText').value,url:$('messageUrl').value.trim(),fileName:$('messageFileName').value.trim(),datetime:$('messageDate').value||nowLocalInput(),delivery:sender==='me'?$('messageDelivery').value:'',reaction:$('messageReaction').value,channel:$('messageChannel').value,edited:$('messageEdited').checked,deleted:$('messageDeleted').checked,replyTo:$('messageReplyTo').value};
    if(kind==='text'&&!data.text.trim()&&!data.deleted){toast('Enter a message first');$('messageText').focus();return;}
    const file=$('messageMedia').files?.[0];
    if(file && file.size>30*1024*1024){toast('Choose media under 30 MB');return;}
    const button=$('saveMessageBtn');if(button.disabled)return;button.disabled=true;
    try{
    if(file){if(kind==='video')data.mediaData=await videoThumbnail(file);else data.mediaData=await fileToDataUrl(file);if(!data.mediaData || !(await loadImage(data.mediaData))){toast('This media could not be decoded. Try JPEG, PNG, or a supported video.');return;}}
    if(editingMessageId){const idx=state.messages.findIndex(m=>m.id===editingMessageId);if(idx>=0)state.messages[idx]={...state.messages[idx],...data};}
    else state.messages.push({id:uid(),mediaData:'',...data});
    state.messages.sort((a,b)=>parseLocalDate(a.datetime)-parseLocalDate(b.datetime));closeDialog($('messageDialog'));await refreshRender();requestAnimationFrame(()=>{scroller.scrollTop=Math.max(0,layoutCache.totalContentHeight-LOGICAL_H);});
    }catch{toast('Could not read that media. Your draft is still here.');}finally{button.disabled=false;}
  }

  function moveMessage(dir){
    if(!editingMessageId)return;
    const i=state.messages.findIndex(m=>m.id===editingMessageId),j=i+dir;
    if(i<0||j<0||j>=state.messages.length)return;
    const dtI=state.messages[i].datetime,dtJ=state.messages[j].datetime;
    [state.messages[i],state.messages[j]]=[state.messages[j],state.messages[i]];
    state.messages[i].datetime=dtI;
    state.messages[j].datetime=dtJ;
    closeDialog($('messageDialog'));
    refreshRender();
    toast(dir<0?'Moved earlier':'Moved later');
  }
  function deleteMessage(){if(!editingMessageId)return;const id=editingMessageId;state.messages=state.messages.filter(m=>m.id!==id).map(m=>m.replyTo===id?{...m,replyTo:''}:m);closeDialog($('messageDialog'));refreshRender();toast('Message deleted');}

  function fileToDataUrl(file){return new Promise((resolve,reject)=>{const r=new FileReader();r.onload=()=>resolve(r.result);r.onerror=reject;r.readAsDataURL(file);});}
  function videoThumbnail(file){return new Promise(async resolve=>{const v=document.createElement('video');const url=URL.createObjectURL(file);v.src=url;v.muted=true;v.playsInline=true;v.preload='metadata';const done=(value)=>{URL.revokeObjectURL(url);resolve(value)};v.onloadeddata=()=>{try{v.currentTime=Math.min(.15,v.duration||.15);}catch{capture();}};v.onseeked=capture;v.onerror=()=>done('');function capture(){try{const c=document.createElement('canvas');c.width=640;c.height=Math.max(360,Math.round(640*(v.videoHeight||360)/(v.videoWidth||640)));const x=c.getContext('2d');x.drawImage(v,0,0,c.width,c.height);done(c.toDataURL('image/jpeg',.86));}catch{done('');}}setTimeout(()=>done(''),5000);});}

  function updateAvatarPreview(){const el=$('avatarPreview');const style=$('avatarStyle').value;el.style.backgroundColor=$('avatarColor').value;el.style.backgroundImage='none';el.textContent=style==='emoji'?($('contactEmoji').value||'🙂'):($('contactInitials').value||'C');const photo=$('avatarPhoto').files?.[0];if(style==='photo'&&photo){fileToDataUrl(photo).then(src=>{el.style.backgroundImage=`url(${src})`;el.textContent='';}).catch(()=>toast('Could not read photo'));}else if(style==='photo'&&state.contact.photo){el.style.backgroundImage=`url(${state.contact.photo})`;el.textContent='';}}
  async function saveContact(){const style=$('avatarStyle').value;let photo=state.contact.photo;const file=$('avatarPhoto').files?.[0];if(file)photo=await fileToDataUrl(file);state.contact={name:$('contactName').value.trim()||'Contact',initials:$('contactInitials').value.trim().slice(0,3)||'C',avatarStyle:style,emoji:$('contactEmoji').value||'🙂',color:$('avatarColor').value,photo};closeDialog($('contactDialog'));refreshRender();}

  function openContact(){const c=state.contact;$('contactName').value=c.name;$('contactInitials').value=c.initials;$('avatarStyle').value=c.avatarStyle;$('contactEmoji').value=c.emoji;$('avatarColor').value=c.color;$('avatarPhoto').value='';updateAvatarPreview();openDialog($('contactDialog'));}

  function openTools(){setSegment($('themeSegment'),'theme',state.theme);$('backgroundEffect').value=state.backgroundEffect;const s=state.status;$('statusTime').value=s.time;$('statusBattery').value=s.battery;$('statusSignal').value=s.signal;$('statusWifi').value=s.wifi;$('statusCharging').checked=s.charging;$('statusSilent').checked=s.silent;$('statusFocus').checked=s.focus;$('statusBatteryPct').checked=s.batteryPct;openDialog($('toolsDialog'));}
  function syncTools(){state.backgroundEffect=$('backgroundEffect').value;state.status={time:$('statusTime').value||'9:41',battery:+$('statusBattery').value||0,signal:+$('statusSignal').value,wifi:+$('statusWifi').value,charging:$('statusCharging').checked,silent:$('statusSilent').checked,focus:$('statusFocus').checked,batteryPct:$('statusBatteryPct').checked};refreshRender();}

  async function exportScreen(){
    syncTools();toast('Rendering 1206 × 2622…');
    const out=document.createElement('canvas');out.width=1206;out.height=2622;const ctx=out.getContext('2d');layoutCache=computeLayout();await renderTo(ctx,EXPORT_SCALE,scroller.scrollTop);
    const blob=await new Promise(r=>out.toBlob(r,'image/png',1));if(!blob){toast('Export failed');return;}
    const file=new File([blob],`Bubble-Lab-${Date.now()}.png`,{type:'image/png'});
    try{if(navigator.canShare?.({files:[file]})){await navigator.share({files:[file],title:'Bubble Lab'});toast('Ready to share');return;}}catch(e){if(e?.name==='AbortError')return;}
    const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=file.name;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(a.href),1000);toast('PNG exported');
  }

  function resetConversation(){if(!confirm('Discard this conversation and reset Bubble Lab?'))return;state=defaultState();scroller.scrollTop=0;closeDialog($('toolsDialog'));refreshRender();toast('Conversation reset');}

  function installChoices(){
    document.querySelectorAll('select').forEach(sel=>{
      const label=sel.closest('label'),title=label?.firstChild.textContent.trim()||'Choose';
      sel.dataset.choice='true';sel.hidden=true;sel.style.display='none';
      const trigger=document.createElement('button');trigger.type='button';trigger.id=sel.id+'Choice';trigger.className='choice-trigger';trigger.setAttribute('aria-label',title);trigger.setAttribute('aria-haspopup','dialog');
      const value=document.createElement('span'),arrow=document.createElement('span');arrow.textContent='›';arrow.className='chevron';arrow.setAttribute('aria-hidden','true');trigger.append(value,arrow);sel.after(trigger);
      trigger.addEventListener('click',()=>{
        $('choiceTitle').textContent=title;const list=$('choiceOptions');list.replaceChildren();list.setAttribute('role','radiogroup');list.setAttribute('aria-label',title);
        Array.from(sel.options).forEach(option=>{
          const b=document.createElement('button');b.type='button';b.className='choice-option';b.setAttribute('role','radio');b.setAttribute('aria-checked',String(sel.value===option.value));
          const txt=document.createElement('span'),check=document.createElement('span');txt.textContent=option.textContent;check.textContent=sel.value===option.value?'✓':'';check.setAttribute('aria-hidden','true');b.append(txt,check);
          b.addEventListener('click',()=>{sel.value=option.value;sel.dispatchEvent(new Event('input',{bubbles:true}));sel.dispatchEvent(new Event('change',{bubbles:true}));syncChoices();closeDialog($('choiceDialog'));});list.appendChild(b);
        });openDialog($('choiceDialog'));
      });
    });syncChoices();
  }

  async function registerOffline(){
    if(!('serviceWorker' in navigator)||location.protocol==='file:')return;
    try{const reg=await navigator.serviceWorker.register('./sw.js',{updateViaCache:'none'});await reg.update();}catch{console.warn('Offline cache unavailable; online editing still works.');}
  }

  async function checkUpdate(){
    try{const response=await fetch('./index.html?check='+Date.now(),{cache:'no-store'});if(!response.ok)throw new Error();const html=await response.text(),version=html.match(/app\.js\?v=([^"']+)/)?.[1];
      if(!version||version===APP_VERSION){toast('You are on Bubble Lab '+APP_VERSION);return;}
      if(confirm('Version '+version+' is available. Reloading discards this conversation. Export it first if needed. Reload now?'))location.replace('./?v='+encodeURIComponent(version));
    }catch{toast('Could not check for updates. Try again when online.');}
  }

  function attachEvents(){
    scroller.addEventListener('scroll',()=>{requestAnimationFrame(renderPreview)} ,{passive:true});
    $('composerHit').addEventListener('click',()=>openMessageDialog());$('headerHit').addEventListener('click',openContact);$('backHit').addEventListener('click',openTools);$('videoHit').addEventListener('click',openContact);
    $('senderSegment').addEventListener('click',e=>{const b=e.target.closest('button[data-sender]');if(!b)return;setSegment($('senderSegment'),'sender',b.dataset.sender);});
    $('kindSegment').addEventListener('click',e=>{const b=e.target.closest('button[data-kind]');if(!b)return;setSegment($('kindSegment'),'kind',b.dataset.kind);updateKindFields(b.dataset.kind);});
    $('themeSegment').addEventListener('click',e=>{const b=e.target.closest('button[data-theme]');if(!b)return;state.theme=b.dataset.theme;setSegment($('themeSegment'),'theme',state.theme);refreshRender();});
    ['backgroundEffect','statusTime','statusBattery','statusSignal','statusWifi','statusCharging','statusSilent','statusFocus','statusBatteryPct'].forEach(id=>$(id).addEventListener('input',syncTools));
    $('saveMessageBtn').addEventListener('click',saveMessage);$('moveUpBtn').addEventListener('click',()=>moveMessage(-1));$('moveDownBtn').addEventListener('click',()=>moveMessage(1));$('deleteMessageBtn').addEventListener('click',deleteMessage);
    $('saveContactBtn').addEventListener('click',saveContact);['contactInitials','contactEmoji','avatarColor','avatarStyle','avatarPhoto'].forEach(id=>$(id).addEventListener('input',updateAvatarPreview));
    $('exportBtn').addEventListener('click',exportScreen);$('resetBtn').addEventListener('click',resetConversation);
    $('updateBtn').addEventListener('click',checkUpdate);
    document.querySelectorAll('.sheet-dialog').forEach(dialog=>{dialog.setAttribute('aria-modal','true');let start=null;dialog.querySelector('.grabber').addEventListener('pointerdown',e=>{start=e.clientY;e.target.setPointerCapture(e.pointerId);});dialog.querySelector('.grabber').addEventListener('pointerup',e=>{if(start!==null&&e.clientY-start>55)closeDialog(dialog);start=null;});});
    window.addEventListener('resize',()=>{fitEditor();refreshRender();});
    window.visualViewport?.addEventListener('resize',()=>{fitEditor();refreshRender();});
  }

  async function init(){
    fitEditor();installChoices();attachEvents();$('messageDate').value=nowLocalInput();$('appVersion').textContent=APP_VERSION;
    registerOffline();
    await refreshRender();
  }

  init();
})();
