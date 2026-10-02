# Pandemonium — Design Foundation

Living document. Decisions are agreed with the user; **Open** items are not yet decided.

## Core
- 4-player **co-op**, friendly fire **off**.
- A **series of puzzle rooms**; each room needs all four characters to solve before the group continues.
- Fixed, hand-built rooms. Everything is **real time** on a grid.

## Characters
| Character | Step into a square | Action button | Extra |
|---|---|---|---|
| Paladin | Enemy square → enemy defeated | Push a boulder (one square in the facing direction) | — |
| Ranger | Open | Fire an arrow from the bow | — |
| Thief | Open | Open | — |
| Wizard | Open | Open | Fairy button (see below) |

### Wizard & fairy
- Fairy button: spawns a fairy on the Wizard's square; direction buttons now move the **fairy**, not the Wizard.
- Press again: fairy returns, control goes back to the Wizard.
- Fairy flies **over gaps/pits**; **flying enemies** can threaten it.
- Fairy hovering over an unlit **torch for several seconds lights it**.
- The Wizard stays in the world and **can be attacked** while the fairy is out.

## Tiles
- **Walkable** floor, **Wall** (blocks movement and light), **Pit/Chasm** (blocks walking; the fairy can fly over).
- Objects on tiles so far: boulders, torches, enemies, doors.
- **Solid** obstacles block movement **and** light (walls). **Transparent** obstacles block movement but let light through.

## Enemies
- **Ground enemies**: defeated by the **Paladin** (stepping into their square).
- **Flying enemies**: defeated by the **Ranger** (arrows). They also threaten the fairy.
- Enemies **patrol**; they **chase** anything within a small circular sight radius.
- What happens when an enemy reaches a player: **open**.

## Rooms
- Each room starts **completely dark**; only character lights and lit torches reveal it. The whole room is on screen.
- Solving the puzzle **opens a door** to the next room. A room may have **1, 2 or 4 doors**; they **all open together**, and each leads to a **different room** (branching paths).

## Tags & layers (confirmed; best practice: keep categories separate)
- Rules check **tags**: only `Paladin` pushes a `Boulder`; only `Fairy` lights a `Torch`.
- Tags: `Paladin`, `Ranger`, `Thief`, `Wizard`, `Fairy`, `GroundEnemy`, `FlyingEnemy`, `Boulder`, `Torch`, `Door`.
- Layers: `Floor`, `SolidObstacle` (blocks light), `TransparentObstacle`, `Pit`, `Players`, `Fairy`, `GroundEnemies`, `FlyingEnemies`, `Projectiles`.
- Unity allows one tag and one layer per object.

## Grid rules
- Pressing a direction always **turns the character to face it**, even if the move is blocked (wall, pit, ...).
- **Holding** a direction keeps walking.
- **All characters move at the same speed.**
- **Players can stack** on the same square.
- Obstacles and players never share a square: a boulder **cannot be pushed** into a square a character occupies.

## Vision
- One shared screen: **one Unity window stretched across both monitors**.
- Walls block light, so you can't see around corners.
- Darkness is **visual only**; game logic ignores it.
- Explored areas **do not** stay revealed; they return to dark.
- Torches, once lit, **stay lit**.

## Audio
- Four separate outputs (Primary/Secondary/Tertiary/Quadernary) already working, set in the main menu.
- Open: what each player hears privately vs. on shared speakers.

## Input
- Each player has **their own Arduino controller**: North, East, South, West + Action. Wizard's controller adds the Fairy button.
- Keyboard fallback for development (layouts TBD).
- Ardity (serial) package is imported and compiling.

## Open questions
1. Ranger arrow: travel speed/range, what stops it (walls, boulders), does it fly over pits, can it hit switches/torches, cooldown?
2. Thief and Wizard actions: later.
3. Enemy contact with a player: consequence?
4. Torches: what does lighting one do besides light (open doors, part of a puzzle)? How many seconds to light?
5. Fairy: own light? range limit?
6. Per-player audio: which sounds are private vs. shared?
7. Arduino message format and how each controller is identified (one COM port per player).
