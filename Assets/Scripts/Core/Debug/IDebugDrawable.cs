namespace Core
{
    /// <summary>
    /// Anything that draws debug wireframes (the shapes that used to be
    /// Gizmos-only). The drawing is written once, in DrawDebug(), against
    /// DebugLines - which sends it either to the Scene view's Gizmos or to
    /// the in-headset line mesh (InHeadsetGizmos), so the two always match.
    ///
    /// The pattern for an implementer:
    /// - OnEnable/OnDisable: DebugDrawRegistry.Register(this)/Unregister(this),
    ///   so InHeadsetGizmos can find it without a scene search.
    /// - OnDrawGizmos: DebugLines.ForGizmos.Draw(this, false).
    /// - OnDrawGizmosSelected (if it has a detailed view):
    ///   DebugLines.ForGizmos.Draw(this, true).
    /// </summary>
    public interface IDebugDrawable
    {
        /// <summary>
        /// Draws this object's debug shapes into lines. detailed is the
        /// "selected in the editor" view - brighter and with extra markers;
        /// in the headset there's no selection, so InHeadsetGizmos has a
        /// setting for which one to show. Matrix and Color start reset to
        /// identity and white.
        /// </summary>
        void DrawDebug(DebugLines lines, bool detailed);
    }
}
