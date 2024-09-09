using UnityEngine;
using Sanicball.Data;

namespace Sanicball.UI
{
    public class RaceCountdown : MonoBehaviour
    {
        private int countdown = 5;
        private float timer = 4f;

        private float currentFontScale = 1.0f;
        private float targetFontScale = 1.0f;

        [SerializeField]
        private AudioClip countdown1;

        [SerializeField]
        private AudioClip countdown2;

        [SerializeField]
        private UnityEngine.UI.Text countdownLabel;

        public event System.EventHandler OnCountdownFinished;

        ESportMode esport;

        void Start()
        {
            if (ActiveData.ESportsFullyReady)
            {
                esport = Instantiate(ActiveData.ESportsPrefab);
            }
        }

        public void ApplyOffset(float time)
        {
            //Since offset time is very likely below 4 seconds, I don't see a need to compensate for it ever being above.
            //If it ends up happening sometimes, causing players to start the race at different times, this method should be rewritten.
            timer -= time;
        }

        private void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                string countdownText = "";
                //int countdownFontSize = 60;

                float countdownScale = 1.0f;

                countdown--;
                switch (countdown)
                {
                    case 4:
                        countdownText = "READY";
                        countdownScale = 1.0f;
                        UISound.Play(countdown1);
                        break;

                    case 3:
                        countdownText = "STEADY";
                        countdownScale = 1.25f;
                        UISound.Play(countdown1);
                        break;

                    case 2:
                        countdownText = "GET SET";
                        countdownScale = 1.5f;
                        UISound.Play(countdown1);
                        break;

                    case 1:
                        countdownText = "GO FAST";
                        countdownScale = 2.0f;
                        UISound.Play(countdown2);
                        OnCountdownFinished?.Invoke(this, new System.EventArgs());
                        if (esport)
                        {
                            esport.StartTheShit();
                        }
                        break;

                    case 0:
                        Destroy(gameObject);
                        break;
                }

                countdownLabel.text = countdownText;
                targetFontScale = countdownScale;

                timer = 1f;
                if (countdown == 1) timer = 2f;
            }

            currentFontScale = Mathf.Lerp(currentFontScale, targetFontScale, Time.deltaTime * 10);
            //countdownLabel.fontSize = (int)currentFontSize;

            countdownLabel.transform.localScale = new Vector3(currentFontScale, currentFontScale, 1);
        }
    }
}