using System;

namespace SanicballCore.MatchMessages
{
    public class PlayerRespawnMessage : MatchMessage
    {
        public Guid ClientGuid { get; private set; }
        public ControlType CtrlType { get; private set; }
        public int OldRings { get; private set; }
        public int NewRings { get; private set; }
        public TimeSpan TimePenalty { get; private set; }

        public PlayerRespawnMessage(Guid clientGuid, ControlType ctrlType, int newRings, int oldRings, TimeSpan timePenalty)
        {
            this.ClientGuid = clientGuid;
            this.CtrlType = ctrlType;
            this.OldRings = oldRings;
            this.NewRings = newRings;
            this.TimePenalty = timePenalty;
        }
    }
}