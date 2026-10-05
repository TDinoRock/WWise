using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Controls for one character, from its Arduino controller and/or the keyboard.
/// Both just call Press / Release on the PlayerMover, so movement behaves the same either way.
///
/// ARDUINO SETUP (Ardity)
/// Put a SerialController (from Ardity) in the scene for this player's COM port and set its
/// "Message Listener" to THIS character's GameObject. Ardity then calls OnMessageArrived(text)
/// here for every line the Arduino prints with Serial.println(...).
///
/// The Arduino may either:
///   - print the direction ("Up") once when pressed and a release line ("UpRelease") when let go, or
///   - keep printing the direction while the button is held (e.g. every 100 ms).
/// "Release After" handles the second style and also stops the character if a release line is lost.
/// </summary>
[RequireComponent(typeof(PlayerMover))]
public class KeyboardPlayerInput : MonoBehaviour
{
    [Header("Keyboard (for testing without a controller)")]
    public bool useKeyboard = true;
    public Key north = Key.W;
    public Key east = Key.D;
    public Key south = Key.S;
    public Key west = Key.A;
    public Key action = Key.Space;

    [Header("Arduino messages (must match the text the Arduino prints)")]
    public string northMessage = "Up";
    public string eastMessage = "Right";
    public string southMessage = "Down";
    public string westMessage = "Left";
    public string actionMessage = "Action";
    [Tooltip("Added to a direction to mean 'button let go', e.g. \"Up\" + \"Release\" = \"UpRelease\". Leave empty if the Arduino doesn't send releases.")]
    public string releaseSuffix = "Release";
    [Tooltip("Seconds without hearing a direction again before it counts as released. " +
             "Needed if the Arduino doesn't send releases. 0 = only release on a release message.")]
    [Min(0f)] public float releaseAfter = 0.25f;
    [Tooltip("Print every Arduino message to the Console.")]
    public bool logMessages = false;

    [Header("Action button")]
    [Tooltip("What the action button does. Hook up abilities here later (e.g. Paladin push).")]
    public UnityEvent onAction = new UnityEvent();

    [Header("Arduino status (shown for debugging)")]
    public bool arduinoConnected;
    public string lastMessage = "";

    private PlayerMover mover;

    // When each direction was last heard from the Arduino (index matches Directions below).
    private readonly float[] lastHeard = new float[4];
    private readonly bool[] arduinoHeld = new bool[4];
    private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    private void Awake()
    {
        mover = GetComponent<PlayerMover>();
    }

    private void Update()
    {
        if (useKeyboard) ReadKeyboard();

        // Auto-release Arduino directions that have gone quiet.
        if (releaseAfter > 0f)
            for (int i = 0; i < 4; i++)
                if (arduinoHeld[i] && Time.time - lastHeard[i] > releaseAfter)
                    ReleaseArduino(i);
    }

    // ---------------------------------------------------------------- keyboard

    private void ReadKeyboard()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return; // no keyboard connected

        CheckKey(keyboard[north], Vector2Int.up);
        CheckKey(keyboard[east], Vector2Int.right);
        CheckKey(keyboard[south], Vector2Int.down);
        CheckKey(keyboard[west], Vector2Int.left);
        if (keyboard[action].wasPressedThisFrame) onAction.Invoke();
    }

    private void CheckKey(UnityEngine.InputSystem.Controls.KeyControl key, Vector2Int direction)
    {
        if (key.wasPressedThisFrame) mover.Press(direction);
        if (key.wasReleasedThisFrame) mover.Release(direction);
    }

    // ---------------------------------------------------------------- Arduino (called by Ardity's SerialController)

    // Ardity calls this for each line the Arduino prints.
    private void OnMessageArrived(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        message = message.Trim();
        lastMessage = message;
        if (logMessages) Debug.Log($"[{name}] Arduino: {message}", this);

        if (message == actionMessage) { onAction.Invoke(); return; }

        for (int i = 0; i < 4; i++)
        {
            string press = DirectionMessage(i);
            if (message == press)
            {
                lastHeard[i] = Time.time;
                if (!arduinoHeld[i]) { arduinoHeld[i] = true; mover.Press(Directions[i]); }
                return;
            }
            if (!string.IsNullOrEmpty(releaseSuffix) && message == press + releaseSuffix)
            {
                ReleaseArduino(i);
                return;
            }
        }
    }

    // Ardity calls this when the COM port connects (true) or disconnects / fails (false).
    private void OnConnectionEvent(bool success)
    {
        arduinoConnected = success;
        Debug.Log($"[{name}] Arduino {(success ? "connected" : "disconnected")}", this);

        // Don't keep walking if the cable is pulled while a button is held.
        if (!success) for (int i = 0; i < 4; i++) ReleaseArduino(i);
    }

    private void ReleaseArduino(int i)
    {
        if (!arduinoHeld[i]) return;
        arduinoHeld[i] = false;
        mover.Release(Directions[i]);
    }

    private string DirectionMessage(int i) => i switch
    {
        0 => northMessage,
        1 => eastMessage,
        2 => southMessage,
        3 => westMessage,
    };
}
