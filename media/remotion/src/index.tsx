import React from 'react';
import {AbsoluteFill, Composition, Img, Sequence, interpolate, registerRoot, staticFile, useCurrentFrame, useVideoConfig} from 'remotion';
import {Audio} from '@remotion/media';
import story from './storyboard.json';

// All motion is derived from the frame clock; no CSS animation, random values or remote assets.
type SceneData = {id: string; seconds: number; tag: string; title: string; lines: string[]; captions: string[]};
const FPS = 30;
const C = {ink:'#102445', blue:'#1269eb', orange:'#ff9c47', muted:'#61718a', paper:'#f5f8ff', line:'#dce5f4', green:'#16816b'};
const FONT = '"Noto Sans CJK SC", "Noto Sans SC", "Microsoft YaHei", sans-serif';
const clamp = {extrapolateLeft:'clamp' as const, extrapolateRight:'clamp' as const};
const seconds = (scenes: SceneData[]) => scenes.reduce((sum, item) => sum + item.seconds, 0);

const Logo: React.FC<{size?:number}> = ({size=50}) => <Img src={staticFile('icon.png')} style={{width:size,height:size,objectFit:'contain'}} />;
const Tag: React.FC<{children:React.ReactNode; dark?:boolean}> = ({children,dark}) => <div style={{fontSize:23,fontWeight:600,letterSpacing:2.5,color:dark?'#91baff':C.blue,marginBottom:26}}>{children}</div>;
const Chip: React.FC<{children:React.ReactNode; active?:boolean; dark?:boolean}> = ({children,active,dark}) => <div style={{padding:'17px 25px',borderRadius:18,fontSize:29,fontWeight:600,color:active?'white':dark?'#dbe8ff':C.ink,background:active?C.blue:dark?'#1f3556':'white',border:`1px solid ${active?C.blue:dark?'#3b5274':C.line}`,whiteSpace:'nowrap'}}>{children}</div>;
const Glyph: React.FC<{kind:string; size?:number}> = ({kind,size=70}) => <svg width={size} height={size} viewBox="0 0 64 64" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
  {kind==='qr' ? <><path d="M8 24V8h16M40 8h16v16M56 40v16H40M24 56H8V40"/><path d="M19 18h9v9h-9zM37 18h9v9h-9zM19 37h9v9h-9zM37 37h5v5h5v6M47 34v5"/></> : kind==='image' ? <><rect x="8" y="10" width="48" height="44" rx="6"/><circle cx="23" cy="24" r="5"/><path d="m12 49 17-16 9 8 7-8 10 13"/></> : kind==='text' ? <><path d="M9 15h29M23 8v7M15 21c4 11 10 17 24 23M32 15c-1 16-10 26-23 32M35 54l10-28 11 28M39 44h14"/></> : kind==='model' ? <><rect x="16" y="16" width="32" height="32" rx="7"/><rect x="24" y="24" width="16" height="16" rx="3"/><path d="M24 7v9M40 7v9M24 48v9M40 48v9M7 24h9M7 40h9M48 24h9M48 40h9"/></> : <><path d="m14 46 28-28 8 8-28 28-11 2zM39 11l3-7M52 18l7-3M50 6l4-4M15 26l2-7 2 7 7 2-7 2-2 7-2-7-7-2z"/></>}
</svg>;

const Backdrop: React.FC<{dark:boolean}> = ({dark}) => {
 const f=useCurrentFrame();
 return <AbsoluteFill style={{background:dark?'#0c1c35':C.paper,overflow:'hidden'}}>
   <div style={{position:'absolute',right:-240,top:-430,width:1200,height:1200,borderRadius:'50%',background:dark?'#17345c':'#e7efff',translate:`${Math.sin(f/110)*16}px ${Math.cos(f/100)*12}px`}}/>
   <div style={{position:'absolute',left:-160,bottom:-440,width:860,height:860,borderRadius:'50%',border:`1px solid ${dark?'#304461':'#d9e4f6'}`}}/>
   <div style={{position:'absolute',right:100,top:110,width:500,height:500,borderRadius:'50%',border:`1px solid ${dark?'#29486f':'#dce7f8'}`}}/>
 </AbsoluteFill>;
};
const Header: React.FC<{guide:boolean; dark:boolean}> = ({guide,dark}) => <div style={{position:'absolute',top:48,left:84,right:84,display:'flex',alignItems:'center',justifyContent:'space-between',color:dark?'white':C.ink}}>
 <div style={{display:'flex',gap:16,alignItems:'center'}}><Logo/><span style={{fontSize:29,fontWeight:700}}>截屏释义</span><span style={{fontSize:21,opacity:.5,marginLeft:14,letterSpacing:3}}>SCREEN INSIGHT</span></div>
 <div style={{fontSize:22,opacity:.7}}>{guide?'首次设置指南':'产品介绍'}<span style={{margin:'0 18px',opacity:.4}}>/</span>v{story.productVersion}</div>
</div>;
const Copy: React.FC<{scene:SceneData; dark:boolean}> = ({scene,dark}) => {
 const f=useCurrentFrame();
 return <div style={{position:'absolute',left:100,top:232,width:700,color:dark?'white':C.ink,opacity:interpolate(f,[6,24],[0,1],clamp),translate:`0 ${interpolate(f,[6,28],[24,0],clamp)}px`}}>
  <Tag dark={dark}>{scene.tag}</Tag>
  <div style={{fontSize:78,fontWeight:800,letterSpacing:-3,lineHeight:1.26,whiteSpace:'pre-line'}}>{scene.title}</div>
  <div style={{width:80,height:6,background:C.orange,margin:'36px 0 30px',borderRadius:3}}/>
  {scene.lines.map((line,i)=><div key={line} style={{fontSize:30,lineHeight:1.65,color:dark?'#b6c9e6':C.muted,marginBottom:7,opacity:interpolate(f,[22+i*6,36+i*6],[0,1],clamp)}}>{line}</div>)}
 </div>;
};
const Cursor: React.FC<{x:number;y:number}> = ({x,y}) => {
 const f=useCurrentFrame();
 const pulse=interpolate(f,[48,58,74],[0,1,0],clamp);
 return <div style={{position:'absolute',left:x,top:y,translate:`${interpolate(f,[10,48],[115,0],clamp)}px ${interpolate(f,[10,48],[95,0],clamp)}px`,zIndex:5,opacity:interpolate(f,[10,20],[0,1],clamp)}}>
  <div style={{position:'absolute',left:-22,top:-22,width:60,height:60,borderRadius:'50%',border:`4px solid ${C.orange}`,scale:1+pulse*.5,opacity:pulse}}/>
  <svg width="37" height="48" viewBox="0 0 32 42"><path d="M3 2v32l8-8 7 13 6-4-7-12 12-1Z" fill={C.ink} stroke="white" strokeWidth="2.5"/></svg>
 </div>;
};
type Box={x:number;y:number;w:number;h:number};
const Shot: React.FC<{name:string; width?:number; height?:number; x?:number;y?:number; mark?:Box; cursor?:boolean; label?:string}> = ({name,width=1000,height=692,x=834,y=184,mark,cursor,label='v0.7.0 原生界面 · 合成演示'}) => {
 const f=useCurrentFrame();
 return <div style={{position:'absolute',left:x,top:y,width,height,opacity:interpolate(f,[12,30],[0,1],clamp),translate:`0 ${interpolate(f,[12,34],[18,0],clamp)}px`}}>
  <div style={{position:'absolute',inset:0,background:'white',borderRadius:22,boxShadow:'0 26px 60px #18375b20',border:'1px solid #d8e2f3',overflow:'hidden'}}><Img src={staticFile(`${name}.png`)} style={{width:'100%',height:'100%',objectFit:'contain'}}/>
   {mark && <div style={{position:'absolute',left:mark.x*width,top:mark.y*height,width:mark.w*width,height:mark.h*height,border:`4px solid ${C.orange}`,borderRadius:14,background:'#ffad4510',opacity:interpolate(f,[32,46],[0,1],clamp)}}/>}
  </div>
  {mark && cursor && <Cursor x={(mark.x+mark.w*.65)*width} y={(mark.y+mark.h*.65)*height}/>}
  <div style={{position:'absolute',left:12,top:height+17,fontSize:20,color:C.muted}}>{label}</div>
 </div>;
};
const LocalArt: React.FC = () => {
 const f=useCurrentFrame();
 return <div style={{position:'absolute',left:900,top:208,width:880,height:650,color:'white'}}>
  <div style={{position:'absolute',inset:'48px 44px 55px',borderRadius:44,border:'1px solid #50729e',background:'#142d50',boxShadow:'0 28px 80px #020e2240'}}/>
  <div style={{position:'absolute',left:293,top:172,width:274,height:254,borderRadius:36,background:'linear-gradient(135deg,#286ee0,#164b9d)',display:'flex',flexDirection:'column',alignItems:'center',justifyContent:'center',gap:14,scale:interpolate(f,[10,42],[.92,1],clamp)}}><Logo size={112}/><div style={{fontSize:35,fontWeight:700}}>本机模型</div><div style={{fontSize:19,color:'#c9dfff',letterSpacing:2}}>LOCAL INFERENCE</div></div>
  {[['text','文字翻译',40,70],['image','图片描述',542,80],['prompt','提示词反推',90,454]].map(([kind,label,x,y],i)=><div key={String(kind)} style={{position:'absolute',left:Number(x),top:Number(y),display:'flex',alignItems:'center',gap:14,padding:'18px 24px',borderRadius:20,background:'#203f65',border:'1px solid #547295',fontSize:28,opacity:interpolate(f,[25+i*8,42+i*8],[0,1],clamp),translate:`0 ${Math.sin((f+i*50)/55)*5}px`}}><Glyph kind={String(kind)} size={46}/>{label}</div>)}
  <div style={{position:'absolute',right:45,bottom:31,padding:'15px 21px',borderRadius:16,background:'#fff',color:C.ink,fontSize:22}}>另有本地二维码解码器</div>
  <div style={{position:'absolute',left:130,top:130,width:595,height:377,border:'1px dashed #53749c',borderRadius:120,zIndex:-1}}/>
 </div>;
};
const TranslationArt: React.FC = () => {
 const f=useCurrentFrame();
 return <div style={{position:'absolute',left:886,top:204,width:914,height:650}}>
  <div style={{background:'white',border:`1px solid ${C.line}`,borderRadius:28,padding:44,boxShadow:'0 22px 50px #123b6210'}}>
   <Tag>SCREEN → TEXT</Tag><div style={{fontSize:45,color:C.ink,lineHeight:1.65}}>Make ideas travel.<br/>Keep your work local.</div>
   <div style={{marginTop:22,color:C.muted,fontSize:22}}>合成英文示例</div>
  </div>
  <div style={{position:'absolute',left:58,top:315,width:800,background:C.blue,borderRadius:28,padding:'34px 42px',boxSizing:'border-box',color:'white',translate:`0 ${interpolate(f,[30,60],[30,0],clamp)}px`,opacity:interpolate(f,[30,60],[0,1],clamp)}}>
   <div style={{fontSize:23,letterSpacing:3,opacity:.72,marginBottom:15}}>TEXT → MEANING</div>
   <div style={{fontSize:44,lineHeight:1.65,fontWeight:700}}>让想法跨越语言。<br/>让工作留在本机。</div>
   <div style={{fontSize:20,marginTop:14,opacity:.8}}>译文为说明流程的创作示例，并非模型实测输出</div>
  </div>
 </div>;
};
const VisionArt: React.FC = () => {
 const f=useCurrentFrame();
 return <div style={{position:'absolute',left:880,top:198,width:918,height:662}}>
  <div style={{height:350,borderRadius:32,overflow:'hidden',background:'#d9e9ff',position:'relative'}}>
   <svg viewBox="0 0 920 350" width="100%" height="100%"><rect width="920" height="350" fill="#dcecff"/><circle cx="710" cy="95" r="46" fill="#ffb765"/><path d="M0 330 225 66 453 335 644 116 920 333V350H0" fill="#718cc0"/><path d="M0 350 285 154 475 348 752 218 920 306V350" fill="#284d85"/><path d="M240 78 226 66 170 132l49-15 37 9z" fill="#f5f8ff"/></svg>
   <div style={{position:'absolute',left:25,bottom:21,fontSize:19,color:'white',background:'#102445b0',padding:'9px 16px',borderRadius:12}}>项目原创合成图形</div>
  </div>
  {['图片描述：山峦、日光与蓝色层次。','参考提示词：极简山景，冷暖对比，平面构图。'].map((t,i)=><div key={t} style={{marginTop:20,padding:'24px 30px',fontSize:i?27:31,background:i?'#fff1e3':'white',borderRadius:20,border:`1px solid ${C.line}`,color:C.ink,opacity:interpolate(f,[25+i*24,42+i*24],[0,1],clamp)}}>{t}</div>)}
  <div style={{fontSize:20,color:C.muted,marginTop:20}}>示意文字，不是模型准确率或原始提示词恢复承诺</div>
 </div>;
};
const PackageArt: React.FC = () => <div style={{position:'absolute',left:860,top:222,width:960}}>
 {[
 ['WINDOWS','安装程序 / 便携包','*-win-x64-setup.exe  /  *-win-x64.zip'],
 ['UBUNTU','DEB / 压缩包','*-linux-x64.deb  /  *-linux-x64.tar.gz'],
 ['COMPLETE','完全版内含应用与运行时','模型权重由你确认后下载']
 ].map(([tag,title,sub],i)=><div key={tag} style={{background:i===2?C.blue:'white',color:i===2?'white':C.ink,border:`1px solid ${C.line}`,padding:'25px 34px',marginBottom:21,borderRadius:24}}><div style={{fontSize:19,letterSpacing:3,opacity:.6,marginBottom:10}}>{tag}</div><div style={{fontSize:34,fontWeight:700}}>{title}</div><div style={{fontSize:24,opacity:.8,marginTop:13}}>{sub}</div></div>)}
</div>;
const UpgradeArt: React.FC = () => <div style={{position:'absolute',left:875,top:238,width:900}}>
 <div style={{padding:40,borderRadius:28,background:'white',border:`1px solid ${C.line}`}}><Tag>SAME COMPLETE APP ID</Tag><div style={{display:'flex',gap:22,alignItems:'center',fontSize:36}}><Chip>旧完全版</Chip><span style={{color:C.blue}}>→</span><Chip active>新完全版</Chip></div><div style={{marginTop:30,fontSize:31,color:C.muted,lineHeight:1.8}}>更新程序文件<br/>保留现有完全版配置和模型目录</div></div>
 <div style={{marginTop:24,padding:'28px 34px',borderRadius:23,background:'#fff1e2',fontSize:27,color:C.ink,lineHeight:1.7}}>旧标准版使用不同 AppId 与数据目录。<br/>它不是本次覆盖升级的目标。</div>
</div>;
const DownloadArt: React.FC = () => {
 const f=useCurrentFrame();
 return <>
  <Shot name="first-run" x={1110} y={169} width={616} height={637} mark={{x:.035,y:.565,w:.56,h:.09}} cursor/>
  <div style={{position:'absolute',left:846,top:263,width:230}}>{['下载','校验','启动','检查并保存'].map((t,i)=><div key={t} style={{padding:'22px 0',fontSize:30,color:f>35+i*45?C.blue:C.muted,display:'flex',alignItems:'center',gap:18}}><span style={{width:47,height:47,borderRadius:24,display:'flex',alignItems:'center',justifyContent:'center',background:f>35+i*45?C.blue:'white',color:f>35+i*45?'white':C.muted,fontSize:23}}>{i+1}</span>{t}</div>)}</div>
  <div style={{position:'absolute',left:866,top:852,fontSize:21,color:C.muted}}>阶段动画为流程示意，不代表真实下载、启动或推理耗时。</div>
 </>;
};
const EndArt: React.FC<{guide:boolean}> = ({guide}) => <div style={{position:'absolute',left:890,top:224,width:900,color:'white'}}>
 <div style={{border:'1px solid #38577d',background:'#152f51',borderRadius:36,padding:48}}><Logo size={108}/><div style={{fontSize:48,fontWeight:800,marginTop:24}}>截屏释义</div><div style={{fontSize:28,color:'#bdcfe7',marginTop:15}}>{guide?'模型配置引导，随时可再打开。':'你的本机模型，你的桌面入口。'}</div><div style={{marginTop:38,height:1,background:'#37567a'}}/><div style={{fontSize:29,marginTop:28}}>github.com/qingshihuan/pingyi</div><div style={{fontSize:25,color:'#88b7ff',marginTop:12}}>Releases → 选择系统对应的程序包</div></div>
 <div style={{display:'flex',gap:16,marginTop:27}}><Chip dark>Windows</Chip><Chip dark>Ubuntu</Chip><Chip dark>本机优先</Chip></div>
</div>;

const Art: React.FC<{id:string}> = ({id}) => {
 switch(id) {
  case 'hero': case 'setupHero': return <LocalArt/>;
  case 'local': return <Shot name="first-run" width={670} height={692} x={1040} y={170} mark={{x:.035,y:.565,w:.56,h:.09}} cursor/>;
  case 'capture': case 'firstCapture': return <Shot name="home" mark={{x:.05,y:.23,w:.34,h:.235}} cursor/>;
  case 'translate': return <TranslationArt/>;
  case 'vision': return <VisionArt/>;
  case 'qr': return <Shot name="qr-result" width={865} height={710} x={931} y={169} mark={{x:.025,y:.085,w:.95,h:.155}}/>;
  case 'manual': case 'correct': return <Shot name="manual-choice" width={865} height={710} x={931} y={169} mark={{x:.025,y:.085,w:.95,h:.155}} cursor/>;
  case 'packages': return <PackageArt/>;
  case 'upgrade': return <UpgradeArt/>;
  case 'choose': return <Shot name="first-run" width={670} height={692} x={1040} y={170} mark={{x:.035,y:.19,w:.93,h:.165}} cursor/>;
  case 'download': return <DownloadArt/>;
  case 'alternatives': return <Shot name="first-run" width={670} height={692} x={1040} y={170} mark={{x:.035,y:.655,w:.68,h:.20}} cursor/>;
  case 'troubleshoot': return <Shot name="home" mark={{x:.17,y:.405,w:.13,h:.072}} cursor/>;
  case 'outro': return <EndArt guide={false}/>;
  case 'finish': return <EndArt guide/>;
  default: return null;
 }
};
const Scene: React.FC<{scene:SceneData; guide:boolean; index:number; count:number}> = ({scene,guide,index,count}) => {
 const f=useCurrentFrame();
 const dark=['hero','setupHero','outro','finish'].includes(scene.id);
 const caption=scene.captions[Math.min(scene.captions.length-1,Math.floor(f/(scene.seconds*FPS/scene.captions.length)))];
 return <AbsoluteFill style={{fontFamily:FONT,opacity:interpolate(f,[0,14,scene.seconds*FPS-10,scene.seconds*FPS],[0,1,1,0],clamp)}}>
  <Backdrop dark={dark}/><Header guide={guide} dark={dark}/><Copy scene={scene} dark={dark}/><Art id={scene.id}/>
  <div style={{position:'absolute',bottom:75,left:100,right:100,display:'flex',gap:8}}>{Array.from({length:count},(_,i)=><div key={i} style={{height:3,flex:1,borderRadius:2,background:i<=index?dark?'#6da6ff':C.blue:dark?'#29415e':'#dce5f4'}}/>)}</div>
  <div style={{position:'absolute',left:84,right:84,bottom:24,height:40,display:'flex',alignItems:'center',justifyContent:'center',color:dark?'#d6e3f9':C.ink,fontSize:29,fontWeight:500,letterSpacing:.2}}>{caption}</div>
 </AbsoluteFill>;
};
const Video: React.FC<{guide:boolean}> = ({guide}) => {
 const {durationInFrames}=useVideoConfig();
 const scenes:SceneData[]=guide?story.setup:story.intro;
 let from=0;
 return <AbsoluteFill style={{background:C.paper}}>
  <Audio src={staticFile('score.m4a')} volume={frame=>interpolate(frame,[0,45,durationInFrames-50,durationInFrames],[0,.6,.6,0],clamp)}/>
  {scenes.map((scene,index)=>{const start=from;from+=scene.seconds*FPS;return <Sequence key={scene.id} from={start} durationInFrames={scene.seconds*FPS} name={scene.tag}><Scene scene={scene} guide={guide} index={index} count={scenes.length}/></Sequence>;})}
 </AbsoluteFill>;
};
const Root:React.FC=()=> <>
 <Composition id="ProductIntro" component={Video} defaultProps={{guide:false}} durationInFrames={seconds(story.intro)*FPS} fps={FPS} width={1920} height={1080}/>
 <Composition id="FirstRunGuide" component={Video} defaultProps={{guide:true}} durationInFrames={seconds(story.setup)*FPS} fps={FPS} width={1920} height={1080}/>
</>;
registerRoot(Root);
