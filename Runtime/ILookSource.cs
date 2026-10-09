using System.Collections.Generic;

namespace LookAnimation
{
    /// <summary>
    /// Tells a <see cref="LookAnimator"/> what to look at when it has no <see cref="LookAnimator.Target"/> of its own. Implement it
    /// on a component of the same object, or register a factory with <see cref="LookSources"/> so another system (a combat or
    /// dialogue system, say) can drive the look of every character that belongs to it without any component being added.
    /// </summary>
    public interface ILookSource
    {
        /// <summary>
        /// Asked every step while the look runs (every frame while it is turning, a few times a second while it is idle). Returns
        /// whether there is something to look at.
        /// </summary>
        /// <param name="now">Seconds of game time.</param>
        /// <param name="point">The world position to look at.</param>
        /// <param name="weightFactor">0 to 1: how much of the look shows right now (0 = keep following the target but show nothing).</param>
        bool TryGetTarget(double now, out UnityEngine.Vector3 point, out float weightFactor);

        /// <summary>Name of what is looked at, for the Inspector (null when there is nothing).</summary>
        string TargetName { get; }
    }

    /// <summary>The factories that make an <see cref="ILookSource"/> for a look animator that has none on its object.</summary>
    public static class LookSources
    {
        /// <summary>Makes a source for <paramref name="look"/>, or returns null when this factory has none for it.</summary>
        public delegate ILookSource Factory(LookAnimator look);

        private static readonly List<Factory> Factories = new List<Factory>();

        /// <summary>Adds a factory (a second call with the same one does nothing). Call it early, e.g. before the first scene loads.</summary>
        public static void Register(Factory factory)
        {
            if (factory != null && !Factories.Contains(factory))
            {
                Factories.Add(factory);
            }
        }

        /// <summary>Removes a factory.</summary>
        public static void Unregister(Factory factory) => Factories.Remove(factory);

        internal static ILookSource Create(LookAnimator look)
        {
            for (var i = 0; i < Factories.Count; i++)
            {
                var source = Factories[i](look);
                if (source != null)
                {
                    return source;
                }
            }

            return null;
        }
    }
}
