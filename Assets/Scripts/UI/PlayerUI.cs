using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sanicball.Data;
using Sanicball.Logic;
using SanicballCore;
using UnityEngine;
using UnityEngine.UI;

namespace Sanicball.UI
{
    public class PlayerUI : MonoBehaviour
    {
        [SerializeField]
        private RectTransform fieldContainer;

        [SerializeField]
        private Text speedField = null;

        [SerializeField]
        private Text speedFieldLabel = null;

        [SerializeField]
        private Text lapField = null;

        [SerializeField]
        private Text timeField = null;

        [SerializeField]
        private Text checkpointTimeField = null;

        [SerializeField]
        private Text checkpointTimeDiffField = null;

        [SerializeField]
        private Text ringsField = null;

        [SerializeField]
        private GameObject ring = null;

        [SerializeField]
        private AudioClip checkpointSound;
        [SerializeField]
        private AudioClip respawnSound;
        [SerializeField]
        private AudioClip oneUpSound;
        [SerializeField]
        private AudioClip ringsSpread;

        [SerializeField]
        private Marker markerPrefab;

        [SerializeField]
        private RectTransform markerContainer;

        private Marker checkpointMarker;
        private List<Marker> playerMarkers = new List<Marker>();

        private RacePlayer targetPlayer;
        private RaceManager targetManager;

        private readonly Color finishedColor = new Color(0f, 0.5f, 1f);

        private int lastRings = 0;
        private int maxRings = 0;

        public RacePlayer TargetPlayer
        {
            get { return targetPlayer; }
            set
            {
                if (targetPlayer != null)
                {
                    targetPlayer.NextCheckpointPassed -= TargetPlayer_NextCheckpointPassed;
                    targetPlayer.Respawned -= TargetPlayer_Respawned;
                    Destroy(checkpointMarker.gameObject);
                    foreach (Marker m in playerMarkers)
                    {
                        Destroy(m.gameObject);
                    }
                }

                targetPlayer = value;

                targetPlayer.NextCheckpointPassed += TargetPlayer_NextCheckpointPassed;
                targetPlayer.Respawned += TargetPlayer_Respawned;

                //Marker following next checkpoint
                checkpointMarker = Instantiate(markerPrefab);
                checkpointMarker.transform.SetParent(markerContainer, false);
                checkpointMarker.Text = "Checkpoint";
                checkpointMarker.Clamp = true;

                //Markers following each player
                for (int i = 0; i < TargetManager.PlayerCount; i++)
                {
                    RacePlayer p = TargetManager[i];
                    if (p == TargetPlayer) continue;

                    var playerMarker = Instantiate(markerPrefab);
                    playerMarker.transform.SetParent(markerContainer, false);
                    playerMarker.Text = p.Name;
                    playerMarker.Target = p.Transform;
                    playerMarker.Clamp = false;

                    //Disabled for now, glitchy as fuck
                    //playerMarker.HideImageWhenInSight = true;

                    Data.CharacterInfo character = ActiveData.Characters[p.Character];
                    //playerMarker.Sprite = character.icon;
                    Color c = character.color;
                    c.a = 0.2f;
                    playerMarker.Color = c;

                    playerMarkers.Add(playerMarker);
                }
            }
        }

        public RaceManager TargetManager
        {
            get { return targetManager; }
            set
            {
                targetManager = value;
            }
        }

        public Camera TargetCamera { get; set; }

        private void TargetPlayer_Respawned(object sender, RespawnEventArgs e)
        {
            UISound.Play(respawnSound);

            if (targetPlayer.Ball.Rings > 0)
            {
                UISound.Play(ringsSpread);
            }

            checkpointTimeField.text = "Respawn lap time penalty";
            checkpointTimeField.GetComponent<ToggleCanvasGroup>().ShowTemporarily(2f);

            checkpointTimeDiffField.color = Color.red;
            checkpointTimeDiffField.text = "+" + Utils.GetTimeString(e.TimePenalty);
            checkpointTimeDiffField.GetComponent<ToggleCanvasGroup>().ShowTemporarily(2f);
        }

        private void TargetPlayer_NextCheckpointPassed(object sender, NextCheckpointPassArgs e)
        {
            UISound.Play(checkpointSound);
            checkpointTimeField.text = Utils.GetTimeString(e.CurrentLapTime);
            checkpointTimeField.GetComponent<ToggleCanvasGroup>().ShowTemporarily(2f);

            if (TargetPlayer.LapRecordsEnabled)
            {
                CharacterTier tier = ActiveData.Characters[targetPlayer.Character].tier;
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                int stage = ActiveData.Stages.Where(a => a.sceneName == sceneName).First().id;

                float time = (float)e.CurrentLapTime.TotalSeconds;

                RaceRecord bestRecord = ActiveData.RaceRecords
                    .Where(a => a.Tier == tier && a.Stage == stage && a.GameVersion == GameVersion.AS_FLOAT && a.WasTesting == GameVersion.IS_TESTING)
                    .OrderBy(a => a.Time)
                    .FirstOrDefault();

                if (bestRecord != null)
                {
                    float diff = time - bestRecord.CheckpointTimes[e.IndexOfPreviousCheckpoint];

                    bool faster = diff < 0;
                    TimeSpan diffSpan = TimeSpan.FromSeconds(Mathf.Abs(diff));

                    checkpointTimeDiffField.text = (faster ? "-" : "+") + Utils.GetTimeString(diffSpan);
                    checkpointTimeDiffField.color = faster ? Color.blue : Color.red;
                    checkpointTimeDiffField.GetComponent<ToggleCanvasGroup>().ShowTemporarily(2f);

                    if (e.IndexOfPreviousCheckpoint == StageReferences.Active.checkpoints.Length - 1 && faster)
                    {
                        checkpointTimeDiffField.text = "New lap record!";
                    }
                }
                else
                {
                    if (e.IndexOfPreviousCheckpoint == StageReferences.Active.checkpoints.Length - 1)
                    {
                        checkpointTimeDiffField.text = "Lap record set!";
                        checkpointTimeDiffField.color = Color.blue;
                        checkpointTimeDiffField.GetComponent<ToggleCanvasGroup>().ShowTemporarily(2f);
                    }
                }
            }
        }

        private void Start()
        {
        }

        private void Update()
        {
            if (TargetCamera)
            {
                fieldContainer.anchorMin = TargetCamera.rect.min;
                fieldContainer.anchorMax = TargetCamera.rect.max;
            }

            if (TargetPlayer == null || TargetManager == null) return;

            float speed = TargetPlayer.Speed;
            string postfix = " ";

            //Speed label
            if (!ActiveData.GameSettings.useImperial)
            {
                postfix += (Mathf.Floor(speed) == 1f) ? "fast/h" : "fasts/h";
            }
            else
            {
                speed *= 0.62f;
                postfix += (Mathf.Floor(speed) == 1f) ? "lightspeed" : "lightspeeds";
                speedFieldLabel.fontSize = 62;
            }

            //Speed field size and color
            var min = 96;
            var max = 150;
            var size = max - (max - min) * Mathf.Exp(-speed * 0.02f);
            speedField.fontSize = (int)size;
            speedField.text = Mathf.Floor(speed).ToString();
            speedFieldLabel.text = postfix;

            //Lap counter
            if (!TargetPlayer.RaceFinished)
            {
                lapField.text = "Lap " + TargetPlayer.Lap + "/" + TargetManager.Settings.Laps;
            }
            else
            {
                if (TargetPlayer.FinishReport.Disqualified)
                {
                    lapField.text = "Disqualified";
                    lapField.color = Color.red;
                }
                else
                {
                    lapField.text = "Race finished";
                    lapField.color = finishedColor;
                }
            }

            ringsField.text = $"{TargetPlayer.Rings:00}";
            if (targetPlayer.Rings != lastRings)
            {
                if (targetPlayer.Rings > lastRings)
                {
                    if ((maxRings = Math.Max(maxRings, lastRings)) % 100 == 0 && maxRings > 0)
                    {
                        maxRings++;
                        UISound.Play(oneUpSound);
                    }

                    if (targetPlayer.Ball.Rings % 2 == 0)
                        targetPlayer.Ball.sounds.RingL.Play();
                    else
                        targetPlayer.Ball.sounds.RingR.Play();
                }

                lastRings = targetPlayer.Rings;
                StartCoroutine(RotateRing());
            }

            if (targetPlayer.Ball.TotalRings == 0)
            {
                StartCoroutine(RingFlash());
            }

            //Race time
            TimeSpan timeToUse = TargetManager.RaceTime;
            if (TargetPlayer.FinishReport != null)
            {
                timeToUse = TargetPlayer.FinishReport.Time;
                timeField.color = finishedColor;
            }
            timeField.text = Utils.GetTimeString(timeToUse);

            if (TargetPlayer.Timeout > 0)
            {
                timeField.text += Environment.NewLine + "<b>Timeout</b> " + Utils.GetTimeString(TimeSpan.FromSeconds(TargetPlayer.Timeout));
            }

            //Checkpoint marker
            if (TargetPlayer.NextCheckpoint != null)
                checkpointMarker.Target = TargetPlayer.NextCheckpoint.transform;
            else
                checkpointMarker.Target = null;
            checkpointMarker.CameraToUse = TargetCamera;

            playerMarkers.RemoveAll(a => a == null); //Remove destroyed markers from the list (Markers are destroyed if the player they're following leaves)
            foreach (Marker m in playerMarkers.ToList())
            {
                m.CameraToUse = TargetCamera;
            }
        }

        // todo: do animation
        bool isRotating = false;
        IEnumerator RotateRing()
        {
            if (isRotating) yield break;

            isRotating = true;

            float time = 0;
            float duration = 0.5f;
            float startAngle = 0;
            float endAngle = 180;

            while (time < duration)
            {
                float angle = Mathf.SmoothStep(startAngle, endAngle, time / duration);
                ring.transform.localRotation = Quaternion.Euler(0, angle, 0);

                time += Time.deltaTime;
                yield return null;
            }

            isRotating = false;
        }


        bool isFlashing = false;

        IEnumerator RingFlash()
        {
            if (isFlashing) yield break;

            isFlashing = true;

            float time = 0;
            // this is horrible why is unity so bad
            while (this.targetPlayer != null && targetPlayer.Ball != null && targetPlayer.Ball.TotalRings == 0)
            {
                // flash the ring text between red and white
                var fade = (Mathf.Sin((time * Mathf.PI) * 2.0f) + 1.0f) / 2.0f;
                this.ringsField.color = Color.LerpUnclamped(Color.white, Color.red, fade);

                time += Time.deltaTime;

                yield return null;
            }

            this.ringsField.color = Color.white;
            this.isFlashing = false;
        }
    }
}