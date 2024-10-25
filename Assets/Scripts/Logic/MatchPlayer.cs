using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SanicballCore;
using SanicballCore.MatchMessages;
using UnityEngine;

namespace Sanicball.Logic
{
    public class MatchPlayerEventArgs : EventArgs
    {
        public MatchPlayer Player { get; private set; }
        public bool IsLocal { get; private set; }

        public MatchPlayerEventArgs(MatchPlayer player, bool isLocal)
        {
            Player = player;
            IsLocal = isLocal;
        }
    }

    [Serializable]
    public class MatchPlayer
    {
        private Guid clientGuid;
        private ControlType ctrlType;
        private double latestMovementTimestamp = int.MinValue;

        public MatchPlayer(Guid clientGuid, ControlType ctrlType, int initialCharacterId)
        {
            this.clientGuid = clientGuid;
            this.ctrlType = ctrlType;
            CharacterId = initialCharacterId;
        }

        public Guid ClientGuid => clientGuid;
        public ControlType CtrlType => ctrlType;
        public int CharacterId { get; set; }
        public Gameplay.Ball BallObject { get; set; }
        public bool ReadyToRace { get; set; }

        public void ProcessMovement(double timestamp, PlayerMovement movement)
        {
            if (timestamp > latestMovementTimestamp)
            {
                Rigidbody ballRb = BallObject.GetComponent<Rigidbody>();

                BallObject.transform.SetPositionAndRotation(movement.Position, movement.Rotation);
                ballRb.linearVelocity = movement.Velocity;
                ballRb.angularVelocity = movement.AngularVelocity;
                BallObject.DirectionVector = movement.DirectionVector;
                BallObject.TotalRings = movement.Rings;

                latestMovementTimestamp = timestamp;
            }
        }
    }
}