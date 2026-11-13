# Combat FPS Player example

The Combat FPS runtime adapter is already shipped in EasyGameFramework. Import this sample only for its local prefab builder, then choose **Tools > EasyGameFramework > Combat > Integrations > Create EasyMovement Combat Player**. ECM2 must be installed. The resulting prefab retains the standard Movement input, camera and gait settings.

`CombatFirstPersonController` consumes `MovementInfluences.Multiplier` after gait selection, respects Move/Jump/Turn restrictions and accepts `ICombatMotionReceiver` impulses. The builder assigns a local movement stat key when none is supplied. Use the same key in timed slow/haste StatusDefinition modifiers. Spatial Slow Volume profiles need no stat key.

The builder creates a unique local prefab and preserves the source prefab. Import this example once; do not copy production adapter code into Assets.
