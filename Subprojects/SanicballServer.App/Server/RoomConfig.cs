using System;
using System.Collections;

namespace SanicballCore.Server
{
    //Used as response when a client sends a server a discovery request.
    public struct RoomConfig
    {
        public string ServerName { get; set; }
        public bool ShowOnList { get; set; }
        public int MaxPlayers { get; set; }
    }
}