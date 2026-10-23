//! Windows-only local capture, font validation and user-bound DPAPI storage.
#![cfg(windows)]
use std::{ffi::{c_char,c_void,CStr,CString},ptr,sync::{atomic::{AtomicBool,AtomicU8,Ordering},Mutex,OnceLock},thread,time::{Duration,Instant}};
use base64::{engine::general_purpose::STANDARD,Engine};
type Handle=*mut c_void;
#[repr(C)] #[derive(Default,Clone,Copy,PartialEq)] struct Point {x:i32,y:i32}
#[repr(C)] #[derive(Default,Clone,Copy,PartialEq)] struct Rect {left:i32,top:i32,right:i32,bottom:i32}
#[repr(C)] struct Blob {size:u32,data:*mut u8}
#[repr(C)] struct LogFont {height:i32,width:i32,escapement:i32,orientation:i32,weight:i32,italic:u8,underline:u8,strikeout:u8,charset:u8,out_precision:u8,clip_precision:u8,quality:u8,pitch:u8,face:[u16;32]}
#[link(name="user32")] extern "system" {
 fn GetForegroundWindow()->Handle; fn GetWindowThreadProcessId(w:Handle,p:*mut u32)->u32;
 fn GetClassNameW(w:Handle,b:*mut u16,n:i32)->i32; fn GetWindowRect(w:Handle,r:*mut Rect)->i32;
 fn GetAsyncKeyState(k:i32)->i16; fn GetCursorPos(p:*mut Point)->i32; fn GetDoubleClickTime()->u32;
 fn GetClipboardSequenceNumber()->u32; fn OpenClipboard(w:Handle)->i32; fn CloseClipboard()->i32;
 fn GetClipboardData(format:u32)->Handle; fn keybd_event(key:u8,scan:u8,flags:u32,extra:usize);
 fn SystemParametersInfoW(action:u32,param:u32,value:Handle,flags:u32)->i32;
 fn GetDC(w:Handle)->Handle; fn ReleaseDC(w:Handle,dc:Handle)->i32;
}
#[link(name="kernel32")] extern "system" {fn GlobalSize(h:Handle)->usize;fn GlobalLock(h:Handle)->Handle;fn GlobalUnlock(h:Handle)->i32;fn LocalFree(h:Handle)->Handle;}
#[link(name="crypt32")] extern "system" {
 fn CryptProtectData(i:*const Blob,desc:*const u16,entropy:*const Blob,reserved:Handle,prompt:Handle,flags:u32,o:*mut Blob)->i32;
 fn CryptUnprotectData(i:*const Blob,desc:Handle,entropy:*const Blob,reserved:Handle,prompt:Handle,flags:u32,o:*mut Blob)->i32;
}
#[link(name="gdi32")] extern "system" {
 fn EnumFontFamiliesExW(dc:Handle,lf:*const LogFont,callback:unsafe extern "system" fn(*const LogFont,Handle,u32,isize)->i32,param:isize,flags:u32)->i32;
 fn CreateFontIndirectW(lf:*const LogFont)->Handle;fn SelectObject(dc:Handle,obj:Handle)->Handle;fn DeleteObject(obj:Handle)->i32;
 fn GetGlyphIndicesW(dc:Handle,text:*const u16,n:i32,glyph:*mut u16,flags:u32)->u32;
}
#[no_mangle] pub extern "C" fn mt_workarea()->*mut c_char {
 unsafe {let mut area=Rect::default();if SystemParametersInfoW(48,0,&mut area as *mut Rect as Handle,0)==0 {return reply(Err("Cannot read display work area".into()))}
 reply(Ok(serde_json::json!({"width":area.right-area.left,"height":area.bottom-area.top}).to_string()))}
}
static ENABLED:AtomicBool=AtomicBool::new(true);
static MODE:AtomicU8=AtomicU8::new(0); // 0: mouse selection; 1: clipboard
static RUNNING:AtomicBool=AtomicBool::new(false);
static QUEUE:OnceLock<Mutex<Option<String>>>=OnceLock::new();
static WORKER:Mutex<Option<thread::JoinHandle<()>>>=Mutex::new(None);
fn reply(result:Result<String,String>)->*mut c_char {
 let value=match result {Ok(v)=>serde_json::json!({"ok":true,"value":v}),Err(e)=>serde_json::json!({"ok":false,"error":e})};
 CString::new(value.to_string()).unwrap().into_raw()
}
unsafe fn input(p:*const c_char)->Result<String,String> {if p.is_null(){return Err("Missing input".into())} CStr::from_ptr(p).to_str().map(str::to_owned).map_err(|_|"Invalid UTF-8".into())}
#[no_mangle] pub unsafe extern "C" fn mt_free(p:*mut c_char) {if !p.is_null(){drop(CString::from_raw(p));}}
#[no_mangle] pub unsafe extern "C" fn mt_protect(p:*const c_char)->*mut c_char {reply(input(p).and_then(|s|crypt(s.as_bytes(),false).map(|b|STANDARD.encode(b))))}
#[no_mangle] pub unsafe extern "C" fn mt_unprotect(p:*const c_char)->*mut c_char {reply(input(p).and_then(|s|STANDARD.decode(s).map_err(|_|"Invalid encrypted key".into())).and_then(|b|crypt(&b,true)).and_then(|b|String::from_utf8(b).map_err(|_|"Invalid decrypted text".into())))}
fn crypt(data:&[u8],decrypt:bool)->Result<Vec<u8>,String> {
 if data.is_empty(){return Ok(vec![])}
 unsafe {let source=Blob{size:data.len() as u32,data:data.as_ptr() as *mut u8};let mut out=Blob{size:0,data:ptr::null_mut()};
 let ok=if decrypt{CryptUnprotectData(&source,ptr::null_mut(),ptr::null(),ptr::null_mut(),ptr::null_mut(),1,&mut out)}else{CryptProtectData(&source,ptr::null(),ptr::null(),ptr::null_mut(),ptr::null_mut(),1,&mut out)};
 if ok==0{return Err("Windows key protection failed for this user".into())}
 let bytes=std::slice::from_raw_parts(out.data,out.size as usize).to_vec();LocalFree(out.data as Handle);Ok(bytes)}
}
unsafe fn target()->Option<(usize,Rect)> {
 let w=GetForegroundWindow();if w.is_null(){return None}
 let mut pid=0;GetWindowThreadProcessId(w,&mut pid);if pid==0||pid==std::process::id(){return None}
 let mut class=[0u16;256];let n=GetClassNameW(w,class.as_mut_ptr(),256);let name=String::from_utf16_lossy(&class[..n.max(0) as usize]);
 if ["ConsoleWindowClass","CASCADIA_HOSTING_WINDOW_CLASS","CabinetWClass","ExploreWClass","Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd"].contains(&name.as_str()){return None}
 let mut rect=Rect::default();if GetWindowRect(w,&mut rect)==0{return None}Some((w as usize,rect))
}
unsafe fn clipboard()->Option<String> {
 if OpenClipboard(ptr::null_mut())==0{return None}
 let h=GetClipboardData(13);let mut result=None;
 if !h.is_null(){let data=GlobalLock(h) as *const u16;if !data.is_null(){let maximum=(GlobalSize(h)/2).min(12001);let mut length=0;while length<maximum&&*data.add(length)!=0 {length+=1}if length<maximum&&length<=12000 {result=Some(String::from_utf16_lossy(std::slice::from_raw_parts(data,length)));}GlobalUnlock(h);}}
 CloseClipboard();result.filter(|s|!s.trim().is_empty())
}
fn push(text:String){if let Ok(mut q)=QUEUE.get_or_init(||Mutex::new(None)).lock(){*q=Some(text)}}
#[no_mangle] pub extern "C" fn mt_capture_enable(enabled:i32){ENABLED.store(enabled!=0,Ordering::Relaxed);if enabled==0 {if let Ok(mut q)=QUEUE.get_or_init(||Mutex::new(None)).lock(){*q=None;}}}
#[no_mangle] pub extern "C" fn mt_capture_mode(mode:i32){MODE.store(if mode==1{1}else{0},Ordering::Relaxed);}
#[no_mangle] pub extern "C" fn mt_capture_start(){let Ok(mut worker)=WORKER.lock() else{return};if let Some(existing)=worker.as_ref(){if !existing.is_finished(){return}}if let Some(old)=worker.take(){let _=old.join();}RUNNING.store(true,Ordering::SeqCst);*worker=Some(thread::spawn(move||unsafe {
 let mut down=false;let mut start:Option<(Point,usize,Rect)>=None;let mut previous:Option<(Instant,Point,usize)>=None;let mut pending:Option<(Instant,usize)>=None;let mut waiting:Option<(u32,Instant,usize)>=None;let mut sequence=GetClipboardSequenceNumber();
 while RUNNING.load(Ordering::Relaxed){thread::sleep(Duration::from_millis(8));let now=Instant::now();let pressed=GetAsyncKeyState(1)<0;let mut p=Point::default();GetCursorPos(&mut p);
 if !ENABLED.load(Ordering::Relaxed){down=pressed;start=None;pending=None;waiting=None;previous=None;sequence=GetClipboardSequenceNumber();continue}
 if MODE.load(Ordering::Relaxed)==1 {let current=GetClipboardSequenceNumber();if current!=sequence {sequence=current;if target().is_some(){if let Some(s)=clipboard(){push(s)}}}down=pressed;start=None;pending=None;waiting=None;continue}
 if let Some((old,deadline,w))=waiting {if GetClipboardSequenceNumber()!=old {if GetForegroundWindow() as usize==w{if let Some(s)=clipboard(){push(s)}}waiting=None}else if now>=deadline{waiting=None}}
 if pressed {pending=None}
 if let Some((due,w))=pending {if now>=due {pending=None;if target().map(|x|x.0)==Some(w)&&![0x10,0x11,0x12,0x5b,0x5c].iter().any(|k|GetAsyncKeyState(*k)<0) {waiting=Some((GetClipboardSequenceNumber(),now+Duration::from_millis(1200),w));keybd_event(0x11,0,0,0);keybd_event(0x43,0,0,0);keybd_event(0x43,0,2,0);keybd_event(0x11,0,2,0);}}}
 if pressed&&!down {start=target().map(|(w,r)|(p,w,r));waiting=None}
 else if down&&!pressed {if let Some((origin,w,rect))=start.take(){let distance=(p.x as i64-origin.x as i64).pow(2)+(p.y as i64-origin.y as i64).pow(2);let double=previous.map(|(t,pt,win)|win==w&&now.duration_since(t).as_millis()<GetDoubleClickTime() as u128&&(p.x as i64-pt.x as i64).pow(2)+(p.y as i64-pt.y as i64).pow(2)<25).unwrap_or(false);previous=Some((now,p,w));if (distance>=16||double)&&target()==Some((w,rect)){pending=Some((now+Duration::from_millis(20),w));}}}down=pressed;
 }
 }));}
#[no_mangle] pub extern "C" fn mt_capture_stop(){let Ok(mut worker)=WORKER.lock() else{RUNNING.store(false,Ordering::SeqCst);return};RUNNING.store(false,Ordering::SeqCst);let deadline=Instant::now()+Duration::from_millis(200);if let Some(handle)=worker.as_ref(){while !handle.is_finished()&&Instant::now()<deadline{thread::sleep(Duration::from_millis(2));}}if worker.as_ref().is_some_and(|h|h.is_finished()){if let Some(handle)=worker.take(){let _=handle.join();}}if let Ok(mut queue)=QUEUE.get_or_init(||Mutex::new(None)).lock(){*queue=None;}}
#[no_mangle] pub extern "C" fn mt_capture_poll()->*mut c_char {let result=QUEUE.get_or_init(||Mutex::new(None)).lock().ok().and_then(|mut q|q.take()).unwrap_or_default();reply(Ok(result))}
unsafe extern "system" fn collect_font(lf:*const LogFont,_:Handle,_:u32,param:isize)->i32 {
 let list=&mut *(param as *mut Vec<String>);let font=&*lf;let n=font.face.iter().position(|c|*c==0).unwrap_or(32);let name=String::from_utf16_lossy(&font.face[..n]);
 if !name.starts_with('@')&&font.charset!=2 {list.push(name)}1
}
#[no_mangle] pub extern "C" fn mt_fonts()->*mut c_char {
 let mut names=Vec::<String>::new();unsafe {let dc=GetDC(ptr::null_mut());if dc.is_null(){return reply(Err("Cannot enumerate fonts".into()))}let mut lf:LogFont=std::mem::zeroed();lf.charset=1;EnumFontFamiliesExW(dc,&lf,collect_font,&mut names as *mut _ as isize,0);ReleaseDC(ptr::null_mut(),dc);}
 names.sort();names.dedup();reply(Ok(serde_json::to_string(&names).unwrap()))
}
#[no_mangle] pub unsafe extern "C" fn mt_font_valid(p:*const c_char)->i32 {
 let Ok(name)=input(p) else{return 0};if name.starts_with('@'){return 0}
 let mut lf:LogFont=std::mem::zeroed();lf.height=-18;lf.charset=1;for (i,c) in name.encode_utf16().take(31).enumerate(){lf.face[i]=c}
 let dc=GetDC(ptr::null_mut());if dc.is_null(){return 0}let font=CreateFontIndirectW(&lf);if font.is_null(){ReleaseDC(ptr::null_mut(),dc);return 0}let old=SelectObject(dc,font);
 let chars:Vec<u16>="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".encode_utf16().collect();let mut glyphs=vec![0u16;chars.len()];let count=GetGlyphIndicesW(dc,chars.as_ptr(),chars.len() as i32,glyphs.as_mut_ptr(),1);SelectObject(dc,old);DeleteObject(font);ReleaseDC(ptr::null_mut(),dc);
 (count!=u32::MAX&&glyphs.iter().all(|g|*g!=0&&*g!=u16::MAX)) as i32
}
#[cfg(test)]mod tests{use super::*;#[test]fn key_roundtrip(){let clear=b"example-test-key";let encrypted=crypt(clear,false).unwrap();assert_ne!(encrypted,clear);assert_eq!(crypt(&encrypted,true).unwrap(),clear);}#[test]fn empty_key(){assert_eq!(crypt(b"",false).unwrap(),b"");}#[test]fn invalid_ciphertext(){assert!(crypt(b"not-an-encrypted-key",true).is_err());}}

