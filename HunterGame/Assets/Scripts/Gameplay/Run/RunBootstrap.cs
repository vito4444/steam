using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Combat;
using Hunter.Gameplay.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hunter.Gameplay.Run
{
    /// Input and per-frame driving for a raid. Reads devices directly rather than through
    /// an action asset so the whole control scheme stays visible in code and the scene
    /// remains reproducible from the forge script.
    public class RunBootstrap : MonoBehaviour
    {
        [SerializeField] RunController run;
        [SerializeField] OverShoulderCamera playerCamera;
        [SerializeField] HunterController player;
        [SerializeField] MeleeCombatant combat;

        [Header("Interaction")]
        [SerializeField] float interactRange = 2.6f;
        [SerializeField] float bellRange = 3.5f;

        [Header("Debug")]
        [Tooltip("Frees the cursor and skips input, for headless capture.")]
        [SerializeField] bool captureMode;

        float _searchProgress;
        Lootable _searching;

        void Start()
        {
            if (!captureMode && Application.isPlaying)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (playerCamera != null) playerCamera.SnapToTarget();
        }

        void Update()
        {
            if (captureMode || run == null || player == null) return;

            float dt = Time.deltaTime;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (mouse != null && playerCamera != null)
                playerCamera.AddLook(mouse.delta.ReadValue());

            var move = Vector2.zero;
            bool sprint = false;
            bool interact = false;

            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed) move.y -= 1f;
                if (keyboard.dKey.isPressed) move.x += 1f;
                if (keyboard.aKey.isPressed) move.x -= 1f;
                sprint = keyboard.leftShiftKey.isPressed;
                interact = keyboard.eKey.isPressed;
            }

            // Movement is camera-relative, which is the only scheme that works with an
            // over-the-shoulder rig the player is also steering.
            if (playerCamera != null && move.sqrMagnitude > 1e-4f)
            {
                var world = playerCamera.PlanarForward * move.y + playerCamera.PlanarRight * move.x;
                move = new Vector2(world.x, world.z);
            }

            player.Move(move, sprint, dt);

            if (playerCamera != null)
            {
                bool aiming = mouse != null && mouse.rightButton.isPressed;
                playerCamera.Tick(dt, player.IsSprinting, aiming);
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame && combat != null)
                combat.TryAttack();

            HandleInteraction(interact, dt);
        }

        void HandleInteraction(bool held, float dt)
        {
            if (!held)
            {
                _searching = null;
                _searchProgress = 0f;
                return;
            }

            var position = player.transform.position;

            // Ringing the bell takes priority: it is never ambiguous what the player meant
            // when they are standing under it.
            foreach (var collider in Physics.OverlapSphere(position, bellRange))
            {
                var bell = collider.GetComponentInParent<BellTower>();
                if (bell == null || bell.Rung) continue;
                run.RingBell();
                return;
            }

            var container = FindContainer(position);
            if (container == null)
            {
                _searching = null;
                _searchProgress = 0f;
                return;
            }

            if (_searching != container)
            {
                _searching = container;
                _searchProgress = 0f;
            }

            _searchProgress += dt;
            if (_searchProgress < container.SearchSeconds) return;

            _searchProgress = 0f;
            TakeBest(container, position);
        }

        Lootable FindContainer(Vector3 position)
        {
            Lootable best = null;
            float bestDistance = float.MaxValue;

            foreach (var collider in Physics.OverlapSphere(position, interactRange))
            {
                var lootable = collider.GetComponentInParent<Lootable>();
                if (lootable == null || lootable.Emptied) continue;

                float distance = Vector3.Distance(position, lootable.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = lootable;
                }
            }
            return best;
        }

        /// Takes the densest item the bag can hold. The appraisal is the same call a rival
        /// hunter makes, so the player's "is this worth it" and the AI's agree by
        /// construction.
        void TakeBest(Lootable container, Vector3 position)
        {
            var contents = container.Peek();
            if (contents.Count == 0) return;

            var context = run.ContextFor(position);
            ItemInstance best = null;
            float bestDensity = 0f;

            foreach (var item in contents)
            {
                var call = LootValuation.Appraise(item, run.Inventory, context);
                if (call.Decision == LootValuation.Decision.Ignore) continue;

                float density = LootValuation.ValueDensity(item);
                if (density > bestDensity)
                {
                    bestDensity = density;
                    best = item;
                }
            }

            if (best != null) run.TryPickUp(container, best, position);
        }
    }
}
