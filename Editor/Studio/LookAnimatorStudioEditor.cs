using Veneer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LookAnimation
{
    /// <summary>
    /// Studio-look Inspector for <see cref="LookAnimator"/>: a header with a state pill (ready, what is missing, or what it is looking
    /// at while playing) and notes that say what would stop the look from working, then three tabs. <b>Look</b> holds what people
    /// change most (the target, the weight, how much of the turn the body takes), <b>Limits</b> the two diagrams that follow the
    /// sliders and the four limits, <b>Bones</b> Automatic Setup, the head and the list of body bones. The speeds and fade times stay
    /// hidden, as before.
    /// </summary>
    [CustomEditor(typeof(LookAnimator))]
    internal sealed class LookAnimatorStudioEditor : VeneerInspector<LookAnimator>
    {
        private LookDiagnostics diagnostics;
        private SerializedProperty targetProperty;
        private string shownTargetName;
        private string pillLookingText = string.Empty;
        private int liveKey = int.MinValue;
        private string liveText = string.Empty;

        /// <inheritdoc />
        protected override VeneerBrand Brand => LookBrand.Value;

        /// <inheritdoc />
        protected override void Build(VeneerInspectorBuilder b)
        {
            diagnostics = new LookDiagnostics(serializedObject);
            targetProperty = serializedObject.FindProperty("_target");

            b.Header("Look Animator", "Head, neck and spine follow a target");
            b.StatePill(PillState);
            b.Live(LiveText);
            BuildNotes(b);

            VisualElement[] tabs = b.Tabs(("Look", "eye"), ("Limits", "fit"), ("Bones", "link"));

            using (b.Into(tabs[0]))
            {
                b.Card("Target", "target", () =>
                {
                    b.Object("_target", "Target", typeof(Transform));
                    b.ConditionalHint(
                        () => targetProperty.objectReferenceValue == null
                            ? "Empty: set it from code with LookAt, or let a plugged-in system choose."
                            : null,
                        "info");
                    b.Slider("_settings.Weight", "Weight", "Off", "Full turn");
                });

                b.Card("Head and body", "user", () =>
                {
                    b.Slider("_settings.BodyTurn", "Body turn", "Head only", "Whole upper body");
                });
            }

            using (b.Into(tabs[1]))
            {
                b.Card("Range of the turn", "fit", () =>
                {
                    b.Add(new LookDiagramView(serializedObject, Target, LookBrand.Value.Accent));
                    b.Section("Left and right");
                    b.Slider("_settings.MaxYaw", "Max yaw", "Narrow", "Wide", unit: "deg");
                    b.Slider("_settings.StopAngle", "Stop angle", "Early", "Late", unit: "deg");
                    b.Section("Up and down");
                    b.Slider("_settings.MaxPitchUp", "Max pitch up", "Low", "High", unit: "deg");
                    b.Slider("_settings.MaxPitchDown", "Max pitch down", "Low", "High", unit: "deg");
                });
            }

            using (b.Into(tabs[2]))
            {
                b.Card("Rig", "link", () =>
                {
                    VisualElement setup = b.Button("Automatic Setup", "wrench", RunAutomaticSetup,
                        "Fills the head and the body bones (spine, chest, neck) from the character's humanoid rig. You can edit them afterwards.");
                    b.VisibleWhen(setup, () => LookAutoSetup.CanRun(Rig()), true);
                    b.ConditionalHint(
                        () => Rig() is Animator rig && !rig.isHuman ? LookAutoSetup.Tooltip(rig) : null,
                        "info",
                        true);
                    b.Object("_head", "Head", typeof(Transform));
                });

                b.Card("Body bones", "list", () =>
                {
                    b.Add(new LookBoneListView(serializedObject.FindProperty("_chain"), LookBrand.Value));
                });
            }
        }

        // The hints under the header, each polled because they depend on the Animator and its bones, not only on this component.
        private void BuildNotes(VeneerInspectorBuilder b)
        {
            b.ConditionalHint(() => LookDiagnostics.NoAnimator(Rig()), "warn", true);
            b.ConditionalHint(() => diagnostics.HumanoidAutomatic(Rig()), "info", true);
            b.ConditionalHint(() => diagnostics.GenericNeedsBones(Rig()), "warn", true);
            b.ConditionalHint(() => diagnostics.BoneNotUnderRig(Rig()), "warn", true);
            b.ConditionalHint(() => LookDiagnostics.AnimatePhysics(Rig()), "info", true);

            VisualElement fix = b.Button("Set Update Mode to Normal", "wrench", SetUpdateModeNormal,
                "Changes the Animator's Update Mode to Normal (undoable), so the look sees the angles in step.");
            b.VisibleWhen(fix, () => LookDiagnostics.AnimatePhysics(Rig()) != null, true);
        }

        private Animator Rig() => LookDiagnostics.FindRig(Target);

        private void RunAutomaticSetup()
        {
            LookAutoSetup.Run(serializedObject, Rig());
        }

        private void SetUpdateModeNormal()
        {
            Animator rig = Rig();
            if (rig == null)
            {
                return;
            }

            Undo.RecordObject(rig, "Set Animator Update Mode");
            rig.updateMode = AnimatorUpdateMode.Normal;
            EditorUtility.SetDirty(rig);
        }

        private (string text, string kind) PillState()
        {
            LookAnimator look = Target;
            if (look == null)
            {
                return (string.Empty, "idle");
            }

            if (Application.isPlaying)
            {
                if (!look.isActiveAndEnabled || !look.IsAttached)
                {
                    return ("Not working", "error");
                }

                if (!look.IsLooking)
                {
                    return ("Idle", "idle");
                }

                string name = look.TargetName;
                if (!string.Equals(name, shownTargetName))
                {
                    shownTargetName = name;
                    pillLookingText = "Looking at " + name;
                }

                return (pillLookingText, "ok");
            }

            Animator rig = LookDiagnostics.FindRig(look);
            if (rig == null)
            {
                return ("Needs an Animator", "warn");
            }

            if (!rig.isHuman && !diagnostics.HasAnyBone)
            {
                return ("Needs a head bone", "warn");
            }

            return ("Ready", "ok");
        }

        // The live strip (Play Mode only): the weight now and the angles to the target; rebuilt only when a rounded number changed.
        private string LiveText()
        {
            LookAnimator look = Target;
            if (look == null)
            {
                return string.Empty;
            }

            int weight = Mathf.RoundToInt(look.CurrentWeight * 100f);
            bool looking = look.IsLooking;
            int yaw = looking ? Mathf.RoundToInt(look.YawToTarget) : 0;
            int pitch = looking ? Mathf.RoundToInt(look.PitchToTarget) : 0;
            int key = (((weight * 2 + (looking ? 1 : 0)) * 721 + yaw + 360) * 361) + pitch + 180;
            if (key != liveKey)
            {
                liveKey = key;
                liveText = looking
                    ? "weight " + weight + "%   yaw " + yaw + " deg   pitch " + pitch + " deg"
                    : "nothing to look at   weight " + weight + "%";
            }

            return liveText;
        }
    }
}
