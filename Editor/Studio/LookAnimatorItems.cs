using System;
using Veneer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LookAnimation
{
    /// <summary>
    /// The pieces of the <see cref="LookAnimator"/> Inspector that are not plain fields, registered with Veneer so the default layout (and
    /// a user's copy of it) can place them: the state pill, the live strip, the rig notes, the Automatic Setup button and hint, and the
    /// two diagrams, plus the widget of the <c>_chain</c> list. They can be moved or hidden in the Inspector Designer, not changed.
    /// </summary>
    internal static class LookAnimatorItems
    {
        /// <summary>Id of the header state pill (header badge).</summary>
        public const string StateId = "look.state";

        /// <summary>Id of the Play Mode strip with the weight and the angles (after the header).</summary>
        public const string LiveId = "look.live";

        /// <summary>Id of the rig notes and the Update Mode button (after the header).</summary>
        public const string NotesId = "look.notes";

        /// <summary>Id of the two diagrams of the limits (card item).</summary>
        public const string DiagramId = "look.diagram";

        /// <summary>Id of the Automatic Setup button and its hint (card item).</summary>
        public const string SetupId = "look.setup";

        private const string SetupTooltip =
            "Fills the head and the body bones (spine, chest, neck) from the character's humanoid rig. You can edit them afterwards.";

        private const string UpdateModeTooltip =
            "Changes the Animator's Update Mode to Normal (undoable), so the look sees the angles in step.";

        /// <summary>Registers the custom items and the list widget of <see cref="LookAnimator"/>. Safe to call again (items are replaced).</summary>
        public static void Register()
        {
            Type type = typeof(LookAnimator);

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = StateId,
                Label = "State",
                Icon = "check-circle",
                Description = "A pill in the header: Ready, what is missing, or while playing what the look is looking at.",
                Slot = VeneerCustomItemSlot.HeaderBadge,
                Build = BuildState,
            });

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = LiveId,
                Label = "Live strip",
                Icon = "eye",
                Description = "While playing: the weight now and the angles to the target. Drawn under the header, whatever tab is open.",
                Slot = VeneerCustomItemSlot.AfterHeader,
                Build = BuildLive,
            });

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = NotesId,
                Label = "Rig notes",
                Icon = "warning",
                Description = "Hints that say what would stop the look from working (no Animator, no bones, a bone outside the rig, Animate Physics) and the Update Mode button. Drawn under the header.",
                Slot = VeneerCustomItemSlot.AfterHeader,
                Build = BuildNotes,
            });

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = DiagramId,
                Label = "Limit diagrams",
                Icon = "fit",
                Description = "Two small diagrams (from above and from the side) that follow the limit sliders, with a dot for the target while playing.",
                Slot = VeneerCustomItemSlot.Item,
                Build = BuildDiagram,
            });

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = SetupId,
                Label = "Automatic Setup",
                Icon = "wrench",
                Description = "The Automatic Setup button (humanoid rigs) and the hint shown for other rigs.",
                Slot = VeneerCustomItemSlot.Item,
                Build = BuildSetup,
            });

            VeneerCustomItems.RegisterWidget(type, "_chain", (context, property) => new LookBoneListView(property, context.Brand));
        }

        private static VisualElement BuildState(VeneerCustomItemContext context)
        {
            var status = new LookAnimatorStatus(context.SerializedObject);
            Label pill = VeneerStyle.Pill(string.Empty, "idle");
            Action refresh = () =>
            {
                (string text, string kind) state = status.PillState();
                VeneerStyle.SetPill(pill, state.kind, state.text);
            };

            // The first run of Poll is on the first tick: show the right state at once.
            refresh();
            context.Poll(refresh);
            return pill;
        }

        private static VisualElement BuildLive(VeneerCustomItemContext context)
        {
            var status = new LookAnimatorStatus(context.SerializedObject);
            context.Builder.Live(status.LiveText);
            return null;
        }

        // The hints depend on the Animator and its bones, not only on this component, so each is polled.
        private static VisualElement BuildNotes(VeneerCustomItemContext context)
        {
            VeneerInspectorBuilder b = context.Builder;
            var status = new LookAnimatorStatus(context.SerializedObject);
            LookDiagnostics diagnostics = status.Diagnostics;
            b.ConditionalHint(() => LookDiagnostics.NoAnimator(status.Rig()), "warn", true);
            b.ConditionalHint(() => diagnostics.HumanoidAutomatic(status.Rig()), "info", true);
            b.ConditionalHint(() => diagnostics.GenericNeedsBones(status.Rig()), "warn", true);
            b.ConditionalHint(() => diagnostics.BoneNotUnderRig(status.Rig()), "warn", true);
            b.ConditionalHint(() => LookDiagnostics.AnimatePhysics(status.Rig()), "info", true);

            SerializedObject serializedObject = context.SerializedObject;
            VisualElement fix = b.Button("Set Update Mode to Normal", "wrench", () => SetUpdateModeNormal(serializedObject), UpdateModeTooltip);
            b.VisibleWhen(fix, () => LookDiagnostics.AnimatePhysics(status.Rig()) != null, true);
            return null;
        }

        private static VisualElement BuildDiagram(VeneerCustomItemContext context)
        {
            return new LookDiagramView(context.SerializedObject, context.SerializedObject.targetObject as LookAnimator, LookBrand.Value.Accent);
        }

        private static VisualElement BuildSetup(VeneerCustomItemContext context)
        {
            VeneerInspectorBuilder b = context.Builder;
            SerializedObject serializedObject = context.SerializedObject;
            var status = new LookAnimatorStatus(serializedObject);
            VisualElement setup = b.Button("Automatic Setup", "wrench", () => LookAutoSetup.Run(serializedObject, status.Rig()), SetupTooltip);
            b.VisibleWhen(setup, () => LookAutoSetup.CanRun(status.Rig()), true);
            b.ConditionalHint(
                () => status.Rig() is Animator rig && !rig.isHuman ? LookAutoSetup.Tooltip(rig) : null,
                "info",
                true);
            return null;
        }

        private static void SetUpdateModeNormal(SerializedObject serializedObject)
        {
            Animator rig = LookDiagnostics.FindRig(serializedObject.targetObject as LookAnimator);
            if (rig == null)
            {
                return;
            }

            Undo.RecordObject(rig, "Set Animator Update Mode");
            rig.updateMode = AnimatorUpdateMode.Normal;
            EditorUtility.SetDirty(rig);
        }
    }
}
