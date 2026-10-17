using System;
using Veneer;

namespace LookAnimation
{
    /// <summary>
    /// The design of the <see cref="LookAnimator"/> Inspector that Look Animator ships: three tabs (Look, Limits, Bones), their cards and
    /// fields, and the system's own items (state pill, live strip, rig notes, limit diagrams, Automatic Setup, body bone list). With
    /// Veneer installed a project can copy it in the Inspector Designer and change the copy; this default is never saved as an asset.
    /// The speeds and fade times are <c>[HideInInspector]</c>, so they stay out of every card, as before.
    /// </summary>
    internal sealed class LookAnimatorDefaultLayout : IVeneerDefaultLayoutProvider
    {
        /// <inheritdoc />
        public Type TargetType => typeof(LookAnimator);

        /// <inheritdoc />
        public VeneerLayout CreateLayout()
        {
            VeneerLayout layout = new VeneerLayoutBuilder(typeof(LookAnimator), "Look Animator", "Head, neck and spine follow a target")
                .Page("Look", "eye")
                .Card("Target", "target")
                // The header pill, the live strip and the rig notes are drawn in their own slots (header, under the header); they only need an entry.
                .Custom(LookAnimatorItems.StateId)
                .Custom(LookAnimatorItems.LiveId)
                .Custom(LookAnimatorItems.NotesId)
                .Field("_target", "Target", widget: VeneerWidget.Object)
                .Note("Empty: set it from code with LookAt, or let a plugged-in system choose.")
                .ShowIf("_target", VeneerCondition.IsNull)
                .Slider("_settings.Weight", "Weight", "Off", "Full turn")
                .Card("Head and body", "user")
                .Slider("_settings.BodyTurn", "Body turn", "Head only", "Whole upper body")
                .Page("Limits", "fit")
                .Card("Range of the turn", "fit")
                .Custom(LookAnimatorItems.DiagramId)
                .Heading("Left and right")
                .Slider("_settings.MaxYaw", "Max yaw", "Narrow", "Wide", "deg")
                .Slider("_settings.StopAngle", "Stop angle", "Early", "Late", "deg")
                .Heading("Up and down")
                .Slider("_settings.MaxPitchUp", "Max pitch up", "Low", "High", "deg")
                .Slider("_settings.MaxPitchDown", "Max pitch down", "Low", "High", "deg")
                .Page("Bones", "link")
                .Card("Rig", "link")
                .Custom(LookAnimatorItems.SetupId)
                .Field("_head", "Head", widget: VeneerWidget.Object)
                .Card("Body bones", "list")
                .Field("_chain", "Body bones")
                .Build();

            // The look of the system (accent, style sheet). UseThemeLogo stays off on purpose: the Inspector's builder already carries the
            // brand and shows its logo in the header, and the renderer would put a second logo next to it.
            layout.ThemeBrand = LookBrand.Value.Name;
            return layout;
        }

        /// <inheritdoc />
        public void RegisterItems()
        {
            LookAnimatorItems.Register();
        }
    }
}
