Pure Volumetric Fog
Raymarched volumetric ground fog for Unity 6 / URP 17+



# Setup

Add "GameObject/BK/Pure Volumetric Fog" to the scene.
Assign specific Terrains, or leave the list empty to use all active Terrains.
Assign a Directional Light, or leave it empty to use RenderSettings.sun.
Adjust fog, lighting and quality settings in the inspector.

Multiple Terrains and tiled worlds are supported.

"Terrain Follow" value controls how closely the fog follows the terrain:
Meshes and other scene objects use scene depth and are not part of the terrain height source.



# Lighting and Shadows

Scene's Global Fog Color sets the base colour.
Scene Ambient Color can follow RenderSettings.ambientLight.
Sun Strength controls directional lighting.
Sun Scattering controls the sun highlight.

Enable Volumetric Shadows enables shadowing.
Include Mesh Shadows adds realtime shadows from terrain, trees, rocks and other shadow-casting objects.

Only realtime shadows from the main Directional Light are sampled.
Point lights, spot lights and baked-only shadows are not supported.



# Performance

Maximum Ray Steps, Minimum Ray Steps and Target Step Length control raymarch quality.
By default, fog renders at full resolution.
The optional Pure Volumetric Fog Renderer Feature can render at Half or Quarter resolution with depth-aware upsampling.



# Runtime API

BKPureNature.PureVolumetricFog

SetTerrain(Terrain)
SetTerrains(IEnumerable<Terrain>) - change the height source and rebuild the fog resources.

FindActiveTerrains() - assigns every active Terrain in the loaded scene.

Refresh() - rebuilds the heightmap, shadow map, and volume bounds.



# Compatibility

Unity 6 / 6000.0+
URP 17+
Shader Model 4.5
Compute shader support
Perspective cameras

HDRP and Built-in Render Pipeline are not supported.
XR, orthographic cameras, camera stacking, mobile, Metal and Vulkan have not been validated.
Transparent shaders that do not write depth cannot block the fog.

