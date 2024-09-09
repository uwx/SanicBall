using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SanicballCore.Server;

namespace SanicballServer.App.Services
{
    public interface ISanicballRoomsService
    {
        IReadOnlyDictionary<Guid, Room> Rooms { get; }

        Task<Room> CreateRoomAsync(RoomConfig config);
    }
}
