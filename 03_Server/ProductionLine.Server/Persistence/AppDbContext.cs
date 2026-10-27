using Microsoft.EntityFrameworkCore;
namespace ProductionLine.Server.Data;

// [DB 관리] 서버가 DB를 쓸 때 드나드는 "문"
// - 아래 DbSet 목록 = DB에 있는 표 목록입니다. 여기에 등록해야 표가 만들어집니다.
// - OnModelCreating = 표끼리 어떻게 연결되는지, 칸에 어떤 규칙이 있는지 정하는 곳입니다.
// - 서버 코드는 db.Issues.Add(...), db.SaveChanges()처럼 쓰고, EF Core가 이걸 SQL(INSERT, SELECT…)로 바꿔 DB에 보냅니다.
// - 이 파일을 바꿔서 표 모양이 달라지면 마이그레이션(Migrations 폴더)을 새로 만들어야 DB에 반영됩니다.
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ===== DB 표 목록 =====
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserLineAccessEntity> UserLineAccesses => Set<UserLineAccessEntity>();
    public DbSet<LineEntity> Lines => Set<LineEntity>();
    public DbSet<InspectionEntity> Inspections => Set<InspectionEntity>();
    public DbSet<IssueEntity> Issues => Set<IssueEntity>();
    public DbSet<IssueHistoryEntity> IssueHistories => Set<IssueHistoryEntity>();
    public DbSet<IssueMessageEntity> IssueMessages => Set<IssueMessageEntity>();
    public DbSet<RequestLogEntity> RequestLogs => Set<RequestLogEntity>();
    public DbSet<LineHardwareLogEntity> LineHardwareLogs => Set<LineHardwareLogEntity>();
    // ===== 표끼리의 연결 규칙 =====
    // HasForeignKey = "이 칸은 다른 표의 번호를 가리킨다" (예: 문제의 LineId → Lines 표)
    // DeleteBehavior.Restrict = 연결된 기록이 있으면 함부로 지우지 못하게 막음 (기록 보존)
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // 같은 사용자에게 같은 라인 권한이 두 번 저장되지 않게 (사용자, 라인) 조합을 고유 키로 사용
        modelBuilder.Entity<UserLineAccessEntity>().HasKey(x => new { x.UserId, x.LineId });
        modelBuilder.Entity<UserLineAccessEntity>().HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserLineAccessEntity>().HasOne<LineEntity>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InspectionEntity>().HasOne<LineEntity>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IssueEntity>().HasOne<LineEntity>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IssueEntity>().HasOne<UserEntity>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IssueHistoryEntity>().HasOne<IssueEntity>().WithMany().HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IssueHistoryEntity>().HasOne<UserEntity>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.Restrict);

        // 문의 메시지 표: 문제·보낸 사람과 연결, 문제별로 빨리 찾는 색인, 내용 1~2,000자 제한
        modelBuilder.Entity<IssueMessageEntity>()
            .HasOne<IssueEntity>().WithMany().HasForeignKey(x => x.IssueId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IssueMessageEntity>()
            .HasOne<UserEntity>().WithMany().HasForeignKey(x => x.SenderId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IssueMessageEntity>()
            .HasIndex(x => new { x.IssueId, x.Id });
        modelBuilder.Entity<IssueMessageEntity>().Property(x => x.Message)
            .IsRequired().HasMaxLength(2000);
        modelBuilder.Entity<IssueMessageEntity>().ToTable(table =>
            table.HasCheckConstraint("CK_IssueMessages_MessageLength", "length(\"Message\") BETWEEN 1 AND 2000"));
        // 시각을 큰 숫자(Ticks)로 바꿔 저장: SQLite가 날짜 정렬·비교를 못 해서 숫자로 저장함
        modelBuilder.Entity<IssueMessageEntity>().Property(x => x.CreatedAt)
            .HasConversion(value => value.UtcDateTime.Ticks,
                value => new DateTimeOffset(value, TimeSpan.Zero));

        // 통신 기록은 존재하지 않는 사용자·문제 번호로 온 요청도 남겨야 하므로 외래 키를 걸지 않습니다.
        modelBuilder.Entity<RequestLogEntity>().Property(x => x.ReceivedAt)
            .HasConversion(value => value.UtcDateTime.Ticks,
                value => new DateTimeOffset(value, TimeSpan.Zero));
        modelBuilder.Entity<RequestLogEntity>().HasIndex(x => x.ReceivedAt);
        modelBuilder.Entity<RequestLogEntity>().Property(x => x.Method).HasMaxLength(10);
        modelBuilder.Entity<RequestLogEntity>().Property(x => x.Path).HasMaxLength(300);
        modelBuilder.Entity<RequestLogEntity>().Property(x => x.Action).HasMaxLength(100);
        modelBuilder.Entity<RequestLogEntity>().Property(x => x.UserId).HasMaxLength(100);
        modelBuilder.Entity<RequestLogEntity>().Property(x => x.ClientIp).HasMaxLength(64);
        modelBuilder.Entity<RequestLogEntity>().Property(x => x.Note).HasMaxLength(500);

        // 검사 기록의 휴대폰 기종: 최대 100자 (김하균)
        modelBuilder.Entity<InspectionEntity>().Property(x => x.PhoneModel).HasMaxLength(100);

        // 센서·벨트 기록: 통신 기록처럼 기록용이라 외래 키 없이 시각으로 찾고 지웁니다. (김하균)
        modelBuilder.Entity<LineHardwareLogEntity>().Property(x => x.RecordedAt)
            .HasConversion(value => value.UtcDateTime.Ticks,
                value => new DateTimeOffset(value, TimeSpan.Zero));
        modelBuilder.Entity<LineHardwareLogEntity>().HasIndex(x => x.RecordedAt);
        modelBuilder.Entity<LineHardwareLogEntity>().HasIndex(x => new { x.LineId, x.RecordedAt });
        modelBuilder.Entity<LineHardwareLogEntity>().Property(x => x.Reason).HasMaxLength(20);
    }
}
