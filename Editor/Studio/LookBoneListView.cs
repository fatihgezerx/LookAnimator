using System.Collections.Generic;
using Veneer;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LookAnimation
{
    /// <summary>
    /// The list of body bones of a Look Animator in the studio look: a header with the bone count and add / remove buttons, then a
    /// reorderable bound list whose rows show the bone, a slider for its number and the share of the body's turn that number gives
    /// it, in percent (its number against the sum of all of them). It edits the same <c>_chain</c> entries as before, so nothing
    /// about the saved data changes. The top of the list is the lowest bone.
    /// </summary>
    internal sealed class LookBoneListView : VisualElement
    {
        private const float DefaultShare = 0.15f;
        private const float RowHeight = 28f;

        private readonly SerializedProperty chain;
        private readonly ListView list;
        private readonly Label countPill;
        private readonly Label emptyNotice;
        private readonly List<RowState> rows = new List<RowState>(8);
        private int shownCount = -1;

        /// <summary>Makes the list and binds it to the serialized object of <paramref name="chainProperty"/>.</summary>
        /// <param name="chainProperty">The <c>_chain</c> list property.</param>
        /// <param name="brand">Brand for icon lookup.</param>
        internal LookBoneListView(SerializedProperty chainProperty, VeneerBrand brand)
        {
            chain = chainProperty.Copy();
            AddToClassList("vn-list");
            tooltip = chain.tooltip;

            var header = new VisualElement();
            header.AddToClassList("vn-list__header");
            var title = new Label("Body bones (top = lowest bone)");
            title.AddToClassList("vn-list__title");
            header.Add(title);
            countPill = VeneerStyle.Pill("0", "info");
            header.Add(countPill);
            header.Add(VeneerStyle.Spacer());
            Button add = VeneerStyle.IconButton("plus", "Add a bone at the end.", null, brand);
            Button remove = VeneerStyle.IconButton("minus", "Remove the selected bones (the last one when none is selected).", null, brand);
            add.clicked += AddBone;
            remove.clicked += RemoveBones;
            header.Add(add);
            header.Add(remove);
            Add(header);

            var columns = new VisualElement();
            columns.AddToClassList("look-bones__columns");
            var columnsText = new Label("Share of the body's turn");
            columnsText.AddToClassList("look-bones__columns-text");
            columns.Add(columnsText);
            Add(columns);

            emptyNotice = VeneerStyle.MutedLabel("No body bones: only the head turns.");
            emptyNotice.AddToClassList("vn-list__notice");
            Add(emptyNotice);

            list = new ListView
            {
                bindingPath = chain.propertyPath,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = RowHeight,
                selectionType = SelectionType.Multiple,
                showAddRemoveFooter = false,
                showFoldoutHeader = false,
                showBoundCollectionSize = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };
            list.AddToClassList("vn-list__view");
            list.makeItem = MakeItem;
            list.bindItem = BindItem;
            list.unbindItem = UnbindItem;
            Add(list);
            list.Bind(chain.serializedObject);

            // The count, the empty note and the percentages follow every change of the data (a slider drag, a reorder, Undo).
            this.TrackSerializedObjectValue(chain.serializedObject, changed => Refresh());
            RegisterCallback<AttachToPanelEvent>(evt => Refresh());
        }

        private sealed class RowState
        {
            public VisualElement Body;
            public Label Percent;
            public int Index = -1;
            public int Shown = -1;
        }

        private VisualElement MakeItem()
        {
            var body = new VisualElement();
            body.AddToClassList("look-bone-row");
            var state = new RowState { Body = body };
            body.userData = state;
            rows.Add(state);
            return body;
        }

        // The controls of a row are rebuilt on every bind: they are bound to the element at this index, and a rebuilt control cannot
        // keep a stale property path after a reorder or a removal.
        private void BindItem(VisualElement element, int index)
        {
            var state = (RowState)element.userData;
            state.Index = index;
            state.Shown = -1;
            element.Clear();
            if (index < 0 || index >= chain.arraySize)
            {
                return;
            }

            var entry = chain.GetArrayElementAtIndex(index);
            var bone = entry.FindPropertyRelative(nameof(LookBone.Bone));
            var weight = entry.FindPropertyRelative(nameof(LookBone.Weight));

            var boneField = new ObjectField
            {
                objectType = typeof(Transform),
                allowSceneObjects = !EditorUtility.IsPersistent(chain.serializedObject.targetObject),
                tooltip = "The bone. It must be under the Animator's object."
            };
            boneField.AddToClassList("look-bone-row__bone");
            boneField.BindProperty(bone);
            element.Add(boneField);

            var share = new Slider(0f, 1f) { tooltip = weight.tooltip };
            share.AddToClassList("look-bone-row__share");
            share.BindProperty(weight);
            element.Add(share);

            var percent = new Label("0%");
            percent.AddToClassList("look-bone-row__percent");
            element.Add(percent);
            state.Percent = percent;

            Refresh();
        }

        private void UnbindItem(VisualElement element, int index)
        {
            var state = (RowState)element.userData;
            state.Index = -1;
            state.Percent = null;
            element.Clear();
        }

        // Growing a list copies the last element: the new bone starts empty with the default share instead.
        private void AddBone()
        {
            var serializedObject = chain.serializedObject;
            serializedObject.Update();
            var index = chain.arraySize;
            chain.arraySize = index + 1;
            var entry = chain.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue = null;
            entry.FindPropertyRelative(nameof(LookBone.Weight)).floatValue = DefaultShare;
            serializedObject.ApplyModifiedProperties();
            list.RefreshItems();
            list.ScrollToItem(index);
        }

        private void RemoveBones()
        {
            var serializedObject = chain.serializedObject;
            serializedObject.Update();
            var indices = new List<int>(list.selectedIndices);
            if (indices.Count == 0 && chain.arraySize > 0)
            {
                indices.Add(chain.arraySize - 1);
            }

            indices.Sort((a, b) => b.CompareTo(a));
            for (var i = 0; i < indices.Count; i++)
            {
                var index = indices[i];
                if (index >= 0 && index < chain.arraySize)
                {
                    chain.DeleteArrayElementAtIndex(index);
                }
            }

            serializedObject.ApplyModifiedProperties();
            list.ClearSelection();
            list.RefreshItems();
        }

        // Updates the count, the empty note and every row's percentage; text is written only when it changed.
        private void Refresh()
        {
            if (chain.serializedObject.targetObject == null)
            {
                return;
            }

            chain.serializedObject.UpdateIfRequiredOrScript();
            var count = chain.arraySize;
            if (count != shownCount)
            {
                shownCount = count;
                VeneerStyle.SetPill(countPill, "info", count.ToString());
                VeneerStyle.SetDisplay(emptyNotice, count == 0);
            }

            // A bone's share of the body's turn: its number against the sum of the numbers of all set bones.
            var sum = 0f;
            var set = 0;
            for (var i = 0; i < count; i++)
            {
                var entry = chain.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue != null)
                {
                    sum += entry.FindPropertyRelative(nameof(LookBone.Weight)).floatValue;
                    set++;
                }
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var state = rows[i];
                if (state.Index < 0 || state.Index >= count || state.Percent == null)
                {
                    continue;
                }

                var own = chain.GetArrayElementAtIndex(state.Index);
                var percent = 0;
                if (own.FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue != null && set > 0)
                {
                    var share = sum > 0f ? own.FindPropertyRelative(nameof(LookBone.Weight)).floatValue / sum : 1f / set;
                    percent = Mathf.RoundToInt(share * 100f);
                }

                if (percent != state.Shown)
                {
                    state.Shown = percent;
                    state.Percent.text = percent + "%";
                }
            }
        }
    }
}
