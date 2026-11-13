# Cells and Paths

**Module:** Building in EasyGameFramework. Production integrations are already included.

After import open **Content/CellsAndPaths.unity**, or use **Tools > EasyGameFramework > Building > Create Cells and Paths Scene**, then Play. The builder writes editable assets to Assets/EasyBuilding Cells Example and reopens an existing scene without overwriting it.

- Left click adds a voxel; right click removes it. 1/2/3 select palette values.
- Z undoes the latest cell stroke. F5 captures cells in memory; F9 restores them.
- Middle drag orbits the camera; the wheel zooms.
- Tab switches cells/road/pipe. Click path points, Enter rebuilds the selected path, Z removes the last pending point.

Inspect Cell World, Cell Palette and Voxel Renderer. No object is created per voxel. This example is free; type Costs and IBuildingEconomy enable paid transactions using the same API. Roads/pipes have local control points. Persistent file storage, Tilemap and Terrain setup are described separately in the developer guide.
