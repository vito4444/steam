using System;
using System.Collections.Generic;
using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Items;
using Hunter.Audio;
using UnityEngine;

namespace Hunter.Gameplay.Run
{
    /// The bell. Ringing it opens the way home and tells everything in the ruin exactly
    /// where you are — the single most consequential button in a raid.
    public class BellTower : MonoBehaviour
    {
        [SerializeField] float audibleRadius = 90f;
        [SerializeField] AudioSource audioSource;

        public float AudibleRadius => audibleRadius;
        public bool Rung { get; private set; }
        public float TimeSinceRung { get; private set; } = float.MaxValue;

        public event Action<BellTower> Rang;

        void Awake()
        {
            if (audioSource != null) return;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.65f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.maxDistance = audibleRadius;
        }

        public bool Ring(RunDirector director)
        {
            if (director == null) return false;
            if (director.RingBell() != BellResult.Rung) return false;

            Rung = true;
            TimeSinceRung = 0f;
            if (audioSource != null) audioSource.PlayOneShot(ProceduralAudio.Bell(), 0.95f);
            Rang?.Invoke(this);
            return true;
        }

        void Update()
        {
            if (Rung && TimeSinceRung < float.MaxValue) TimeSinceRung += Time.deltaTime;
        }
    }
}
