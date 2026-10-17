using Veneer;
using UnityEditor;

namespace LookAnimation
{
    /// <summary>
    /// Studio-look Inspector for <see cref="LookAnimator"/>, drawn from a Veneer layout: the project's own copy made in the Inspector
    /// Designer when there is one, otherwise <see cref="LookAnimatorDefaultLayout"/> (tabs Look, Limits and Bones). The state pill, the
    /// live strip, the rig notes, the diagrams, Automatic Setup and the body bone list are custom items (<see cref="LookAnimatorItems"/>).
    /// The title is the fixed text of the layout. With several objects selected the layout is drawn for the first one.
    /// </summary>
    [CustomEditor(typeof(LookAnimator))]
    internal sealed class LookAnimatorStudioEditor : VeneerLayoutDrivenInspector<LookAnimator>
    {
        /// <inheritdoc />
        protected override VeneerBrand Brand => LookBrand.Value;
    }
}
