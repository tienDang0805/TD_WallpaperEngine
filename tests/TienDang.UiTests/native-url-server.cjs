const http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..');
const media={'.mp4':fs.readFileSync(path.join(root,'design-demos/assets/galaxy-preview.mp4')),'.webm':fs.readFileSync(path.join(root,'artifacts/video-tools/VP9-Opus.WebM')),'.jpg':fs.readFileSync(path.join(root,'design-demos/assets/galaxy-poster.jpg'))};
const server=http.createServer((req,res)=>{
 const name=new URL(req.url,'http://localhost').pathname,ext=path.extname(name);
 if(name==='/missing.mp4'){res.writeHead(404);res.end();return}
 let body=media[ext]||Buffer.from('unknown'),mime=ext==='.mp4'?'video/mp4':ext==='.webm'?'video/webm':'image/jpeg';
 if(name==='/page.mp4'){body=Buffer.from('<html>not media</html>');mime='text/html'}
 if(name==='/corrupt.mp4')body=Buffer.from('not a real video');
 let start=0,end=body.length-1,status=200,unknown=name==='/unknown.webm',slow=name==='/slow.webm'||unknown;
 const headers={'Content-Type':mime,'Accept-Ranges':'bytes'};
 if(req.headers.range&&!unknown){const m=req.headers.range.match(/bytes=(\d+)-(\d*)/);if(m){start=Number(m[1]);end=m[2]?Math.min(end,Number(m[2])):end;status=206;headers['Content-Range']='bytes '+start+'-'+end+'/'+body.length}}
 if(!unknown)headers['Content-Length']=end-start+1;
 res.writeHead(status,headers);
 if(req.method==='HEAD'){res.end();return}
 if(slow){let offset=start;const timer=setInterval(()=>{if(offset>end){clearInterval(timer);res.end();return}const next=Math.min(offset+16384,end+1);res.write(body.subarray(offset,next));offset=next},75);res.on('close',()=>clearInterval(timer))}
 else res.end(body.subarray(start,end+1));
});
server.listen(0,'127.0.0.1',()=>{const url='http://127.0.0.1:'+server.address().port;fs.writeFileSync(path.join(root,'artifacts/native-url-fixture.txt'),url);console.log(url)});
