using System.Collections.Generic;
using Hunter.Gameplay.AI;
using Hunter.Gameplay.Combat;
using Hunter.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;

namespace Hunter.Tests
{
    public class SoftLockTests
    {
        static List<SoftLock.Candidate> One(Vector3 position, string name = "target")
            => new() { new SoftLock.Candidate(position, name) };

        [Test]
        public void SnapsToTargetInsideCorrectionBudget()
        {
            // Target sits 15 degrees off the aim direction, inside the 25 degree budget.
            var target = Quaternion.Euler(0f, 15f, 0f) * Vector3.forward * 3f;
            var result = SoftLock.Resolve(Vector3.zero, Vector3.forward, One(target), maxRange: 5f);

            Assert.IsTrue(result.Corrected);
            Assert.AreEqual(15f, result.CorrectionDegrees, 0.5f);
            Assert.AreEqual(0f, Vector3.Angle(result.Direction, target.normalized), 0.5f);
        }

        [Test]
        public void LeavesAimAloneBeyondCorrectionBudget()
        {
            var target = Quaternion.Euler(0f, 40f, 0f) * Vector3.forward * 3f;
            var result = SoftLock.Resolve(Vector3.zero, Vector3.forward, One(target), maxRange: 5f);

            Assert.IsFalse(result.Corrected);
            Assert.AreEqual(0f, Vector3.Angle(result.Direction, Vector3.forward), 0.5f);
        }

        [Test]
        public void IgnoresTargetsOutOfRange()
        {
            var target = Quaternion.Euler(0f, 5f, 0f) * Vector3.forward * 20f;
            var result = SoftLock.Resolve(Vector3.zero, Vector3.forward, One(target), maxRange: 5f);

            Assert.IsFalse(result.Corrected);
        }

        [Test]
        public void PrefersSmallestCorrectionRatherThanNearestTarget()
        {
            var candidates = new List<SoftLock.Candidate>
            {
                // Very close but well off-axis.
                new(Quaternion.Euler(0f, 22f, 0f) * Vector3.forward * 1.2f, "close-wide"),
                // Further away but almost dead ahead: this is what the player meant.
                new(Quaternion.Euler(0f, 3f, 0f) * Vector3.forward * 4f, "far-narrow"),
            };

            var result = SoftLock.Resolve(Vector3.zero, Vector3.forward, candidates, maxRange: 6f);

            Assert.AreEqual("far-narrow", result.Target);
        }

        [Test]
        public void DisabledBudgetLeavesAimUntouched()
        {
            var target = Quaternion.Euler(0f, 5f, 0f) * Vector3.forward * 2f;
            var result = SoftLock.Resolve(Vector3.zero, Vector3.forward, One(target),
                maxRange: 5f, maxCorrectionDegrees: 0f);

            Assert.IsFalse(result.Corrected, "hardcore mode must not steer the swing");
        }

        [Test]
        public void SweepHitsInsideArcAndMissesOutside()
        {
            var inside = Quaternion.Euler(0f, 40f, 0f) * Vector3.forward * 2f;
            var outside = Quaternion.Euler(0f, 80f, 0f) * Vector3.forward * 2f;

            Assert.IsTrue(SoftLock.SweepHits(Vector3.zero, Vector3.forward, inside, 3f, 110f));
            Assert.IsFalse(SoftLock.SweepHits(Vector3.zero, Vector3.forward, outside, 3f, 110f));
        }

        [Test]
        public void SweepMissesBeyondRange()
        {
            Assert.IsFalse(SoftLock.SweepHits(Vector3.zero, Vector3.forward, Vector3.forward * 5f, 3f, 110f));
        }
    }

    public class MeleeStateMachineTests
    {
        static MeleeStateMachine NewMachine(out MeleeProfile profile)
        {
            profile = new MeleeProfile
            {
                StartupTime = 0.10f, ActiveTime = 0.10f, RecoveryTime = 0.20f, ComboWindow = 0.08f,
            };
            return new MeleeStateMachine(profile);
        }

        [Test]
        public void AttackProgressesStartupToActiveToRecoveryToIdle()
        {
            var machine = NewMachine(out _);
            Assert.IsTrue(machine.TryStartAttack());
            Assert.AreEqual(AttackPhase.Startup, machine.Phase);

            machine.Tick(0.11f);
            Assert.AreEqual(AttackPhase.Active, machine.Phase);

            machine.Tick(0.11f);
            Assert.AreEqual(AttackPhase.Recovery, machine.Phase);

            machine.Tick(0.21f);
            Assert.AreEqual(AttackPhase.Idle, machine.Phase);
        }

        [Test]
        public void SecondAttackDuringComboWindowChainsInsteadOfRestarting()
        {
            var machine = NewMachine(out _);
            machine.TryStartAttack();
            machine.Tick(0.21f);        // into recovery
            machine.Tick(0.13f);        // inside the trailing combo window

            Assert.IsTrue(machine.CanStartAttack);
            Assert.IsTrue(machine.TryStartAttack());
            Assert.AreEqual(2, machine.ComboIndex);
        }

        [Test]
        public void ThirdHitIsTheFinisher()
        {
            var machine = NewMachine(out _);
            for (int i = 0; i < 3; i++)
            {
                machine.TryStartAttack();
                machine.Tick(0.21f);
                machine.Tick(0.13f);
            }
            Assert.AreEqual(3, machine.ComboIndex);
            Assert.IsTrue(machine.IsFinisher);
        }

        [Test]
        public void EarlyPressIsBufferedRatherThanDropped()
        {
            var machine = NewMachine(out _);
            machine.TryStartAttack();

            machine.Tick(0.05f);
            Assert.IsFalse(machine.TryStartAttack(), "cannot attack during startup");

            // Buffered press must fire on its own once the swing finishes.
            machine.Tick(0.45f);
            Assert.AreNotEqual(AttackPhase.Idle, machine.Phase,
                "the buffered input should have started a new swing");
        }

        [Test]
        public void InterruptOnlyWorksDuringStartup()
        {
            var machine = NewMachine(out _);
            machine.TryStartAttack();
            Assert.IsTrue(machine.TryInterrupt());
            Assert.AreEqual(AttackPhase.Idle, machine.Phase);

            machine.TryStartAttack();
            machine.Tick(0.11f);   // now Active, blade is committed
            Assert.IsFalse(machine.TryInterrupt());
            Assert.AreEqual(AttackPhase.Active, machine.Phase);
        }

        [Test]
        public void HitStopIsLongerOnFinishers()
        {
            var profile = new MeleeProfile { HitStopSeconds = 0.08f };
            Assert.Greater(profile.HitStopFor(true), profile.HitStopFor(false));
            // Must stay inside the band that reads as impact rather than as a stall.
            Assert.LessOrEqual(profile.HitStopFor(true), 0.2f);
        }
    }

    public class RivalHunterBrainTests
    {
        static ItemInstance Treasure(int value, float weight = 1f)
        {
            var def = new ItemDefinition("t" + value, "t", ItemCategory.Treasure, ItemRarity.Common, value, weight);
            return new ItemInstance(def);
        }

        static Perception Idle(float health = 1f) =>
            new(health, false, 99f, 0, false, 999f, false, null, 50f, false);

        [Test]
        public void WoundedAndCarryingMeansFlee()
        {
            var inv = new Inventory(40f, 20);
            inv.TryAdd(Treasure(300));
            var brain = new RivalHunterBrain(inv);

            var intent = brain.Decide(Idle(health: 0.2f), LootValuation.Context.FreshRun);

            Assert.AreEqual(HunterGoal.Flee, intent.Goal);
        }

        [Test]
        public void SovereignNearbyOverridesGreed()
        {
            var inv = new Inventory(40f, 20);
            var brain = new RivalHunterBrain(inv);
            var perception = new Perception(1f, true, 5f, 900, false, 999f, true, Treasure(900), 40f,
                sovereignNearby: true);

            Assert.AreEqual(HunterGoal.Flee, brain.Decide(perception, LootValuation.Context.FreshRun).Goal);
        }

        [Test]
        public void AnswersTheBellWhenCloseEnough()
        {
            var inv = new Inventory(40f, 20);
            var brain = new RivalHunterBrain(inv, new RivalHunterBrain.Profile { Aggression = 0.5f });
            var perception = new Perception(1f, false, 99f, 0,
                heardBell: true, distanceToBell: 40f, lootInReach: false, visibleLoot: null,
                distanceToExtraction: 50f, sovereignNearby: false);

            Assert.AreEqual(HunterGoal.Ambush, brain.Decide(perception, LootValuation.Context.FreshRun).Goal);
        }

        [Test]
        public void IgnoresABellRungTooFarAway()
        {
            var inv = new Inventory(40f, 20);
            var brain = new RivalHunterBrain(inv, new RivalHunterBrain.Profile { BellResponseRange = 50f });
            var perception = new Perception(1f, false, 99f, 0, true, 300f, false, null, 50f, false);

            Assert.AreNotEqual(HunterGoal.Ambush, brain.Decide(perception, LootValuation.Context.FreshRun).Goal);
        }

        [Test]
        public void AttacksAHunterCarryingRealValue()
        {
            var inv = new Inventory(40f, 20);
            var brain = new RivalHunterBrain(inv,
                new RivalHunterBrain.Profile { Aggression = 0.2f, MinHaulWorthFighting = 200 });
            var perception = new Perception(1f, true, 10f, rivalVisibleHaul: 800,
                false, 999f, false, null, 50f, false);

            var intent = brain.Decide(perception, LootValuation.Context.FreshRun);

            Assert.AreEqual(HunterGoal.Fight, intent.Goal);
            Assert.AreEqual("target is loaded", intent.Reason);
        }

        [Test]
        public void TimidRivalDoesNotFightAnEmptyHandedHunter()
        {
            var inv = new Inventory(40f, 20);
            var brain = new RivalHunterBrain(inv,
                new RivalHunterBrain.Profile { Aggression = 0.1f, MinHaulWorthFighting = 200 });
            var perception = new Perception(1f, true, 10f, rivalVisibleHaul: 5,
                false, 999f, false, null, 50f, false);

            Assert.AreNotEqual(HunterGoal.Fight, brain.Decide(perception, LootValuation.Context.FreshRun).Goal);
        }

        [Test]
        public void RacesThePlayerForLootBothCanSee()
        {
            var inv = new Inventory(40f, 20);
            var brain = new RivalHunterBrain(inv, new RivalHunterBrain.Profile { MinHaulWorthFighting = 100000 });
            var perception = new Perception(1f, canSeeRival: true, distanceToRival: 30f, rivalVisibleHaul: 0,
                false, 999f, lootInReach: true, visibleLoot: Treasure(900, 1f),
                distanceToExtraction: 50f, sovereignNearby: false);

            Assert.AreEqual(HunterGoal.ContestLoot, brain.Decide(perception, LootValuation.Context.FreshRun).Goal);
        }

        [Test]
        public void LeavesWhenTheHaulAndClockSaySo()
        {
            var inv = new Inventory(30f, 20);
            inv.TryAdd(Treasure(1300, 24f));
            var brain = new RivalHunterBrain(inv);
            var context = new LootValuation.Context(0.8f, 40f, 0.8f);

            Assert.AreEqual(HunterGoal.Extract, brain.Decide(Idle(), context).Goal);
        }

        [Test]
        public void FallsBackToScavengingWithNothingHappening()
        {
            var brain = new RivalHunterBrain(new Inventory(40f, 20));
            Assert.AreEqual(HunterGoal.Scavenge, brain.Decide(Idle(), LootValuation.Context.FreshRun).Goal);
        }

        [Test]
        public void ExecuteLootCall_SwapPutsTheOriginalBackIfTheNewItemDoesNotFit()
        {
            var inv = new Inventory(weightLimit: 10f, slotLimit: 10);
            var held = Treasure(60, 9f);
            inv.TryAdd(held);
            var brain = new RivalHunterBrain(inv);

            // Appraisal says swap, but this candidate is too heavy to actually fit.
            var tooHeavy = Treasure(5000, 50f);
            var bogusCall = new LootValuation.Appraisal(
                LootValuation.Decision.SwapForWorseItem, held, 100f, 1f);

            Assert.IsFalse(brain.ExecuteLootCall(bogusCall, tooHeavy));
            Assert.AreEqual(1, inv.Count, "the dropped item must not vanish");
            Assert.AreSame(held, inv.Items[0]);
        }

        [Test]
        public void ExecuteLootCall_TakeAddsToTheBag()
        {
            var inv = new Inventory(40f, 20);
            var brain = new RivalHunterBrain(inv);
            var prize = Treasure(500, 2f);
            var call = new LootValuation.Appraisal(LootValuation.Decision.Take, null, 250f, 4f);

            Assert.IsTrue(brain.ExecuteLootCall(call, prize));
            Assert.AreEqual(1, inv.Count);
        }

        [Test]
        public void RivalsUseTheSameBagRulesAsThePlayer()
        {
            // The design contract: an AI cannot carry out more than a player could.
            var inv = new Inventory(weightLimit: 10f, slotLimit: 20);
            var brain = new RivalHunterBrain(inv);

            var call = new LootValuation.Appraisal(LootValuation.Decision.Take, null, 999f, 1f);
            Assert.IsTrue(brain.ExecuteLootCall(call, Treasure(100, 9f)));
            Assert.IsFalse(brain.ExecuteLootCall(call, Treasure(100, 9f)),
                "second heavy item must be refused by the shared weight limit");
        }
    }
}
