using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

public sealed class GroupChatStore
{
    private const int Retention = 5000;
    private readonly object _gate = new();
    private readonly string _path;
    // 파일을 메모장으로 열어도 읽히도록: 한글은 그대로, 메시지마다 줄바꿈 (김하균 변경)
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true
    };
    private ChatData _data;

    public GroupChatStore(IHostEnvironment environment)
    {
        var directory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "group-chat.json");
        if (!File.Exists(_path)) { _data = new(0, []); return; }
        try
        {
            _data = JsonSerializer.Deserialize<ChatData>(File.ReadAllText(_path), Json)
                ?? throw new InvalidDataException("단체 채팅 이력이 비어 있습니다.");
            if (_data.Messages is null || _data.LastId < 0 || _data.Messages.Count > Retention
                || _data.Messages.Any(x => x is null || x.Id <= 0 || x.Id > _data.LastId || x.ClientMessageId == Guid.Empty
                    || string.IsNullOrWhiteSpace(x.SenderId) || string.IsNullOrWhiteSpace(x.SenderName) || !Enum.IsDefined(x.SenderRole)
                    || !ValidText(x.Text))
                || _data.Messages.Select(x => x.Id).Distinct().Count() != _data.Messages.Count
                || _data.Messages.Select(x => (x.SenderId, x.ClientMessageId)).Distinct().Count() != _data.Messages.Count
                || !_data.Messages.Select(x => x.Id).SequenceEqual(_data.Messages.Select(x => x.Id).Order()))
                throw new InvalidDataException("단체 채팅 이력 형식을 확인해 주세요. 원본 파일은 유지했습니다.");
        }
        catch (JsonException ex) { throw new InvalidDataException("단체 채팅 이력을 읽지 못했습니다. data/group-chat.json 원본을 확인해 주세요.", ex); }
    }

    public ChatMessagePage GetMessages(long afterId, int limit)
    {
        lock (_gate)
        {
            if (afterId > _data.LastId) return new([], _data.LastId, false, false, true);
            var initial = afterId == 0;
            var candidates = initial ? _data.Messages.TakeLast(limit + 1).ToArray()
                : _data.Messages.Where(x => x.Id > afterId).Take(limit + 1).ToArray();
            var truncated = initial ? _data.Messages.Count > limit
                : _data.Messages.Count > 0 && afterId < _data.Messages[0].Id - 1;
            var messages = initial ? candidates.TakeLast(limit).ToArray() : candidates.Take(limit).ToArray();
            return new(messages, _data.LastId, !initial && candidates.Length > limit, truncated, false);
        }
    }

    public ChatMessageDto AddMessage(UserDto sender, SendChatMessageRequest request)
    {
        var text = request.Text?.Trim();
        if (request.ClientMessageId == Guid.Empty || !ValidText(text))
            throw new ArgumentException("메시지는 공백을 제외한 1~2,000자로 입력하세요. 제어 문자는 사용할 수 없습니다.");
        lock (_gate)
        {
            var previous = _data.Messages.FirstOrDefault(x => x.SenderId == sender.Id && x.ClientMessageId == request.ClientMessageId);
            if (previous is not null)
            {
                if (previous.Text != text) throw new InvalidOperationException("같은 전송 ID에 다른 메시지를 보낼 수 없습니다.");
                return previous;
            }
            var message = new ChatMessageDto(checked(_data.LastId + 1), request.ClientMessageId, sender.Id,
                string.IsNullOrWhiteSpace(sender.Name) ? sender.Id : sender.Name, sender.Role, text!, DateTimeOffset.Now); // 보낸 시각을 한국 시간(+09:00)으로 저장
            var next = new ChatData(message.Id, _data.Messages.Append(message).TakeLast(Retention).ToList());
            // 저장에 성공한 뒤 메모리에도 반영한다. 응답이 유실돼도 같은 전송 ID로 중복 없이 재시도할 수 있다.
            var temp = _path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(next, Json));
                MoveWithRetry(temp, _path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _data = next;
            return message;
        }
    }

    // 윈도우에서는 백신·검색 색인이 방금 쓴 파일을 잠깐 붙잡아 교체가 거절될 수 있음 → 짧게 기다렸다 다시 시도 (김하균 추가)
    private static void MoveWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { File.Move(source, destination, true); return; }
            catch (Exception ex) when (attempt < 10 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static bool ValidText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 2000
        && !text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
    private sealed record ChatData(long LastId, List<ChatMessageDto> Messages);
}
