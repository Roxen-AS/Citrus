import fs from 'node:fs';import path from 'node:path';import crypto from 'node:crypto';import{execFileSync}from'node:child_process';import{createRequire}from'node:module';
import {assertPublicEvidence,publicGenerationReceipt,publicMotionReceipt,publicVerificationSummary} from './public-evidence.mjs';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'website/public/assets/launch-v1'),docs=path.join(root,'docs/launch/2026-10-21/media'),native=path.join(root,'launch-media/native');
const modules=process.env.CODEX_NODE_MODULES||path.join(process.env.USERPROFILE,'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules'),sharp=createRequire(path.join(modules,'_launch.cjs'))('sharp');
const hash=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const privateArchive=path.join(root,'.local/launch-media-private/2026-10-21');
function archivePrivate(file,label){
  const resolved=path.resolve(file);
  if(!resolved.startsWith(root+path.sep))throw new Error('Archive source outside workspace');
  fs.mkdirSync(privateArchive,{recursive:true});
  const destination=path.join(privateArchive,label+'-'+hash(file).slice(0,16)+path.extname(file));
  if(fs.existsSync(destination)){if(hash(destination)!==hash(file))throw new Error('Archive hash conflict');}
  else fs.copyFileSync(file,destination,fs.constants.COPYFILE_EXCL);
  if(hash(destination)!==hash(file))throw new Error('Archive copy verification failed');
}
// Preserve exact raw receipts locally before removing their public copies.
for(const filename of ['capture.log','capture.trx']){
  const file=path.join(docs,filename);
  if(fs.existsSync(file)){archivePrivate(file,'docs-'+path.parse(filename).name);fs.unlinkSync(file);}
}
for(const [base,label]of [[docs,'docs'],[path.join(root,'launch-media/source'),'source']]){
  for(const filename of ['authoring-verification.json','citrus-garden-generation.json','motion-source.json']){
    const file=path.join(base,filename);
    if(fs.existsSync(file)){const data=JSON.parse(fs.readFileSync(file,'utf8'));try{assertPublicEvidence(data,filename);}catch{archivePrivate(file,label+'-'+path.parse(filename).name);}}
  }
}
const manifest=JSON.parse(fs.readFileSync(path.join(out,'manifest.json'),'utf8'));
const nativeRecord=JSON.parse(fs.readFileSync(path.join(native,'capture-record.json'),'utf8'));
const videoRecord=JSON.parse(fs.readFileSync(path.join(native,'video.json'),'utf8'));
const photo=path.join(root,'launch-media/source/citrus-garden.png'),photoMeta=await sharp(photo).metadata();
const generationFile=path.join(root,'launch-media/source/citrus-garden-generation.json');
const generation=publicGenerationReceipt(JSON.parse(fs.readFileSync(generationFile,'utf8')));
fs.writeFileSync(generationFile,JSON.stringify(generation,null,2)+'\n');
const motionFile=path.join(root,'launch-media/source/motion-source.json');
const motion=publicMotionReceipt(JSON.parse(fs.readFileSync(motionFile,'utf8')));
fs.writeFileSync(motionFile,JSON.stringify(motion,null,2)+'\n');
if(hash(photo).toUpperCase()!==nativeRecord.OriginalPhotoSha256)throw new Error('Original sample photograph changed');
for(const [movie,poster,at]of[['wheel-workflow.mp4','wheel-workflow-poster.webp',1.8],['video-trim.mp4','video-trim-poster.webp',3.8]]){
const png=execFileSync('ffmpeg',['-hide_banner','-loglevel','error','-ss',String(at),'-i',path.join(out,movie),'-frames:v','1','-f','image2pipe','-c:v','png','pipe:1'],{maxBuffer:16*1024*1024});
await sharp(png).webp({quality:91,effort:5}).toFile(path.join(out,poster));const metadata=await sharp(path.join(out,poster)).metadata();manifest.assets.push({file:poster,width:metadata.width,height:metadata.height,role:'Decoded poster frame from the corresponding edited demonstration',time:at});
}
for(const filename of['citrus-launch-10s.vtt','wheel-workflow.vtt','video-trim.vtt']){
fs.copyFileSync(path.join(root,'launch-media/source/captions',filename),path.join(out,filename));
const contents=fs.readFileSync(path.join(out,filename),'utf8');if(!contents.startsWith('WEBVTT\n'))throw new Error('Invalid scene description '+filename);manifest.assets.push({file:filename,language:'en',role:'Descriptive captions for silent edited demonstration'});
}
const decode=[];
for(const filename of['citrus-launch-10s.mp4','wheel-workflow.mp4','video-trim.mp4']){
execFileSync('ffmpeg',['-hide_banner','-loglevel','error','-i',path.join(out,filename),'-f','null','-'],{stdio:'pipe'});
const probe=JSON.parse(execFileSync('ffprobe',['-v','error','-show_format','-show_streams','-of','json',path.join(out,filename)],{encoding:'utf8'}));
if(probe.streams.some(stream=>stream.codec_type==='audio'))throw new Error('Unexpected audio in intentionally silent film');
decode.push({file:filename,fullDecode:'Passed',width:probe.streams[0].width,height:probe.streams[0].height,durationSeconds:Number(probe.format.duration),codec:probe.streams[0].codec_name,frameRate:probe.streams[0].avg_frame_rate});
}
const provenance={version:1,date:'2026-10-21',credits:{product:'Citrus by Skytuhua',interface:'Actual current production WPF visual-tree exports. Wheel uses the production Direct3D11 shader and retained WPF controls over owned sample imagery.',photo:'Original AI-generated sample photograph commissioned for Citrus. A digital camera move over this still supplies the video editor input; it is not live camera footage.',brand:'Existing approved Citrus website brand-v3 mark, preserved separately from the real app icon.',typography:'Bundled Inter Display, SIL Open Font License1.1. Existing notices retained in the website font assets.',sound:'Intentionally silent. Descriptive English WebVTT files provided. No soundtrack or third-party audio.'},source:{file:'citrus-garden.png',nativeWidth:photoMeta.width,nativeHeight:photoMeta.height,sha256:hash(photo),generatedInterface:false},production:{composition:'Original deterministic Canvas2D layout and authored motion, rendered with installed Chromium and encoded with system FFmpeg libx264.',qualification:'Edited product demonstrations and product images. Native UI structure/labels are not repainted. Not a live Explorer recording, native input benchmark, capture-to-scanout measurement or claim of native4K generation.',desktopCaptureUsed:false,captureExclusionChanged:false,softwareSource:'https://github.com/Skytuhua/Citrus',authoring:'launch-media/'},verification:{sourceHashesUnchanged:nativeRecord.SourcePreservationVerified,imageOutputs:'Real image adjustment/background and PNG-to-WEBP engine outputs were independently reopened.',videoTrim:{inputDurationSeconds:8,startSeconds:1.2,endSeconds:6.5,reopenedDurationSeconds:videoRecord.ReopenedDurationSeconds,width:videoRecord.Width,height:videoRecord.Height},webVideos:decode}};
fs.writeFileSync(path.join(out,'provenance.json'),JSON.stringify(provenance,null,2)+'\n');manifest.assets.push({file:'provenance.json',role:'Relative-path public production credits and qualifications'});
// Repeated finalization never duplicates entries and always refreshes byte/hash records.
manifest.assets=[...new Map(manifest.assets.map(asset=>[asset.file,asset])).values()];
for(const asset of manifest.assets){const filename=path.join(out,asset.file);asset.bytes=fs.statSync(filename).size;asset.sha256=hash(filename);fs.copyFileSync(filename,path.join(docs,asset.file));}
fs.writeFileSync(path.join(out,'manifest.json'),JSON.stringify(manifest,null,2)+'\n');fs.copyFileSync(path.join(out,'manifest.json'),path.join(docs,'manifest.json'));
const outputFiles=[];
for(const name of['image-output.png','background-output.png','converted-output.webp','video-trim-output.mp4']){const file=path.join(native,name);fs.copyFileSync(file,path.join(docs,'sample-'+name));outputFiles.push({file:'sample-'+name,bytes:fs.statSync(file).size,sha256:hash(file)});}
fs.writeFileSync(path.join(docs,'authoring-verification.json'),JSON.stringify(publicVerificationSummary({capture:nativeRecord,video:videoRecord,image:JSON.parse(fs.readFileSync(path.join(native,'image-output.json'),'utf8')),background:JSON.parse(fs.readFileSync(path.join(native,'background-output.json'),'utf8')),decode,outputFiles}),null,2)+'\n');
fs.copyFileSync(path.join(root,'launch-media/source/citrus-garden-generation.json'),path.join(docs,'citrus-garden-generation.json'));
fs.copyFileSync(path.join(root,'launch-media/source/motion-source.json'),path.join(docs,'motion-source.json'));
fs.mkdirSync(path.join(docs,'licenses'),{recursive:true});
fs.copyFileSync(path.join(root,'launch-media/source/fonts/Inter-OFL.txt'),path.join(docs,'licenses/Inter-OFL.txt'));
let publicJsonFiles=0;
function checkPublicJson(directory){for(const entry of fs.readdirSync(directory,{withFileTypes:true})){const file=path.join(directory,entry.name);if(entry.isDirectory())checkPublicJson(file);else if(entry.name.endsWith('.json')){assertPublicEvidence(JSON.parse(fs.readFileSync(file,'utf8')),path.relative(root,file));publicJsonFiles++;}}}
for(const directory of[docs,out,path.join(root,'launch-media/source')])checkPublicJson(directory);
console.log('Public JSON privacy check passed: '+publicJsonFiles+' files');
console.log(JSON.stringify({assets:manifest.assets.length,totalBytes:manifest.assets.reduce((sum,asset)=>sum+asset.bytes,0),sourcePhoto:{width:photoMeta.width,height:photoMeta.height},decoded:decode,publicLocalPaths:JSON.stringify(provenance).includes('C:/')},null,2));
