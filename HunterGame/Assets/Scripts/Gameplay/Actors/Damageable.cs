using System;
using UnityEngine;

namespace Hunter.Gameplay.Actors
{
    /// Health, stamina and the hit reaction that sells a connection. Shared by the player
    /// and by rival hunters so a fight reads the same from either side.
    public class Damageable : MonoBehaviour
    {
        [SerializeField] float maxHealth = 100f;
        [SerializeField] float maxStamina = 100f;
        [SerializeField] float staminaRegenPerSecond = 14f;
        [SerializeField] float staminaRegenDelay = 0.9f;

        [Header("Hit feedback")]
        [Tooltip("How long the victim flashes on being hit. Layer 3 of the hit feedback.")]
        [SerializeField] float flashSeconds = 0.12f;
        [SerializeField] Color flashColor = new(1f, 0.85f, 0.6f);

        float _staminaCooldown;
        float _flashRemaining;
        MaterialPropertyBlock _block;
        Renderer[] _renderers;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public float Health { get; private set; }
        public float Stamina { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead => Health <= 0f;
        public float Health01 => maxHealth <= 0f ? 0f : Mathf.Clamp01(Health / maxHealth);

        public event Action<float, Vector3> Damaged;
        public event Action Died;

        void Awake()
        {
            Health = maxHealth;
            Stamina = maxStamina;
            _renderers = GetComponentsInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;

            if (_staminaCooldown > 0f) _staminaCooldown -= dt;
            else if (Stamina < maxStamina) Stamina = Mathf.Min(maxStamina, Stamina + staminaRegenPerSecond * dt);

            if (_flashRemaining > 0f)
            {
                _flashRemaining -= dt;
                ApplyFlash(Mathf.Clamp01(_flashRemaining / Mathf.Max(flashSeconds, 1e-4f)));
            }
        }

        public bool TrySpendStamina(float amount)
        {
            if (amount <= 0f) return true;
            if (Stamina < amount) return false;

            Stamina -= amount;
            _staminaCooldown = staminaRegenDelay;
            return true;
        }

        public void ApplyDamage(float amount, Vector3 fromDirection)
        {
            if (IsDead || amount <= 0f) return;

            Health = Mathf.Max(0f, Health - amount);
            _flashRemaining = flashSeconds;
            Damaged?.Invoke(amount, fromDirection);

            if (Health <= 0f) Died?.Invoke();
        }

        void ApplyFlash(float intensity)
        {
            if (_renderers == null) return;
            var color = flashColor * intensity * 2.2f;

            foreach (var renderer in _renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_block);
                _block.SetColor(EmissionColorId, color);
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
