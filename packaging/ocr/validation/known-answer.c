/* Citrus-owned synthetic raw-pixel fixture. Requires native Windows x64. */
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include <math.h>

typedef void* (__cdecl *create_fn)(void);
typedef int (__cdecl *init_fn)(void*, const char*, int, const char*, int, char**, int, char**, char**, size_t, int);
typedef void (__cdecl *setimage_fn)(void*, const unsigned char*, int, int, int, int);
typedef void (__cdecl *setseg_fn)(void*, int);
typedef int (__cdecl *recognize_fn)(void*, void*);
typedef char* (__cdecl *gettext_fn)(void*);
typedef char* (__cdecl *gettsv_fn)(void*, int);
typedef const char* (__cdecl *version_fn)(void);
typedef void (__cdecl *delete_text_fn)(const char*);
typedef void (__cdecl *delete_api_fn)(void*);
#define API(type, var, name) type var; { FARPROC entry=GetProcAddress(module,name); memcpy(&var,&entry,sizeof(var)); } if (!var) { fprintf(stderr,"Missing export %s\n",name); FreeLibrary(module); return 3; }

static int absolute_path(const wchar_t* path) {
  return wcslen(path)>3 && path[1]==L':' && path[2]==L'\\';
}
static int owned_module(HMODULE module, const wchar_t* expected) {
  wchar_t actual[32768];
  DWORD n=GetModuleFileNameW(module,actual,32768);
  return n>0 && n<32768 && _wcsicmp(actual,expected)==0;
}
static int verify_words(char* tsv, int width, int height) {
  const char* words[]={"CITRUS","OCR","12345"};
  char* context=NULL;
  /* GetTsvText API returns rows without the renderer's column header. */
  int count=0, last_right=0;
  for(char* row=strtok_s(tsv,"\n",&context);row;row=strtok_s(NULL,"\n",&context)) {
    int level,page,block,paragraph,line,word,x,y,w,h,offset=0;
    double confidence;
    if(sscanf(row,"%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%lf\t%n",
        &level,&page,&block,&paragraph,&line,&word,&x,&y,&w,&h,&confidence,&offset)!=11)return 0;
    if(level!=5)continue;
    if(count>=3 || strcmp(row+offset,words[count])!=0 || page!=1 || block!=1 || paragraph!=1 || line!=1 || word!=count+1 ||
       x<35 || y<45 || w<=0 || h<=0 || x>width-w || y>height-h || x<last_right || !isfinite(confidence) || confidence<50.0 || confidence>100.0)return 0;
    printf("WORD=%s bounds=%d,%d,%d,%d confidence=%.6f\n",words[count],x,y,w,h,confidence);
    last_right=x+w;count++;
  }
  return count==3;
}
int wmain(int argc, wchar_t** argv) {
  if(argc!=3 || !absolute_path(argv[1]) || !absolute_path(argv[2])) {
    fputs("Usage: known-answer.exe ABSOLUTE_DLL_PATH ABSOLUTE_ENG_TRAINEDDATA_FILE\n",stderr);return 2;
  }
  /* Resolve dependencies only beside our absolute DLL or in the OS system directory. */
  HMODULE module=LoadLibraryExW(argv[1],NULL,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
  if(!module) { fprintf(stderr,"Load failed: %lu\n",GetLastError());return 3; }
  if(!owned_module(module,argv[1])) { FreeLibrary(module);return 3; }
  const wchar_t* dependencies[]={L"libleptonica-1.87.0.dll",L"libwinpthread-1.dll"};
  for(int i=0;i<2;i++) {
    wchar_t expected[32768];
    if(wcslen(argv[1])+wcslen(dependencies[i])>=32768) { FreeLibrary(module);return 3; }
    wcscpy(expected,argv[1]);wchar_t* leaf=wcsrchr(expected,L'\\');wcscpy(leaf+1,dependencies[i]);
    HMODULE dependency=GetModuleHandleW(dependencies[i]);
    if(!dependency || !owned_module(dependency,expected)) { FreeLibrary(module);return 3; }
  }
  API(version_fn,version,"TessVersion");
  API(create_fn,create,"TessBaseAPICreate");
  API(init_fn,init,"TessBaseAPIInit5");
  API(setimage_fn,setimage,"TessBaseAPISetImage");
  API(setseg_fn,setseg,"TessBaseAPISetPageSegMode");
  API(recognize_fn,recognize,"TessBaseAPIRecognize");
  API(gettext_fn,gettext,"TessBaseAPIGetUTF8Text");
  API(gettsv_fn,gettsv,"TessBaseAPIGetTsvText");
  API(delete_text_fn,delete_text,"TessDeleteText");
  API(delete_api_fn,delete_api,"TessBaseAPIDelete");
  int result=5; HANDLE file=INVALID_HANDLE_VALUE; char* model=NULL;void* engine=NULL;
  HDC dc=NULL;HBITMAP bitmap=NULL;HFONT font=NULL;HGDIOBJ previous=NULL,old_font=NULL;
  unsigned char* gray=NULL;char* text=NULL;char* tsv=NULL;
  printf("TessVersion=%s\n",version());
  if(strcmp(version(),"5.5.3")!=0) { result=4;goto cleanup; }
  file=CreateFileW(argv[2],GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);
  if(file==INVALID_HANDLE_VALUE)goto cleanup;
  LARGE_INTEGER data_size;DWORD count=0;
  if(!GetFileSizeEx(file,&data_size)||data_size.QuadPart!=4113088)goto cleanup;
  model=(char*)malloc((size_t)data_size.QuadPart);
  if(!model||!ReadFile(file,model,(DWORD)data_size.QuadPart,&count,NULL)||count!=(DWORD)data_size.QuadPart)goto cleanup;
  CloseHandle(file);file=INVALID_HANDLE_VALUE;
  engine=create();
  /* Init5 consumes owned bytes; no narrow native model path or config files. */
  if(!engine||init(engine,model,(int)data_size.QuadPart,"eng",1,NULL,0,NULL,NULL,0,FALSE)!=0)goto cleanup;
  const int width=1100,height=180;
  BITMAPINFO info;ZeroMemory(&info,sizeof(info));
  info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=width;info.bmiHeader.biHeight=-height;
  info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
  unsigned char* bgra=NULL;dc=CreateCompatibleDC(NULL);
  if(!dc) { result=6;goto cleanup; }
  bitmap=CreateDIBSection(dc,&info,DIB_RGB_COLORS,(void**)&bgra,NULL,0);
  if(!bitmap||!bgra) { result=6;goto cleanup; }
  previous=SelectObject(dc,bitmap);PatBlt(dc,0,0,width,height,WHITENESS);
  font=CreateFontW(-72,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,ANSI_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,ANTIALIASED_QUALITY,FF_SWISS,L"Arial");
  if(!font) { result=6;goto cleanup; }
  old_font=SelectObject(dc,font);
  wchar_t face[64];
  if(!GetTextFaceW(dc,64,face)||wcscmp(face,L"Arial")!=0) { result=6;goto cleanup; }
  SetTextColor(dc,RGB(0,0,0));SetBkColor(dc,RGB(255,255,255));
  const wchar_t* expected=L"CITRUS OCR 12345";
  if(!TextOutW(dc,35,45,expected,(int)wcslen(expected))||!GdiFlush()) { result=6;goto cleanup; }
  gray=(unsigned char*)malloc((size_t)width*height);
  if(!gray) { result=6;goto cleanup; }
  for(int i=0;i<width*height;i++)gray[i]=(unsigned char)((77*bgra[i*4+2]+150*bgra[i*4+1]+29*bgra[i*4]+128)>>8);
  setseg(engine,7);setimage(engine,gray,width,height,1,width);
  if(recognize(engine,NULL)!=0) { result=7;goto cleanup; }
  text=gettext(engine);tsv=gettsv(engine,0);
  if(!text||!tsv) { result=8;goto cleanup; }
  printf("TEXT=%s\nTSV=%s\n",text,tsv);
  size_t text_length=strlen(text);
  while(text_length>0 && (text[text_length-1]=='\r' || text[text_length-1]=='\n'))text[--text_length]='\0';
  result=strcmp(text,"CITRUS OCR 12345")==0 && verify_words(tsv,width,height) ? 0 : 9;
cleanup:
  if(tsv)delete_text(tsv);
  if(text)delete_text(text);
  if(engine)delete_api(engine);
  if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);
  free(model);free(gray);
  if(old_font)SelectObject(dc,old_font);
  if(font)DeleteObject(font);
  if(previous)SelectObject(dc,previous);
  if(bitmap)DeleteObject(bitmap);
  if(dc)DeleteDC(dc);
  FreeLibrary(module);
  if(result==0)puts("PASS: actual Init5 raw grayscale inference, exact text/TSV, word bounds/confidence, owned DLL closure");
  else fprintf(stderr,"FAIL: native fixture code=%d\n",result);
  return result;
}
