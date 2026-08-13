using System;
using System.Collections.Generic;
using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Camp;
using Hunter.Gameplay.Combat;
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
        [SerializeField] MeleeCombatant playerMelee;
        [SerializeField] float carryWeightLimit = FacilityInfo.BaseCarryWeight;
        [SerializeField] int carrySlots = FacilityInfo.BaseCarrySlots;

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

        /// Persistent progress. Owned here so a raid always resolves into the camp, whether
        /// it ends in a portal or in a corpse.
        public CampState Camp { get; private set; }

        void Awake() => EnsureInitialised();

        /// Safe to call from editor tooling; play mode calls it from Awake.
        public void EnsureInitialised()
        {
            if (Director != null) return;

            Camp = new CampState();
            CampSave.TryLoad(Camp);

            // Camp upgrades are applied at insertion, so an upgrade bought after the last
            // raid is felt on this one.
            var modifiers = Camp.BuildModifiers();
            Inventory = new Inventory(
                carryWeightLimit + modifiers.CarryWeightBonus,
                carrySlots + modifiers.CarrySlotBonus);
            Director = new RunDirector(Inventory, new RunDirector.Settings
            {
                RunDurationSeconds = runDuration,
                SovereignSpawnSeconds = sovereignSpawn,
                ExtractionWindowSeconds = extractionWindow,
                EarliestBellSeconds = earliestBell,
            });

            if (player != null) player.Inventory = Inventory;

            // Shrine and forge levels were previously computed and then dropped on the floor,
            // so those two facilities charged aurum and changed nothing in the raid.
            if (playerHealth != null)
                playerHealth.SetMaxHealth(FacilityInfo.BaseVitality + modifiers.VitalityBonus);

            if (playerMelee != null)
            {
                int forge = Camp.LevelOf(FacilityKind.Forge);
                var weapon = MeleeProfile.ForForgeLevel(forge);
                // Tier alone only changes at three levels; honing is what makes every forge
                // purchase land, and it has to match what the camp screen quoted.
                weapon.BaseDamage *= FacilityInfo.ForgeDamageMultiplier(forge);
                playerMelee.SetProfile(weapon);
            }

            if (playerHealth != null && Application.isPlaying) playerHealth.Died += OnPlayerDied;

            Director.PhaseChanged += OnPhaseChanged;
        }

        /// Resolving the raid into the camp is deliberately driven off the phase change
        /// rather than the extraction call, so a run that ends any other way — timeout,
        /// death, a future disconnect path — cannot skip being recorded.
        void OnPhaseChanged(RunPhase phase)
        {
            switch (phase)
            {
                case RunPhase.Extracted:
                    Camp.BankHaul(Director.BankedItems);
                    CampSave.Save(Camp);
                    break;

                case RunPhase.Died:
                case RunPhase.TimedOut:
                    Camp.RecordLoss(_valueAtRisk, _itemsAtRisk);
                    CampSave.Save(Camp);
                    break;
            }
        }

        int _valueAtRisk;
        int _itemsAtRisk;

        void OnDestroy()
        {
            if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
            if (Director != null) Director.PhaseChanged -= OnPhaseChanged;
        }

        void Start() => Director.Begin();

        void Update()
        {
            if (Director.IsOver) return;

            // Snapshot before ticking: once the run ends the bag has already been emptied,
            // so the loss record would otherwise always read zero.
            _valueAtRisk = Inventory.TotalValue;
            _itemsAtRisk = Inventory.Count;

            Director.Tick(Time.deltaTime);
        }

        void OnPlayerDied()
        {
            _valueAtRisk = Inventory.TotalValue;
            _itemsAtRisk = Inventory.Count;
            Director.Kill();
        }

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
