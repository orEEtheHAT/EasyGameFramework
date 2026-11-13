# Framework Playground

This sample connects the framework modules in one small playable scene. It uses original procedural feedback assets and creates editable definitions in `Assets/EasyGameFramework/Playground/Content`.

1. Install ECM2 and EasyGameFramework in a Unity 6.3 URP project with New Input System enabled.
2. Import this sample from Package Manager.
3. Choose **Tools > EasyGameFramework > Examples > Create Framework Playground**.
4. Enter Play Mode. The command creates its own localization setup when needed. Repeating it opens the existing scene and preserves your edits.

| Input | Action |
| --- | --- |
| WASD / mouse | Move and look |
| E | Interact; release the held object even while turning |
| G | Throw the held object |
| Left click | Fire the sample fireball; place a selected building in building mode |
| H | Open the available interaction actions |
| T | Open the building catalog |
| R / Q | Rotate the building preview |
| Escape | Exit the current mode / close a window |
| Tab | Open the lab menu |
| F5 / F9 | Save / load gameplay progress |

The settings menu demonstrates rebinding, audio, language and a graphics child window. Window input restrictions are owned independently. Progress loading uses EasySave and leaves user preferences separate.

The carried crate uses stabilized holding with inertia and excludes collisions with its holder. The combat target demonstrates a fireball and its ground effect. The workbench and building area share resource costs through production integrations.

The opt-in `EasySuiteSmoke` component exercises the scene and reports its results. It is dormant during ordinary play. Its checks can also be invoked in a standalone diagnostic Player with `-easySuiteSmoke <absolute-report-path>`; that report is a diagnostic, not a saved game.
