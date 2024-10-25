using Newtonsoft.Json;
using SanicballCore;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sanicball.Data
{
    public class ActiveData : MonoBehaviour
    {
        #region Fields

        public List<RaceRecord> raceRecords = new List<RaceRecord>();

        //Pseudo-singleton pattern - this field accesses the current instance.
        public static ActiveData instance;

        //This data is saved to a json file
        private GameSettings gameSettings = new GameSettings();

        private KeybindCollection keybinds = new KeybindCollection();
        private MatchSettings matchSettings = MatchSettings.CreateDefault();

        //This data is set from the editor and remains constant
        [Header("Static data")]
        [SerializeField]
        private StageInfo[] stages;

        [SerializeField]
        private CharacterInfo[] characters;

        [SerializeField]
        private GameObject christmasHat;
        [SerializeField]
        private Material eSportsTrail;
        [SerializeField]
        private GameObject eSportsHat;
        [SerializeField]
        private AudioClip eSportsMusic;
        [SerializeField]
        private ESportMode eSportsPrefab;

        #endregion Fields

        #region Properties

        public static GameSettings GameSettings => instance.gameSettings;
        public static KeybindCollection Keybinds => instance.keybinds;
        public static ref MatchSettings MatchSettings => ref instance.matchSettings;
        public static List<RaceRecord> RaceRecords => instance.raceRecords;

        public static StageInfo[] Stages => instance.stages;
        public static CharacterInfo[] Characters => instance.characters;
        public static GameObject ChristmasHat => instance.christmasHat;
        public static Material ESportsTrail => instance.eSportsTrail;
        public static GameObject ESportsHat => instance.eSportsHat;
        public static AudioClip ESportsMusic => instance.eSportsMusic;
        public static ESportMode ESportsPrefab => instance.eSportsPrefab;

        public static bool ESportsFullyReady
        {
            get
            {
                var possible = false;
                if (GameSettings.eSportsReady)
                {
                    var m = FindAnyObjectByType<Logic.MatchManager>();
                    if (m)
                    {
                        var players = m.Players;
                        foreach (var p in players)
                        {
                            if (p.CtrlType != ControlType.None)
                            {
                                if (p.CharacterId == 13)
                                {
                                    possible = true;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                        }
                    }
                }
                return possible;
            }
        }

        #endregion Properties

        #region Unity functions

        //Make sure there is never more than one GameData object
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);

                SceneManager.sceneLoaded += OnSceneWasLoaded;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnEnable()
        {
            LoadAll();
        }
        
        private void OnApplicationQuit()
        {
            SaveAll();
        }

        #endregion Unity functions

        #region Saving and loading

        public void LoadAll()
        {
            Load("GameSettings", ref gameSettings);
            Load("GameKeybinds", ref keybinds);
            Load("MatchSettings", ref matchSettings);
            Load("Records", ref raceRecords);

            GameSettings.Apply(false);
        }

        public void SaveAll()
        {
            Save("GameSettings", gameSettings);
            Save("GameKeybinds", keybinds);
            Save("MatchSettings", matchSettings);
            Save("Records", raceRecords);
        }

        private void OnSceneWasLoaded(Scene scene, LoadSceneMode mode)
        {
            GameSettings.Apply(false);
        }

        private void Load<T>(string filename, ref T output)
        {
            filename = Path.GetFileNameWithoutExtension(filename);
            if (PlayerPrefs.HasKey(filename))
            {
                //Load file contents
                var dataString = PlayerPrefs.GetString(filename);
                //Deserialize from JSON into a data object
                try
                {
                    var dataObj = JsonConvert.DeserializeObject<T>(dataString);
                    //Make sure an object was created, this would't end well with a null value
                    if (dataObj != null)
                    {
                        output = dataObj;
                        Debug.Log(filename + " loaded successfully.");
                    }
                    else
                    {
                        Debug.LogError("Failed to load " + filename + ": file is empty.");
                    }
                }
                catch (JsonException ex)
                {
                    Debug.LogError("Failed to parse " + filename + "! JSON converter info: " + ex.Message);
                }
            }
            else
            {
                Debug.Log(filename + " has not been loaded - file not found.");
            }
        }

        private void Save(string filename, object objToSave)
        {
            filename = Path.GetFileNameWithoutExtension(filename);
            PlayerPrefs.SetString(filename, JsonConvert.SerializeObject(objToSave));
            Debug.Log(filename + " saved successfully.");
        }

        #endregion Saving and loading
    }
}
