using System;
using SanicballCore;

namespace Sanicball.Data
{

    [Serializable]
    public class RaceRecord
    {
		private CharacterTier tier;
        private float time;
        private DateTime date;
        private int stage;
        private int character;
        private float[] checkpointTimes;
        private float gameVersion;
        private bool wasTesting;

        public CharacterTier Tier => tier;
        public float Time => time;
        public DateTime Date => date;
        public int Stage => stage;
        public int Character => character;
        public float[] CheckpointTimes => checkpointTimes;
        public float GameVersion => gameVersion;
        public bool WasTesting => wasTesting;

        public RaceRecord(CharacterTier tier, float time, DateTime date, int stage, int character, float[] checkpointTimes, float gameVersion, bool isTesting)
        {
            this.tier = tier;
            this.time = time;
            this.date = date;
            this.stage = stage;
            this.character = character;
            this.checkpointTimes = checkpointTimes;
            this.gameVersion = gameVersion;
            this.wasTesting = isTesting;
        }
    }
}