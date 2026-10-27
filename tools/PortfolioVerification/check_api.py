from pathlib import Path
import os,json,uuid,socket,subprocess,time,tempfile,urllib.request,urllib.error
REPO=Path(__file__).resolve().parents[2]
OUT=Path(tempfile.mkdtemp(prefix='productionline-api-checks-'))
DATA=OUT/('isolated-api-'+uuid.uuid4().hex);DATA.mkdir()
with socket.socket() as listener:
 listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
URL=f'http://127.0.0.1:{port}'
env=os.environ.copy()
env.update({'GEMINI_API_KEY':'','OPENAI_API_KEY':'','Gemini__UseSavedKey':'false','Gemini__Enabled':'false','OpenAi__Enabled':'false','AI_PROVIDER':'Gemini','DOTNET_CLI_TELEMETRY_OPTOUT':'1','ServerUrl':URL})
checks=[];proc=None;log=None
def req(method,path,body=None,token=None):
 headers={'Content-Type':'application/json'}
 if token:headers['Authorization']='Bearer '+token
 data=None if body is None else json.dumps(body).encode()
 request=urllib.request.Request(URL+path,data=data,headers=headers,method=method)
 try:
  with urllib.request.urlopen(request,timeout=8) as response:return response.status,json.load(response)
 except urllib.error.HTTPError as ex:
  raw=ex.read()
  try:body=json.loads(raw)
  except Exception:body={}
  return ex.code,body
def check(name,value):
 assert value,name
 checks.append(name);print('PASS:',name,flush=True)
def start():
 global proc,log
 log=(DATA/'server.log').open('a',encoding='utf-8')
 proc=subprocess.Popen(['dotnet',str(REPO/'03_Server/ProductionLine.Server/bin/Release/net8.0/ProductionLine.Server.dll')],cwd=DATA,env=env,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
 for _ in range(100):
  if proc.poll() is not None:raise RuntimeError('Isolated server exited; see private log')
  try:
   if req('GET','/')[0]==200:return
  except OSError:pass
  time.sleep(.1)
 raise TimeoutError('Isolated server startup')
def stop():
 if proc and proc.poll() is None:proc.terminate();proc.wait(timeout=10)
 if log:log.close()
def login(user,password):
 code,result=req('POST','/api/auth/login',{'userId':user,'password':password})
 assert code==200
 return result['token']
try:
 start()
 check('anonymous chat denied',req('GET','/api/chat/messages')[0]==401)
 worker=login('worker1','worker123!');admin=login('admin','admin123!')
 check('worker sees only permitted line',len(req('GET','/api/lines',token=worker)[1])==1)
 check('admin sees four demo lines',len(req('GET','/api/lines',token=admin)[1])==4)
 check('worker cannot read account management',req('GET','/api/users',token=worker)[0]==403)
 payload={'clientMessageId':str(uuid.uuid4()),'text':'포트폴리오 격리 테스트 메시지'}
 code,first=req('POST','/api/chat/messages',payload,worker)
 check('group chat send uses authenticated sender',code==200 and first['senderId']=='worker1')
 check('group chat retry is idempotent',req('POST','/api/chat/messages',payload,worker)[1]['id']==first['id'])
 check('same id with changed text rejected',req('POST','/api/chat/messages',{**payload,'text':'changed'},worker)[0]==409)
 page=req('GET','/api/chat/messages?afterId=0&limit=100',token=admin)[1]
 check('admin receives worker group message',any(m['id']==first['id'] for m in page['messages']))
 check('incremental cursor avoids duplicates',not req('GET',f"/api/chat/messages?afterId={first['id']}",token=admin)[1]['messages'])
 check('invalid page limit rejected',req('GET','/api/chat/messages?limit=101',token=worker)[0]==400)
 code,issue=req('POST','/api/issues',{'lineId':1,'type':0,'title':'격리 테스트','description':'샘플 점검'},worker)
 check('issue registration',code==200 and issue['status']==0)
 issue_id=issue['id'];path='/api/issues/'+issue_id
 check('worker chat unavailable before in-progress',req('POST',path+'/messages',{'message':'점검 전'},worker)[0]==409)
 for status in [1,2]:assert req('PUT',path+'/status',{'status':status,'actionNote':'격리 검증'},worker)[0]==200
 check('worker issue chat allowed in progress',req('POST',path+'/messages',{'message':'점검 결과 공유'},worker)[0]==200)
 check('administrator can reply',req('POST',path+'/messages',{'message':'관리자 확인'},admin)[0]==200)
 check('issue conversation persists in sequence',len(req('GET',path+'/messages',token=worker)[1])==2)
 assert req('PUT',path+'/status',{'status':3,'actionNote':'완료 요청'},worker)[0]==200
 check('worker issue chat read-only after completion request',req('POST',path+'/messages',{'message':'추가'},worker)[0]==409)
 check('only admin can close issue',req('PUT',path+'/status',{'status':4,'actionNote':'종결'},worker)[0]==403)
 check('admin can reject to in-progress',req('PUT',path+'/status',{'status':2,'actionNote':'재점검 요청'},admin)[0]==200)
 assert req('PUT',path+'/status',{'status':3,'actionNote':'재점검 완료'},worker)[0]==200
 check('admin final closure',req('PUT',path+'/status',{'status':4,'actionNote':'관리자 종결'},admin)[0]==200)
 before=len(req('GET','/api/issues',token=worker)[1])
 code,response=req('POST','/api/ai/chat',{'lineId':1,'conversationId':str(uuid.uuid4()),'question':'카메라 영상이 안 나와요'},worker)
 check('AI works with manual fallback without any key',code==200 and bool(response.get('answer')) and '카메라' in response.get('manualSource',''))
 check('general AI chat does not create production issue',len(req('GET','/api/issues',token=worker)[1])==before)
 check('AI status configuration endpoint',req('GET','/api/ai/chat/status',token=worker)[1]['aiConfigured'] is False)
 code,diag=req('GET','/api/lines/1/diagnostics',token=worker)
 check('disconnected device diagnostics still available',code==200 and bool(diag.get('devices')))
 stop();start();admin=login('admin','admin123!')
 check('group messages survive isolated server restart',any(m['id']==first['id'] for m in req('GET','/api/chat/messages',token=admin)[1]['messages']))
 check('SQLite issue chat survives server restart',len(req('GET',path+'/messages',token=admin)[1])==2)
 (OUT/'api-checks.json').write_text(json.dumps({'passed':len(checks),'checks':checks,'real_hardware_used':False,'external_ai_used':False,'data_directory':str(DATA)},ensure_ascii=False,indent=2),encoding='utf-8')
 print(f'{len(checks)} API CHECKS PASSED; isolated database, no physical hardware or external AI')
finally:stop()
