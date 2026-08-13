using System;
using UnityEngine;

namespace Hunter.Gameplay.Combat
{
    /// Timing and feel numbers for one weapon class, kept in one place so hit feel can be
    /// tuned as data rather than by hunting through code.
    ///
    /// The three-layer hit feedback (audio, camera shake, flash) plus hitstop is the
    /// concrete answer to the competitor's "the hit lands a second late" reviews: the
    /// player must be told they connected within the same frame the damage applies.
    [Serializable]
    public sealed class MeleeProfile
    {
        [Header("Timing (seconds)")]
        [Tooltip("Wind-up before the hitbox opens.")]
        public float StartupTime = 0.18f;

        [Tooltip("How long the hitbox stays open.")]
        public float ActiveTime = 0.12f;

        [Tooltip("Committed recovery after the swing. This is the punish window.")]
        public float RecoveryTime = 0.30f;

        [Tooltip("Window near the end of recovery where the next attack can be buffered.")]
        public float ComboWindow = 0.22f;

        [Header("Reach")]
        public float Range = 2.6f;
        [Tooltip("Total sweep width in degrees.")]
        public float ArcDegrees = 110f;

        [Header("Damage")]
        public float BaseDamage = 24f;
        [Tooltip("Multiplier applied to the final hit of a combo chain.")]
        public float FinisherMultiplier = 1.6f;
        public float StaminaCost = 18f;

        [Header("Feel")]
        [Tooltip("Freeze on connect. 60-90ms is the band that reads as impact without " +
                 "feeling like a frame drop.")]
        [Range(0f, 0.2f)] public float HitStopSeconds = 0.075f;

        [Range(0f, 1f)] public float CameraShake = 0.35f;
        [Range(0f, 1f)] public float ControllerRumble = 0.5f;

        [Tooltip("Backward impulse applied to the victim.")]
        public float Knockback = 3.2f;

        public float TotalDuration => StartupTime + ActiveTime + RecoveryTime;

        /// Hitstop scales with how meaty the blow was, so a finisher lands heavier than a
        /// jab without needing separate profiles.
        public float HitStopFor(bool isFinisher) => isFinisher ? HitStopSeconds * 1.5f : HitStopSeconds;

        public float DamageFor(bool isFinisher, float weaponDamage)
        {
            float damage = BaseDamage + weaponDamage;
            return isFinisher ? damage * FinisherMultiplier : damage;
        }

        public static MeleeProfile Falchion() => new()
        {
            StartupTime = 0.16f, ActiveTime = 0.11f, RecoveryTime = 0.26f, ComboWindow = 0.24f,
            Range = 2.5f, ArcDegrees = 115f, BaseDamage = 22f, StaminaCost = 16f,
            HitStopSeconds = 0.07f, CameraShake = 0.32f, Knockback = 2.8f,
        };

        public static MeleeProfile Halberd() => new()
        {
            StartupTime = 0.30f, ActiveTime = 0.14f, RecoveryTime = 0.44f, ComboWindow = 0.20f,
            Range = 3.6f, ArcDegrees = 135f, BaseDamage = 38f, StaminaCost = 27f,
            HitStopSeconds = 0.095f, CameraShake = 0.55f, Knockback = 5.0f,
        };
    }

    public enum AttackPhase
    {
        Idle,
        Startup,
        Active,
        Recovery,
    }

    /// Advances one attack through its phases. Separated from the MonoBehaviour so combo
    /// and cancel rules can be tested at exact timings instead of by feel.
    public sealed class MeleeStateMachine
    {
        readonly MeleeProfile _profile;
        float _phaseTime;
        bool _bufferedAttack;

        public MeleeStateMachine(MeleeProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public AttackPhase Phase { get; private set; } = AttackPhase.Idle;
        public int ComboIndex { get; private set; }
        public bool HasConnectedThisSwing { get; private set; }

        /// Combos cap at three; the third is the finisher.
        public const int MaxCombo = 3;
        public bool IsFinisher => ComboIndex >= MaxCombo;

        public bool CanStartAttack =>
            Phase == AttackPhase.Idle ||
            (Phase == AttackPhase.Recovery && _phaseTime >= _profile.RecoveryTime - _profile.ComboWindow);

        public bool TryStartAttack()
        {
            if (!CanStartAttack)
            {
                // Buffer late presses instead of dropping them. Eating inputs is the other
                // half of why melee reads as unresponsive.
                if (Phase != AttackPhase.Idle) _bufferedAttack = true;
                return false;
            }

            ComboIndex = Phase == AttackPhase.Recovery ? Math.Min(ComboIndex + 1, MaxCombo) : 1;
            Phase = AttackPhase.Startup;
            _phaseTime = 0f;
            _bufferedAttack = false;
            HasConnectedThisSwing = false;
            return true;
        }

        public void NotifyConnected() => HasConnectedThisSwing = true;

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (Phase == AttackPhase.Idle) return;

            _phaseTime += deltaSeconds;

            switch (Phase)
            {
                case AttackPhase.Startup when _phaseTime >= _profile.StartupTime:
                    _phaseTime -= _profile.StartupTime;
                    Phase = AttackPhase.Active;
                    goto case AttackPhase.Active;

                case AttackPhase.Active when _phaseTime >= _profile.ActiveTime:
                    _phaseTime -= _profile.ActiveTime;
                    Phase = AttackPhase.Recovery;
                    break;

                case AttackPhase.Recovery when _phaseTime >= _profile.RecoveryTime:
                    Phase = AttackPhase.Idle;
                    _phaseTime = 0f;
                    ComboIndex = 0;
                    if (_bufferedAttack)
                    {
                        _bufferedAttack = false;
                        TryStartAttack();
                    }
                    break;

                case AttackPhase.Active:
                case AttackPhase.Startup:
                case AttackPhase.Recovery:
                    break;
            }
        }

        /// Getting hit during wind-up interrupts the swing. Once the blade is out it is
        /// committed, which is what makes trading blows a real decision.
        public bool TryInterrupt()
        {
            if (Phase != AttackPhase.Startup) return false;
            Phase = AttackPhase.Idle;
            _phaseTime = 0f;
            ComboIndex = 0;
            _bufferedAttack = false;
            return true;
        }
    }
}
