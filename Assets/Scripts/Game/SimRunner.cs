using System;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Drives the simulation at a fixed tick rate and exposes it to the view.
    ///
    /// The simulation advances in whole ticks and never sees frame time, which is what
    /// keeps it reproducible. Leftover real time is carried in an accumulator, and a
    /// long frame is allowed to catch up only so far before the remainder is dropped:
    /// spiralling into an ever-growing backlog is worse than losing a few ticks.
    /// </summary>
    public sealed class SimRunner : MonoBehaviour
    {
        [Tooltip("Simulation speed multiplier. 0 pauses.")]
        public int SpeedMultiplier = 1;

        [Tooltip("Seed for the world. Changing this restarts the factory.")]
        public uint Seed = 1;

        [Tooltip("How many workers the factory opens with.")]
        public int StartingWorkers = 4;

        [Tooltip("Start from the fully belted layout instead of the opening one.")]
        public bool StartAutomated;

        /// <summary>Hard cap on catch-up ticks in one frame, so a hitch cannot snowball.</summary>
        private const int MaxTicksPerFrame = 8;

        public SimWorld World { get; private set; }

        public event Action<SimWorld> WorldCreated;

        private float _accumulator;

        private void Awake()
        {
            CreateWorld();
        }

        public void CreateWorld()
        {
            World = StartAutomated
                ? Scenarios.AutomatedFactory(Seed, StartingWorkers)
                : Scenarios.StarterFactory(Seed, StartingWorkers);

            Scenarios.AddOpeningOrder(World);
            _accumulator = 0f;
            WorldCreated?.Invoke(World);
        }

        private void Update()
        {
            if (World == null || SpeedMultiplier <= 0) return;

            float secondsPerTick = 1f / (SimConfig.TicksPerSecond * SpeedMultiplier);
            _accumulator += Time.deltaTime;

            int ticks = 0;
            while (_accumulator >= secondsPerTick && ticks < MaxTicksPerFrame)
            {
                World.Step();
                _accumulator -= secondsPerTick;
                ticks++;
            }

            // Dropping the remainder keeps a stalled frame from queueing work forever.
            if (_accumulator > secondsPerTick * MaxTicksPerFrame) _accumulator = 0f;
        }

        /// <summary>Fraction of the way through the current tick, for view interpolation.</summary>
        public float TickAlpha
        {
            get
            {
                if (SpeedMultiplier <= 0) return 0f;
                float secondsPerTick = 1f / (SimConfig.TicksPerSecond * SpeedMultiplier);
                return Mathf.Clamp01(_accumulator / secondsPerTick);
            }
        }
    }
}
