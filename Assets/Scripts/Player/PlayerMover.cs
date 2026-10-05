using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Moves a character one grid square at a time.
///
/// This script doesn't read any buttons itself. Whatever controls the character (the keyboard
/// script now, the Arduino script later) just calls Press(direction) and Release(direction).
/// That keeps movement identical no matter where the input comes from.
///
/// Uses Unity's Grid to convert between world positions and squares, and the Tilemaps to decide
/// which squares can be walked on.
/// </summary>
public class PlayerMover : MonoBehaviour
{
    [Header("Level")]
    [Tooltip("The Grid that the tilemaps belong to.")]
    public Grid grid;
    [Tooltip("A square must have a floor tile to be walkable.")]
    public Tilemap floor;
    [Tooltip("Squares with a wall tile block movement.")]
    public Tilemap walls;
    [Tooltip("Squares with a pit tile block walking.")]
    public Tilemap pits;

    [Header("Movement")]
    [Tooltip("Seconds to move one square. Keep the same on every character so they all move at the same speed.")]
    [Min(0.01f)] public float stepTime = 0.2f;
    [Tooltip("Rotate the character to face the direction pressed. The sprite should point up by default.")]
    public bool rotateToFace = true;

    [Header("Current state (shown for debugging)")]
    public Vector3Int cell;
    public Vector2Int facing = Vector2Int.up;
    public bool isMoving;

    // Directions currently held down. The most recently pressed one wins.
    private readonly List<Vector2Int> held = new List<Vector2Int>();

    private Vector3 stepFrom;
    private Vector3 stepTo;
    private float stepTimer;

    private void Start()
    {
        // Snap onto the centre of whatever square we were placed on in the editor.
        cell = grid.WorldToCell(transform.position);
        transform.position = CellCenter(cell);
        Face(facing);
    }

    // Centre of a square in world space, keeping our own z so 2D sorting isn't affected.
    private Vector3 CellCenter(Vector3Int square)
    {
        Vector3 center = grid.GetCellCenterWorld(square);
        center.z = transform.position.z;
        return center;
    }

    // ---------------------------------------------------------------- input (called by input scripts)

    /// <summary>A direction button went down. The character turns to face it right away, even if it can't move there.</summary>
    public void Press(Vector2Int direction)
    {
        if (!held.Contains(direction)) held.Add(direction);
        Face(direction);

        // Step right away, so even a very quick tap (pressed and released before the next frame) moves.
        if (!isMoving) TryStep(direction);
    }

    /// <summary>A direction button came up.</summary>
    public void Release(Vector2Int direction)
    {
        held.Remove(direction);
    }

    // ---------------------------------------------------------------- movement

    private void Update()
    {
        if (isMoving)
        {
            // Slide from the old square to the new one over stepTime seconds.
            stepTimer += Time.deltaTime;
            float t = Mathf.Clamp01(stepTimer / stepTime);
            transform.position = Vector3.Lerp(stepFrom, stepTo, t);
            if (t >= 1f) isMoving = false;
            return;
        }

        // Holding a direction keeps walking: start the next step as soon as the last one ends.
        if (held.Count > 0)
        {
            Vector2Int direction = held[held.Count - 1];
            Face(direction);
            TryStep(direction);
        }
    }

    private void TryStep(Vector2Int direction)
    {
        Vector3Int target = cell + new Vector3Int(direction.x, direction.y, 0);
        if (!CanEnter(target)) return; // blocked: we already turned to face it, so nothing else to do

        cell = target;
        stepFrom = transform.position;
        stepTo = CellCenter(target);
        stepTimer = 0f;
        isMoving = true;
    }

    /// <summary>
    /// Walkable = has floor, no wall, no pit. Other players don't block (characters can stack).
    /// </summary>
    public bool CanEnter(Vector3Int target)
    {
        if (floor != null && !floor.HasTile(target)) return false;
        if (walls != null && walls.HasTile(target)) return false;
        if (pits != null && pits.HasTile(target)) return false;
        return true;
    }

    private void Face(Vector2Int direction)
    {
        facing = direction;
        if (rotateToFace)
        {
            // Angle measured from "up", because the sprite points up when not rotated.
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
