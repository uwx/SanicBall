using System;
using System.Collections;
using System.Linq;
using Sanicball.Data;
using Sanicball.Gameplay;
using SanicballCore;
using SanicballCore.MatchMessages;
using UnityEngine;

namespace Sanicball.Logic
{
    public class RaceFinishReport
    {
        /// <summary>
        /// Finishing with this position means the player has been disqualified.
        /// </summary>
        public const int DISQUALIFIED_POS = -1;

        private TimeSpan time;
        private int position;

        public int Position => position;
        public TimeSpan Time => time;
        public bool Disqualified => position == DISQUALIFIED_POS;

        public RaceFinishReport(int position, TimeSpan time)
        {
            this.position = position;
            this.time = time;
        }
    }

    public class NextCheckpointPassArgs : EventArgs
    {
        public int IndexOfPreviousCheckpoint { get; private set; }
        public TimeSpan CurrentLapTime { get; private set; }

        public NextCheckpointPassArgs(int indexOfPreviousCheckpoint, TimeSpan currentLapTime)
        {
            IndexOfPreviousCheckpoint = indexOfPreviousCheckpoint;
            CurrentLapTime = currentLapTime;
        }
    }

    public class RespawnEventArgs : EventArgs
    {
        public int OldRings { get; private set; }
        public int NewRings { get; private set; }
        public TimeSpan TimePenalty { get; private set; }

        public RespawnEventArgs(int oldRings, int newRings, TimeSpan timePenalty)
        {
            this.OldRings = oldRings;
            this.NewRings = newRings;
            this.TimePenalty = timePenalty;
        }
    }

    [Serializable] //This is so the list if race players can be viewed in the inspector
    public class RacePlayer
    {
        private Ball ball;
        private IBallCamera ballCamera;
        private RaceFinishReport finishReport;

        //Race progress
        private int lap;
        private int currentCheckpointIndex;

        //Checkpoint related stuff
        private Vector3 currentCheckpointPos;
        private Checkpoint nextCheckpoint;

        //Time
        private float lapTime;
        private float[] checkpointTimes;
        private float timeout;

        //Cache of the scene's StageReferences object (Because it's used often)
        private StageReferences sr;

        //Associated logic
        private MatchMessenger matchMessenger;
        private MatchPlayer associatedMatchPlayer;
        private bool waitingForCheckpointMessage;
        private bool waitingForRespawnMessage;

        //Events
        public event EventHandler<NextCheckpointPassArgs> NextCheckpointPassed;
        public event EventHandler<RespawnEventArgs> Respawned;
        public event EventHandler FinishLinePassed;
        public event EventHandler Destroyed;

        //Readonly properties that get stuff from the player's ball
        public Ball Ball => ball;
        public bool IsPlayer => ball.Type == BallType.Player;
        public string Name => ball.Nickname;
        public ControlType CtrlType => ball.CtrlType;
        public int Character => ball.CharacterId;
        public Transform Transform => ball.transform;
        public float Speed => ball.GetComponent<Rigidbody>().velocity.magnitude;
        public IBallCamera Camera => ballCamera;
        public int Rings => ball.Rings;

        //Race progress properties
        public int Lap => lap;
        public bool RaceFinished => finishReport != null;
        public RaceFinishReport FinishReport => finishReport;
        public float Timeout => timeout;

        //Misc properties
        public MatchPlayer AssociatedMatchPlayer => associatedMatchPlayer;
        public bool LapRecordsEnabled { get; set; }
        public int Position { get; set; }
        public Checkpoint NextCheckpoint => nextCheckpoint;

        public RacePlayer(Ball ball, MatchMessenger matchMessenger, MatchPlayer associatedMatchPlayer)
        {
            sr = StageReferences.Active;

            this.matchMessenger = matchMessenger;
            this.associatedMatchPlayer = associatedMatchPlayer;
            matchMessenger.CreateListener<CheckpointPassedMessage>(CheckpointPassedHandler);
            matchMessenger.CreateListener<RaceTimeoutMessage>(RaceTimeoutHandler);
            matchMessenger.CreateListener<PlayerRespawnMessage>(PlayerRespawnHandler);

            lap = 1;

            ball.CanMove = false;
            ball.AutoBrake = true;
            ball.CheckpointPassed += Ball_CheckpointPassed;
            ball.RespawnRequested += Ball_RespawnRequested;
            currentCheckpointPos = sr.checkpoints[0].transform.position;
            this.ball = ball;

            ball.CameraCreated += (sender, e) =>
            {
                ballCamera = e.CameraCreated;
                ballCamera.SetDirection(sr.checkpoints[0].transform.rotation);
            };

            checkpointTimes = new float[StageReferences.Active.checkpoints.Length];

            SetNextCheckpoint();
        }

        public void StartRace()
        {
            ball.CanMove = true;
            ball.AutoBrake = false;
        }

        public void FinishRace(RaceFinishReport report)
        {
            if (finishReport == null)
            {
                finishReport = report;
                //Stop movement of AI balls
                if (ball.Type == BallType.AI)
                    ball.CanMove = false;
                //Set layer to Racer Ghost to block collision with racing players
                ball.gameObject.layer = LayerMask.NameToLayer("Racer Ghost");
            }
            else
            {
                throw new InvalidOperationException("RacePlayer tried to finish a race twice for some reason");
            }
        }

        public float CalculateRaceProgress()
        {
            //This function returns race progress as laps done (1..*) + progress to next lap (0..1)

            //If the race is finished, ignore lap progress and just return the current lap (Which would be 1 above the number of laps in the race)
            if (RaceFinished)
            {
                return Lap;
            }

            float progPerCheckpoint = 1f / sr.checkpoints.Length;

            Vector3 nextPos = nextCheckpoint.transform.position;
            float ballToNext = Vector3.Distance(ball.transform.position, nextPos);
            float currToNext = Vector3.Distance(currentCheckpointPos, nextPos);

            float distToNextPercentile = 1f - Mathf.Clamp(ballToNext / currToNext, 0f, 1f);

            float distToNextProg = distToNextPercentile * progPerCheckpoint;
            float lapProg = currentCheckpointIndex * progPerCheckpoint + distToNextProg;

            return Lap + lapProg;
        }

        private void Ball_CheckpointPassed(object sender, CheckpointPassArgs e)
        {
            if (e.CheckpointPassed == nextCheckpoint)
            {
                if (ball.Type == BallType.Player && ball.CtrlType != ControlType.None)
                {
                    if (!waitingForCheckpointMessage)
                    {
                        //Send a match message for local players
                        waitingForCheckpointMessage = true;
                        matchMessenger.SendMessage(new CheckpointPassedMessage(associatedMatchPlayer.ClientGuid, ball.CtrlType, lapTime));
                    }
                }
                else if (ball.Type == BallType.AI)
                {
                    //Invoke PassNextCheckpoint directly for AI balls
                    PassNextCheckpoint(lapTime);
                }
            }
        }

        private void CheckpointPassedHandler(CheckpointPassedMessage msg, float travelTime)
        {
            if (associatedMatchPlayer != null && msg.ClientGuid == associatedMatchPlayer.ClientGuid && msg.CtrlType == associatedMatchPlayer.CtrlType)
            {
                PassNextCheckpoint(msg.LapTime);
                waitingForCheckpointMessage = false;
            }
        }

        private void RaceTimeoutHandler(RaceTimeoutMessage msg, float travelTime)
        {
            if (associatedMatchPlayer != null && msg.ClientGuid == associatedMatchPlayer.ClientGuid && msg.CtrlType == associatedMatchPlayer.CtrlType)
            {
                timeout = msg.Time - travelTime;
            }
        }

        private void PlayerRespawnHandler(PlayerRespawnMessage msg, float travelTime)
        {
            if (associatedMatchPlayer != null && msg.ClientGuid == associatedMatchPlayer.ClientGuid && msg.CtrlType == associatedMatchPlayer.CtrlType)
            {
                RespawnBall(new RespawnEventArgs(msg.OldRings, msg.NewRings, msg.TimePenalty));
                waitingForRespawnMessage = false;
            }
        }

        private void PassNextCheckpoint(float lapTime)
        {
            checkpointTimes[currentCheckpointIndex] = lapTime;

            //Call NextCheckpointPassed BEFORE doing anything else. This ensures things like lap records work correctly.
            if (NextCheckpointPassed != null)
                NextCheckpointPassed(this, new NextCheckpointPassArgs(currentCheckpointIndex, TimeSpan.FromSeconds(lapTime)));

            currentCheckpointIndex = (currentCheckpointIndex + 1) % sr.checkpoints.Length;
            currentCheckpointPos = nextCheckpoint.transform.position;

            if (currentCheckpointIndex == 0)
            {
                lap++;

                if (FinishLinePassed != null)
                    FinishLinePassed(this, EventArgs.Empty);

                if (LapRecordsEnabled)
                {
                    CharacterTier tier = ActiveData.Characters[Character].tier;
                    string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    int stage = ActiveData.Stages.Where(a => a.sceneName == sceneName).First().id;

                    ActiveData.RaceRecords.Add(new RaceRecord(
                        tier,
                        lapTime,
                        DateTime.Now,
                        stage,
                        Character,
                        checkpointTimes,
                        GameVersion.AS_FLOAT,
                        GameVersion.IS_TESTING
                        ));

                    ActiveData.instance.SaveAll();

                    Debug.Log("Saved lap record (" + TimeSpan.FromSeconds(lapTime) + ")");
                }

                //Reset lap time
                this.lapTime = 0 + (this.lapTime - lapTime);
                checkpointTimes = new float[StageReferences.Active.checkpoints.Length];
            }

            SetNextCheckpoint();

            //Set next target node if this is an AI ball
            TrySetAITarget();
        }

        private void Ball_RespawnRequested(object sender, RespawnEventArgs e)
        {
            if (ball.Type == BallType.Player && ball.CtrlType != ControlType.None)
            {
                if (!waitingForRespawnMessage)
                {
                    //Send a match message for local players
                    waitingForRespawnMessage = true;
                    matchMessenger.SendMessage(new PlayerRespawnMessage(associatedMatchPlayer.ClientGuid, ball.CtrlType, e.NewRings, e.OldRings, e.TimePenalty));
                }
            }
            else if (ball.Type == BallType.AI)
            {
                //Invoke PassNextCheckpoint directly for AI balls
                RespawnBall(e);
            }
        }

        private void RespawnBall(RespawnEventArgs e)
        {
            ball.AddRings(e.NewRings - e.OldRings);

            var position = sr.checkpoints[currentCheckpointIndex].GetRespawnPoint() + (0.5f * ball.transform.localScale.x * Vector3.up);
            var rotation = sr.checkpoints[currentCheckpointIndex].transform.rotation;

            var rb = ball.GetComponent<Rigidbody>();
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            ball.transform.SetPositionAndRotation(position, rotation);
            ballCamera?.SetDirection(rotation);

            ball.SpawnLostRings(e.OldRings - e.NewRings, position);

            Respawned?.Invoke(this, e);

            //Time penalty
            lapTime += (float)e.TimePenalty.TotalSeconds;

            //Set next target node if this is an AI ball
            TrySetAITarget();
        }

        private void TrySetAITarget()
        {
            BallControlAI ai = ball.GetComponent<BallControlAI>();
            if (ai)
            {
                Checkpoint activeCheckpoint = StageReferences.Active.checkpoints[currentCheckpointIndex];
                ai.Target = activeCheckpoint.FirstAINode;
            }
        }

        private void SetNextCheckpoint()
        {
            if (RaceFinished)
            {
                nextCheckpoint = null;
            }
            else if (currentCheckpointIndex == sr.checkpoints.Length - 1)
            {
                nextCheckpoint = sr.checkpoints[0];
            }
            else
            {
                nextCheckpoint = sr.checkpoints[currentCheckpointIndex + 1];
            }
        }

        public void UpdateTimer(float dt)
        {
            lapTime += dt;

            //This is also a good time to decrement the timeout timer if it's above 0
            if (timeout > 0)
            {
                timeout = Mathf.Max(0, timeout - Time.deltaTime);
            }
        }

        public void Destroy()
        {
            matchMessenger.RemoveListener<CheckpointPassedMessage>(CheckpointPassedHandler);

            if (Destroyed != null)
                Destroyed(this, EventArgs.Empty);
        }
    }
}