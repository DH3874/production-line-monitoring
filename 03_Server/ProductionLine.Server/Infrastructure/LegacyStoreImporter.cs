using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionLine.Server.Data;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// 이전 JSON 저장소를 보존하면서 SQLite로 한 번만 옮깁니다.
public static class LegacyStoreImporter
{
    public static void Import(IDbContextFactory<AppDbContext> factory, string contentRoot)
    {
        var source = Path.Combine(contentRoot, "data", "store.json");
        var marker = Path.Combine(contentRoot, "App_Data", "legacy-store-imported.json");
        if (!File.Exists(source) || File.Exists(marker)) return;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = JsonSerializer.Deserialize<LegacyData>(File.ReadAllText(source), options)
            ?? throw new InvalidDataException("기존 검사 저장소를 읽지 못했습니다.");
        File.Copy(source, Path.Combine(contentRoot, "App_Data", "legacy-store-backup.json"), true);
        using var db = factory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        foreach (var line in legacy.Lines)
        {
            var entity = db.Lines.Find(line.Id);
            if (entity is null)
            {
                entity = new LineEntity { Id = line.Id };
                db.Lines.Add(entity);
            }
            // 기존 작업본의 라인 상태와 누적값을 보존합니다.
            entity.Name = line.Name;
            entity.IsReal = line.IsReal;
            entity.OperationStatus = line.OperationStatus;
            entity.InspectionStatus = line.InspectionStatus;
            entity.NormalCount = Math.Max(entity.NormalCount, line.NormalCount);
            entity.DefectCount = Math.Max(entity.DefectCount, line.DefectCount);
            entity.UndeterminedCount = Math.Max(entity.UndeterminedCount, line.UndeterminedCount);
            entity.UpdatedAt = line.UpdatedAt > entity.UpdatedAt ? line.UpdatedAt : entity.UpdatedAt;
        }
        foreach (var user in legacy.Users)
        {
            if (db.Users.Find(user.Id) is null)
                db.Users.Add(new UserEntity { Id = user.Id, Name = user.Name, Role = user.Role,
                    PasswordHash = user.PasswordHash, IsActive = user.IsActive });
        }
        db.SaveChanges();
        foreach (var user in legacy.Users)
        {
            foreach (var lineId in user.AllowedLineIds)
                if (db.Lines.Any(x => x.Id == lineId)
                    && !db.UserLineAccesses.Any(x => x.UserId == user.Id && x.LineId == lineId))
                    db.UserLineAccesses.Add(new UserLineAccessEntity { UserId = user.Id, LineId = lineId });
        }
        foreach (var item in legacy.Inspections)
        {
            if (db.Inspections.Any(x => x.Id == item.Id)) continue;
            db.Inspections.Add(new InspectionEntity { Id = item.Id, LineId = item.LineId,
                Result = item.Result,
                Reason = item.Reason, InspectedAt = item.InspectedAt,
                DetectionConfidence = item.DetectionConfidence,
                HasImage = item.HasImage && File.Exists(Path.Combine(contentRoot, "data", "inspection-images", item.Id.ToString("N") + ".jpg")) });
        }
        foreach (var item in legacy.Issues)
        {
            if (db.Issues.Any(x => x.Id == item.Id)) continue;
            db.Issues.Add(new IssueEntity { Id = item.Id, LineId = item.LineId, Type = item.Type,
                Title = item.Title, Description = item.Description, Status = item.Status,
                CreatedBy = item.CreatedBy, ActionNote = item.ActionNote,
                CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt });
        }
        db.SaveChanges();
        foreach (var item in legacy.Messages)
        {
            if (db.IssueMessages.Any(x => x.Id == item.Id)) continue;
            db.IssueMessages.Add(new IssueMessageEntity { Id = item.Id, IssueId = item.IssueId,
                SenderId = item.SenderId, SenderName = item.SenderName, SenderRole = item.SenderRole,
                Message = item.Message, CreatedAt = item.CreatedAt });
        }
        db.SaveChanges();
        transaction.Commit();
        File.WriteAllText(marker, JsonSerializer.Serialize(new { importedAt = DateTimeOffset.Now,
            users = legacy.Users.Count, inspections = legacy.Inspections.Count,
            issues = legacy.Issues.Count, messages = legacy.Messages.Count }, options));
    }

    private sealed class LegacyData
    {
        public List<UserRecord> Users { get; set; } = [];
        public List<LineStatusDto> Lines { get; set; } = [];
        public List<InspectionDto> Inspections { get; set; } = [];
        public List<IssueDto> Issues { get; set; } = [];
        public List<IssueMessageDto> Messages { get; set; } = [];
    }
}
