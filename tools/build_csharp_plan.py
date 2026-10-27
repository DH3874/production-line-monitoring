from pathlib import Path

# Build the revised all-C# proposal by reusing the verified v3 document and
# applying the technology decisions consistently across every page.
source = (Path(__file__).parent / "build_revised_plan.py").read_text(encoding="utf-8")

replacements = {
    "영상처리_생산라인_검사_AI상담_통합기획서_v3.pdf": "CSharp_생산라인_검사_AI상담_통합기획서_v4.pdf",
    "통합 기획안 v3.0": "통합 기획안 v4.0",
    "PROJECT PROPOSAL v3.0  /  C# + C++·MFC + VISION + AI ASSISTANT": "PROJECT PROPOSAL v4.0  /  ALL C# + OPENCV + AI ASSISTANT",
    "'C# 관리자 시스템','전체 라인 / 사용자·권한 / 기준 / 종결 / 감사'": "'C# WinForms 관리자 앱','전체 라인 / 사용자·권한 / 기준 / 종결 / 감사'",
    "'C++·MFC 사용자 시스템','허용 라인 / 영상·결과 / 문제 / 조치 / 문의'": "'C# WinForms 사용자 앱','허용 라인 / 영상·결과 / 문제 / 조치 / 문의'",
    "'C# 공용 서버'": "'C# ASP.NET Core 공용 서버'",
    "('영상처리 모듈','카메라·형태/윤곽·색상')": "('C# 영상처리 모듈','OpenCvSharp·형태/윤곽·색상')",
    "('상담 AI','문서 검색·대화 안내')": "('C# 상담 AI 모듈','문서 검색·대화 안내')",
    "('데이터 저장소','DB·이미지·매뉴얼')": "('C# 데이터 계층','EF Core·DB·이미지·매뉴얼')",
    "('1번 · C# 관리자 UI','관리자 로그인, 대시보드, 대표 라인 상세, 사용자·권한·기준·감사 화면')": "('1번 · C# 관리자 앱','WinForms 관리자 로그인, 대시보드, 대표 라인 상세, 사용자·권한·기준·감사 화면')",
    "('2번 · MFC 사용자 UI','사용자 로그인, 카메라·검사 결과, 라인 상세, 문제·상담·문의·조치 화면')": "('2번 · C# 사용자 앱','WinForms 사용자 로그인, 카메라·검사 결과, 라인 상세, 문제·상담·문의·조치 화면')",
    "('3번 · 서버·통신·DB','인증·권한, 상태·검사·문제·대화 저장, 실시간 전달, 감사·통합 실행')": "('3번 · C# 서버·DB·통합','이더넷 TCP/IP·REST API, 인증·권한, EF Core 저장, 상태 조회, 감사·통합')",
    "('4번 · 영상처리','카메라 연동, 전처리, 형태·윤곽·색상 검사, 3결과, 불량 분리 시뮬레이션')": "('4번 · C# 영상처리','OpenCvSharp 카메라·전처리, 형태·윤곽·색상 검사, 3결과, 분리 시뮬레이션')",
    "('5번 · 상담·업무 흐름','매뉴얼·점검표, 상담 AI, 관리자 문의, 문제 상태·완료 요청·종결 연결')": "('5번 · C# AI·업무 흐름','C# AI 모듈, 매뉴얼·점검표 검색, 관리자 문의, 완료 요청·종결 연결')",
    "영상처리와 상담 AI를 각각 담당하고 D07에 첫 통합, D13에 발표 버전을 고정합니다.": "모든 프로그램을 C# 솔루션으로 구성하고 D07에 첫 통합, D13에 발표 버전을 고정합니다.",
    "('D01-02','품목·카메라·두 검사 기준, 문제 3~5개, 문서·권한·메시지 규격 확정','영상처리 시험 + 양쪽 로그인·통신')": "('D01-02','C# 솔루션·공용 DTO, 이더넷 IP/포트, 검사 기준, 문제·권한·API 규격 확정','OpenCvSharp 시험 + 양쪽 로그인·통신')",
    "('D03-04','관리자/사용자 UI 뼈대, 인증·권한, DB, 가상 라인, 대표 라인 영상 입력','허용 계정·라인만 접근')": "('D03-04','두 WinForms 앱, ASP.NET Core 인증·권한, EF Core DB, 가상 라인·영상 입력','허용 계정·라인만 접근')",
    "('장치·분리','사용 카메라·조명, 실제 분리 장치 또는 화면 시뮬레이션')": "('기술·통신·장치','WinForms/.NET·DB, 유선 이더넷 IP/포트, 카메라·조명, 분리 시뮬레이션')",
}

for old, new in replacements.items():
    if old not in source:
        raise RuntimeError(f"Expected source text not found: {old}")
    source = source.replace(old, new)

# Keep all runtime components explicitly inside the C#/.NET ecosystem.
source = source.replace(
    "관리자와 사용자는 같은 서버 데이터를 공유하지만 화면과 권한은 분리됩니다.",
    "관리자·사용자 WinForms 앱은 ASP.NET Core 서버를 공유하지만 화면과 권한은 분리됩니다."
)
source = source.replace(
    "클라이언트는 데이터베이스를 직접 열지 않습니다.",
    "두 C# WinForms 클라이언트는 데이터베이스를 직접 열지 않습니다."
)
source = source.replace(
    "카메라 영상에서 형태·윤곽과 색상을 분석하고 설정 기준에 따라 결과를 생성합니다.",
    "C# OpenCvSharp가 카메라 영상에서 형태·윤곽과 색상을 분석하고 설정 기준에 따라 결과를 생성합니다."
)
source = source.replace(
    "프로토타입은 로컬 DB와 파일 저장소를 사용하되 서버를 통해서만 접근합니다.",
    "프로토타입은 EF Core 기반 로컬 DB와 파일 저장소를 사용하되 ASP.NET Core 서버로만 접근합니다."
)
source = source.replace(
    "인증·권한 / 라인 상태 / 검사 결과 / 문제 상태 / 채팅·알림 / 이력·감사 / AI 요청 중계",
    "유선 이더넷 TCP/IP · REST/JSON / 인증·권한 / 검사·문제 / 이력·감사 / AI 요청 중계"
)
source = source.replace(
    "로그인 계정·역할·허용 라인, 서버 연결 상태와 마지막 갱신 시각",
    "로그인 계정·역할·허용 라인, 이더넷 서버 연결 상태·IP와 마지막 갱신 시각"
)

exec(compile(source, __file__, "exec"))
