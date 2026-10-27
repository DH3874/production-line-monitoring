using Microsoft.EntityFrameworkCore;
using ProductionLine.Server.Data;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// [DB 준비 담당] 서버가 켜질 때 Program.cs가 한 번 부릅니다.
// 1) Migrate: DB 파일·표가 없으면 만들고, 새 마이그레이션이 있으면 적용 (기존 데이터는 그대로)
// 2) 사용자·라인이 하나도 없을 때만 시연용 기본 데이터를 넣음
//    - 계정: admin / admin123! (관리자), worker1 / worker123! (작업자, 1번 라인)
//    - 라인: 1번 실제 라인, 2~4번 가상 라인
// DB를 처음부터 다시 만들고 싶으면 서버를 끄고 App_Data/productionline.db 파일만 지우면 됩니다.
public static class DatabaseInitializer
{
    public static void Initialize(IDbContextFactory<AppDbContext> contextFactory, string? contentRoot = null)
    {
        using var db = contextFactory.CreateDbContext();

        // DB 파일이 없거나 새 마이그레이션이 있으면 표를 만들고 적용합니다.
        // 이미 적용된 마이그레이션은 다시 실행하지 않으므로 기존 데이터는 유지됩니다.
        db.Database.Migrate();
        if (contentRoot is not null) LegacyStoreImporter.Import(contextFactory, contentRoot);

        // 이미 사용자나 라인이 있으면 기존 데이터를 유지합니다.
        if (db.Users.Any() || db.Lines.Any())
            return;

        var now = DateTimeOffset.Now;

        db.Lines.AddRange(
            new LineEntity
            {
                Id = 1, Name = "LINE 1 · 대표 라인", IsReal = true,
                OperationStatus = "가동", InspectionStatus = "검사 대기",
                UpdatedAt = now
            },
            new LineEntity
            {
                Id = 2, Name = "LINE 2 · 가상", IsReal = false,
                OperationStatus = "가동", InspectionStatus = "가상 데이터",
                NormalCount = 96, DefectCount = 2, UpdatedAt = now
            },
            new LineEntity
            {
                Id = 3, Name = "LINE 3 · 가상", IsReal = false,
                OperationStatus = "공정 정지", InspectionStatus = "가상 데이터",
                NormalCount = 81, DefectCount = 3, UpdatedAt = now
            },
            new LineEntity
            {
                Id = 4, Name = "LINE 4 · 가상", IsReal = false,
                OperationStatus = "가동", InspectionStatus = "가상 데이터",
                NormalCount = 103, DefectCount = 1,
                UndeterminedCount = 2, UpdatedAt = now
            });

        // 기존 AppStore와 동일한 시연용 기본 계정입니다.
        // 비밀번호 원문은 DB에 저장하지 않습니다.
        db.Users.AddRange(
            new UserEntity
            {
                Id = "admin", Name = "관리자", Role = UserRole.Admin,
                PasswordHash = PasswordHasher.Hash("admin123!"),
                IsActive = true
            },
            new UserEntity
            {
                Id = "worker1", Name = "1라인 작업자", Role = UserRole.User,
                PasswordHash = PasswordHasher.Hash("worker123!"),
                IsActive = true
            });

        for (var lineId = 1; lineId <= 4; lineId++)
        {
            db.UserLineAccesses.Add(new UserLineAccessEntity
            {
                UserId = "admin", LineId = lineId
            });
        }

        db.UserLineAccesses.Add(new UserLineAccessEntity
        {
            UserId = "worker1", LineId = 1
        });

        // 한 번의 저장으로 사용자·라인·권한을 함께 등록합니다.
        db.SaveChanges();
    }
}
