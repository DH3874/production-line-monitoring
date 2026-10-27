from pathlib import Path

base = (Path(__file__).parent / 'build_plan.py').read_text(encoding='utf-8').split('# 01')[0]
base = base.replace('생산라인_실시간_모니터링_협업채팅_프로젝트_기획서.pdf', '5팀_화장품용뚜껑_검사_AI상담_핵심기획서.pdf')
base = base.replace('생산라인 실시간 모니터링 & 협업 채팅 시스템', '화장품용 뚜껑 영상검사 및 AI 문제대응 시스템')
base = base.replace("f'{page:02d} / 10'", "f'{page:02d} / 02'")
exec(compile(base, __file__, 'exec'))
c.setTitle('화장품용 뚜껑 영상검사 및 AI 문제대응 시스템 | 2페이지 핵심 기획서')

def hflow(items, y, height=68):
    gap = 8
    w = (CW - gap * (len(items) - 1)) / len(items)
    for i, (title, body) in enumerate(items):
        x = M + i * (w + gap)
        rect(x, y, w, height, PALE)
        para(title, x + 9, y + 11, w - 18, 9.1, TEAL, True)
        para(body, x + 9, y + 34, w - 18, 7.9, INK)
        if i < len(items) - 1:
            arrow(x + w, y + height / 2, x + w + gap, y + height / 2)
    return y + height

# 01 - Project summary
page = 1
rect(0, 0, W, 230, NAVY, r=0)
label('TWO-PAGE PROJECT BRIEF  /  C# · ETHERNET · VISION · AI ASSISTANT', M, 42, colors.HexColor('#72D4CF'), 8.1)
para('화장품용 뚜껑 영상검사<br/>및 AI 문제대응 지원 시스템', M, 79, CW, 25, WHITE, True, 36)
para('컨베이어 검사부터 문제 상담·조치·관리자 종결까지 연결하는 5명·14일 프로토타입', M, 176, CW, 10.5, colors.HexColor('#DAE9F0'))
for i, (a, b) in enumerate([('검사 품목', '화장품용 플라스틱 뚜껑'), ('제품 검사', '형태·윤곽 + 색상'), ('통신', '유선 이더넷 TCP/IP')]):
    x = M + i * (CW + 12) / 3
    w = (CW - 24) / 3
    rect(x, 257, w, 63, BG)
    para(a, x + 11, 267, w - 22, 8.2, MUTED)
    para(b, x + 11, 290, w - 22, 10.2, NAVY, True)
y = section('프로젝트 목적', 352)
y = para('컨베이어로 이동하는 화장품용 플라스틱 뚜껑을 카메라로 촬영해 형태·윤곽과 색상을 검사합니다. 사용자는 대표 라인의 검사와 문제 조치를 수행하고, 라인 문제 발생 후에는 매뉴얼 기반 상담 AI와 관리자 문의로 해결을 지원합니다.', M, y, CW, 10.2)
y = section('핵심 기능', y + 22)
y = table(['영역', '핵심 구현'], [
    ('관리자', '개인 계정 로그인, 다중 라인 대시보드, 사용자·권한 관리, 완료 검토·종결'),
    ('사용자', '허용 라인 로그인, 카메라·검사 결과, 문제 확인·조치·완료 요청'),
    ('검사', '센서·카메라 영상 획득, 뚜껑 형태·윤곽/색상 검사, 정상·불량·판정 불가'),
    ('문제 지원', '매뉴얼·점검표 검색, AI 원인·점검 순서 안내, 미해결 시 관리자 문의'),
    ('기록', '검사·문제·상담·조치·계정·권한 변경의 수행자와 시각 저장')
], [93, CW - 93], y, 8.8, 7.5)
y = section('전체 이용 흐름', y + 22)
y = hflow([
    ('01 검사', '뚜껑 촬영·판정'),
    ('02 문제', '문제 표시·확인'),
    ('03 상담', 'AI·매뉴얼 안내'),
    ('04 조치', '사용자 기록·문의'),
    ('05 종결', '관리자 확인·이력')
], y, 67)
footer()

# 02 - Architecture, roles, roadmap
start(2, 'TECH & TEAM', '기술 구조와 역할분담', '프로젝트별 폴더를 분리하고 공용 데이터 계약은 서버·통합 담당자가 관리합니다.')
for x, title, body in [
    (M, 'C# 관리자 WinForms', '전체 라인·사용자·종결·통계'),
    (M + 269, 'C# 사용자 WinForms', '대표 라인·검사·조치·상담')
]:
    rect(x, 151, 242, 61, BG, LINE)
    para(title, x + 12, 163, 218, 10.1, NAVY, True)
    para(body, x + 12, 188, 218, 8.5, MUTED)
arrow(M + 121, 212, M + 121, 237)
arrow(M + 390, 212, M + 390, 237)
rect(M, 237, CW, 60, PALE)
para('C# ASP.NET Core 서버', M + 14, 249, CW - 28, 10.7, TEAL, True)
para('유선 이더넷 TCP/IP · REST/JSON · 인증·권한 · EF Core/DB · 이력·감사', M + 14, 275, CW - 28, 8.8)
for i, (a, b) in enumerate([('영상처리', 'OpenCvSharp'), ('상담 AI', '매뉴얼·RAG'), ('저장', '검사·문제·대화')]):
    w = (CW - 24) / 3
    x = M + i * (w + 12)
    arrow(x + w / 2, 297, x + w / 2, 316)
    rect(x, 316, w, 51, BG, LINE)
    para(a, x + 10, 326, w - 20, 9.3, NAVY, True)
    para(b, x + 10, 348, w - 20, 8.0, MUTED)
y = section('역할분담', 397)
y = table(['담당자', '전담 영역', '주요 책임'], [
    ('조영준', '관리자 앱', '대시보드, 사용자 관리, 문제 종결·이력·통계'),
    ('장서진', '사용자 앱', '라인 상세, 카메라·결과, 문제 조치·상담 화면'),
    ('김하균', '서버·DB·통합', '인증·권한, 이더넷 API, 저장, 공용 DTO·병합'),
    ('문성찬', '영상처리', '카메라, 전처리, 뚜껑 형태·윤곽·색상 검사, 3결과'),
    ('공도현', '상담 AI', '매뉴얼·RAG, 답변 근거, 관리자 문의 전환')
], [73, 119, CW - 192], y, 8.6, 7)
y = section('14일 일정', y + 18)
y = table(['기간', '목표'], [
    ('D01-04', '공용 DTO·API·DB·권한 확정, 두 WinForms 화면과 로그인·이더넷 통신'),
    ('D05-10', '뚜껑 영상검사·3결과·문제 상태·AI 상담·관리자 문의·종결 연결'),
    ('D11-14', '기록·통계·권한·장애 검수, 전체 통합, 발표 실행본·리허설')
], [78, CW - 78], y, 8.6, 7)
footer()

c.save()
for p, t, b, txt in checks:
    if b > 786 and abs(t - (H - 33)) > .01:
        print('OVERFLOW', p, round(b, 1), txt)
print('CREATED', OUT)
print('PAGES', 2)
