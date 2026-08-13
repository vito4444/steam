using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Run;
using UnityEngine;

namespace Hunter.Audio
{
    /// Wires the synthesised clips into the raid. Everything except the wind bed is 3D so
    /// a rival searching a chest two rooms away is something the player can locate by ear,
    /// which is the point of having audio in this genre at all.
    public class RaidAudio : MonoBehaviour
    {
        [SerializeField] RunController run;
        [SerializeField] Transform listenerTarget;
        [SerializeField] HunterController player;

        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] float windVolume = 0.35f;
        [SerializeField, Range(0f, 1f)] float droneVolume = 0.55f;
        [SerializeField] float footstepInterval = 0.46f;

        AudioSource _wind;
        AudioSource _drone;
        AudioSource _oneShots;
        float _footstepTimer;
        int _footVariant;

        void Awake()
        {
            _wind = CreateSource("Wind", loop: true, spatial: 0f);
            _wind.clip = ProceduralAudio.Wind();
            _wind.volume = windVolume;

            _drone = CreateSource("SovereignDrone", loop: true, spatial: 0f);
            _drone.clip = ProceduralAudio.SovereignDrone();
            _drone.volume = 0f;

            _oneShots = CreateSource("OneShots", loop: false, spatial: 0f);

            if (listenerTarget != null && listenerTarget.GetComponent<AudioListener>() == null)
                listenerTarget.gameObject.AddComponent<AudioListener>();
        }

        AudioSource CreateSource(string name, bool loop, float spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = spatial;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = 45f;
            return source;
        }

        void Start()
        {
            _wind.Play();
            _drone.Play();

            if (run != null)
            {
                run.Director.PhaseChanged += OnPhaseChanged;
                run.Director.SovereignAwakened += OnSovereignAwakened;
            }
        }

        void OnDestroy()
        {
            if (run?.Director == null) return;
            run.Director.PhaseChanged -= OnPhaseChanged;
            run.Director.SovereignAwakened -= OnSovereignAwakened;
        }

        void OnPhaseChanged(RunPhase phase)
        {
            if (phase == RunPhase.ExtractionWindow) _oneShots.PlayOneShot(ProceduralAudio.Bell(), 0.9f);
        }

        void OnSovereignAwakened() => _drone.volume = droneVolume;

        void Update()
        {
            if (player == null) return;

            // Footstep cadence follows actual ground speed, so a laden hunter's steps slow
            // down along with their movement. The bag's weight becomes audible.
            float speed = player.PlanarVelocity.magnitude;
            if (speed < 0.4f)
            {
                _footstepTimer = footstepInterval * 0.4f;
                return;
            }

            _footstepTimer -= Time.deltaTime * Mathf.Clamp(speed / 3.6f, 0.4f, 2.2f);
            if (_footstepTimer > 0f) return;

            _footstepTimer = footstepInterval;
            _footVariant = (_footVariant + 1) % 4;
            _oneShots.PlayOneShot(ProceduralAudio.Footstep(_footVariant), Mathf.Clamp01(speed / 6f) * 0.45f);
        }

        public void PlayLootPickup() => _oneShots.PlayOneShot(ProceduralAudio.LootPickup(), 0.6f);
    }
}
