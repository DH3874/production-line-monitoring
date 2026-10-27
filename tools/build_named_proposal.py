from pathlib import Path

# Apply the confirmed team-member assignments to the verified all-C# proposal.
source = (Path(__file__).parent / "build_csharp_plan.py").read_text(encoding="utf-8")

replacements = {
    "CSharp_생산라인_검사_AI상담_통합기획서_v4.pdf": "CSharp_생산라인_검사_AI상담_통합기획서_v5_역할분담.pdf",
    "통합 기획안 v4.0": "통합 기획안 v5.0",
    "PROJECT PROPOSAL v4.0": "PROJECT PROPOSAL v5.0",
    "('1번 · C# 관리자 앱'": "('1번 · 조영준 · 관리자 앱'",
    "('2번 · C# 사용자 앱'": "('2번 · 문성찬 · 사용자 앱'",
    "('3번 · C# 서버·DB·통합'": "('3번 · 김하균 · 서버·DB·통합'",
    "('4번 · C# 영상처리'": "('4번 · 장서진 · 영상처리'",
    "('5번 · C# AI·업무 흐름'": "('5번 · 공도현 · AI·업무 흐름'",
}

for old, new in replacements.items():
    if old not in source:
        raise RuntimeError(f"Expected source text not found: {old}")
    source = source.replace(old, new)

exec(compile(source, __file__, "exec"))
