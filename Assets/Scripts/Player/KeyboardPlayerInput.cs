using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keyboard controls for one character, for testing without the Arduino controllers.
/// Each key just calls Press / Release on the PlayerMover, exactly like the Arduino input will.
/// Pick different keys for each player in the Inspector.
/// </summary>
[RequireComponent(typeof(PlayerMover))]
public class KeyboardPlayerInput : MonoBehaviour
{
    public Key north = Key.W;
    public Key east = Key.D;
    public Key south = Key.S;
    public Key west = Key.A;

    private PlayerMover mover;

    private void Awake()
    {
        mover = GetComponent<PlayerMover>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return; // no keyboard connected

        Check(keyboard[north], Vector2Int.up);
        Check(keyboard[east], Vector2Int.right);
        Check(keyboard[south], Vector2Int.down);
        Check(keyboard[west], Vector2Int.left);
    }

    private void Check(UnityEngine.InputSystem.Controls.KeyControl key, Vector2Int direction)
    {
        if (key.wasPressedThisFrame) mover.Press(direction);
        if (key.wasReleasedThisFrame) mover.Release(direction);
    }
}
