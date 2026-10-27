using ProductionLine.Shared;
namespace _01_AdminApp.Services;
public interface ILineDetailService
{
    Task<List<InspectionDto>> GetInspectionsAsync(int lineId);
    Task<byte[]> GetInspectionImageAsync(Guid inspectionId, CancellationToken cancellationToken = default) =>
        Task.FromException<byte[]>(new NotSupportedException("이 데이터에는 저장된 검사 사진이 없습니다."));
}
public sealed class ApiLineDetailService : ILineDetailService
{
    private readonly ProductionLineApiClient _api;
    public ApiLineDetailService(ProductionLineApiClient api) => _api = api;
    public async Task<List<InspectionDto>> GetInspectionsAsync(int lineId)
    {
        var records = await ServerCall.Run(() => _api.GetInspectionsAsync(lineId));
        return records.Count == 0 ? VirtualInspectionSamples.Get(lineId) : records;
    }
    public Task<byte[]> GetInspectionImageAsync(Guid inspectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sample = VirtualInspectionSamples.GetImage(inspectionId);
        return sample == null ? _api.GetInspectionImageAsync(inspectionId, cancellationToken) : Task.FromResult(sample);
    }
}
public sealed class MockLineDetailService : ILineDetailService
{
    public Task<List<InspectionDto>> GetInspectionsAsync(int lineId) => Task.FromResult(VirtualInspectionSamples.Get(lineId));
    public Task<byte[]> GetInspectionImageAsync(Guid inspectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(VirtualInspectionSamples.GetImage(inspectionId)
            ?? throw new FileNotFoundException("이 기록에는 가상 예시 사진이 없습니다."));
    }
}
