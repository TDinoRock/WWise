namespace Arena
{
    /// <summary>
    /// The entire contract between an input device and <see cref="GridMover"/>.
    ///
    /// <list type="bullet">
    ///   <item><see cref="HeldDirection"/> — the direction currently held, or None. Drives continuous movement.</item>
    ///   <item><see cref="PressedThisFrame"/> — a direction that became held this frame, or None.
    ///         GridMover buffers this so a tap mid-step still fires on the next landing.</item>
    /// </list>
    ///
    /// Keyboard, gamepad and (later) Arduino serial all implement this; movement never sees the device.
    /// </summary>
    public interface IPlayerInputSource
    {
        GridDirection HeldDirection { get; }
        GridDirection PressedThisFrame { get; }
    }
}
