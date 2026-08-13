using System.Collections;
using System.Linq;
using Hunter.Gameplay.AI;
using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Combat;
using Hunter.Gameplay.Items;
using Hunter.Gameplay.Run;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hunter.Tests
{
    /// Proves the forged scene actually runs. Unit tests cover the rules; these cover the
    /// wiring, which is where a code-generated scene is most likely to be silently broken.
    public class PlayModeSmokeTests
    {
        RunController _run;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("AurumMist", LoadSceneMode.Single);
            yield return null;
            _run = Object.FindFirstObjectByType<RunController>();
        }

        [UnityTest]
        public IEnumerator SceneBootsIntoAScavengingRun()
        {
            Assert.IsNotNull(_run, "RunController must be present in the forged scene");
            yield return null;

            Assert.IsNotNull(_run.Director);
            Assert.AreEqual(RunPhase.Scavenging, _run.Director.Phase);
            Assert.IsNotNull(_run.Inventory);
        }

        [UnityTest]
        public IEnumerator RunClockAdvancesWithRealFrames()
        {
            float start = _run.Director.ElapsedSeconds;
            for (int i = 0; i < 10; i++) yield return null;

            Assert.Greater(_run.Director.ElapsedSeconds, start);
        }

        [UnityTest]
        public IEnumerator PlayerRigIsCompleteAndCanMove()
        {
            var player = Object.FindFirstObjectByType<HunterController>();
            Assert.IsNotNull(player, "player rig missing");
            Assert.IsNotNull(player.GetComponent<CharacterController>());
            Assert.IsNotNull(player.GetComponent<Damageable>());
            Assert.IsNotNull(player.GetComponent<MeleeCombatant>());
            Assert.IsNotNull(player.Inventory, "run controller must hand the player its bag");

            var before = player.transform.position;
            const float step = 0.05f;
            for (int i = 0; i < 40; i++)
            {
                player.Move(Vector2.up, sprintHeld: false, step);
                yield return null;
            }

            Assert.Greater(Vector3.Distance(before, player.transform.position), 0.15f,
                "player should have covered ground");
        }

        [UnityTest]
        public IEnumerator CameraTracksThePlayer()
        {
            var camera = Object.FindFirstObjectByType<OverShoulderCamera>();
            Assert.IsNotNull(camera);
            Assert.IsNotNull(camera.Target, "camera must be bound to the hunter");

            var player = Object.FindFirstObjectByType<HunterController>();
            for (int i = 0; i < 20; i++)
            {
                player.Move(Vector2.up, false, 0.05f);
                camera.Tick(0.05f, false, false);
                yield return null;
            }

            float distance = Vector3.Distance(camera.transform.position, player.transform.position);
            Assert.Less(distance, 6f, "camera should stay on the hunter's shoulder");
        }

        [UnityTest]
        public IEnumerator RivalHuntersExistAndReachADecision()
        {
            var rivals = Object.FindObjectsByType<RivalHunterAgent>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(rivals.Length, 3, "the raid should be populated with rivals");

            // Let their decision timers fire at least once.
            for (int i = 0; i < 40; i++) yield return null;

            foreach (var rival in rivals)
            {
                Assert.IsNotNull(rival.Inventory, "rivals must carry the same Inventory type as the player");
                Assert.IsNotEmpty(rival.GoalReason, "every decision must carry a reason");
            }
        }

        [UnityTest]
        public IEnumerator LootCachesRollContentsOnInspection()
        {
            var caches = Object.FindObjectsByType<Lootable>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(caches.Length, 3);
            yield return null;

            foreach (var cache in caches)
            {
                var contents = cache.Peek();
                Assert.Greater(contents.Count, 0, "a cache must contain something");
                Assert.IsTrue(contents.All(i => i.Value > 0));
            }
        }

        [UnityTest]
        public IEnumerator BellOpensAnExtractionWindow()
        {
            var bell = Object.FindFirstObjectByType<BellTower>();
            Assert.IsNotNull(bell);

            // Skip past the earliest-bell lockout without waiting in real time.
            _run.Director.Tick(120f);
            yield return null;

            Assert.IsTrue(bell.Ring(_run.Director), "bell should ring once the lockout has passed");
            Assert.AreEqual(RunPhase.ExtractionWindow, _run.Director.Phase);
            Assert.Greater(_run.Director.PortalSecondsRemaining, 0f);
        }

        [UnityTest]
        public IEnumerator SovereignStaysAsleepUntilItsHour()
        {
            var sovereign = Object.FindFirstObjectByType<MistSovereign>(FindObjectsInactive.Include);
            Assert.IsNotNull(sovereign, "the raid needs its pursuer");
            yield return null;

            Assert.IsFalse(sovereign.Awakened, "sovereign must not be active at insertion");
        }

        [UnityTest]
        public IEnumerator AttackRunsThroughItsPhases()
        {
            var combat = Object.FindFirstObjectByType<MeleeCombatant>();
            Assert.IsNotNull(combat);

            Assert.IsTrue(combat.TryAttack());
            yield return null;
            Assert.AreNotEqual(AttackPhase.Idle, combat.Phase, "swing should be underway");

            float timeout = combat.Profile.TotalDuration * 3f + 1f;
            float elapsed = 0f;
            while (combat.Phase != AttackPhase.Idle && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(AttackPhase.Idle, combat.Phase, "swing must return to idle");
        }

        [UnityTest]
        public IEnumerator PlayerDeathEndsTheRunAndVoidsTheHaul()
        {
            var health = Object.FindFirstObjectByType<HunterController>().GetComponent<Damageable>();
            var def = new ItemDefinition("t", "t", ItemCategory.Treasure, ItemRarity.Common, 500, 1f);
            _run.Inventory.TryAdd(new ItemInstance(def));
            yield return null;

            health.ApplyDamage(9999f, Vector3.forward);
            yield return null;

            Assert.AreEqual(RunPhase.Died, _run.Director.Phase);
            Assert.AreEqual(0, _run.Director.BankedValue);
            Assert.AreEqual(0, _run.Inventory.Count);
        }
    }
}
