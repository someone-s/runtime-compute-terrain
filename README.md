# Runtime Compute Terrain
This is an experiement in making responsive deformable terrain post build, allowing end users (i.e. players) to modify terrain to their liking.

Screenshot 1                | Screenshot 2                  | Screenshot 3
---------------------------------:|:--------------------------------:|:--------------------------------
![Spline](/Readme/Spline.png)     | ![Tiles](/Readme/Distance.png)   | ![Performance](/Readme/Performance.png)

## Features
- **Infinite tiles**: New tiles are generated based on the players position, and commands requesting modification on tiles that does not exist
- **Projection based deformation**: This package includes a compute shader renderer to render meshes that deforms the terrain onto the terrain. Suitable for embankment generated my splines.
- **Build in spline library**: This demo includes a custom spline library that uses compute shaders to generate splines during runtime, supporting modification such as moving the controlling nodes
- **Procedural Vegetation**: Trees, grasses and other meshes can be placed onto the surface of the terrain, they are then automatically moved if the terrain is modified. A simple LOD system reduce the amount of vegetation further away from the camera.
- **Performant**:
  - 100+FPS on a RTX2060 Mobile (Asus G14 2020)
  - 40FPS on a Ryzen 4900HS APU (Asus G14 2020)
- **Universal Render Pipeline**: Built for and on URP, utilizing new Unity features to perform direct mesh buffer manipulation in compute shader

## Run this demo
1. Clone/Download this repo
2. Open with Unity 6
3. Open ```Scenes/Main Scene```
3. Press the run button

## Controls
- Hold shift to rotate camera
- Use WASD keys to move camera
- Use cursor and hold left mouse button to perform terrain modification
  - Change type of modification (Add, Subtract, Level, Smooth) with the TerrainCaster component on the Terrain gameobject
- Move the splines with the AnchorX gameobjects nested in the Embankment gameobjects






