﻿using Sanicball.Data;
using SanicballCore;
using static SanicballCore.Cerealizer;
using System;
using System.IO;
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
        private UI.PopupHandler popupHandler = null;

        private UI.PopupConnecting activeConnectingPopup;

        //NetClient for when joining online matches
        private WebSocket joiningClient;

        public void BeginLocalGame()
        {
            var manager = Instantiate(matchManagerPrefab);
            manager.InitLocalMatch();
        }

        public IEnumerator JoinOnlineGame(Uri endpoint)
        {
            joiningClient = new WebSocket(endpoint);

            popupHandler.OpenPopup(connectingPopupPrefab);
            activeConnectingPopup = FindObjectOfType<UI.PopupConnecting>();

            yield return StartCoroutine(joiningClient.Connect());

            if (joiningClient.error != null)
            {
                activeConnectingPopup.ShowMessage($"Failed to join! {joiningClient.error}");
                joiningClient = null;
            }
            else
            {
                using (var newMessage = new MessageWrapper(MessageTypes.Connect))
                {
                    ClientInfo info = new ClientInfo(GameVersion.AS_FLOAT, GameVersion.IS_TESTING);
                    newMessage.Writer.Write(Cereal(info));
                    var buffer = newMessage.GetBytes();
                    joiningClient.Send(buffer);
                }
            }
        }

        private bool done = false;

        private void Update()
        {
            if (joiningClient != null)
            {
                byte[] msg;
                while ((msg = joiningClient.Recv()) != null)
                {
                    using (var message = new MessageWrapper(msg))
                    {
                        switch (message.Type)
                        {                                
                            case MessageTypes.Validate: // should only be recieved if validation fails

                                var valid = message.Reader.ReadBoolean();
                                if (!valid)
                                {
                                    var str = message.Reader.ReadString();
                                    activeConnectingPopup.ShowMessage($"Failed to join! {str}");
                                    joiningClient.Close();
                                    joiningClient = null;
                                }

                                break;

                            case MessageTypes.Connect:
                                Debug.Log("Connected! Now waiting for match state");
                                activeConnectingPopup.ShowMessage("Receiving match state...");

                                try
                                {
                                    var matchInfo = UnCereal<MatchState>(ReadAllBytes(message.Reader));
                                    done = true;
                                    BeginOnlineGame(matchInfo);
                                }
                                catch (Exception ex)
                                {
                                    activeConnectingPopup.ShowMessage("Failed to read match message - cannot join server!");
                                    Debug.LogError("Could not read match state, error: " + ex.Message);
                                }

                                break;

                            case MessageTypes.Disconnect:
                                activeConnectingPopup.ShowMessage(message.Reader.ReadString());
                                break;
                        }
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                popupHandler.CloseActivePopup();
                joiningClient.Close();
                joiningClient = null;
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