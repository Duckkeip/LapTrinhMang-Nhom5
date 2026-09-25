using System.Collections.Concurrent;
using ChatProtocol;
using ProtocolRoomInfo = ChatProtocol.RoomInfo;

namespace ChatServer;

/// <summary>
/// Quan ly danh sach phong va thanh vien dang online trong tung phong, hoan toan trong RAM.
/// </summary>
public class RoomManager
{
    private readonly ConcurrentDictionary<string, HashSet<ClientSession>> _rooms = new();
    private readonly object _lock = new();

    public RoomManager(IEnumerable<string> defaultRooms)
    {
        foreach (var r in defaultRooms)
            _rooms.TryAdd(r, new HashSet<ClientSession>());
    }

    public bool RoomExists(string room) => _rooms.ContainsKey(room);

    public void CreateRoomIfMissing(string room)
    {
        _rooms.TryAdd(room, new HashSet<ClientSession>());
    }

    public void Join(ClientSession client, string room)
    {
        lock (_lock)
        {
            LeaveInternal(client);

            if (!_rooms.TryGetValue(room, out var set))
            {
                set = new HashSet<ClientSession>();
                _rooms[room] = set;
            }
            set.Add(client);
            client.CurrentRoom = room;
        }
    }

    public void Leave(ClientSession client)
    {
        lock (_lock)
        {
            LeaveInternal(client);
        }
    }

    private void LeaveInternal(ClientSession client)
    {
        if (client.CurrentRoom != null && _rooms.TryGetValue(client.CurrentRoom, out var set))
        {
            set.Remove(client);
        }
        client.CurrentRoom = null;
    }

    public List<ClientSession> GetMembers(string room)
    {
        lock (_lock)
        {
            return _rooms.TryGetValue(room, out var set) ? set.ToList() : new List<ClientSession>();
        }
    }

    public List<string> GetUsernames(string room)
    {
        lock (_lock)
        {
            return _rooms.TryGetValue(room, out var set)
                ? set.Select(c => c.Username).OrderBy(u => u).ToList()
                : new List<string>();
        }
    }

    public List<ProtocolRoomInfo> GetRoomList()
    {
        lock (_lock)
        {
            return _rooms
                .Select(kv => new ProtocolRoomInfo(kv.Key, kv.Value.Count))
                .OrderBy(r => r.Name)
                .ToList();
        }
    }

    /// <summary>Gui 1 message (type + payload) toi tat ca thanh vien dang trong 1 phong, dong thoi.</summary>
    public async Task BroadcastAsync(string room, string type, object? data, ClientSession? except = null)
    {
        var members = GetMembers(room).Where(m => m != except);
        await Task.WhenAll(members.Select(m => m.SendAsync(type, data)));
    }
}
