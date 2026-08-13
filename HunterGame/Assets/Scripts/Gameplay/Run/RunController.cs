using System;
using System.Collections.Generic;
using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Items;
using UnityEngine;

namespace Hunter.Gameplay.Run
{
    /// Owns one raid: ticks the director, wires the world to it, and is the thing every
    /// other gameplay component asks about run state.
    public class RunController : MonoBehaviour
    {
        [Header("Hunter")]
        [SerializeField] HunterController player;
        [SerializeField] Damageable playerHealth;
        [SerializeField] float carryWeightLimit = 32f;
        [SerializeField] int carrySlots = 18;

        [Header("World")]
        [SerializeField] BellTower bell;
        [SerializeField] Transform extractionPoint;

        [Header("Timings (seconds)")]
        [SerializeField] float runDuration = 1080f;
        [SerializeField] float sovereignSpawn = 720f;
        [SerializeField] float extractionWindow = 15f;
        [SerializeField] float earliestBell = 45f;

        public RunDirector Director { get; private set; }
        public Inventory Inventory { get; private set; }

        void Awake()
        {
            Inventory = new Inventory(carryWeightLimit, carrySlots);
            Director = new RunDirector(Inventory, new RunDirector.Settings
            {
                RunDurationSeconds = runDuration,
                SovereignSpawnSeconds = sovereignSpawn,
                ExtractionWindowSeconds = extractionWindow,
                EarliestBellSeconds = earliestBell,
            });

            if (player != null) player.Inventory = Inventory;
            if (playerHealth != null) playerHealth.Died += OnPlayerDied;
        }

        void OnDestroy()
        {
            if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
        }

        void Start() => Director.Begin();

        void Update()
        {
            if (Director.IsOver) return;
            Director.Tick(Time.deltaTime);
        }

        void OnPlayerDied() => Director.Kill();

        public bool RingBell() => bell != null && bell.Ring(Director);

        public float DistanceToExtraction(Vector3 from)
            => extractionPoint == null ? 120f : Vector3.Distance(from, extractionPoint.position);

        public LootValuation.Context ContextFor(Vector3 position)
            => Director.BuildValuationContext(DistanceToExtraction(position));

        /// Player-side pickup. Runs through the same appraisal the AI uses so the HUD can
        /// explain why something is or is not worth taking.
        public bool TryPickUp(Lootable container, ItemInstance item, Vector3 hunterPosition)
        {
            if (container == null || item == null) return false;
            if (Inventory.TryAdd(item) != AddResult.Added) return false;
            if (container.TryTake(item)) return true;

            Inventory.Remove(item);
            return false;
        }
    }
}
