using Microsoft.Extensions.Logging;
//using System.Threading.Tasks;
using Newtonsoft.Json;
using SanicballCore.MatchMessages;
using SanicballServer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SanicballCore.Server
{
    public class LogArgs : EventArgs
    {
        public LogEntry Entry { get; }

        public LogArgs(LogEntry entry)
        {
            Entry = entry;
        }
    }

    public struct LogEntry
    {
        public DateTime Timestamp { get; }
        public string Message { get; }
        public LogLevel Type { get; }

        public LogEntry(DateTime timestamp, string message, LogLevel type)
        {
            Timestamp = timestamp;
            Message = message;
            Type = type;
        }
    }

    public class Room : IAsyncDisposable
    {
        public const string CONFIG_FILENAME = "ServerConfig.json";
        private const string SETTINGS_FILENAME = "MatchSettings.json";
        private const int TICKRATE = 50;
        private const int STAGE_COUNT = 5; //Hardcoded stage count for now.. can't receive the actual count since it's part of a Unity prefab.
        private readonly CharacterTier[] characterTiers = new[] { //Hardcoded character tiers, same reason
            CharacterTier.Normal,       //Sanic
            CharacterTier.Normal,       //Knackles
            CharacterTier.Normal,       //Taels
            CharacterTier.Normal,       //Ame
            CharacterTier.Normal,       //Shedew
            CharacterTier.Normal,       //Roge
            CharacterTier.Normal,       //Asspio
            CharacterTier.Odd,          //Big
            CharacterTier.Odd,          //Aggmen
            CharacterTier.Odd,          //Chermy
            CharacterTier.Normal,       //Sulver
            CharacterTier.Normal,       //Bloze
            CharacterTier.Normal,       //Vactor
            CharacterTier.Hyperspeed,   //Super Sanic
            CharacterTier.Odd,       //Metal Sanic
            CharacterTier.Odd,          //Ogre
        };

        public event EventHandler<LogArgs> OnLog;

        //Server utilities
        private List<LogEntry> log = new List<LogEntry>();
        private Dictionary<string, CommandHandler> _commandHandlers = new Dictionary<string, CommandHandler>();
        private Dictionary<string, string> _commandHelp = new Dictionary<string, string>();
        private ConcurrentQueue<Command> _commandQueue;
        private Random _random = new Random();

        //Server state
        private bool _running;
        private bool _debugMode;
        private List<ServClient> _clients = new List<ServClient>();
        private List<ServPlayer> _players = new List<ServPlayer>();
        private MatchSettings _matchSettings;
        private string _motd;
        private bool _inRace;

        private ConcurrentDictionary<Guid, WebSocketWrapper> connectedClients
            = new ConcurrentDictionary<Guid, WebSocketWrapper>();

        public readonly RoomConfig Config;

        private readonly ILogger _logger;

        private CancellationTokenSource _source;
        private CancellationToken _token;

        public int ConnectedClients => _clients.Count;
        public bool InGame => _inRace;

        public Guid Id { get; }

        #region Timers

        //Server browser ping timer
        private Stopwatch serverListPingTimer = new Stopwatch();
        private const float SERVER_BROWSER_PING_INTERVAL = 600;

        //Timer for starting a match by all players being ready
        private Stopwatch lobbyTimer = new Stopwatch();
        private const float LOBBY_MATCH_START_TIME = 3;

        //Timer for starting a match automatically
        private Stopwatch autoStartTimer = new Stopwatch();

        //Timeout for clients loading stage
        private Stopwatch stageLoadingTimeoutTimer = new Stopwatch();
        private const float STAGE_LOADING_TIMEOUT = 30;

        // Timeout for heartbeat
        private Stopwatch heartbeatTimer = new Stopwatch();
        private const float HEARTBEAT_TIMEOUT = 5;

        //Timer for going back to lobby at the end of a race
        private Stopwatch backToLobbyTimer = new Stopwatch();

        #endregion Timers

        public bool Running { get { return _running; } }

        public Room(Guid id, RoomConfig config, ILogger logger)
        {
            Id = id;
            Config = config;

            _commandQueue = new ConcurrentQueue<Command>();
            _logger = logger;

            #region Command handlers

            AddCommandHandler("help",
            "help help help",
            cmd =>
            {
                var builder = new StringBuilder();
                if (cmd.Content.Trim() == string.Empty)
                {
                    builder.AppendLine("Available commands:");
                    foreach (var name in _commandHandlers.Keys)
                    {
                        builder.AppendLine(name);
                    }
                    builder.AppendLine("Use 'help [command name]' for a command decription");
                }
                else
                {
                    string help;
                    if (_commandHelp.TryGetValue(cmd.Content.Trim(), out help))
                    {
                        builder.AppendLine(help);
                    }
                }

                return builder.ToString();
            });
            AddCommandHandler("toggleDebug",
            "Debug mode displays a ton of extra technical output. Useful if you suspect something is wrong with the server.",
            cmd =>
            {
                _debugMode = !_debugMode;
                return ("Debug mode set to " + _debugMode);
            });
            //AddCommandHandler("stop",
            //"Stops the server. I recommend stopping it this way - any other probably won't save the server log.",
            //cmd =>
            //{
            //    _running = false;
            //});
            AddCommandHandler("say",
            "Chat to clients on the server.",
            cmd =>
            {
                if (cmd.Content.Trim() == string.Empty)
                {
                    return ("Usage: say [message]");
                }
                else
                {
                    SendToAll(new ChatMessage("Server", ChatMessageType.System, cmd.Content));
                    return ("Chat message sent");
                }
            });
            AddCommandHandler("clients",
            "Displays a list of connected clients. (A client is a technical term another Sanicball instance)",
            cmd =>
            {
                var builder = new StringBuilder();
                builder.AppendLine(_clients.Count + " connected client(s)");
                foreach (var client in _clients)
                {
                    builder.AppendLine(client.Name);
                }
                return builder.ToString();
            });
            AddCommandHandler("players",
            "Displays a list of active players. Clients can have multiple players for splitscreen, or none at all to spectate.",
            cmd =>
            {
                return (_clients.Count + " players(s) in match");
            });
            AddCommandHandler("kick",
            "Kicks a player from the server. Of course he could just re-join, but hopefully he'll get the message.",
            cmd =>
            {
                if (cmd.Content.Trim() == string.Empty)
                {
                    return ("Usage: kick [client name/part of name]");
                }
                else
                {
                    var matching = SearchClients(cmd.Content);
                    if (matching.Count == 0)
                    {
                        return ("No clients match your search.");
                    }
                    else if (matching.Count == 1)
                    {
                        Kick(matching[0], "Kicked by server");
                        return $"Kicked {matching[0].Name}";
                    }
                }

                return "Multiple users match your search!!";
            });
            AddCommandHandler("returnToLobby",
            "Force exits any ongoing race.",
            cmd =>
            {
                ReturnToLobbyAsync();
                return "Done";
            });
            AddCommandHandler("forceStart",
            "Force starts a race when in the lobby. Use this carefully, players may not be ready for racing",
            cmd =>
            {
                if (_inRace == false)
                {
                    LoadRaceAsync();
                    return "The race has been forcefully started.";
                }
                else
                {
                    return ("Race can only be force started in the lobby.");
                }
            });
            //AddCommandHandler("showSettings",
            //"Shows match settings. Settings like stage rotation are just shown as a number (Example: if StageRotationMode shows '1', it means 'Sequenced')",
            //cmd =>
            //{
            //    Log(JsonConvert.SerializeObject(_matchSettings, Formatting.Indented));
            //});

            AddCommandHandler("setStage",
            "Sets the stage by index. 0 = Green Hill, 1 = Flame Core, 2 = Ice Mountain, 3 = Rainbow Road, 4 = Dusty Desert",
            cmd =>
            {
                int inputInt;
                if (int.TryParse(cmd.Content, out inputInt) && inputInt >= 0 && inputInt < STAGE_COUNT)
                {
                    _matchSettings.StageId = inputInt;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    return ("Stage set to " + inputInt);
                }
                else
                {
                    return ("Usage: setStage [0-" + (STAGE_COUNT - 1) + "]");
                }
            });
            AddCommandHandler("setLaps",
            "Sets the number of laps per race. Laps are pretty long so 2 or 3 is recommended.",
            cmd =>
            {
                int inputInt;
                if (int.TryParse(cmd.Content, out inputInt) && inputInt > 0)
                {
                    _matchSettings.Laps = inputInt;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    return ("Lap count set to " + inputInt);
                }
                else
                {
                    return ("Usage: setLaps [>0]");
                }
            });
            AddCommandHandler("setAutoStartTime",
            "Sets the time required (in seconds) with enough players in the lobby before a race is automatically started.",
            cmd =>
            {
                int inputInt;
                if (int.TryParse(cmd.Content, out inputInt) && inputInt > 0)
                {
                    _matchSettings.AutoStartTime = inputInt;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    return ("Match auto start time set to " + inputInt);
                }
                else
                {
                    return ("Usage: setAutoStartTime [>0]");
                }
            });
            AddCommandHandler("setAutoStartMinPlayers",
            "Sets the minimum amount of players needed in the lobby before the auto start countdown begins.",
            cmd =>
            {
                int inputInt;
                if (int.TryParse(cmd.Content, out inputInt) && inputInt > 0)
                {
                    _matchSettings.AutoStartMinPlayers = inputInt;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    return ("Match auto start minimum players set to " + inputInt);
                }
                else
                {
                    return ("Usage: setAutoStartMinPlayers [>0]");
                }
            });
            AddCommandHandler("setStageRotationMode",
            "If not set to None, stages will change either randomly or in sequence every time the server returns to lobby.",
            cmd =>
            {
                try
                {
                    var rotMode = (StageRotationMode)Enum.Parse(typeof(StageRotationMode), cmd.Content);
                    _matchSettings.StageRotationMode = rotMode;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    return ("Stage rotation mode set to " + rotMode);
                }
                catch (Exception)
                {
                    var modes = Enum.GetNames(typeof(StageRotationMode));
                    var modesStr = string.Join("|", modes);
                    return ("Usage: setStageRotationMode [" + modesStr + "]");
                }
            });
            AddCommandHandler("setAllowedTiers",
            "Controls what ball tiers players can use.",
            cmd =>
            {
                try
                {
                    var tiers = (AllowedTiers)Enum.Parse(typeof(AllowedTiers), cmd.Content);
                    _matchSettings.AllowedTiers = tiers;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    CorrectPlayerTiersAsync();
                    Broadcast(GetAllowedTiersText());
                    return ("Allowed tiers set to " + tiers);
                }
                catch (Exception)
                {
                    var modes = Enum.GetNames(typeof(AllowedTiers));
                    var modesStr = string.Join("|", modes);
                    return ("Usage: setAllowedTiers [" + modesStr + "]");
                }
            });
            AddCommandHandler("setTierRotationMode",
            "If not None, allowed ball tiers will change (To either NormalOnly, OddOnly or HyperspeedOnly) each time the server returns to lobby. WeightedRandom has a 10/14 chance of picking NormalOnly, 3/14 of OddOnly and 1/14 of HyperspeedOnly.",
            cmd =>
            {
                try
                {
                    var mode = (TierRotationMode)Enum.Parse(typeof(TierRotationMode), cmd.Content);
                    _matchSettings.TierRotationMode = mode;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    return ("Tier rotation mode set to " + mode);
                }
                catch (Exception)
                {
                    var modes = Enum.GetNames(typeof(TierRotationMode));
                    var modesStr = string.Join("|", modes);
                    return ("Usage: setTierRotationMode [" + modesStr + "]");
                }
            });
            AddCommandHandler("setVoteRatio",
            "Sets the fraction of players required to select 'return to lobby' before the server returns to lobby. 1.0, the default, requires all players. Something like 0.8 would be good for a very big server.",
            cmd =>
            {
                float newVoteRatio;
                if (float.TryParse(cmd.Content, out newVoteRatio) && newVoteRatio >= 0f && newVoteRatio <= 1f)
                {
                    _matchSettings.VoteRatio = newVoteRatio;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    return ("Match vote ratio set to " + newVoteRatio);
                }
                else
                {
                    return ("Usage: setVoteRatio [0.0-1.0]");
                }
            });
            AddCommandHandler("setDisqualificationTime",
            "Sets the time a player needs to loiter around without passing any checkpoints before they are disqualified from a race. If too low, players might get DQ'd just for being slow. 0 disables disqualifying.",
            cmd =>
            {
                int inputInt;
                if (int.TryParse(cmd.Content, out inputInt) && inputInt >= 0)
                {
                    _matchSettings.DisqualificationTime = inputInt;
                    SaveMatchSettings();
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                    return ("Disqualification time set to " + inputInt);
                }
                else
                {
                    return ("Usage: setDisqualificationTime [>=0]");
                }
            });

            #endregion Command handlers

        }

        public async Task StartAsync(CancellationToken token)
        {
            _matchSettings = MatchSettings.CreateDefault();
            _source = CancellationTokenSource.CreateLinkedTokenSource(token);
            _token = _source.Token;

            LoadMOTD();

            //Welcome message
            Log("Welcome! Type 'help' for a list of commands. Type 'stop' to shut down the server.");

            _running = true;

#if DEBUG
            _debugMode = true;
#endif
            await MessageLoop();
        }

        // TODO: Random MOTD
        private void LoadMOTD()
        {
            _motd = "Hello, world!";
        }

        public async Task ConnectClientAsync(WebSocket socket, CancellationToken token)
        {
            var wrapper = new WebSocketWrapper(socket, CancellationTokenSource.CreateLinkedTokenSource(token, _token).Token);
            Log($"Received a socket connection from {socket}, requesting validation.");

            connectedClients[wrapper.Id] = wrapper;
            await wrapper.ReceiveLoop();
        }

        private async Task MessageLoop()
        {
            long startTimestamp = 0;
            long endTimestamp = 0;
            long tickDuration = Stopwatch.Frequency / TICKRATE;

            heartbeatTimer.Start();

            using (var dest = new MemoryStream())
            using (var writer = new BinaryWriter(dest, Encoding.UTF8))
            {
                while (_running)
                {
                    if (_token.IsCancellationRequested)
                        break;

                    endTimestamp = Stopwatch.GetTimestamp();

                    var time = TimeSpan.FromSeconds((double)(Math.Max(0, tickDuration - (endTimestamp - startTimestamp)) / (double)Stopwatch.Frequency));
                    await Task.Delay(time);

                    startTimestamp = Stopwatch.GetTimestamp();

                    //Check server browser ping timer
                    if (serverListPingTimer.IsRunning)
                    {
                        if (serverListPingTimer.Elapsed.TotalSeconds >= SERVER_BROWSER_PING_INTERVAL)
                        {
                            serverListPingTimer.Reset();
                            serverListPingTimer.Start();
                        }
                    }

                    //Check lobby timer
                    if (lobbyTimer.IsRunning)
                    {
                        if (lobbyTimer.Elapsed.TotalSeconds >= LOBBY_MATCH_START_TIME)
                        {
                            Log("The race has been started by all players being ready.", LogLevel.Debug);
                            LoadRaceAsync();
                        }
                    }

                    //Check stage loading timer
                    if (stageLoadingTimeoutTimer.IsRunning)
                    {
                        if (stageLoadingTimeoutTimer.Elapsed.TotalSeconds >= STAGE_LOADING_TIMEOUT)
                        {
                            SendToAll(new StartRaceMessage(), 1000);
                            stageLoadingTimeoutTimer.Reset();

                            foreach (var c in _clients.Where(a => a.CurrentlyLoadingStage))
                            {
                                await Kick(c, "Took too long to load the race");
                            }
                        }
                    }


                    //Check auto start timer
                    if (autoStartTimer.IsRunning)
                    {
                        if (autoStartTimer.Elapsed.TotalSeconds >= _matchSettings.AutoStartTime)
                        {
                            Log("The race has been automatically started.", LogLevel.Debug);
                            LoadRaceAsync();
                        }
                    }

                    //Check back to lobby timer
                    if (backToLobbyTimer.IsRunning)
                    {
                        if (backToLobbyTimer.Elapsed.TotalSeconds >= _matchSettings.AutoReturnTime)
                        {
                            ReturnToLobbyAsync();
                            backToLobbyTimer.Reset();
                        }
                    }

                    //Check racing timeout timers
                    foreach (var p in _players)
                    {
                        if (_matchSettings.DisqualificationTime > 0)
                        {
                            if (!p.TimeoutMessageSent && p.RacingTimeout.Elapsed.TotalSeconds > _matchSettings.DisqualificationTime / 2.0f)
                            {
                                SendToAll(new RaceTimeoutMessage(p.ClientGuid, p.CtrlType, _matchSettings.DisqualificationTime / 2.0f));
                                p.TimeoutMessageSent = true;
                            }
                            if (p.RacingTimeout.Elapsed.TotalSeconds > _matchSettings.DisqualificationTime)
                            {
                                Log("A player was too slow to race and has been disqualified.");

                                FinishRaceAsync(p);
                                SendToAll(new DoneRacingMessage(p.ClientGuid, p.CtrlType, 0, true), 100);
                            }
                        }
                    }

                    //Check command queue
                    while (_commandQueue.TryDequeue(out var cmd))
                    {
                        var def = _clients.FirstOrDefault(c => c.IsDefault);

                        string response;
                        CommandHandler handler;
                        if (_commandHandlers.TryGetValue(cmd.Name, out handler))
                        {
                            response = handler(cmd);
                        }
                        else
                        {
                            response = "Command '" + cmd.Name + "' not found.";
                        }

                        Whisper(def, response);
                    }

                    if (heartbeatTimer.Elapsed.TotalSeconds >= HEARTBEAT_TIMEOUT)
                    {
                        var availableClients = connectedClients.Values.Where(c => c.HeartbeatTimestamp == 0);
                        if (availableClients.Any())
                        {
                            var wrapper = new MessageWrapper(MessageTypes.Heartbeat);
                            wrapper.Writer.Write(DateTime.UtcNow.Ticks);

                            Log($"Sending heartbeat to {availableClients.Count()} clients.", LogLevel.Debug);

                            foreach (var client in availableClients)
                            {
                                client.HeartbeatTimestamp = Stopwatch.GetTimestamp();
                                client.Send(wrapper, 100);
                            }
                        }

                        heartbeatTimer.Restart();
                    }


                    foreach (var client in connectedClients.Where(c => c.Value.TicksSinceLastMessage > 1000).ToArray())
                    {
                        Log($"Client {client.Key} has missed over since last heartbeat 1000 ticks, disconnecting...", LogLevel.Warning);
                        DisconnectPlayer("You're too slow!", client.Key);
                    }

                    //Check network message queue
                    foreach (var client in connectedClients)
                    {
                        if (!stageLoadingTimeoutTimer.IsRunning && client.Value.HeartbeatTimestamp != 0)
                            client.Value.TicksSinceLastMessage++;

                        while (client.Value.Dequeue(out var msg))
                        {
                            try
                            {
                                switch (msg.Type)
                                {
                                    case MessageTypes.Connect:

                                        ClientInfo clientInfo = null;
                                        try
                                        {
                                            clientInfo = JsonConvert.DeserializeObject<ClientInfo>(msg.Reader.ReadString());
                                        }
                                        catch (JsonException ex)
                                        {
                                            Log("Error reading client connection approval: \"" + ex.Message + "\". Client rejected.", LogLevel.Warning);

                                            writer.Write(false);
                                            writer.Write("Invalid client info! You are likely using a different game version than the server.");
                                            client.Value.Send(MessageTypes.Validate, writer, dest);

                                            Log($"Refused to validate {client.Key}");
                                            break;
                                        }

                                        if (clientInfo.Version != GameVersion.AS_FLOAT || clientInfo.IsTesting != GameVersion.IS_TESTING)
                                        {
                                            writer.Write(false);
                                            writer.Write("Invalid client info! You are likely using a different game version than the server.");
                                            client.Value.Send(MessageTypes.Validate, writer, dest);

                                            Log($"Refused to validate {client.Key}");
                                            break;
                                        }

                                        float autoStartTimeLeft = 0;
                                        if (autoStartTimer.IsRunning)
                                        {
                                            autoStartTimeLeft = _matchSettings.AutoStartTime - (float)autoStartTimer.Elapsed.TotalSeconds;
                                        }
                                        var clientStates = new List<MatchClientState>();
                                        foreach (var c in _clients)
                                        {
                                            clientStates.Add(new MatchClientState(c.Guid, c.Name));
                                        }
                                        var playerStates = new List<MatchPlayerState>();
                                        foreach (var p in _players)
                                        {
                                            playerStates.Add(new MatchPlayerState(p.ClientGuid, p.CtrlType, p.ReadyToRace, p.CharacterId));
                                        }

                                        var state = new MatchState(clientStates, playerStates, _matchSettings, _inRace, autoStartTimeLeft);
                                        var str = JsonConvert.SerializeObject(state);
                                        writer.Write(str);

                                        client.Value.Send(MessageTypes.Connect, writer, dest);

                                        Log("Sent match state to newly connected client", LogLevel.Debug);

                                        break;
                                    case MessageTypes.Disconnect:
                                        var statusMsg = "Client disconnected";
                                        try { statusMsg = msg?.Reader?.ReadString() ?? statusMsg; } catch { }
                                        var connectionId = msg.Source;

                                        DisconnectPlayer(statusMsg, connectionId);

                                        break;
                                    case MessageTypes.PlayerMovement:
                                        var recipients = _clients.Where(a => a.Connection.Id != msg.Source).ToList();
                                        if (recipients.Count > 0)
                                        {
                                            foreach (var item in recipients)
                                            {
                                                item.Connection.Send(msg, -10);
                                            }
                                        }

                                        break;
                                    case MessageTypes.Heartbeat:
                                        client.Value.Ping = TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - client.Value.HeartbeatTimestamp) / (double)Stopwatch.Frequency).TotalMilliseconds;
                                        Log($"Client {client.Key} heartbeat after {client.Value.Ping}ms", LogLevel.Debug);
                                        client.Value.TicksSinceLastMessage = 0;
                                        client.Value.HeartbeatTimestamp = 0;
                                        break;
                                    case MessageTypes.Match:
                                        double timestamp = msg.Reader.ReadInt64();
                                        MatchMessage matchMessage = null;
                                        try
                                        {
                                            matchMessage = JsonConvert.DeserializeObject<MatchMessage>(msg.Reader.ReadString(), new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.All });
                                        }
                                        catch (JsonException ex)
                                        {
                                            Log("Failed to deserialize received match message. Error description: " + ex.Message, LogLevel.Warning);
                                            continue; //Skip to next message in queue
                                        }

                                        if (matchMessage is ClientJoinedMessage)
                                        {
                                            ClientJoinedAsync(msg, matchMessage);
                                        }

                                        if (matchMessage is PlayerJoinedMessage)
                                        {
                                            PlayerJoinedAsync(msg, matchMessage);
                                        }

                                        if (matchMessage is PlayerLeftMessage)
                                        {
                                            PlayerLeftAsync(msg, matchMessage);
                                        }

                                        if (matchMessage is CharacterChangedMessage)
                                        {
                                            CharacterChangedAsync(msg, matchMessage);
                                        }

                                        if (matchMessage is ChangedReadyMessage)
                                        {
                                            ChangedReadyAsync(msg, matchMessage);
                                        }

                                        if (matchMessage is SettingsChangedMessage)
                                        {
                                            //var castedMsg = (SettingsChangedMessage)matchMessage;
                                            //matchSettings = castedMsg.NewMatchSettings;
                                            //SendToAll(matchMessage);

                                            Log("A player tried to change match settings", LogLevel.Debug);
                                        }

                                        if (matchMessage is StartRaceMessage)
                                        {
                                            StartRace(msg);
                                        }

                                        if (matchMessage is ChatMessage)
                                        {
                                            ChatMessageAsync(msg, matchMessage);
                                        }

                                        if (matchMessage is LoadLobbyMessage)
                                        {
                                            LoadLobbyAsync(msg);
                                        }

                                        if (matchMessage is CheckpointPassedMessage)
                                        {
                                            CheckpointPassedAsync(matchMessage);
                                        }

                                        if (matchMessage is DoneRacingMessage)
                                        {
                                            DoneRacingAsync(matchMessage);
                                        }

                                        if (matchMessage is PlayerRespawnMessage respawnMessage)
                                        {
                                            SendToAll(respawnMessage);
                                        }

                                        break;

                                    default:
                                        Log("Received data message of unknown type", LogLevel.Debug);
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"An error occurred processing a message from {client.Key}");
                            }
                            finally
                            {
                                dest.SetLength(0);
                                dest.Seek(0, SeekOrigin.Begin);
                            }
                        }
                    }
                }
            }
        }

        private void DisconnectPlayer(string statusMsg, Guid connectionId)
        {
            var associatedClient = _clients.FirstOrDefault(a => a.Connection.Id == connectionId);
            if (associatedClient != null)
            {
                Log($"Disconnected {associatedClient.Name}");

                //Remove all players created by this client
                _players.RemoveAll(a => a.ClientGuid == associatedClient.Guid);

                //If no players are left and we're in a race, return to lobby
                if (_players.Count == 0 && _inRace)
                {
                    Log("No players left in race!", LogLevel.Debug);
                    ReturnToLobbyAsync();
                }

                //If there are now less players than AutoStartMinPlayers, stop the auto start timer
                if (_players.Count < _matchSettings.AutoStartMinPlayers && autoStartTimer.IsRunning)
                {
                    Log("Too few players, match auto start timer stopped", LogLevel.Debug);
                    StopAutoStartTimerAsync();
                }

                //Remove the client
                _clients.Remove(associatedClient);

                //Tell connected clients to remove the client+players
                SendToAll(new ClientLeftMessage(associatedClient.Guid), 50);
                Broadcast(associatedClient.Name + " has left the match (" + statusMsg + ")");

                try
                {
                    _ = associatedClient.Connection.DisconnectAsync(statusMsg);
                }
                catch { }

                if (associatedClient.IsDefault)
                {
                    var newClient = _clients.FirstOrDefault();
                    if (newClient != null)
                    {
                        newClient.IsDefault = true;
                        Whisper(newClient, "You are now the server administrator!");
                    }
                }
            }
            else
            {
                Log("Unknown client disconnected (Client was most likely not done connecting)", LogLevel.Debug);
            }

            connectedClients.Remove(connectionId, out _);
        }

        private void ClientJoinedAsync(MessageWrapper msg, MatchMessage matchMessage)
        {
            var castedMsg = (ClientJoinedMessage)matchMessage;

            var newClient = new ServClient(castedMsg.ClientGuid, castedMsg.ClientName, connectedClients[msg.Source], _clients.Count == 0);
            _clients.Add(newClient);

            Broadcast(castedMsg.ClientName + " has joined the match");

            if (_motd != null)
            {
                Whisper(newClient, "Server's message of the day:");
                Whisper(newClient, _motd);
            }
            else
            {
                Whisper(newClient, "Welcome to the server!");
            }

            if (newClient.IsDefault)
            {
                Whisper(newClient, "You are currently the server administrator!");
            }

            Whisper(newClient, GetAllowedTiersText());
            SendToAll(matchMessage);
        }

        private void PlayerJoinedAsync(MessageWrapper msg, MatchMessage matchMessage)
        {
            var castedMsg = (PlayerJoinedMessage)matchMessage;

            //Check if the message was sent from the same client it wants to act for
            var client = _clients.FirstOrDefault(a => a.Connection.Id == msg.Source);

            if (client == null || castedMsg.ClientGuid != client.Guid)
            {
                Log("Received PlayerJoinedMessage with invalid ClientGuid property", LogLevel.Warning);
            }
            else
            {
                if (VerifyCharacterTier(castedMsg.InitialCharacter))
                {
                    var player = new ServPlayer(castedMsg.ClientGuid, castedMsg.CtrlType, castedMsg.InitialCharacter);
                    _players.Add(player);
                    Log("Player " + client.Name + " (" + castedMsg.CtrlType + ") joined", LogLevel.Debug);
                    SendToAll(matchMessage);

                    if (_players.Count >= _matchSettings.AutoStartMinPlayers && !autoStartTimer.IsRunning && _matchSettings.AutoStartTime > 0)
                    {
                        Log("Match will auto start in " + _matchSettings.AutoStartTime + " seconds.", LogLevel.Debug);
                        StartAutoStartTimerAsync();
                    }
                }
                else
                {
                    Whisper(client, "You cannot join with this character - " + GetAllowedTiersText());
                }

            }
        }

        private void PlayerLeftAsync(MessageWrapper msg, MatchMessage matchMessage)
        {
            var castedMsg = (PlayerLeftMessage)matchMessage;

            //Check if the message was sent from the same client it wants to act for
            var client = _clients.FirstOrDefault(a => a.Connection.Id == msg.Source);
            if (client == null || castedMsg.ClientGuid != client.Guid)
            {
                Log("Received PlayerLeftMessage with invalid ClientGuid property", LogLevel.Warning);
            }
            else
            {
                var player = _players.FirstOrDefault(a => a.ClientGuid == castedMsg.ClientGuid && a.CtrlType == castedMsg.CtrlType);
                _players.Remove(player);
                Log("Player " + client.Name + " (" + castedMsg.CtrlType + ") left", LogLevel.Debug);
                SendToAll(matchMessage);

                if (_players.Count < _matchSettings.AutoStartMinPlayers && autoStartTimer.IsRunning)
                {
                    Log("Too few players, match auto start timer stopped", LogLevel.Debug);
                    StopAutoStartTimerAsync();
                }
            }
        }

        private void CharacterChangedAsync(MessageWrapper msg, MatchMessage matchMessage)
        {
            var castedMsg = (CharacterChangedMessage)matchMessage;

            //Check if the message was sent from the same client it wants to act for
            var client = _clients.FirstOrDefault(a => a.Connection.Id == msg.Source);
            if (client == null || client.Guid != castedMsg.ClientGuid)
            {
                Log("Received CharacterChangedMessage with invalid ClientGuid property", LogLevel.Warning);
            }
            else
            {
                var player = _players.FirstOrDefault(a => a.ClientGuid == castedMsg.ClientGuid && a.CtrlType == castedMsg.CtrlType);
                if (player != null)
                {
                    if (VerifyCharacterTier(castedMsg.NewCharacter))
                    {
                        player.CharacterId = castedMsg.NewCharacter;
                        Log("Player " + client.Name + " (" + castedMsg.CtrlType + ") set character to " + castedMsg.NewCharacter, LogLevel.Debug);
                        SendToAll(matchMessage);
                    }
                    else
                    {
                        Whisper(client, "You can't use this character - " + GetAllowedTiersText());
                        Log("Player " + client.Name + " (" + castedMsg.CtrlType + ") tried to set character to " + castedMsg.NewCharacter + " but the character's tier is not allowed", LogLevel.Debug);
                    }
                }
            }
        }

        private void ChangedReadyAsync(MessageWrapper msg, MatchMessage matchMessage)
        {
            var castedMsg = (ChangedReadyMessage)matchMessage;

            //Check if the message was sent from the same client it wants to act for
            var client = _clients.FirstOrDefault(a => a.Connection.Id == msg.Source);
            if (client == null || client.Guid != castedMsg.ClientGuid)
            {
                Log("Received ChangeReadyMessage with invalid ClientGuid property", LogLevel.Warning);
            }
            else
            {
                var player = _players.FirstOrDefault(a => a.ClientGuid == castedMsg.ClientGuid && a.CtrlType == castedMsg.CtrlType);
                if (player != null)
                {
                    var index = _players.IndexOf(player);
                    _players[index].ReadyToRace = castedMsg.Ready;
                }
                Log("Player " + client.Name + " (" + castedMsg.CtrlType + ") set ready to " + castedMsg.Ready, LogLevel.Debug);

                //Start lobby timer if all players are ready - otherwise reset it if it's running
                var allPlayersReady = _players.All(a => a.ReadyToRace);
                if (allPlayersReady)
                {
                    lobbyTimer.Start();
                    Log("All players ready, timer started", LogLevel.Debug);
                }
                else
                {
                    if (lobbyTimer.IsRunning)
                    {
                        lobbyTimer.Reset();
                        Log("Not all players are ready, timer stopped", LogLevel.Debug);
                    }
                }

                SendToAll(matchMessage);
            }
        }

        private void StartRace(MessageWrapper msg)
        {
            var clientsLoadingStage = _clients.Count(a => a.CurrentlyLoadingStage);
            if (clientsLoadingStage > 0)
            {
                var client = _clients.FirstOrDefault(a => a.Connection.Id == msg.Source);
                client.CurrentlyLoadingStage = false;
                clientsLoadingStage--;
                if (clientsLoadingStage > 0)
                {
                    Log("Waiting for " + clientsLoadingStage + " client(s) to load", LogLevel.Debug);
                }
                else
                {
                    Log("Starting race!");
                    SendToAll(new StartRaceMessage(), 1000);
                    stageLoadingTimeoutTimer.Reset();
                    //Indicate that all currently active players are racing
                    _players.ForEach(a =>
                    {
                        a.CurrentlyRacing = true;
                        a.RacingTimeout.Start();
                    });
                }
            }
        }

        private void ChatMessageAsync(MessageWrapper msg, MatchMessage matchMessage)
        {
            var chatMsg = (ChatMessage)matchMessage;
            Log(string.Format("[{0}] {1}: {2}", chatMsg.Type, chatMsg.From, chatMsg.Text));

            if (chatMsg.Text.ToLower().Contains("shrek") && VerifyCharacterTier(15))
            {
                var client = _clients.FirstOrDefault(a => a.Connection.Id == msg.Source);
                var playersFromClient = _players.Where(a => a.ClientGuid == client.Guid).ToArray();
                foreach (var p in playersFromClient)
                {
                    p.CharacterId = 15;
                    SendToAll(new CharacterChangedMessage(p.ClientGuid, p.CtrlType, 15));
                }
            }

            if (chatMsg.Text.StartsWith("/") && chatMsg.Text.Length > 1)
            {
                var servClient = _clients.FirstOrDefault(u => u.Name == chatMsg.From);
                if (servClient != null)
                {
                    if (servClient.IsDefault)
                    {
                        _commandQueue.Enqueue(new Command(chatMsg.Text.Substring(1)));
                    }
                    else
                    {
                        Whisper(servClient, "Hey! You can't do that!");
                    }
                }
            }
            else
            {
                SendToAll(matchMessage);
            }
        }

        private void LoadLobbyAsync(MessageWrapper msg)
        {
            var client = _clients.FirstOrDefault(a => a.Connection.Id == msg.Source);
            if (!client.WantsToReturnToLobby)
            {
                client.WantsToReturnToLobby = true;

                var clientsRequiredToReturn = (int)(_clients.Count * _matchSettings.VoteRatio);

                if (_clients.Count(a => a.WantsToReturnToLobby) >= clientsRequiredToReturn)
                {
                    Broadcast("Returning to lobby by user vote.");
                    ReturnToLobbyAsync();
                }
                else
                {
                    var clientsNeeded = clientsRequiredToReturn - _clients.Count(a => a.WantsToReturnToLobby);
                    Broadcast(client.Name + " wants to return to the lobby. " + clientsNeeded + " more vote(s) needed.");
                }
            }
        }

        private void CheckpointPassedAsync(MatchMessage matchMessage)
        {
            var castedMsg = (CheckpointPassedMessage)matchMessage;

            var player = _players.FirstOrDefault(a => a.ClientGuid == castedMsg.ClientGuid && a.CtrlType == castedMsg.CtrlType);
            if (player != null)
            {
                //As long as all players are racing, timeouts should be reset.
                if (_players.All(a => a.CurrentlyRacing))
                {
                    player.RacingTimeout.Reset();
                    player.RacingTimeout.Start();
                    if (player.TimeoutMessageSent)
                    {
                        player.TimeoutMessageSent = false;
                        SendToAll(new RaceTimeoutMessage(player.ClientGuid, player.CtrlType, 0));
                    }
                }
                SendToAll(matchMessage);
            }
            else
            {
                Log("Received CheckpointPassedMessage for invalid player", LogLevel.Debug);
            }
        }

        private void DoneRacingAsync(MatchMessage matchMessage)
        {
            var castedMsg = (DoneRacingMessage)matchMessage;
            var player = _players.FirstOrDefault(a => a.ClientGuid == castedMsg.ClientGuid && a.CtrlType == castedMsg.CtrlType);
            if (player != null)
            {
                FinishRaceAsync(player);
            }
            SendToAll(matchMessage);
        }

        #region Gameplay methods

        private void LoadRaceAsync()
        {
            lobbyTimer.Reset();
            StopAutoStartTimerAsync();
            SendToAll(new LoadRaceMessage(), 100);
            _inRace = true;
            //Set ready to false for all players
            _players.ForEach(a => a.ReadyToRace = false);
            //Wait for clients to load the stage
            _clients.ForEach(a => a.CurrentlyLoadingStage = true);
            //Start timeout timer
            stageLoadingTimeoutTimer.Start();
        }

        private void ReturnToLobbyAsync()
        {
            if (_inRace)
            {
                Log("Returned to lobby");
                _inRace = false;
                SendToAll(new LoadLobbyMessage(), 100);

                backToLobbyTimer.Reset();

                _players.ForEach(a =>
                {
                    a.CurrentlyRacing = false;
                    a.RacingTimeout.Reset();
                    a.TimeoutMessageSent = false;
                });
                _clients.ForEach(a => a.WantsToReturnToLobby = false);

                var matchSettingsChanged = false;

                //Stage rotation
                switch (_matchSettings.StageRotationMode)
                {
                    case StageRotationMode.Random:
                        Log("Picking random stage", LogLevel.Debug);
                        var newStage = _matchSettings.StageId;

                        while (newStage == _matchSettings.StageId)
                            newStage = _random.Next(STAGE_COUNT);

                        _matchSettings.StageId = newStage;
                        matchSettingsChanged = true;
                        break;

                    case StageRotationMode.Sequenced:
                        Log("Picking next stage", LogLevel.Debug);
                        var nextStage = _matchSettings.StageId + 1;
                        if (nextStage >= STAGE_COUNT) nextStage = 0;
                        _matchSettings.StageId = nextStage;
                        matchSettingsChanged = true;
                        break;
                }

                //Tier rotation
                var newTiers = _matchSettings.AllowedTiers;
                switch (_matchSettings.TierRotationMode)
                {
                    case TierRotationMode.Cycle:
                        switch (_matchSettings.AllowedTiers)
                        {
                            case AllowedTiers.NormalOnly:
                                newTiers = AllowedTiers.OddOnly;
                                break;
                            case AllowedTiers.OddOnly:
                                newTiers = AllowedTiers.HyperspeedOnly;
                                break;
                            default:
                                newTiers = AllowedTiers.NormalOnly;
                                break;
                        }
                        break;
                    case TierRotationMode.Random:
                        var rand = _random.Next() % 3;
                        switch (rand)
                        {
                            case 0:
                                newTiers = AllowedTiers.NormalOnly;
                                break;
                            case 1:
                                newTiers = AllowedTiers.OddOnly;
                                break;
                            case 2:
                                newTiers = AllowedTiers.HyperspeedOnly;
                                break;
                        }
                        break;
                    case TierRotationMode.WeightedRandom:
                        var choices = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 2 };
                        var choice = choices[_random.Next() % choices.Length];
                        switch (choice)
                        {
                            case 0:
                                newTiers = AllowedTiers.NormalOnly;
                                break;
                            case 1:
                                newTiers = AllowedTiers.OddOnly;
                                break;
                            case 2:
                                newTiers = AllowedTiers.HyperspeedOnly;
                                break;
                        }
                        break;
                }
                if (newTiers != _matchSettings.AllowedTiers)
                {
                    _matchSettings.AllowedTiers = newTiers;
                    matchSettingsChanged = true;
                    CorrectPlayerTiersAsync();
                    Broadcast(GetAllowedTiersText());
                }

                if (matchSettingsChanged)
                {
                    SendToAll(new SettingsChangedMessage(_matchSettings));
                }

                if (_players.Count >= _matchSettings.AutoStartMinPlayers && _matchSettings.AutoStartTime > 0)
                {
                    Log("There are still players, autoStartTimer started", LogLevel.Debug);
                    StartAutoStartTimerAsync();
                }
            }
            else
            {
                Log("Already in lobby");
            }
        }

        private void StartAutoStartTimerAsync()
        {
            autoStartTimer.Reset();
            autoStartTimer.Start();
            SendToAll(new AutoStartTimerMessage(true), 100);
        }

        private void StopAutoStartTimerAsync()
        {
            autoStartTimer.Reset();
            SendToAll(new AutoStartTimerMessage(false), 100);
        }

        private void FinishRaceAsync(ServPlayer p)
        {
            p.CurrentlyRacing = false;
            p.RacingTimeout.Reset();
            SendToAll(new RaceTimeoutMessage(p.ClientGuid, p.CtrlType, 0), 100);

            var playersStillRacing = _players.Count(a => a.CurrentlyRacing);
            if (playersStillRacing == 0)
            {
                Log("All players are done racing.");
                if (_matchSettings.AutoReturnTime > 0)
                {
                    Broadcast("Returning to lobby in " + _matchSettings.AutoReturnTime + " seconds");
                    backToLobbyTimer.Start();
                }
            }
            else
            {
                Log(playersStillRacing + " players(s) still racing");
            }
        }



        #endregion Gameplay methods

        #region Utility methods

        private void SendToAll(MatchMessage matchMsg, float priority = 5)
        {
            Log($"Sending message of type {matchMsg.GetType()} to {_clients.Count} connection(s) with priority {priority}", LogLevel.Debug);
            var matchMsgSerialized = JsonConvert.SerializeObject(matchMsg, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All });

            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory, Encoding.UTF8, false))
            {
                writer.Write(DateTime.UtcNow.Ticks);
                writer.Write(matchMsgSerialized);
                writer.Flush();

                var data = memory.ToArray();
                foreach (var item in _clients)
                {
                    item.Connection.Send(MessageTypes.Match, data, priority);
                }
            }
        }

        private void SendTo(MatchMessage matchMsg, ServClient receiver, float priority = 10)
        {
            Log($"Sending message of type {matchMsg.GetType()} to client {receiver.Name} with priority {priority}", LogLevel.Debug);
            var matchMsgSerialized = JsonConvert.SerializeObject(matchMsg, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All });

            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory, Encoding.UTF8, false))
            {
                writer.Write(DateTime.UtcNow.Ticks);
                writer.Write(matchMsgSerialized);
                writer.Flush();

                var data = memory.ToArray();
                receiver.Connection.Send(MessageTypes.Match, data, priority);
            }
        }

        /// <summary>
        /// Logs a string and sends it as a chat message to all clients.
        /// </summary>
        /// <param name="text"></param>
        private void Broadcast(string text)
        {
            Log(text);
            SendToAll(new ChatMessage("Server", ChatMessageType.System, text));
        }

        private void Whisper(ServClient reciever, string text)
        {
            Log("Sending whisper to client " + reciever.Name + "(Text: " + text + ")", LogLevel.Debug);
            SendTo(new ChatMessage("Server", ChatMessageType.System, text), reciever, 5);
        }

        /// <summary>
        /// Writes a message to the server log.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="type"></param>
        public void Log(object message, LogLevel type = LogLevel.Information)
        {
            _logger.Log(type, message.ToString());
        }

        private List<ServClient> SearchClients(string name)
        {
            return _clients.Where(a => a.Name.Contains(name)).ToList();
        }

        public void AddCommandHandler(string commandName, string help, CommandHandler handler)
        {
            _commandHandlers.Add(commandName, handler);
            _commandHelp.Add(commandName, help);
        }

        public async Task Kick(ServClient client, string reason)
        {
            await client.Connection.DisconnectAsync(reason);
        }

        private void CorrectPlayerTiersAsync()
        {
            foreach (var player in _players)
            {
                if (!VerifyCharacterTier(player.CharacterId))
                {
                    var client = _clients.FirstOrDefault(a => a.Guid == player.ClientGuid);
                    if (client != null)
                    {
                        for (var i = 0; i < characterTiers.Length; i++)
                        {
                            if (VerifyCharacterTier(i))
                            {
                                player.CharacterId = i;
                                SendToAll(new CharacterChangedMessage(player.ClientGuid, player.CtrlType, i));
                                Whisper(client, "Your character is not allowed and has been automatically changed.");
                                break;
                            }
                        }
                    }
                }
            }
        }

        private bool VerifyCharacterTier(int id)
        {
            var t = characterTiers[id];
            switch (_matchSettings.AllowedTiers)
            {
                case AllowedTiers.All:
                    return true;
                case AllowedTiers.NormalOnly:
                    return t == CharacterTier.Normal;
                case AllowedTiers.OddOnly:
                    return t == CharacterTier.Odd;
                case AllowedTiers.HyperspeedOnly:
                    return t == CharacterTier.Hyperspeed;
                case AllowedTiers.NoHyperspeed:
                    return t != CharacterTier.Hyperspeed;
                default:
                    return true;
            }
        }

        private string GetAllowedTiersText()
        {
            switch (_matchSettings.AllowedTiers)
            {
                case AllowedTiers.NormalOnly:
                    return "Only characters from the Normal tier are allowed.";
                case AllowedTiers.OddOnly:
                    return "Only characters from the Odd tier are allowed.";
                case AllowedTiers.HyperspeedOnly:
                    return "Only characters from the Hyperspeed tier are allowed.";
                case AllowedTiers.NoHyperspeed:
                    return "Any character NOT from the Hyperspeed tier is allowed.";
                default:
                    return "All characters are allowed.";
            }
        }
        #endregion Utility methods

        private void SaveMatchSettings()
        {
            //using (var sw = new StreamWriter(SETTINGS_FILENAME))
            //{
            //    sw.Write(JsonConvert.SerializeObject(_matchSettings));
            //}
        }

        public async ValueTask DisposeAsync()
        {
            Log("Saving match settings...");
            SaveMatchSettings();

            await Task.WhenAll(connectedClients.Select(s => s.Value.DisconnectAsync("Server is shutting down")));

            Log("The server has been closed.");

            _source.Cancel();
        }
    }
}
