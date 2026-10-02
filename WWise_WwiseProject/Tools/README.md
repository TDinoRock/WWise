# Arena placeholder content — four audio outputs

`author_arena_content.pl` synthesises the placeholder WAVs into `Originals/SFX/Arena/` and rewrites the
Default Work Units for **Devices, Busses, Switches, Containers and Events**, plus
`Assets/ArenaScaffold/WwiseIds.txt` (the GUID manifest Unity reads when wiring event references).

Every GUID in the script is fixed, so re-running is idempotent — existing Wwise objects keep their
identity. The device and bus GUIDs are the ones authored by hand in Wwise.

```
perl Tools/author_arena_content.pl "<path>/WWise_WwiseProject"
"C:\Audiokinetic\Wwise_2025.1.10.9233\Authoring\x64\Release\bin\WwiseConsole.exe" \
    generate-soundbank WWise_WwiseProject.wproj --platform Windows --platform Mac
```

It overwrites those five work units wholesale. **Save and close Wwise before running it**, and don't
hand-edit those branches expecting edits to survive.

## Routing model: game-defined aux sends

A sound is **not tied to any one output**. Every sound's dry path is muted, and it reaches the
outputs through *game-defined auxiliary sends*, so the game decides per game object which outputs
hear it — any combination, each at its own level.

```
Sound (dry: OutputBusVolume -96 dB)  ──► Dry (Muted)  [BusVolume -96 dB]      (never heard)
      │ game-defined aux sends
      ├──► Players_Primary_Send    ► Players_Primary    ► Main Audio Bus       ► System
      ├──► Players_Secondary_Send  ► Players_Secondary  ► Secondary Audio Bus  ► System_Secondary
      ├──► Players_Tertiary_Send   ► Players_Tertiary   ► Tertiary Audio Bus   ► System_Tertiary
      └──► Players_Quadernary_Send ► Players_Quadernary ► Quadernary Audio Bus ► System_Quadernary
      (Arena_* sounds use the matching Arena_<Route>_Send busses)
```

Each of the 7 sounds sets `OverrideOutput`, `OverrideGameAuxSends` and `UseGameAuxSends` to True.
In Unity, `RoutedSound` lists all four outputs; it calls `SetGameObjectAuxSendValues` with one
entry per enabled output, sending into `<MixGroup>_<Route>_Send` at that row's level.
`AudioOutputManager` opens each ShareSet on a device; `AudioMenu` (Intro_MainMenu scene) is the UI.

Events: `Play_Footstep`, `Play_Bump`, `Play_Turn`, `Play_Drip`, `Play_Creak`, `Play_Chirp`,
`Play_Hum` (loop), `Stop_Hum`. Auto-defined SoundBanks, so no bank management is needed.

> The spelling **"Quadernary"** comes from the hand-authored Wwise objects. The C# enum and the aux
> bus names match it exactly on purpose; if they disagree the send silently goes nowhere.

## Gotchas worth remembering

- **Set `OverrideOutput` explicitly.** A child object inherits its parent's Output Bus unless
  `OverrideOutput = True`. The earlier switch-container design lost a debugging session to this.
  The generator still sets it on every sound, even at top level, so the dry route can never fall
  back to an inherited bus.
- **`AkGameObj` can wipe game-defined aux sends.** With `isEnvironmentAware` on, it pushes an empty
  send list when it registers the object. `RoutedSound` turns it off and applies
  the sends right before each Post. Side effect: `AkEnvironment` volumes don't affect these emitters.
- **Send level is linear gain.** 0.5 = −6 dB, 0.25 = −12 dB (verified by capture).
- **No listener management is needed.** Each ShareSet has exactly one output, so Wwise routes purely
  by the bus/device association.
- **Two endpoints on one codec won't both stream.** Realtek "Speakers" and "Headphones" are one
  codec; Windows drives one at a time. Use genuinely separate devices for a real split.
  `AudioOutputManager` polls and reports this as `live = false`.

## Verifying

- **Project, as Wwise evaluates it:** run a headless WAAPI server on the project and query it —
  works even when the GUI's WAAPI isn't answering.
  ```
  WwiseConsole.exe waapi-server WWise_WwiseProject.wproj --wamp-port 8095 --http-port 8096 --no-source-control
  curl -X POST http://127.0.0.1:8096/waapi -H "Content-Type: application/json" -d "{\"uri\":\"ak.wwise.core.object.get\",\"args\":{\"waql\":\"$ from type Sound\"},\"options\":{\"return\":[\"name\",\"@OverrideOutput\",\"@UseGameAuxSends\",\"@OverrideGameAuxSends\"]}}"
  ```
- **Runtime:** `AkUnitySoundEngine.StartOutputCapture("name.wav")` writes one WAV per active output
  (`name.wav`, `name1.wav`, `name2.wav`, `name3.wav`), in output-creation order: Primary,
  Secondary, Tertiary, Quadernary. In the Editor they land in
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\WWise\`.
