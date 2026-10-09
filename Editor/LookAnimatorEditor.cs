using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace LookAnimation
{
    /// <summary>
    /// The Look Animator Inspector: a banner that says whether the look is ready (or, while playing, what it is doing), then three
    /// cards: the target and the overall weight, the limits (drawn as two small diagrams that follow the sliders) and the bones
    /// that turn (Automatic Setup fills them from a humanoid rig).
    /// </summary>
    [CustomEditor(typeof(LookAnimator))]
    internal sealed class LookAnimatorEditor : UnityEditor.Editor
    {
        private const int DiagramSize = 112;

        private static readonly Color Accent = new Color(0.31f, 0.67f, 1f);
        private static readonly Color Good = new Color(0.36f, 0.80f, 0.52f);
        private static readonly Color Caution = new Color(1f, 0.74f, 0.28f);
        private static readonly Color Bad = new Color(0.95f, 0.42f, 0.42f);

        private static GUIStyle _title;
        private static GUIStyle _subtitle;
        private static GUIStyle _section;
        private static GUIStyle _note;
        private static GUIStyle _caption;
        private static GUIStyle _chip;
        private static GUIStyle _cardPadding;

        private SerializedProperty _settings;
        private SerializedProperty _target;
        private SerializedProperty _head;
        private SerializedProperty _chain;
        private ReorderableList _bodyList;
        private Texture2D _topView;
        private Texture2D _sideView;
        private Vector2 _topKey = new Vector2(-1f, -1f);
        private Vector3 _sideKey = new Vector3(-1f, -1f, -1f);

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        private void OnEnable()
        {
            _settings = serializedObject.FindProperty("_settings");
            _target = serializedObject.FindProperty("_target");
            _head = serializedObject.FindProperty("_head");
            _chain = serializedObject.FindProperty("_chain");
            BuildList();
        }

        private void OnDisable()
        {
            DestroyTexture(ref _topView);
            DestroyTexture(ref _sideView);
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            var look = (LookAnimator)target;
            var rig = look.GetComponentInChildren<Animator>();

            serializedObject.Update();

            DrawBanner(look, rig);
            DrawNotes(look, rig);
            DrawTargetCard();
            DrawLimitsCard(look);
            DrawBonesCard(rig);

            serializedObject.ApplyModifiedProperties();
        }

        #region Banner and notes

        private void DrawBanner(LookAnimator look, Animator rig)
        {
            var rect = GUILayoutUtility.GetRect(0f, 54f, GUILayout.ExpandWidth(true));
            Rounded(rect, EditorGUIUtility.isProSkin ? new Color(0.15f, 0.22f, 0.31f) : new Color(0.76f, 0.85f, 0.96f), 8f);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x + 8f, rect.y + 11f, 3f, 32f), Accent);
            }

            GUI.Label(new Rect(rect.x + 18f, rect.y + 7f, rect.width - 170f, 22f), "Look Animator", _title);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 29f, rect.width - 170f, 16f), "Head, neck and spine follow a target", _subtitle);

            string text;
            Color color;
            if (Application.isPlaying)
            {
                if (!look.isActiveAndEnabled || !look.IsAttached)
                {
                    text = "Not working";
                    color = Bad;
                }
                else if (look.IsLooking)
                {
                    text = "Looking at " + look.TargetName;
                    color = Good;
                }
                else
                {
                    text = "Idle";
                    color = Color.gray;
                }
            }
            else if (rig == null)
            {
                text = "Needs an Animator";
                color = Caution;
            }
            else if (!HasBones() && !rig.isHuman)
            {
                text = "Needs a head bone";
                color = Caution;
            }
            else
            {
                text = "Ready";
                color = Good;
            }

            Chip(new Rect(rect.xMax - 152f, rect.y + 17f, 142f, 20f), text, color);

            if (Application.isPlaying)
            {
                var meter = new Rect(rect.x + 18f, rect.yMax - 7f, rect.width - 36f, 3f);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(meter, new Color(0f, 0f, 0f, 0.3f));
                    EditorGUI.DrawRect(new Rect(meter.x, meter.y, meter.width * Mathf.Clamp01(look.CurrentWeight), meter.height), Accent);
                }
            }
        }

        private void DrawNotes(LookAnimator look, Animator rig)
        {
            var notes = new List<KeyValuePair<string, Color>>();
            if (rig == null)
            {
                notes.Add(new KeyValuePair<string, Color>("There is no Animator on this object or under it, so the look has no bones to turn.", Caution));
            }
            else
            {
                if (!HasBones() && !_head.objectReferenceValue)
                {
                    notes.Add(rig.isHuman
                        ? new KeyValuePair<string, Color>("Nothing is set: the spine, chest, neck and head are found by themselves when the game starts. Press Automatic Setup to see and edit them.", Accent)
                        : new KeyValuePair<string, Color>("This is a generic rig: set the Head and the body bones, otherwise the look does nothing.", Caution));
                }

                CheckUnderRig(notes, rig, _head.objectReferenceValue as Transform, "The head");
                for (var i = 0; i < _chain.arraySize; i++)
                {
                    var bone = _chain.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue as Transform;
                    CheckUnderRig(notes, rig, bone, "The body bone");
                }

                if (rig.updateMode == AnimatorUpdateMode.Fixed)
                {
                    notes.Add(new KeyValuePair<string, Color>($"The Animator on '{rig.name}' updates with the physics (Animate Physics), so the look sees the angles up to one frame late. Set its Update Mode to Normal.", Accent));
                }
            }

            foreach (var note in notes)
            {
                DrawNote(note.Key, note.Value);
            }
        }

        private static void CheckUnderRig(List<KeyValuePair<string, Color>> notes, Animator rig, Transform bone, string what)
        {
            if (bone != null && !bone.IsChildOf(rig.transform))
            {
                notes.Add(new KeyValuePair<string, Color>($"{what} '{bone.name}' is not under the Animator's object '{rig.name}', so it is skipped.", Caution));
            }
        }

        private static void DrawNote(string message, Color color)
        {
            var content = new GUIContent(message);
            var width = EditorGUIUtility.currentViewWidth - 46f;
            var rect = GUILayoutUtility.GetRect(0f, _note.CalcHeight(content, width) + 12f, GUILayout.ExpandWidth(true));
            rect.y += 2f;
            rect.height -= 2f;
            Rounded(rect, new Color(color.r, color.g, color.b, EditorGUIUtility.isProSkin ? 0.16f : 0.22f), 6f);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x + 6f, rect.y + 6f, 3f, rect.height - 12f), color);
            }

            GUI.Label(new Rect(rect.x + 16f, rect.y + 4f, rect.width - 24f, rect.height - 8f), content, _note);
        }

        #endregion

        #region Cards

        private void DrawTargetCard()
        {
            BeginCard();
            CardTitle("Target");
            EditorGUILayout.PropertyField(_target, new GUIContent("Target", "What to look at. Empty = it comes from code (LookAt) or from another system that plugs in, such as the Combat System."));
            if (!_target.objectReferenceValue)
            {
                GUILayout.Label("Empty: set it from code with LookAt, or let a plugged-in system choose.", _caption);
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.PropertyField(_settings.FindPropertyRelative(nameof(LookSettings.Weight)), new GUIContent("Weight", "How much of the turn shows. 0 = the look does nothing, 1 = the full turn."));
            EndCard();
        }

        private void DrawLimitsCard(LookAnimator look)
        {
            var maxYaw = _settings.FindPropertyRelative(nameof(LookSettings.MaxYaw));
            var pitchUp = _settings.FindPropertyRelative(nameof(LookSettings.MaxPitchUp));
            var pitchDown = _settings.FindPropertyRelative(nameof(LookSettings.MaxPitchDown));
            var stop = _settings.FindPropertyRelative(nameof(LookSettings.StopAngle));

            BeginCard();
            CardTitle("Limits");

            EnsureDiagrams(maxYaw.floatValue, stop.floatValue, pitchUp.floatValue, pitchDown.floatValue);
            var live = Application.isPlaying && look.IsAttached && look.IsLooking;
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawDiagram(_topView, "Left / right (from above)", live, look.YawToTarget, true);
            GUILayout.Space(16f);
            DrawDiagram(_sideView, "Up / down (from the side)", live, look.PitchToTarget, false);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Blue: follows the target. Light blue: soft end. Grey: held at the limit. Red: the look goes back to forward.", _caption);
            EditorGUILayout.Space(4f);

            EditorGUILayout.PropertyField(maxYaw, new GUIContent("Max Yaw", "Farthest the character turns to the side, in degrees. The last 30 percent is a soft end."));
            EditorGUILayout.PropertyField(pitchUp, new GUIContent("Max Pitch Up", "Farthest it looks up, in degrees."));
            EditorGUILayout.PropertyField(pitchDown, new GUIContent("Max Pitch Down", "Farthest it looks down, in degrees."));
            EditorGUILayout.PropertyField(stop, new GUIContent("Stop Angle", "Past this angle from the front (target behind the character) the look goes back to forward."));
            EndCard();
        }

        private void DrawBonesCard(Animator rig)
        {
            BeginCard();
            var header = CardTitle("Bones");
            using (new EditorGUI.DisabledScope(rig == null || !rig.isHuman))
            {
                var button = new Rect(header.xMax - 132f, header.y, 132f, 18f);
                if (GUI.Button(button, new GUIContent("Automatic Setup", rig != null && rig.isHuman
                        ? "Fills the head and the body bones (spine, chest, neck) from the character's humanoid rig. You can edit them afterwards."
                        : "Needs a humanoid rig. On any other rig, set the bones by hand.")))
                {
                    AutomaticSetup(rig);
                }
            }

            EditorGUILayout.PropertyField(_head, new GUIContent("Head", "The head bone: it turns the most. Found by itself on a humanoid."));
            EditorGUILayout.Space(3f);
            _bodyList.DoLayoutList();
            EditorGUILayout.PropertyField(_settings.FindPropertyRelative(nameof(LookSettings.BodyTurn)),
                new GUIContent("Body Turn", "How much of the turn the neck and the spine take once they have caught up; the head takes the rest. The head always starts the turn alone and the body follows later, with smaller angles. 0 = only the head turns."));
            EndCard();
        }

        // Fills the head and the body bones from the humanoid rig; one undo step.
        private void AutomaticSetup(Animator rig)
        {
            if (HasBones() &&
                !EditorUtility.DisplayDialog("Automatic Setup", "This replaces the head and the body bones with the ones found on the humanoid rig.", "Replace", "Cancel"))
            {
                return;
            }

            if (!LookDefaults.FillHumanoid(rig, out var head, out var chain))
            {
                EditorUtility.DisplayDialog("Automatic Setup", "No head bone was found on this humanoid rig. Set the bones by hand.", "OK");
                return;
            }

            _head.objectReferenceValue = head;
            _chain.arraySize = chain.Length;
            for (var i = 0; i < chain.Length; i++)
            {
                var entry = _chain.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue = chain[i].Bone;
                entry.FindPropertyRelative(nameof(LookBone.Weight)).floatValue = chain[i].Weight;
            }

            GUI.changed = true;
        }

        private void BuildList()
        {
            _bodyList = new ReorderableList(serializedObject, _chain, true, true, true, true)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 6f,
                drawHeaderCallback = rect =>
                {
                    GUI.Label(new Rect(rect.x, rect.y, rect.width - 150f, rect.height), "Body bones (top of the list = lowest bone)", EditorStyles.miniBoldLabel);
                    GUI.Label(new Rect(rect.xMax - 140f, rect.y, 140f, rect.height), "Share of the body's turn", EditorStyles.miniLabel);
                },
                drawElementCallback = (rect, index, active, focused) =>
                {
                    var entry = _chain.GetArrayElementAtIndex(index);
                    var bone = entry.FindPropertyRelative(nameof(LookBone.Bone));
                    var weight = entry.FindPropertyRelative(nameof(LookBone.Weight));
                    rect.y += 3f;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    var boneRect = new Rect(rect.x, rect.y, rect.width * 0.5f - 4f, rect.height);
                    var sliderRect = new Rect(boneRect.xMax + 6f, rect.y, rect.width * 0.5f - 52f, rect.height);
                    var percentRect = new Rect(sliderRect.xMax + 4f, rect.y, 42f, rect.height);
                    EditorGUI.PropertyField(boneRect, bone, GUIContent.none);
                    weight.floatValue = GUI.HorizontalSlider(sliderRect, weight.floatValue, 0f, 1f);
                    GUI.Label(percentRect, Percent(index) + "%", EditorStyles.miniLabel);
                },
                onAddCallback = list =>
                {
                    var index = _chain.arraySize;
                    _chain.arraySize++;
                    var entry = _chain.GetArrayElementAtIndex(index);
                    entry.FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue = null;
                    entry.FindPropertyRelative(nameof(LookBone.Weight)).floatValue = 0.15f;
                    list.index = index;
                },
                drawNoneElementCallback = rect => GUI.Label(rect, "No body bones: only the head turns.", EditorStyles.miniLabel)
            };
        }

        // The share of the body's turn that a bone takes, in percent: its number against the sum of all of them.
        private int Percent(int index)
        {
            var sum = 0f;
            var count = 0;
            for (var i = 0; i < _chain.arraySize; i++)
            {
                var entry = _chain.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue != null)
                {
                    sum += entry.FindPropertyRelative(nameof(LookBone.Weight)).floatValue;
                    count++;
                }
            }

            var own = _chain.GetArrayElementAtIndex(index);
            if (own.FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue == null || count == 0)
            {
                return 0;
            }

            var share = sum > 0f ? own.FindPropertyRelative(nameof(LookBone.Weight)).floatValue / sum : 1f / count;
            return Mathf.RoundToInt(share * 100f);
        }

        private bool HasBones()
        {
            for (var i = 0; i < _chain.arraySize; i++)
            {
                if (_chain.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue != null)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Diagrams

        // The two diagrams are small textures drawn from the numbers; they are rebuilt only when a number changes.
        private void EnsureDiagrams(float maxYaw, float stopAngle, float pitchUp, float pitchDown)
        {
            var topKey = new Vector2(maxYaw, stopAngle);
            if (_topView == null || _topKey != topKey)
            {
                DestroyTexture(ref _topView);
                _topView = BuildTopView(maxYaw, stopAngle);
                _topKey = topKey;
            }

            var sideKey = new Vector3(pitchUp, pitchDown, 0f);
            if (_sideView == null || _sideKey != sideKey)
            {
                DestroyTexture(ref _sideView);
                _sideView = BuildSideView(pitchUp, pitchDown);
                _sideKey = sideKey;
            }
        }

        private static void DrawDiagram(Texture2D texture, string caption, bool live, float angle, bool fromAbove)
        {
            GUILayout.BeginVertical(GUILayout.Width(DiagramSize));
            var rect = GUILayoutUtility.GetRect(DiagramSize, DiagramSize, GUILayout.Width(DiagramSize), GUILayout.Height(DiagramSize));
            Rounded(rect, EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.14f) : new Color(0.70f, 0.70f, 0.72f), 6f);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
                if (live)
                {
                    var radians = angle * Mathf.Deg2Rad;
                    var reach = DiagramSize * 0.5f * 0.78f;
                    var centre = rect.center;
                    var dot = fromAbove
                        ? new Vector2(centre.x + Mathf.Sin(radians) * reach, centre.y - Mathf.Cos(radians) * reach)
                        : new Vector2(centre.x + Mathf.Cos(radians) * reach, centre.y - Mathf.Sin(radians) * reach);
                    EditorGUI.DrawRect(new Rect(dot.x - 4f, dot.y - 4f, 8f, 8f), Color.black);
                    EditorGUI.DrawRect(new Rect(dot.x - 3f, dot.y - 3f, 6f, 6f), Color.white);
                }
            }

            GUILayout.Label(caption, _caption, GUILayout.Width(DiagramSize));
            GUILayout.EndVertical();
        }

        // From above: the character is the dot in the middle, facing up. Colored by how far from the front a direction is.
        private static Texture2D BuildTopView(float maxYaw, float stopAngle)
        {
            var knee = maxYaw * LookSettings.SoftStart;
            return BuildDiagram((x, y) =>
            {
                var angle = Mathf.Abs(Mathf.Atan2(x, y)) * Mathf.Rad2Deg;
                if (angle <= knee) return Solid;
                if (angle <= maxYaw) return Soft;
                return angle <= stopAngle ? Held : Behind;
            }, false);
        }

        // From the side: the character faces right. Colored by how far above or below the horizon a direction is.
        private static Texture2D BuildSideView(float pitchUp, float pitchDown)
        {
            return BuildDiagram((x, y) =>
            {
                if (x <= 0f)
                {
                    return Behind;
                }

                var pitch = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
                var limit = pitch >= 0f ? pitchUp : pitchDown;
                var magnitude = Mathf.Abs(pitch);
                if (magnitude <= limit * LookSettings.SoftStart) return Solid;
                return magnitude <= limit ? Soft : Held;
            }, true);
        }

        private static readonly Color Solid = new Color(0.31f, 0.67f, 1f, 0.95f);
        private static readonly Color Soft = new Color(0.31f, 0.67f, 1f, 0.50f);
        private static readonly Color Held = new Color(0.60f, 0.66f, 0.74f, 0.35f);
        private static readonly Color Behind = new Color(0.95f, 0.40f, 0.40f, 0.26f);

        // A disc colored by a function of the direction (x to the right, y up, from the middle), 2 x 2 samples a pixel.
        private static Texture2D BuildDiagram(System.Func<float, float, Color> zone, bool facingRight)
        {
            const int size = DiagramSize;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            var middle = size * 0.5f;
            var radius = middle - 3f;
            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var sum = Color.clear;
                    var alpha = 0f;
                    for (var sy = 0; sy < 2; sy++)
                    {
                        for (var sx = 0; sx < 2; sx++)
                        {
                            var x = px + 0.25f + 0.5f * sx - middle;
                            var y = py + 0.25f + 0.5f * sy - middle;
                            var distance = Mathf.Sqrt(x * x + y * y);
                            Color sample;
                            if (distance <= 4f)
                            {
                                sample = new Color(1f, 1f, 1f, 1f); // the character
                            }
                            else if (IsForwardMark(x, y, facingRight))
                            {
                                sample = new Color(1f, 1f, 1f, 0.9f);
                            }
                            else if (distance <= radius)
                            {
                                sample = zone(x, y);
                            }
                            else
                            {
                                continue;
                            }

                            sum += new Color(sample.r * sample.a, sample.g * sample.a, sample.b * sample.a, 0f);
                            alpha += sample.a;
                        }
                    }

                    pixels[py * size + px] = alpha > 0f ? new Color(sum.r / alpha, sum.g / alpha, sum.b / alpha, alpha * 0.25f) : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // A short tick from the character towards the front (up in the top view, to the right in the side view).
        private static bool IsForwardMark(float x, float y, bool facingRight)
        {
            return facingRight
                ? Mathf.Abs(y) <= 1f && x >= 4f && x <= 15f
                : Mathf.Abs(x) <= 1f && y >= 4f && y <= 15f;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }

        #endregion

        #region Drawing helpers

        private static void EnsureStyles()
        {
            if (_title != null)
            {
                return;
            }

            var muted = EditorGUIUtility.isProSkin ? new Color(0.66f, 0.70f, 0.76f) : new Color(0.28f, 0.32f, 0.38f);
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            _subtitle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            _subtitle.normal.textColor = muted;
            _section = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _note = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
            _caption = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _caption.normal.textColor = muted;
            _chip = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            _cardPadding = new GUIStyle { padding = new RectOffset(12, 12, 8, 10), margin = new RectOffset(0, 0, 4, 4) };
        }

        private static void BeginCard()
        {
            var rect = EditorGUILayout.BeginVertical(_cardPadding);
            var border = EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.13f) : new Color(0.60f, 0.60f, 0.62f);
            var fill = EditorGUIUtility.isProSkin ? new Color(0.265f, 0.265f, 0.275f) : new Color(0.88f, 0.88f, 0.89f);
            Rounded(rect, border, 7f);
            Rounded(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), fill, 6f);
        }

        private static void EndCard() => EditorGUILayout.EndVertical();

        // The card's title row: a small accent bar and the name. Returns the row, so a button can sit at its right end.
        private static Rect CardTitle(string title)
        {
            var rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 4f, 3f, 14f), Accent);
            }

            GUI.Label(new Rect(rect.x + 9f, rect.y, rect.width - 9f, rect.height), title, _section);
            return rect;
        }

        private static void Chip(Rect rect, string text, Color color)
        {
            Rounded(rect, new Color(color.r, color.g, color.b, 0.28f), rect.height * 0.5f);
            var previous = GUI.contentColor;
            GUI.contentColor = EditorGUIUtility.isProSkin ? Color.Lerp(color, Color.white, 0.35f) : Color.Lerp(color, Color.black, 0.45f);
            GUI.Label(new Rect(rect.x + 6f, rect.y, rect.width - 12f, rect.height), text, _chip);
            GUI.contentColor = previous;
        }

        // A filled rectangle with rounded corners (drawn on repaint only).
        private static void Rounded(Rect rect, Color color, float radius)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, Vector4.zero, new Vector4(radius, radius, radius, radius));
        }

        #endregion
    }
}
