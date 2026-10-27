using UnityEngine;
using Player;

/// <summary>
/// Draws each hand's interaction ray in the Scene view (and Game view, if
/// its Gizmos toggle is on) during Play Mode, so PlayerHandInteraction's
/// leftHandRayAngleOffset/rightHandRayAngleOffset can be tuned visually
/// instead of by guesswork.
///
/// Editor-only visualization - Debug.DrawRay never renders in a build, so
/// there's no need to strip this out for release.
/// </summary>
public class HandRayDebug : MonoBehaviour
{
    [Tooltip("The hand rays whose rays are drawn in the Scene view (Ray Length long), e.g. drag the Hands object here. Must be set: it isn't looked for.")]
    [SerializeField] private PlayerHandInteraction playerHandInteraction;

    [Tooltip("The colour the left hand's ray is drawn in, in the Scene view only, e.g. red.")]
    [SerializeField] private Color leftRayColor = Color.red;
    [Tooltip("The colour the right hand's ray is drawn in, in the Scene view only, e.g. blue.")]
    [SerializeField] private Color rightRayColor = Color.blue;

    private void Update()
    {
        Debug.DrawRay(
            playerHandInteraction.LeftRayOrigin,
            playerHandInteraction.LeftRayDirection * playerHandInteraction.RayLength,
            leftRayColor);

        Debug.DrawRay(
            playerHandInteraction.RightRayOrigin,
            playerHandInteraction.RightRayDirection * playerHandInteraction.RayLength,
            rightRayColor);
    }
}
