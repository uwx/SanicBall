using System;
using System.Collections;
using System.Threading;
using Sanicball.Data;
using Sanicball.Logic;
using SanicballCore;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sanicball.Gameplay
{
    public enum BallType
    {
        Player,
        LobbyPlayer,
        AI
    }

    public class CheckpointPassArgs : System.EventArgs
    {
        public CheckpointPassArgs(Checkpoint c)
        {
            CheckpointPassed = c;
        }

        public Checkpoint CheckpointPassed { get; private set; }
    }

    public class CameraCreationArgs : System.EventArgs
    {
        public CameraCreationArgs(IBallCamera cameraCreated)
        {
            CameraCreated = cameraCreated;
        }

        public IBallCamera CameraCreated { get; private set; }
    }

    [System.Serializable]
    public class BallMotionSounds
    {
        [SerializeField]
        private AudioSource jump;
        [SerializeField]
        private AudioSource roll;
        [SerializeField]
        private AudioSource speedNoise;
        [SerializeField]
        private AudioSource brake;
        [SerializeField]
        private AudioSource ringL;
        [SerializeField]
        private AudioSource ringR;

        public AudioSource Jump => jump;
        public AudioSource Roll => roll;
        public AudioSource SpeedNoise => speedNoise;
        public AudioSource Brake => brake;
        public AudioSource RingL => ringL;
        public AudioSource RingR => ringR;
    }

    [System.Serializable]
    public class BallPrefabs
    {
        [SerializeField]
        private DriftySmoke smoke;
        [SerializeField]
        private OmniCamera camera;
        [SerializeField]
        private PivotCamera oldCamera;
        [SerializeField]
        private ParticleSystem removalParticles;
        [SerializeField]
        private SpeedFire speedFire;
        [SerializeField]
        private LostRang lostRang;

        public DriftySmoke Smoke => smoke;
        public OmniCamera Camera => camera;
        public PivotCamera OldCamera => oldCamera;
        public ParticleSystem RemovalParticles => removalParticles;
        public SpeedFire SpeedFire => speedFire;
        public LostRang LostRang => lostRang;
    }

    [RequireComponent(typeof(Rigidbody))]
    public class Ball : MonoBehaviour
    {
        //These are set using Init() when balls are instantiated
        //But you can set them from the editor to quickly test out a track
        [Header("Initial stats")]
        [SerializeField]
        private BallType type;
        [SerializeField]
        private ControlType ctrlType;
        [SerializeField]
        private int characterId;
        [SerializeField]
        private string nickname;
        [SerializeField]
        private GameObject hatPrefab;

        public BallType Type => type;
        public ControlType CtrlType => ctrlType;
        public int CharacterId => characterId;

        [Header("Subcategories")]
        [SerializeField]
        private BallPrefabs prefabs;
        [SerializeField]
        public BallMotionSounds sounds;

        //State
        private BallStats characterStats;
        private bool canMove = true;
        private BallControlInput input;
        private bool grounded = false;
        private float groundedTimer = 0;
        private float upResetTimer = 0;
        private DriftySmoke smoke;
        private SpeedFire speedFire;

        public bool CanMove { get { return canMove; } set { canMove = value; } }
        public bool AutoBrake { get; set; }
        public Vector3 DirectionVector { get; set; }
        public Vector3 Up { get; set; }
        public bool Brake { get; set; }
        public string Nickname => nickname;

        public int Rings { get; private set; }

        public int TotalRings
        {
            get => Math.Clamp(Rings + ringsToAdd, 0, 999);
            set
            {
                Rings = Math.Clamp(value, 0, 999);
                ringsToAdd = 0;
            }
        }

        //Component caches
        private Rigidbody rb;
        public BallControlInput Input => input;

        //Events
        public event EventHandler<CheckpointPassArgs> CheckpointPassed;
        public event EventHandler<RespawnEventArgs> RespawnRequested;
        public event EventHandler<CameraCreationArgs> CameraCreated;

        public new IBallCamera camera;

        public void Jump()
        {
            if (grounded && CanMove)
            {
                rb.AddForce(Up * characterStats.jumpHeight, ForceMode.Impulse);
                if (sounds.Jump != null)
                {
                    sounds.Jump.Play();
                }
                grounded = false;
            }
        }

        public void RequestRespawn()
        {
            // take a maximum of 100 rings from the player, scale the time penalty up to -5 seconds
            var rings = this.TotalRings; // total rings is the instantanious ring count, unaffected by easing
            var newRings = Math.Max(rings - 100, 0);
            var ringDifference = rings - newRings;

            var timePenalty = 5.0 * ((100 - ringDifference) / 100.0f);
            var ts = TimeSpan.FromSeconds(timePenalty);

            RespawnRequested?.Invoke(this, new RespawnEventArgs(rings, newRings, ts));
        }

        public void Init(BallType type, ControlType ctrlType, int characterId, string nickname)
        {
            this.type = type;
            this.ctrlType = ctrlType;
            this.characterId = characterId;
            this.nickname = nickname;
        }

        private int ringsToAdd = 0;

        public void AddRings(int x)
        {
            if (type == BallType.Player)
            {
                Interlocked.Add(ref ringsToAdd, x);
            }
            else
            {
                Rings += x;
            }
        }

        IEnumerator AddRingsCoroutine()
        {
            while (true)
            {
                while (ringsToAdd > 0)
                {
                    Rings = Math.Min(Rings + 1, 999);
                    Interlocked.Decrement(ref ringsToAdd);

                    yield return new WaitForSeconds(0.025f);
                }

                while (ringsToAdd < 0)
                {
                    Interlocked.Increment(ref ringsToAdd);
                    Rings = Math.Max(0, Rings - 1);

                    yield return null;
                }

                yield return null;
            }
        }

        public void SpawnLostRings(int ringDifference, Vector3 position)
        {
            StartCoroutine(SpawnLostRingsCoroutine(ringDifference, position));
        }

        private IEnumerator SpawnLostRingsCoroutine(int ringDifference, Vector3 position)
        {
            var rings = Math.Min(ringDifference, 32);
            const float radius = 10.0f;

            Vector3 GetPositionCore(int idx)
                => position
                + Mathf.Cos(idx * Mathf.PI * 2 / rings) * radius * Vector3.forward
                + Mathf.Sin(idx * Mathf.PI * 2 / rings) * radius * Vector3.right
                + 10.0f * Vector3.up;

            for (int i = 0; i < rings; i++)
            {
                var rangPosition = GetPositionCore(i);
                var lostRang = Instantiate(prefabs.LostRang, rangPosition, Quaternion.identity);

                yield return new WaitForSeconds(0.0025f);
            }
        }

        private void Start()
        {
            Up = Vector3.up;

            //Set up drifty smoke
            smoke = Instantiate(prefabs.Smoke);
            smoke.target = this;
            smoke.DriftAudio = sounds.Brake;

            //Grab reference to Rigidbody
            rb = GetComponent<Rigidbody>();
            //Set angular velocity (This is necessary for fast)
            rb.maxAngularVelocity = 1000f;

            //Set object name
            gameObject.name = type.ToString() + " - " + nickname;

            //Set character
            if (CharacterId >= 0 && CharacterId < ActiveData.Characters.Length)
            {
                SetCharacter(ActiveData.Characters[CharacterId]);
            }

            //Set up speed effect
            speedFire = Instantiate(prefabs.SpeedFire);
            speedFire.Init(this);

            //Crimbus
            DateTime now = DateTime.UtcNow;
            if (now.Month == 12 && now.Day > 20 && now.Day <= 31)
            {
                hatPrefab = ActiveData.ChristmasHat;
            }

            if (ActiveData.GameSettings.eSportsReady)
            {
                hatPrefab = ActiveData.ESportsHat;
            }

            //Spawn hat
            if (hatPrefab)
            {
                GameObject hat = Instantiate(hatPrefab);
                hat.transform.SetParent(transform, false);
            }

            //Create objects and components based on ball type
            if (type == BallType.Player)
            {
                if (ctrlType != ControlType.None)
                {
                    //Create camera
                    if (ActiveData.GameSettings.useOldControls)
                    {
                        camera = Instantiate(prefabs.OldCamera);
                        ((PivotCamera)camera).UseMouse = ctrlType == ControlType.Keyboard;
                    }
                    else
                    {
                        camera = Instantiate(prefabs.Camera);
                    }
                    camera.Target = rb;
                    camera.CtrlType = ctrlType;

                    if (CameraCreated != null)
                        CameraCreated(this, new CameraCreationArgs(camera));
                }
            }
            if (type == BallType.LobbyPlayer)
            {
                //Make the lobby camera follow this ball
                var cam = FindObjectOfType<LobbyCamera>();
                if (cam)
                {
                    cam.AddBall(this);
                }
            }
            if ((type == BallType.Player || type == BallType.LobbyPlayer) && ctrlType != ControlType.None)
            {
                //Create input component
                input = gameObject.AddComponent<BallControlInput>();
            }
            if (type == BallType.AI)
            {
                //Create AI component
                gameObject.AddComponent<BallControlAI>();
            }

            StartCoroutine(AddRingsCoroutine());
        }

        private void SetCharacter(Data.CharacterInfo c)
        {
            GetComponent<Renderer>().material = c.material;
            GetComponent<TrailRenderer>().material = c.trail;
            if (c.name == "Super Sanic" && ActiveData.GameSettings.eSportsReady)
            {
                GetComponent<TrailRenderer>().material = ActiveData.ESportsTrail;
            }
            transform.localScale = new Vector3(c.ballSize, c.ballSize, c.ballSize);
            if (c.alternativeMesh != null)
            {
                GetComponent<MeshFilter>().mesh = c.alternativeMesh;
            }
            //set collision mesh too
            if (c.collisionMesh != null)
            {
                if (c.collisionMesh.vertexCount <= 255)
                {
                    Destroy(GetComponent<Collider>());
                    MeshCollider mc = gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = c.collisionMesh;
                    mc.convex = true;
                }
                else
                {
                    Debug.LogWarning("Vertex count for " + c.name + "'s collision mesh is bigger than 255!");
                }
            }
            characterStats = c.stats;
        }

        private void FixedUpdate()
        {
            if (CanMove)
            {
                //If grounded use torque
                if (DirectionVector != Vector3.zero)
                {
                    rb.AddTorque(DirectionVector * characterStats.rollSpeed);
                }
                //If not use both
                if (!grounded)
                {
                    rb.AddForce((Quaternion.Euler(0, -90, 0) * DirectionVector) * characterStats.airSpeed);
                }
            }

            if (AutoBrake)
            {
                //Always brake when AutoBrake is on
                Brake = true;
            }

            //Braking
            if (Brake)
            {
                //Force ball to brake by resetting angular velocity every update
                rb.angularVelocity = Vector3.zero;
            }

            // Downwards torque for extra grip - currently not used
            if (grounded)
            {
                //rigidbody.AddForce(-up*stats.grip * (rigidbody.velocity.magnitude/400)); //Downwards gravity to increase grip
                //Debug.Log(stats.grip * Mathf.Pow(rigidbody.velocity.magnitude/100,2));
            }
        }

        private void Update()
        {
            float rollSpd = Mathf.Clamp(rb.angularVelocity.magnitude / 230, 0, 16);
            float vel = (-128f + rb.velocity.magnitude) / 256; //Start at 128 fph, end at 256
            vel = Mathf.Clamp(vel, 0, 1);

            //Rolling sounds
            if (grounded)
            {
                if (sounds.Roll != null)
                {
                    sounds.Roll.pitch = Mathf.Max(rollSpd, 0.8f);
                    sounds.Roll.volume = Mathf.Min(rollSpd, 1);
                }
                if (sounds.SpeedNoise != null)
                {
                    sounds.SpeedNoise.pitch = 0.8f + vel;
                    sounds.SpeedNoise.volume = Mathf.Min(vel, sounds.SpeedNoise.volume + 0.005f);
                }
            }
            else
            {
                //Fade sounds out when in the air
                if (sounds.Roll != null && sounds.Roll.volume > 0)
                {
                    sounds.Roll.volume = Mathf.Max(0, sounds.Roll.volume - 0.2f);
                }
                if (sounds.SpeedNoise != null && sounds.SpeedNoise.volume > 0)
                {
                    sounds.SpeedNoise.pitch = 0.8f + vel;
                    sounds.SpeedNoise.volume = Mathf.Max(vel * 0.25f, sounds.SpeedNoise.volume - 0.001f);
                }
            }

            //Grounded timer
            if (groundedTimer > 0)
            {
                groundedTimer -= Time.deltaTime;
                if (groundedTimer <= 0)
                {
                    grounded = false;
                    upResetTimer = 1f;
                }
            }

            if (!grounded)
            {
                if (upResetTimer > 0)
                {
                    upResetTimer -= Time.deltaTime;
                }
                else
                {
                    Up = Vector3.MoveTowards(Up, Vector3.up, Time.deltaTime * 10);
                }
            }

            //Smoke
            if (smoke != null)
            {
                smoke.grounded = grounded;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var c = other.GetComponent<Checkpoint>();

            if (c)
            {
                CheckpointPassed?.Invoke(this, new CheckpointPassArgs(c));
            }

            if (other.GetComponent<TriggerRespawn>())
                RequestRespawn();
        }

        private void OnCollisionStay(Collision c)
        {
            //Enable grounded and reset timer
            grounded = true;
            groundedTimer = 0;
            Up = c.contacts[0].normal;
        }

        private void OnCollisionExit(Collision c)
        {
            //Disable grounded when timer is done
            groundedTimer = 0.08f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, Up);
        }

        public void CreateRemovalParticles()
        {
            //TODO: Create a special version of the particle system for Super Sanic that has a cloud of pot leaves instead. No, really.
            Instantiate(prefabs.RemovalParticles, transform.position, transform.rotation);
        }
    }
}
