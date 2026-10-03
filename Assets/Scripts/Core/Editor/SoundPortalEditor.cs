using UnityEditor;

namespace Core
{
    /// <summary>
    /// The Inspector for a SoundPortal: the normal fields, then a box
    /// saying which two rooms it joins right now - worked out live from
    /// where the portal and the rooms are, so it updates as either is
    /// moved - or a warning if it joins nothing. Lets a portal be checked
    /// while placing it, rather than from a Console warning after pressing
    /// Play.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    [CustomEditor(typeof(SoundPortal))]
    [CanEditMultipleObjects]
    public class SoundPortalEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // With several portals selected there's no single answer to
            // show.
            if (targets.Length > 1) {
                return;
            }

            SoundPortal portal = (SoundPortal)target;

            // A disabled portal (or one on an inactive object) isn't part
            // of the level's sound at all.
            if (!portal.isActiveAndEnabled) {
                EditorGUILayout.HelpBox("Disabled: sound doesn't pass through this portal.", MessageType.Info);
                return;
            }

            portal.ProbeRooms(out SoundRoom front, out SoundRoom back);

            if (front == back) {
                EditorGUILayout.HelpBox(
                    $"Joins nothing: both sides are in {RoomName(front)}. Check its position, which way it faces (local Z goes through the opening) and Probe Distance.",
                    MessageType.Warning);
            } else {
                EditorGUILayout.HelpBox($"Joins {RoomName(front)} (front, +Z) and {RoomName(back)} (back, -Z).", MessageType.Info);
            }
        }

        /// <summary>
        /// A room's name for the box, "outside" for no room.
        /// </summary>
        private static string RoomName(SoundRoom room)
        {
            return room != null ? $"'{room.name}'" : "outside";
        }
    }
}
