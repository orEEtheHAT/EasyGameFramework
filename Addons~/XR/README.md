# EasyGameFramework XR extension

Install EasyGameFramework at the same release first. Install this directory as a separate UPM package with the repository Git URL and `?path=Addons~/XR#v0.1.0`.

The manifest adds XR Interaction Toolkit 3.3.0. The included EasyInteraction and EasyBuilding adapters connect XR interaction to those modules' public APIs. They do not supply a complete headset rig, XR provider, locomotion configuration or VR conversion of the FPS example.

Configure and test the project's XR provider, input rig and target headset separately. Read each module folder's README before wiring its adapter. Desktop framework acceptance is not headset validation.
