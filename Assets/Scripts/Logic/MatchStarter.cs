using Newtonsoft.Json;
using Sanicball.Data;
using Sanicball.UI;
using SanicballCore;
using System;
using System.Collections;
using UnityEngine;

namespace Sanicball.Logic
{
    public class MatchStarter : MonoBehaviour
    {
        public const string APP_ID = "Sanicball";

        [SerializeField]
        private MatchManager matchManagerPrefab = null;
        [SerializeField]
        private UI.Popup connectingPopupPrefab = null;
        [SerializeField]
        private UI.Popup disconnectedPopupPrefab = null;
        [SerializeField]
        private UI.PopupHandler popupHandler = null;

        //NetClient for when joining online matches
        private WebSocket joiningClient;

        private string error;

        internal void JoinOnlineGame(Guid id)
        {
            var baseUri = new Uri(ActiveData.GameSettings.serverListURL);
            var uri = new UriBuilder(new Uri(baseUri, id.ToString())) { Scheme = baseUri.Scheme == "https" ? "wss" : "ws" };

            StartCoroutine(JoinOnlineGame(uri.Uri));
        }

        public void BeginLocalGame()
        {
            CameraFade.StartAlphaFade(Color.black, false, 1f, true);

            var manager = Instantiate(matchManagerPrefab);
            manager.InitLocalMatch();
        }

        public IEnumerator JoinOnlineGame(Uri endpoint)
        {
            joiningClient = new WebSocket(endpoint);

            popupHandler.OpenPopup(connectingPopupPrefab);
            var activeConnectingPopup = FindObjectOfType<UI.PopupConnecting>();

            yield return StartCoroutine(joiningClient.Connect());

            if (joiningClient.error != null)
            {
                error = joiningClient.error;
            }
            else
            {
                using (var newMessage = new MessageWrapper(MessageTypes.Connect))
                {
                    var info = new ClientInfo(GameVersion.AS_FLOAT, GameVersion.IS_TESTING);
                    newMessage.Writer.Write(JsonConvert.SerializeObject(info));
                    var buffer = newMessage.GetBytes();
                    yield return joiningClient.SendAsync(buffer);
                }

                var done = false;
                byte[] msg;
                while (!done && joiningClient != null)
                {
                    if ((msg = joiningClient.Recv()) != null)
                    {
                        using var message = new MessageWrapper(msg);
                        switch (message.Type)
                        {
                            case MessageTypes.Validate: // should only be recieved if validation fails

                                var valid = message.Reader.ReadBoolean();
                                if (!valid)
                                {
                                    error = message.Reader.ReadString();
                                    done = true;
                                }

                                break;

                            case MessageTypes.Connect:
                                Debug.Log("Connected! Now waiting for match state");
                                activeConnectingPopup.ShowMessage("Receiving match state...");

                                try
                                {
                                    var str = message.Reader.ReadString();
                                    var matchInfo = JsonConvert.DeserializeObject<MatchState>(str);
                                    BeginOnlineGame(matchInfo);
                                }
                                catch (Exception ex)
                                {
                                    error = "Failed to read match message - cannot join server!";
                                    Debug.LogError("Could not read match state, error: " + ex.Message);
                                }

                                done = true;
                                break;

                            case MessageTypes.Disconnect:
                                error = message.Reader.ReadString();
                                done = true;
                                break;
                        }
                    }

                    if (Input.GetKeyDown(KeyCode.Escape))
                    {
                        popupHandler.CloseActivePopup();

                        if (joiningClient != null)
                        {
                            joiningClient.Close();
                            joiningClient = null;
                        }
                    }

                    yield return null;
                }
            }

            popupHandler.CloseActivePopup();

            if (error != null)
            {
                if (joiningClient != null)
                {
                    joiningClient.Close();
                    joiningClient = null;
                }

                popupHandler.OpenPopup(disconnectedPopupPrefab);

                var disconnectedPopup = FindObjectOfType<UI.PopupDisconnected>();
                disconnectedPopup.Reason = error;
            }
        }

        //Called when succesfully connected to a server
        private void BeginOnlineGame(MatchState matchState)
        {
            var manager = Instantiate(matchManagerPrefab);
            manager.InitOnlineMatch(joiningClient, matchState);
        }
    }
}