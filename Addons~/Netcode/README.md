# EasyGameFramework Netcode extension

Install EasyGameFramework at the same release first. Install this directory as a separate UPM package with the repository Git URL and `?path=Addons~/Netcode#v0.1.0`.

The manifest adds Unity Netcode for GameObjects 2.7.0. `EasyInteraction` contains the NGO-specific actor/command bridge. Configure network authority, ownership and the local player for your game; these adapters do not synchronize every framework module.

`EasyBuilding` contains a separate TCP transport demonstration. It is not an NGO implementation and must not be presented as shared NGO authority or persistence. See the README in each module folder for its contracts and limitations.

The main framework's desktop acceptance does not certify multiplayer behavior. Run this extension's own tests and validate your actual server/client topology before shipping.
