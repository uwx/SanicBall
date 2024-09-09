using System;
using SanicballCore.Server;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace SanicballServer.App.Model
{
    internal class RoomInfo(string id, Room server)
    {
        public string Id { get; } = id;
        public string Name { get; } = server.Config.ServerName;
        public int MaxPlayers { get; } = server.Config.MaxPlayers;
        public int CurrentPlayers { get; } = server.ConnectedClients;
        public bool InGame { get; } = server.InGame;

        public override bool Equals(object obj)
        {
            return obj is RoomInfo other &&
                   Id == other.Id &&
                   Name == other.Name &&
                   MaxPlayers == other.MaxPlayers &&
                   CurrentPlayers == other.CurrentPlayers &&
                   InGame == other.InGame;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name, MaxPlayers, CurrentPlayers, InGame);
        }
    }
}
