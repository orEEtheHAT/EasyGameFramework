# Rigidbody Motor — first run

Requires **EasyGameFramework** and its standard New Input System dependency. No prerequisite sample imports. Combat support is already included, and can be enabled on the example body when needed.

1. Import this sample once through Package Manager.
2. Choose **Tools > EasyGameFramework > Movement > Create Rigidbody Motor Scene**. It creates editable content in `Assets/EasyGameFramework Examples/Movement/Rigidbody` and opens the scene additively without saving your existing scene. Repeating the command keeps your edits.
3. Press Play and click Game. **W/S / left stick Y** controls throttle, **Space / left trigger** brakes. **J / West** adds a sideways impulse. The orange block preserves the sideways motion, coasts under its soft caps, reverses gradually and collides with the wall.
4. Try **CoastDragMotor_Sample** from the package's Rigidbody/Profiles folder to add neutral drag. The Rigidbody inspector owns mass, gravity, rotation locks, collision detection and friction; the motor does not replace these settings.

For Combat, press **Enable Combat Speed Effects** in the block's motor Inspector. The adapter is supplied by the framework, outside this sample. Configure a trigger Collider and **Slow Volume** with a **Movement Influence** SO on each area. Timed Movement Speed Stat effects and full physics semantics are described in `Modules/EasyMovement/Documentation~/RigidbodyMotor.md`.

The sample owns simple local New Input System actions and an allocating IMGUI help panel. It demonstrates inputs, not a player-control architecture or a performance benchmark. Keep one imported copy of its assembly. Source control your edits before reimporting.

For maintainers: core module tests do not depend on this sample. After importing it, run `EasyMovement.Rigidbody.Sample.EditorTests` to verify creation/idempotence and paths. Create the default example with the menu command, then run `EasyMovement.Rigidbody.Sample.Tests` in Play Mode for real input/physics. Sample tests are separate from the production adapter and excluded from ordinary Player builds.
