using System.Collections.Generic;
using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Combat;
using Hunter.Gameplay.Items;
using Hunter.Gameplay.Run;
using UnityEngine;

namespace Hunter.Gameplay.AI
{
    /// Drives a rival gold-hunter in the world from the decisions RivalHunterBrain makes.
    /// This class deliberately holds no judgement of its own: every "should I" question
    /// goes to the brain, which goes to LootValuation, which is the same function the
    /// player's HUD uses.
    [RequireComponent(typeof(Damageable))]
    public class RivalHunterAgent : MonoBehaviour
    {
        [Header("Perception")]
        [SerializeField] float sightRange = 24f;
        [SerializeField, Range(0f, 180f)] float sightConeDegrees = 130f;
        [SerializeField] float lootSenseRange = 6f;
        [SerializeField] LayerMask sightBlockers = ~0;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 2.3f;
        [SerializeField] float chaseSpeed = 4.6f;
        [SerializeField] float fleeSpeed = 5.1f;
        [SerializeField] float turnSpeed = 6f;
        [SerializeField] float arriveRadius = 1.4f;

        [Header("Acting on decisions")]
        [Tooltip("How close before a rival commits to a swing.")]
        [SerializeField] float strikeRange = 2.2f;
        [Tooltip("How close before a rival starts searching a container.")]
        [SerializeField] float reachRange = 1.9f;
        [SerializeField] float searchSeconds = 1.3f;

        [Header("Profile")]
        [SerializeField, Range(0f, 1f)] float aggression = 0.5f;
        [SerializeField] float carryWeightLimit = 28f;
        [SerializeField] int carrySlots = 14;
        [SerializeField] float decisionIntervalSeconds = 0.35f;

        // Serialized so the wiring survives the editor-time forge into play mode. An
        // earlier revision assigned these in a Bind() call at forge time only, and every
        // rival woke up in play mode with no brain at all.
        [Header("Wiring")]
        [SerializeField] RunController run;
        [SerializeField] Transform playerTarget;
        [SerializeField] MistSovereign sovereign;
        [SerializeField] BellTower bell;
        [SerializeField] List<Transform> route = new();

        RivalHunterBrain _brain;
        Damageable _self;
        Damageable _playerHealth;
        MeleeCombatant _combat;
        Lootable _searchTarget;
        float _searchProgress;
        int _routeIndex;
        float _decisionTimer;
        Vector3 _destination;

        public HunterGoal Goal { get; private set; } = HunterGoal.Scavenge;
        public string GoalReason { get; private set; } = "";
        public Inventory Inventory => _brain?.Inventory;

        /// Editor-time wiring. Only writes serialized fields; the brain itself is built in
        /// Awake so it exists in play mode too.
        public void Bind(RunController runController, Transform player, MistSovereign pursuer,
            BellTower bellTower, IEnumerable<Transform> patrolRoute, float aggressionOverride = -1f)
        {
            run = runController;
            playerTarget = player;
            sovereign = pursuer;
            bell = bellTower;

            route.Clear();
            if (patrolRoute != null) route.AddRange(patrolRoute);

            if (aggressionOverride >= 0f) aggression = aggressionOverride;
        }

        void Awake()
        {
            _self = GetComponent<Damageable>();
            _combat = GetComponent<MeleeCombatant>();
            _brain = new RivalHunterBrain(
                new Inventory(carryWeightLimit, carrySlots),
                new RivalHunterBrain.Profile { Aggression = aggression });
            _destination = transform.position;
        }

        void Start()
        {
            if (run == null) run = FindFirstObjectByType<RunController>();
            if (bell == null) bell = FindFirstObjectByType<BellTower>();
            if (sovereign == null) sovereign = FindFirstObjectByType<MistSovereign>(FindObjectsInactive.Include);
            if (playerTarget == null)
            {
                var hunter = FindFirstObjectByType<HunterController>();
                if (hunter != null) playerTarget = hunter.transform;
            }
            _playerHealth = playerTarget != null ? playerTarget.GetComponent<Damageable>() : null;
        }

        void Update()
        {
            if (_self.IsDead || _brain == null || run == null) return;

            float dt = Time.deltaTime;
            _decisionTimer -= dt;
            if (_decisionTimer <= 0f)
            {
                _decisionTimer = decisionIntervalSeconds;
                Think();
            }

            MoveTowardsDestination(dt);
            ActOnGoal(dt);
        }

        /// Turns a decision into an action once the agent is actually in position. The
        /// brain decides what to want; this decides nothing, it only executes.
        void ActOnGoal(float dt)
        {
            switch (Goal)
            {
                case HunterGoal.Fight:
                case HunterGoal.Ambush:
                    TryStrike();
                    break;

                case HunterGoal.Scavenge:
                case HunterGoal.ContestLoot:
                    TrySearch(dt);
                    break;

                default:
                    _searchTarget = null;
                    _searchProgress = 0f;
                    break;
            }
        }

        void TryStrike()
        {
            if (_combat == null || playerTarget == null) return;
            if (_playerHealth != null && _playerHealth.IsDead) return;

            var offset = playerTarget.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > strikeRange * strikeRange) return;

            // Face the target before committing; the swing arc is narrow enough that a
            // rival flailing at empty air would look broken.
            transform.rotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
            _combat.TryAttack();
        }

        void TrySearch(float dt)
        {
            var container = NearestContainer();
            if (container == null)
            {
                _searchTarget = null;
                _searchProgress = 0f;
                return;
            }

            if (Vector3.Distance(transform.position, container.transform.position) > reachRange)
            {
                // Walk to it first. Overriding the destination here is what makes a rival
                // visibly break off its patrol and beeline for a chest.
                _destination = container.transform.position;
                _searchProgress = 0f;
                return;
            }

            // Hold position while searching; the patrol destination set by Think() would
            // otherwise walk the rival straight off the chest it is opening.
            _destination = transform.position;

            if (_searchTarget != container)
            {
                _searchTarget = container;
                _searchProgress = 0f;
            }

            _searchProgress += dt;
            if (_searchProgress < searchSeconds) return;
            _searchProgress = 0f;

            TakeFrom(container);
        }

        void TakeFrom(Lootable container)
        {
            var contents = container.Peek();
            if (contents.Count == 0) return;

            var context = run.ContextFor(transform.position);

            // Evaluate every item, act on the best call. Same appraisal the player's
            // pickup path uses, so a rival never grabs something a player would scoff at.
            ItemInstance chosen = null;
            LootValuation.Appraisal chosenCall = default;
            float bestDensity = 0f;

            foreach (var item in contents)
            {
                var call = LootValuation.Appraise(item, _brain.Inventory, context);
                if (call.Decision == LootValuation.Decision.Ignore) continue;

                float density = LootValuation.ValueDensity(item);
                if (density <= bestDensity) continue;

                bestDensity = density;
                chosen = item;
                chosenCall = call;
            }

            if (chosen == null) return;
            if (!container.TryTake(chosen)) return;

            if (!_brain.ExecuteLootCall(chosenCall, chosen))
            {
                // Refused after the fact: do not let the item evaporate.
                container.Restore(chosen);
            }
        }

        Lootable NearestContainer()
        {
            Lootable best = null;
            float bestDistance = float.MaxValue;

            foreach (var collider in Physics.OverlapSphere(transform.position, lootSenseRange,
                         ~0, QueryTriggerInteraction.Collide))
            {
                var lootable = collider.GetComponentInParent<Lootable>();
                if (lootable == null || lootable.Emptied) continue;

                float distance = Vector3.Distance(transform.position, lootable.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = lootable;
                }
            }
            return best;
        }

        void Think()
        {
            var perception = Perceive();
            var context = run.ContextFor(transform.position);
            var intent = _brain.Decide(perception, context);

            Goal = intent.Goal;
            GoalReason = intent.Reason;

            switch (intent.Goal)
            {
                case HunterGoal.Flee:
                case HunterGoal.Extract:
                    _destination = FleeDestination();
                    break;

                case HunterGoal.Ambush:
                    _destination = bell != null ? bell.transform.position : transform.position;
                    break;

                case HunterGoal.Fight:
                case HunterGoal.ContestLoot:
                    _destination = playerTarget != null ? playerTarget.position : transform.position;
                    break;

                default:
                    _destination = NextRoutePoint();
                    break;
            }
        }

        Perception Perceive()
        {
            bool canSee = false;
            float distanceToPlayer = float.MaxValue;
            int visibleHaul = 0;

            if (playerTarget != null)
            {
                var offset = playerTarget.position - transform.position;
                distanceToPlayer = offset.magnitude;

                if (distanceToPlayer <= sightRange &&
                    Vector3.Angle(transform.forward, offset) <= sightConeDegrees * 0.5f &&
                    !Physics.Linecast(transform.position + Vector3.up * 1.4f,
                        playerTarget.position + Vector3.up * 1.2f, sightBlockers,
                        QueryTriggerInteraction.Ignore))
                {
                    canSee = true;
                    // Rivals judge a target by what they can see glinting on them, not by
                    // reading the player's actual inventory.
                    visibleHaul = run.Inventory?.TotalValue ?? 0;
                }
            }

            var loot = NearestLoot(out float lootDistance);

            bool bellHeard = bell != null && bell.Rung && bell.TimeSinceRung < 25f;
            float bellDistance = bell != null
                ? Vector3.Distance(transform.position, bell.transform.position)
                : float.MaxValue;

            return new Perception(
                _self.Health01,
                canSee,
                distanceToPlayer,
                visibleHaul,
                bellHeard,
                bellDistance,
                loot != null && lootDistance <= lootSenseRange,
                loot,
                run.DistanceToExtraction(transform.position),
                sovereign != null && sovereign.IsPressuring);
        }

        ItemInstance NearestLoot(out float distance)
        {
            distance = float.MaxValue;
            Lootable best = null;

            foreach (var container in Physics.OverlapSphere(transform.position, lootSenseRange))
            {
                var lootable = container.GetComponentInParent<Lootable>();
                if (lootable == null || lootable.Emptied) continue;

                float d = Vector3.Distance(transform.position, lootable.transform.position);
                if (d < distance)
                {
                    distance = d;
                    best = lootable;
                }
            }

            if (best == null) return null;
            var contents = best.Peek();
            return contents.Count > 0 ? contents[0] : null;
        }

        Vector3 NextRoutePoint()
        {
            if (route.Count == 0) return transform.position;

            var target = route[_routeIndex % route.Count];
            if (target != null && Vector3.Distance(transform.position, target.position) <= arriveRadius)
                _routeIndex++;

            target = route[_routeIndex % route.Count];
            return target != null ? target.position : transform.position;
        }

        Vector3 FleeDestination()
        {
            if (bell != null) return bell.transform.position;
            if (route.Count > 0 && route[0] != null) return route[0].position;
            return transform.position;
        }

        void MoveTowardsDestination(float dt)
        {
            var offset = _destination - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < arriveRadius * arriveRadius) return;

            float speed = Goal switch
            {
                HunterGoal.Flee => fleeSpeed,
                HunterGoal.Extract => fleeSpeed * 0.92f,
                HunterGoal.Fight or HunterGoal.Ambush or HunterGoal.ContestLoot => chaseSpeed,
                _ => walkSpeed,
            };

            speed *= _brain.Inventory.SpeedMultiplier;

            var direction = offset.normalized;
            var facing = Vector3.RotateTowards(transform.forward, direction, turnSpeed * dt, 0f);
            transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            transform.position += direction * (speed * dt);
        }
    }
}
