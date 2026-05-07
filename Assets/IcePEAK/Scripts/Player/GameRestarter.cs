using UnityEngine;
using UnityEngine.InputSystem;

namespace IcePEAK.Player
{
    /// <summary>
    /// Pressing the B button (right controller) restarts the full run:
    ///   - Resets checkpoint to original spawn point and teleports player there
    ///   - Restores all breakable ice (via FallHandler -> IceRespawner)
    ///   - Resets and restarts the climb timer
    ///   - Re-arms the destination trigger
    ///
    /// The B button binding is created in code — no Inspector action asset needed.
    /// </summary>
    public class GameRestarter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FallHandler fallHandler;
        [SerializeField] private ClimbTimer climbTimer;
        [SerializeField] private DestinationTrigger destinationTrigger;

        private InputAction _bButton;

        private void Awake()
        {
            // B button = secondaryButton on the right XR controller (Quest B / Index B)
            _bButton = new InputAction(
                name: "RestartGame",
                type: InputActionType.Button,
                binding: "<XRController>{RightHand}/secondaryButton");
        }

        private void OnEnable()
        {
            _bButton.performed += OnBPressed;
            _bButton.Enable();
        }

        private void OnDisable()
        {
            _bButton.performed -= OnBPressed;
            _bButton.Disable();
        }

        private void OnDestroy() => _bButton.Dispose();

        private void OnBPressed(InputAction.CallbackContext ctx) => Restart();

        private void Restart()
        {
            destinationTrigger?.ResetTrigger();
            climbTimer?.ResetTimer();
            fallHandler?.Restart();
        }

        [ContextMenu("TEST: Restart Now")]
        private void DebugRestart() => Restart();
    }
}
