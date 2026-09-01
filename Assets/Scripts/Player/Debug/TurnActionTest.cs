using UnityEngine;
using UnityEngine.InputSystem;

public class TurnActionTest : MonoBehaviour
{
    [SerializeField] private InputActionReference turnAction;

    private void OnEnable()
    {
        Debug.Log($"Reference assigned: {turnAction != null}");

        if (turnAction != null)
        {
            Debug.Log($"Action object exists: {turnAction.action != null}");

            if (turnAction.action != null)
            {
                Debug.Log($"Action name: {turnAction.action.name}");
                turnAction.action.Enable();
            }
        }
    }

    private void Update()
    {
        if (turnAction?.action == null)
        {
            return;
        }

        Vector2 value = turnAction.action.ReadValue<Vector2>();

        if (value.sqrMagnitude > 0.01f)
        {
            Debug.Log($"TURN ACTION: {value}");
        }
    }
}