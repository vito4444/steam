using Monster.Presentation;
using Monster.Shift;
using UnityEngine;

namespace Monster.Audio
{
    /// <summary>Wires the booth's synthesised sounds to what happens in it.
    ///
    /// The mix is three continuous beds plus one-shots. The beds carry the room: mains hum,
    /// wind on the glass, and the heater cycling on and off. The concept's design note that
    /// "when the heater dies the room tone changes and the game becomes noticeably worse to
    /// sit in" is a real mechanic, so the heater is a separate source that can be turned
    /// down rather than part of a single ambience loop.</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class BoothAudio : MonoBehaviour
    {
        [SerializeField] private BoothPresenter presenter;
        /// <summary>Bed levels, chosen from the measured loudness of the clips rather than
        /// by feel, because nobody involved can hear them.
        ///
        /// Every synthesised clip is normalised to the same peak, so their loudness in
        /// isolation says nothing about how they sit together: room tone came out at an RMS
        /// of 0.350 and wind at 0.129, and mixing both near half meant the air in the room
        /// was three times louder than the weather outside it. These numbers put each bed
        /// at a chosen level in the mix instead.
        ///
        /// Targets, in RMS of full scale: room tone 0.020, wind 0.032, heater 0.025, the
        /// CRT whine 0.013. A quiet night in a small room, with the weather the loudest
        /// thing in it. A test recomputes these from the clips and fails if the ordering
        /// stops holding.</summary>
        public const float RoomToneLevel = 0.057f;
        public const float WindLevel = 0.248f;
        public const float HeaterLevel = 0.176f;
        public const float CrtLevel = 0.026f;

        [SerializeField, Range(0f, 1f)] private float roomToneLevel = RoomToneLevel;
        [SerializeField, Range(0f, 1f)] private float windLevel = WindLevel;
        [SerializeField, Range(0f, 1f)] private float heaterLevel = HeaterLevel;
        [SerializeField, Range(0f, 1f)] private float crtLevel = CrtLevel;
        [SerializeField, Range(0f, 1f)] private float oneShotLevel = 0.75f;

        private AudioSource _oneShots;
        private AudioClip _switchThrow;
        private AudioClip _stamp;
        private AudioClip _paper;
        private AudioClip _telephone;
        private AudioClip _vehicle;

        public void Bind(BoothPresenter boothPresenter) => presenter = boothPresenter;

        private void Awake()
        {
            _oneShots = GetComponent<AudioSource>();
            _oneShots.playOnAwake = false;
            _oneShots.spatialBlend = 0f;

            _switchThrow = ProceduralAudio.SwitchThrow();
            _stamp = ProceduralAudio.Stamp();
            _paper = ProceduralAudio.PaperRustle();
            _telephone = ProceduralAudio.TelephoneRing();
            _vehicle = ProceduralAudio.VehicleArrival();

            Bed("RoomTone", ProceduralAudio.RoomTone(), roomToneLevel);
            Bed("Wind", ProceduralAudio.Wind(), windLevel);
            Bed("Heater", ProceduralAudio.Heater(), heaterLevel);

            // Three cathode ray tubes a metre from the player's head were silent until now.
            Bed("CrtWhine", ProceduralAudio.CrtWhine(), crtLevel);
        }

        private void OnEnable()
        {
            if (presenter != null)
            {
                presenter.DecisionMade += OnDecision;
                presenter.ShiftEnded += OnShiftEnded;
            }
        }

        private void OnDisable()
        {
            if (presenter != null)
            {
                presenter.DecisionMade -= OnDecision;
                presenter.ShiftEnded -= OnShiftEnded;
            }
        }

        private void OnDecision(Decision decision)
        {
            // The order is the order the objects would actually be touched: throw the
            // switch, stamp the form, pull the next one off the stack, and the next
            // vehicle pulls up outside.
            Play(_switchThrow, 0.70f);
            Play(_stamp, 0.85f, 0.18f);
            Play(_paper, 0.55f, 0.42f);
            Play(_vehicle, 0.5f, 0.9f);
        }

        private void OnShiftEnded(NightlyStatement statement) => Play(_paper, 0.7f);

        public void RingTelephone() => Play(_telephone, 0.9f);

        private void Play(AudioClip clip, float scale, float delaySeconds = 0f)
        {
            if (clip == null || _oneShots == null)
            {
                return;
            }

            if (delaySeconds <= 0f)
            {
                _oneShots.PlayOneShot(clip, oneShotLevel * scale);
                return;
            }

            StartCoroutine(PlayLater(clip, scale, delaySeconds));
        }

        private System.Collections.IEnumerator PlayLater(AudioClip clip, float scale, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            if (_oneShots != null)
            {
                _oneShots.PlayOneShot(clip, oneShotLevel * scale);
            }
        }

        private void Bed(string sourceName, AudioClip clip, float level)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = level;
            source.spatialBlend = 0f;
            source.playOnAwake = true;
            source.Play();
        }
    }
}
