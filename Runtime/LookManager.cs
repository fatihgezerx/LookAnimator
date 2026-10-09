using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LookAnimation
{
    /// <summary>
    /// The one place that runs every <see cref="LookAnimator"/>: a registry of the looks and a single UniTask loop for all of
    /// them (no <c>Update</c> on any character, no coroutine). While no look has a target or any weight left to fade, the loop
    /// only wakes a few times a second, to let idle characters pick something to look at; as soon as any look has a target or is
    /// returning to neutral it runs every frame. The loop starts with the first look and ends with the last.
    /// </summary>
    /// <remarks>
    /// It runs at <see cref="PlayerLoopTiming.PreLateUpdate"/>: after every <c>Update</c> (so the character's rotation of this
    /// frame is final) and before the animation is evaluated (so the angles written here are the ones shown this frame).
    /// </remarks>
    public static class LookManager
    {
        // How often the loop wakes while no look has anything to move, in milliseconds.
        private const int SleepMilliseconds = 100;

        private static readonly List<LookAnimator> Looks = new List<LookAnimator>();

        private static CancellationTokenSource _loopCts;
        private static bool _loopRunning;
        private static int _generation;

        /// <summary>The enabled looks. Entries can be null for a moment while one is being removed; do not modify.</summary>
        public static IReadOnlyList<LookAnimator> All => Looks;

        /// <summary>Stops the loop and forgets every look. Safe to call at any time.</summary>
        public static void Shutdown()
        {
            _generation++;
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
            _loopRunning = false;
            Looks.Clear();
        }

        internal static void Add(LookAnimator look)
        {
            if (look == null || Looks.Contains(look))
            {
                return;
            }

            Looks.Add(look);

            if (_loopRunning)
            {
                return;
            }

            _loopRunning = true;
            _loopCts?.Dispose();
            _loopCts = new CancellationTokenSource();
            RunLoopAsync(_generation, _loopCts.Token).Forget();
        }

        internal static void Remove(LookAnimator look)
        {
            // The loop may be walking the list right now, so leave a hole instead of shifting it.
            var index = Looks.IndexOf(look);
            if (index >= 0)
            {
                Looks[index] = null;
            }
        }

        private static async UniTaskVoid RunLoopAsync(int generation, CancellationToken token)
        {
            try
            {
                while (true)
                {
                    var frame = false;
                    var now = Time.timeAsDouble;
                    var dt = Mathf.Min(Time.deltaTime, 0.1f);

                    for (var i = 0; i < Looks.Count; i++)
                    {
                        var look = Looks[i];
                        if (look != null)
                        {
                            frame |= look.Step(now, dt);
                        }
                    }

                    Compact();
                    if (Looks.Count == 0)
                    {
                        return;
                    }

                    bool canceled;
                    if (frame)
                    {
                        canceled = await UniTask.Yield(PlayerLoopTiming.PreLateUpdate, token).SuppressCancellationThrow();
                    }
                    else
                    {
                        canceled = await UniTask.Delay(SleepMilliseconds, DelayType.DeltaTime, PlayerLoopTiming.PreLateUpdate, token)
                            .SuppressCancellationThrow();
                    }

                    if (canceled)
                    {
                        return;
                    }
                }
            }
            finally
            {
                if (generation == _generation)
                {
                    _loopRunning = false;
                }
            }
        }

        // Removes the holes left by disabled looks, keeping the order.
        private static void Compact()
        {
            var write = 0;
            for (var read = 0; read < Looks.Count; read++)
            {
                var look = Looks[read];
                if (look != null)
                {
                    Looks[write++] = look;
                }
            }

            Looks.RemoveRange(write, Looks.Count - write);
        }

        // Keeps stale state out when "Enter Play Mode Options" skips the domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Shutdown();
    }
}
