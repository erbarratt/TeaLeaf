using UnityEditor;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Scene view editing for a GodRayBox: the box's outline, a dot on each
    /// of its eight corners, and the ordinary move handle on the corner
    /// whose dot was clicked (click it again to let go; the object's own
    /// move tool is hidden meanwhile so the two never overlap). The handle
    /// turns with the object when the toolbar's pivot rotation is Local.
    /// Moves go through the serialized corners, so Undo works and the
    /// prefab/scene is marked changed.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    [CustomEditor(typeof(GodRayBox))]
    public class GodRayBoxEditor : Editor
    {
        // Dot colours: top corners (the opening), bottom corners, and the
        // one being moved.
        private static readonly Color _topColor = new(1f, 0.85f, 0.3f);
        private static readonly Color _bottomColor = new(0.5f, 0.75f, 1f);
        private static readonly Color _selectedColor = Color.white;

        // Dot size as a share of the handle size at that point (so dots
        // stay the same size on screen).
        private const float DotSize = 0.08f;

        // The corner being moved, -1 for none.
        private int _selected = -1;

        private void OnDisable()
        {
            Tools.hidden = false;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox(
                "Scene view: click a corner's dot to move it with the move handle; click it again to let go. Yellow = top (the opening), blue = bottom.",
                MessageType.Info);
        }

        private void OnSceneGUI()
        {
            GodRayBox box = (GodRayBox)target;

            if (box.CornerCount != GodRayBox.Sides * 2) {
                return;
            }

            Transform boxTransform = box.transform;
            var world = new Vector3[GodRayBox.Sides * 2];

            for (int i = 0; i < world.Length; i++) {
                world[i] = boxTransform.TransformPoint(box.GetCorner(i));
            }

            DrawOutline(world);

            // A dot per corner; clicking one picks it (or lets it go).
            for (int i = 0; i < world.Length; i++) {
                Handles.color = i == _selected ? _selectedColor : i < GodRayBox.Sides ? _topColor : _bottomColor;
                float size = HandleUtility.GetHandleSize(world[i]) * DotSize;

                if (Handles.Button(world[i], Quaternion.identity, size, size, Handles.SphereHandleCap)) {
                    _selected = _selected == i ? -1 : i;
                }
            }

            Tools.hidden = _selected >= 0;

            if (_selected < 0) {
                return;
            }

            // The move handle on the picked corner.
            Quaternion handleRotation = Tools.pivotRotation == PivotRotation.Local ? boxTransform.rotation : Quaternion.identity;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world[_selected], handleRotation);

            if (EditorGUI.EndChangeCheck()) {
                serializedObject.Update();
                SerializedProperty corners = serializedObject.FindProperty("corners");
                corners.GetArrayElementAtIndex(_selected).vector3Value = boxTransform.InverseTransformPoint(moved);
                serializedObject.ApplyModifiedProperties();

                // Rebuild now rather than a tick later, so the shaft follows
                // the drag.
                box.Rebuild();
            }
        }

        /// <summary>
        /// The box's edges: round the top, round the bottom, and down each corner.
        /// </summary>
        private static void DrawOutline(Vector3[] world)
        {
            int sides = GodRayBox.Sides;

            for (int i = 0; i < sides; i++) {
                int next = (i + 1) % sides;
                Handles.color = _topColor;
                Handles.DrawLine(world[i], world[next]);
                Handles.color = _bottomColor;
                Handles.DrawLine(world[sides + i], world[sides + next]);
                Handles.color = Color.Lerp(_topColor, _bottomColor, 0.5f);
                Handles.DrawLine(world[i], world[sides + i]);
            }
        }
    }
}
