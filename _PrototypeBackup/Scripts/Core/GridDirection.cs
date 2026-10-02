using UnityEngine;

namespace Arena
{
    /// <summary>Four-way cardinal direction. <see cref="None"/> means "no input / not moving".</summary>
    public enum GridDirection
    {
        None = 0,
        Up = 1,
        Down = 2,
        Left = 3,
        Right = 4,
    }

    public static class GridDirectionExtensions
    {
        /// <summary>Unit cell offset. Up = +Y, matching Unity's Tilemap convention.</summary>
        public static Vector2Int ToVector(this GridDirection d) => d switch
        {
            GridDirection.Up => Vector2Int.up,
            GridDirection.Down => Vector2Int.down,
            GridDirection.Left => Vector2Int.left,
            GridDirection.Right => Vector2Int.right,
            _ => Vector2Int.zero,
        };

        public static GridDirection Opposite(this GridDirection d) => d switch
        {
            GridDirection.Up => GridDirection.Down,
            GridDirection.Down => GridDirection.Up,
            GridDirection.Left => GridDirection.Right,
            GridDirection.Right => GridDirection.Left,
            _ => GridDirection.None,
        };

        /// <summary>Z rotation in degrees where Right = 0 and Up = 90.</summary>
        public static float ToAngle(this GridDirection d) => d switch
        {
            GridDirection.Up => 90f,
            GridDirection.Down => 270f,
            GridDirection.Left => 180f,
            _ => 0f,
        };

        public static bool IsHorizontal(this GridDirection d) => d == GridDirection.Left || d == GridDirection.Right;
        public static bool IsVertical(this GridDirection d) => d == GridDirection.Up || d == GridDirection.Down;

        /// <summary>True when both lie on the same axis. None never shares an axis.</summary>
        public static bool SameAxis(this GridDirection a, GridDirection b)
            => (a.IsHorizontal() && b.IsHorizontal()) || (a.IsVertical() && b.IsVertical());

        /// <summary>
        /// Collapses an analog stick to a cardinal direction with axis hysteresis: the axis already held
        /// keeps winning until the other axis exceeds it by <paramref name="hysteresis"/> (a ratio), which
        /// stops a diagonal stick from chattering between Up and Right on successive frames.
        /// </summary>
        public static GridDirection FromAnalog(Vector2 input, GridDirection current, float deadzone, float hysteresis)
        {
            if (input.sqrMagnitude < deadzone * deadzone) return GridDirection.None;

            float ax = Mathf.Abs(input.x), ay = Mathf.Abs(input.y);
            bool horizontal;

            if (current.IsHorizontal()) horizontal = ay <= ax * (1f + hysteresis);
            else if (current.IsVertical()) horizontal = ax > ay * (1f + hysteresis);
            else horizontal = ax >= ay;

            if (horizontal) return input.x >= 0f ? GridDirection.Right : GridDirection.Left;
            return input.y >= 0f ? GridDirection.Up : GridDirection.Down;
        }
    }
}
