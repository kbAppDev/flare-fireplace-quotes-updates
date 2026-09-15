const CACHE='bubble-lab-v0.4.2';
const ASSETS=['./','./index.html','./styles.css?v=0.4.2','./app.js?v=0.4.2','./manifest.webmanifest','./icons/icon-180.png','./icons/icon-192.png','./icons/icon-512.png'];
self.addEventListener('install',event=>event.waitUntil(caches.open(CACHE).then(cache=>cache.addAll(ASSETS)).then(()=>self.skipWaiting())));
self.addEventListener('activate',event=>event.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(k=>k.startsWith('bubble-lab-')&&k!==CACHE).map(k=>caches.delete(k)))).then(()=>self.clients.claim())));
self.addEventListener('fetch',event=>{
  const request=event.request,url=new URL(request.url);
  if(request.method!=='GET'||url.origin!==self.location.origin||url.searchParams.has('check'))return;
  if(request.mode==='navigate'){
    event.respondWith(fetch(request,{cache:'no-store'}).catch(()=>caches.open(CACHE).then(cache=>cache.match('./index.html'))));return;
  }
  // Only the explicit static shell is cached; no message or uploaded-media data.
  if(!ASSETS.some(path=>new URL(path,self.location).href===url.href))return;
  event.respondWith(caches.open(CACHE).then(async cache=>(await cache.match(request))||fetch(request)));
});
