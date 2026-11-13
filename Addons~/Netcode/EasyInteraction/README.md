# Netcode interaction addon

SDK integration for EasyInteraction in EasyGameFramework. Requires Netcode for GameObjects2.7.0+. This directory is intentionally outside base import; root supplies the addon manifest/installation. SDK guards target the actual Unity package, not former Easy package IDs.

NetworkInteractionActor/NetcodeInteractionCommands connect transport-independent InteractionAuthority and command contracts to NGO. Tools~/NetworkSmoke retains two-process loopback server/client validation. Core authority tests remain in the module because they have no NGO dependency. The preserved smoke is not a WAN/latency/loss certification. No commercial ECM2 source is included.
