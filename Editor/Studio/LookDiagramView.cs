using System;
using Veneer;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LookAnimation
{
    /// <summary>
    /// The two small diagrams of the limits in the studio look: the turn seen from above (left / right, the stop angle) and from the
    /// side (up / down). They are textures drawn from the numbers and rebuilt only when a number changes; while the game plays, a dot in
    /// each shows where the target is, refreshed five times a second. The textures exist only while the view is on a panel.
    /// </summary>
    internal sealed class LookDiagramView : VisualElement
    {
        private const int Size = 112;
        private const long TickMilliseconds = 200;
        private const string LegendText = "Pink: follows the target. Light pink: soft end. Grey: held at the limit. Orange: the look goes back to forward.";

        private readonly SerializedObject serializedObject;
        private readonly LookAnimator look;
        private readonly SerializedProperty maxYaw;
        private readonly SerializedProperty stopAngle;
        private readonly SerializedProperty pitchUp;
        private readonly SerializedProperty pitchDown;
        private readonly Image topImage;
        private readonly Image sideImage;
        private readonly VisualElement topDot;
        private readonly VisualElement sideDot;
        private readonly Color solid;
        private readonly Color soft;
        private readonly IVisualElementScheduledItem tick;
        private Texture2D topTexture;
        private Texture2D sideTexture;
        private Vector2 topKey = new Vector2(-1f, -1f);
        private Vector2 sideKey = new Vector2(-1f, -1f);
        private Vector2Int topDotAt = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int sideDotAt = new Vector2Int(int.MinValue, int.MinValue);
        private bool dotsShown;

        private static readonly Color Held = new Color(0.60f, 0.66f, 0.74f, 0.35f);
        private static readonly Color Behind = new Color(1f, 0.58f, 0.20f, 0.30f);

        /// <summary>Makes the diagrams for the Look Animator behind <paramref name="serializedObject"/>.</summary>
        /// <param name="serializedObject">The serialized Look Animator.</param>
        /// <param name="look">The component, for the live angles while playing.</param>
        /// <param name="accent">The brand accent: the colour of the zone where the look follows the target.</param>
        internal LookDiagramView(SerializedObject serializedObject, LookAnimator look, Color accent)
        {
            this.serializedObject = serializedObject;
            this.look = look;
            var settings = serializedObject.FindProperty("_settings");
            maxYaw = settings.FindPropertyRelative(nameof(LookSettings.MaxYaw));
            stopAngle = settings.FindPropertyRelative(nameof(LookSettings.StopAngle));
            pitchUp = settings.FindPropertyRelative(nameof(LookSettings.MaxPitchUp));
            pitchDown = settings.FindPropertyRelative(nameof(LookSettings.MaxPitchDown));
            solid = new Color(accent.r, accent.g, accent.b, 0.95f);
            soft = new Color(accent.r, accent.g, accent.b, 0.50f);

            var row = new VisualElement();
            row.AddToClassList("look-diagrams");
            row.Add(MakeColumn("Left / right (from above)", out topImage, out topDot));
            row.Add(MakeColumn("Up / down (from the side)", out sideImage, out sideDot));
            Add(row);

            var legend = new Label(LegendText);
            legend.AddToClassList("look-legend");
            Add(legend);

            tick = schedule.Execute(Tick).Every(TickMilliseconds);
            tick.Pause();
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            this.TrackSerializedObjectValue(serializedObject, changed => Rebuild());
        }

        private static VisualElement MakeColumn(string caption, out Image image, out VisualElement dot)
        {
            var column = new VisualElement();
            column.AddToClassList("look-diagram-column");

            var frame = new VisualElement();
            frame.AddToClassList("look-diagram");
            image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList("look-diagram__image");
            frame.Add(image);
            dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("look-diagram__dot");
            frame.Add(dot);
            column.Add(frame);

            var label = new Label(caption);
            label.AddToClassList("look-diagram__caption");
            column.Add(label);
            return column;
        }

        private void OnAttached(AttachToPanelEvent evt)
        {
            Rebuild();
            tick.Resume();
        }

        private void OnDetached(DetachFromPanelEvent evt)
        {
            tick.Pause();
            Release(ref topTexture, topImage);
            Release(ref sideTexture, sideImage);
            topKey = new Vector2(-1f, -1f);
            sideKey = new Vector2(-1f, -1f);
        }

        private static void Release(ref Texture2D texture, Image image)
        {
            image.image = null;
            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(texture);
                texture = null;
            }
        }

        // The textures are rebuilt only when the numbers they are drawn from changed.
        private void Rebuild()
        {
            if (panel == null || serializedObject.targetObject == null)
            {
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();
            var topNow = new Vector2(maxYaw.floatValue, stopAngle.floatValue);
            if (topTexture == null || topNow != topKey)
            {
                Release(ref topTexture, topImage);
                topTexture = BuildTopView(topNow.x, topNow.y);
                topImage.image = topTexture;
                topKey = topNow;
            }

            var sideNow = new Vector2(pitchUp.floatValue, pitchDown.floatValue);
            if (sideTexture == null || sideNow != sideKey)
            {
                Release(ref sideTexture, sideImage);
                sideTexture = BuildSideView(sideNow.x, sideNow.y);
                sideImage.image = sideTexture;
                sideKey = sideNow;
            }
        }

        // While playing and looking, a dot per diagram shows where the target is (the angles are as of the look's last step).
        private void Tick()
        {
            if (panel == null)
            {
                return;
            }

            var live = Application.isPlaying && look != null && look.IsAttached && look.IsLooking;
            if (live != dotsShown)
            {
                dotsShown = live;
                VeneerStyle.SetDisplay(topDot, live);
                VeneerStyle.SetDisplay(sideDot, live);
                if (!live)
                {
                    topDotAt = new Vector2Int(int.MinValue, int.MinValue);
                    sideDotAt = new Vector2Int(int.MinValue, int.MinValue);
                }
            }

            if (!live)
            {
                return;
            }

            const float reach = Size * 0.5f * 0.78f;
            const float centre = Size * 0.5f;
            var yaw = look.YawToTarget * Mathf.Deg2Rad;
            var pitch = look.PitchToTarget * Mathf.Deg2Rad;
            Place(topDot, ref topDotAt, centre + Mathf.Sin(yaw) * reach, centre - Mathf.Cos(yaw) * reach);
            Place(sideDot, ref sideDotAt, centre + Mathf.Cos(pitch) * reach, centre - Mathf.Sin(pitch) * reach);
        }

        // Moves a dot (centre at x, y inside the diagram) only when it moved by at least a pixel.
        private static void Place(VisualElement dot, ref Vector2Int at, float x, float y)
        {
            var left = Mathf.RoundToInt(x - 4f);
            var top = Mathf.RoundToInt(y - 4f);
            if (at.x == left && at.y == top)
            {
                return;
            }

            at = new Vector2Int(left, top);
            dot.style.left = left;
            dot.style.top = top;
        }

        // From above: the character is the dot in the middle, facing up. Coloured by how far from the front a direction is.
        private Texture2D BuildTopView(float yawLimit, float stop)
        {
            var knee = yawLimit * LookSettings.SoftStart;
            return BuildDiagram((x, y) =>
            {
                var angle = Mathf.Abs(Mathf.Atan2(x, y)) * Mathf.Rad2Deg;
                if (angle <= knee) return solid;
                if (angle <= yawLimit) return soft;
                return angle <= stop ? Held : Behind;
            }, false);
        }

        // From the side: the character faces right. Coloured by how far above or below the horizon a direction is.
        private Texture2D BuildSideView(float up, float down)
        {
            return BuildDiagram((x, y) =>
            {
                if (x <= 0f)
                {
                    return Behind;
                }

                var pitch = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
                var limit = pitch >= 0f ? up : down;
                var magnitude = Mathf.Abs(pitch);
                if (magnitude <= limit * LookSettings.SoftStart) return solid;
                return magnitude <= limit ? soft : Held;
            }, true);
        }

        // A disc coloured by a function of the direction (x to the right, y up, from the middle), 2 x 2 samples a pixel.
        private static Texture2D BuildDiagram(Func<float, float, Color> zone, bool facingRight)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[Size * Size];
            var middle = Size * 0.5f;
            var radius = middle - 3f;
            for (var py = 0; py < Size; py++)
            {
                for (var px = 0; px < Size; px++)
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

                    pixels[py * Size + px] = alpha > 0f ? new Color(sum.r / alpha, sum.g / alpha, sum.b / alpha, alpha * 0.25f) : Color.clear;
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
    }
}
