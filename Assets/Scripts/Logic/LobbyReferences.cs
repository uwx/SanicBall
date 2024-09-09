using UnityEngine;

namespace Sanicball.Logic
{
    public class LobbyReferences : MonoBehaviour
    {
        [SerializeField]
        private UI.LobbyStatusBar statusBar = null;

        [SerializeField]
        private UI.LocalPlayerManager localPlayerManager = null;

        [SerializeField]
        private UI.MatchSettingsPanel matchSettingsPanel = null;

        [SerializeField]
        private LobbyBallSpawner ballSpawner = null;

        [SerializeField]
        private UnityEngine.UI.Text countdownField = null;

        [SerializeField]
        private RectTransform markerContainer = null;

        public static LobbyReferences Active
        {
            get; private set;
        }

        public UI.LobbyStatusBar StatusBar => statusBar;
        public UI.LocalPlayerManager LocalPlayerManager => localPlayerManager;
        public UI.MatchSettingsPanel MatchSettingsPanel => matchSettingsPanel;
        public LobbyBallSpawner BallSpawner => ballSpawner;
        public UnityEngine.UI.Text CountdownField => countdownField;
        public RectTransform MarkerContainer => markerContainer;

        private void Awake()
        {
            Active = this;
            CameraFade.StartAlphaFade(Color.black, true, 1f);
        }
    }
}
