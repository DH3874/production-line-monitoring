import json
import os
from pathlib import Path
import socket
import subprocess
import time
import tempfile
import shutil
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[2]
run_dir = Path(tempfile.mkdtemp(prefix='production-line-http-test-'))
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    port = sock.getsockname()[1]
base = f'http://127.0.0.1:{port}'
env = dict(os.environ, ServerUrl=base, DOTNET_CLI_TELEMETRY_OPTOUT='1', OPENAI_API_KEY='', GEMINI_API_KEY='', Gemini__UseSavedKey='false', AI_PROVIDER='Gemini', Gemini__Enabled='true')
server = root / '03_Server/ProductionLine.Server/bin/Debug/net8.0/ProductionLine.Server.dll'

def request(method, path, body=None, token=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    data = None if body is None else json.dumps(body).encode('utf-8')
    req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=5) as response:
        return json.load(response)

with (run_dir / 'server.log').open('w', encoding='utf-8') as log:
    proc = subprocess.Popen([shutil.which('dotnet') or 'C:/Program Files/dotnet/dotnet.exe', str(server)], cwd=run_dir,
        env=env, stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        for _ in range(50):
            if proc.poll() is not None:
                raise RuntimeError('Test server exited')
            try:
                request('GET', '/')
                break
            except (OSError, urllib.error.URLError):
                time.sleep(.1)
        else:
            raise RuntimeError('Test server did not start')
        token = request('POST', '/api/auth/login', {'userId': 'worker1', 'password': 'worker123!'})['token']
        for title, question, expected in [('MQ-2 상태 확인', '값 500은 정상인가요?', 'MQ-2'), ('DHT11 수신', 'NaN이 나와요', 'DHT11')]:
            issue = request('POST', '/api/issues', {'lineId': 1, 'type': 0, 'title': title, 'description': '가상 데이터로 상담 연동 확인'}, token)
            result = request('POST', '/api/ai/consult', {'issueId': issue['id'], 'question': question}, token)
            assert set(result) == {'answer', 'manualSource', 'needAdmin'}
            assert expected in result['manualSource']
            assert 'AI 연결이 설정되지 않아' in result['answer']
            assert result['needAdmin'] is True
            issues = request('GET', '/api/issues', token=token)
            assert next(x for x in issues if x['id'] == issue['id'])['status'] == issue['status']
            print('PASS: HTTP consultation, compatible response, unchanged issue status - ' + expected)
        forwarded = request('POST', '/api/issues', {
            'lineId': 1, 'type': 0, 'title': '관리자 확인 요청 · 설비 동작 이상',
            'description': '관련 문제: 설비 동작 이상\n사용자 질문: 점검 문의\n상담 내용: [AI 점검 안내]\n가스 경보·누출 의심 표현이 포함되어 있습니다.\n실제 가스 방출이나 화염으로 시험하지 마세요.\n[근거]\nMQ-2 상담 가이드'
        }, token)
        for question, source in [
            ('촬영 조건·제품 위치·검사 기준 버전 확인', '촬영·제품 위치'),
            ('DHT11 값이 NaN으로 나옵니다', 'DHT11'),
            ('카메라 영상이 안 나와요', '카메라'),
            ('서버 연결 오류', '검사 프로그램'),
            ('제품이 밀려서 쌓여요', '설비'),
        ]:
            result = request('POST', '/api/ai/consult', {'issueId': forwarded['id'], 'question': question}, token)
            assert source in result['manualSource'], (question, result['manualSource'])
            assert '비상 대응 절차' not in result['answer']
            assert '별도 확인:' not in result['answer']
            print('PASS: quoted gas advice does not override new question - ' + source)
        result = request('POST', '/api/ai/consult', {'issueId': forwarded['id'], 'question': '지금 가스 냄새가 나요'}, token)
        assert result['needAdmin'] and '비상 대응 절차' in result['answer']
        print('PASS: new gas incident still gets priority')
        for question, source, detail in [
            ('가스 누출', '가스 누출', '119'),
            ('온도가 너무 높아요', '온도 높음', '냉각장치'),
            ('온도가 낮아요', '온도 낮음', '동결·누수'),
            ('습도가 높아요', '습도 높음', '제습기'),
            ('컨베이어 밸트 멈춤', '컨베이어 벨트 멈춤', '잠금·표지'),
            ('제품 인식이 안돼요', '제품 인식 오류', '센서·렌즈 오염'),
        ]:
            result = request('POST', '/api/ai/consult', {'issueId': forwarded['id'], 'question': question}, token)
            assert source in result['manualSource'], (question, result['manualSource'])
            assert detail in result['answer'] and '[조치 후 확인]' in result['answer']
            assert result['needAdmin'] is True
            print('PASS: detailed JSON scenario through HTTP - ' + source)
        chat_issue = request('POST', '/api/issues', {'lineId': 1, 'type': 0, 'title': '후속 상담 연동 검증', 'description': '가상 상황'}, token)
        def consult_chat(question, auth_token=token):
            return request('POST', '/api/ai/consult', {'issueId': chat_issue['id'], 'question': question}, auth_token)
        first = consult_chat('습도가 너무 높아요')
        assert '현재 상대습도는 몇 %' in first['answer']
        for unsupported in ['오늘 점심 추천', '오늘 날씨 어때요?', '주말 여행 추천해줘']:
            rejected = consult_chat(unsupported)
            assert set(rejected) == {'answer', 'manualSource', 'needAdmin'}
            assert rejected['manualSource'].startswith('근거 없음')
            assert '등록된 매뉴얼로 안내할 수 없는 내용' in rejected['answer']
            assert '제습기' not in rejected['answer']
        print('PASS: HTTP unsupported questions are refused without replacing the ongoing manual conversation')
        second = consult_chat('80%')
        assert '80%RH' in second['answer'] and '결로가 보이나요' in second['answer']
        third = consult_chat('있어요')
        assert '80%RH' in third['answer'] and '결로가 보인다는' in third['answer'] and '젖은 전기 장치' in third['answer']
        fourth = consult_chat('제습기를 켰지만 아직 습도가 높아요')
        assert '80%RH' in fourth['answer'] and '제습기를 켰지만' in fourth['answer']
        print('PASS: HTTP four-turn humidity conversation retains values and follows condensation branch')
        admin_token = request('POST', '/api/auth/login', {'userId': 'admin', 'password': 'admin123!'})['token']
        admin_reply = consult_chat('80%', admin_token)
        assert '80%RH' not in admin_reply['answer']
        print('PASS: same issue has isolated conversation for another authenticated user')
        after_chat = request('GET', '/api/issues', token=token)
        assert next(x for x in after_chat if x['id'] == chat_issue['id'])['status'] == chat_issue['status']
        assert consult_chat('새 상담')['manualSource'].startswith('상담 흐름')
        assert '80%RH' not in consult_chat('80%')['answer']
        print('PASS: conversation does not change issue state and explicit reset clears its context')
        try:
            request('POST', '/api/ai/consult', {'issueId': issue['id'], 'question': 'DHT11 문의'})
            raise AssertionError('Unauthenticated request unexpectedly succeeded')
        except urllib.error.HTTPError as exc:
            assert exc.code == 401
        print('PASS: anonymous consultation rejected')
        for missing_id in ['00000000-0000-0000-0000-000000000001']:
            try:
                request('POST', '/api/ai/consult', {'issueId': missing_id, 'question': '습도 문의'}, token)
                raise AssertionError('Missing issue unexpectedly succeeded')
            except urllib.error.HTTPError as exc:
                assert exc.code == 404
        denied_issue = request('POST', '/api/issues', {'lineId': 2, 'type': 0, 'title': '권한 검사', 'description': '가상 상황'}, admin_token)
        try:
            request('POST', '/api/ai/consult', {'issueId': denied_issue['id'], 'question': '습도 문의'}, token)
            raise AssertionError('Forbidden line unexpectedly succeeded')
        except urllib.error.HTTPError as exc:
            assert exc.code == 403
        print('PASS: missing issue 404 and forbidden line 403 preserved')
    finally:
        proc.terminate()
        proc.wait(timeout=10)

shutil.rmtree(run_dir)
