using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Arena
{
    /// <summary>
    /// The one input component. Reads either a shared keyboard (four players on one board) or a
    /// Vector2 action from a <see cref="PlayerInput"/> (gamepad, device pairing, split-screen).
    ///
    /// An Arduino serial source later just needs its own <see cref="IPlayerInputSource"/>; nothing
    /// in movement changes.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerInputSource : MonoBehaviour, IPlayerInputSource
    {
        public enum Mode
        {
            /// <summary>Read four keys straight off Keyboard.current.</summary>
            Keyboard = 0,
            /// <summary>Read a Vector2 action from a PlayerInput component on this GameObject.</summary>
            InputAction = 1,
        }

        public enum KeyboardLayout { Custom = 0, WASD = 1, Arrows = 2, IJKL = 3, Numpad = 4 }

        [Header("Mode")]
        [SerializeField] private Mode mode = Mode.Keyboard;

        [Header("Keyboard")]
        [Tooltip("Pick a preset to fill the four keys below. Choose Custom to edit them freely.")]
        [SerializeField] private KeyboardLayout layout = KeyboardLayout.WASD;
        [SerializeField] private Key upKey = Key.W;
        [SerializeField] private Key downKey = Key.S;
        [SerializeField] private Key leftKey = Key.A;
        [SerializeField] private Key rightKey = Key.D;

        [Header("Input Action")]
        [Tooltip("Name (or 'Map/Name') of the Vector2 action on the PlayerInput's action asset.")]
        [SerializeField] private string moveActionName = "Move";

        [Tooltip("Stick magnitude below this reads as no input.")]
        [Range(0f, 0.95f)]
        [SerializeField] private float deadzone = 0.35f;

        [Tooltip("How much larger (as a ratio) the other axis must be before the held axis switches. 0 = no hysteresis, which lets a diagonal stick chatter.")]
        [Range(0f, 2f)]
        [SerializeField] private float axisHysteresis = 0.35f;

        [Header("Behaviour")]
        [Tooltip("Ignore input entirely without disabling the component.")]
        [SerializeField] private bool inputEnabled = true;

        [Header("Runtime (read-only)")]
        [InspectorReadOnly, SerializeField] private GridDirection heldDirection;
        [InspectorReadOnly, SerializeField] private GridDirection pressedThisFrame;
        [InspectorReadOnly, SerializeField] private Vector2 rawMove;
        [InspectorReadOnly, SerializeField] private string pairedDevices = "";

        public GridDirection HeldDirection => heldDirection;
        public GridDirection PressedThisFrame => pressedThisFrame;
        public Vector2 RawMove => rawMove;
        public Mode CurrentMode => mode;
        public bool InputEnabled { get => inputEnabled; set => inputEnabled = value; }

        // Keyboard directions in press order; the last entry wins, which is what players expect
        // when rolling from one key to the next.
        private readonly List<GridDirection> pressOrder = new(4);
        private PlayerInput playerInput;
        private InputAction moveAction;

        private void Awake()
        {
            if (mode == Mode.InputAction) ResolveAction();
        }

        private void OnEnable()
        {
            if (mode == Mode.InputAction && moveAction == null) ResolveAction();
        }

        private void OnDisable() => Clear();

        private void OnValidate() => ApplyLayout(layout);

        /// <summary>Fills the four keys from a preset. Custom leaves them untouched.</summary>
        public void ApplyLayout(KeyboardLayout newLayout)
        {
            layout = newLayout;
            switch (newLayout)
            {
                case KeyboardLayout.WASD:   upKey = Key.W; downKey = Key.S; leftKey = Key.A; rightKey = Key.D; break;
                case KeyboardLayout.Arrows: upKey = Key.UpArrow; downKey = Key.DownArrow; leftKey = Key.LeftArrow; rightKey = Key.RightArrow; break;
                case KeyboardLayout.IJKL:   upKey = Key.I; downKey = Key.K; leftKey = Key.J; rightKey = Key.L; break;
                case KeyboardLayout.Numpad: upKey = Key.Numpad8; downKey = Key.Numpad2; leftKey = Key.Numpad4; rightKey = Key.Numpad6; break;
            }
        }

        private void ResolveAction()
        {
            moveAction = null;
            playerInput = GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                Debug.LogWarning($"[PlayerInputSource] '{name}' is in InputAction mode but has no PlayerInput component.", this);
                return;
            }
            if (playerInput.actions == null) return;

            moveAction = playerInput.actions.FindAction(moveActionName, throwIfNotFound: false);
            if (moveAction == null)
                Debug.LogWarning($"[PlayerInputSource] Action '{moveActionName}' not found on '{name}'.", this);
        }

        private void Clear()
        {
            pressOrder.Clear();
            heldDirection = GridDirection.None;
            pressedThisFrame = GridDirection.None;
            rawMove = Vector2.zero;
        }

        private void Update()
        {
            if (!inputEnabled) { Clear(); return; }

            if (mode == Mode.Keyboard) ReadKeyboard();
            else ReadAction();
        }

        private void ReadKeyboard()
        {
            pressedThisFrame = GridDirection.None;
            rawMove = Vector2.zero;

            Keyboard kb = Keyboard.current;
            if (kb == null) { Clear(); return; }

            Track(kb[upKey], GridDirection.Up);
            Track(kb[downKey], GridDirection.Down);
            Track(kb[leftKey], GridDirection.Left);
            Track(kb[rightKey], GridDirection.Right);

            heldDirection = pressOrder.Count > 0 ? pressOrder[^1] : GridDirection.None;
            rawMove = heldDirection.ToVector();
        }

        private void Track(KeyControl key, GridDirection direction)
        {
            if (key.wasPressedThisFrame)
            {
                pressOrder.Remove(direction);
                pressOrder.Add(direction);
                pressedThisFrame = direction;
            }
            else if (!key.isPressed)
            {
                pressOrder.Remove(direction);
            }
        }

        private void ReadAction()
        {
            GridDirection previous = heldDirection;

            if (moveAction == null) { Clear(); return; }

            rawMove = moveAction.ReadValue<Vector2>();
            heldDirection = GridDirectionExtensions.FromAnalog(rawMove, previous, deadzone, axisHysteresis);
            pressedThisFrame = heldDirection != GridDirection.None && heldDirection != previous
                ? heldDirection
                : GridDirection.None;

#if UNITY_EDITOR
            pairedDevices = playerInput != null && playerInput.devices.Count > 0
                ? string.Join(", ", playerInput.devices)
                : "(none)";
#endif
        }
    }
}
