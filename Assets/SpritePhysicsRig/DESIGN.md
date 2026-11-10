# Sprite Physics Rig Design

## Status

- Phase: 11 - optimization, diagnostics, and final verification
- Status: Runtime optimization and author-facing diagnostics implemented
- Target editor: Unity 6000.0.32f1
- Render pipeline: URP 17.0.3

This document defines the rules that runtime data, editor tools, mesh generation,
skinning, and physics must share. Changes to a rule in this document require a
serialized-data version bump once `SpriteRigAsset` exists.

## Product boundary

The system deforms one ordinary Unity `Sprite` by skinning a custom polygon mesh
to a hierarchy of 2D bones. A `SpriteRigAsset` stores reusable rig data and never
stores a reference to a `Sprite`. A scene component binds one Sprite and one rig
asset at runtime.

The first production milestone must support:

- One Sprite and one rig asset per component.
- A user-authored polygon mesh with vertices, edges, and triangles.
- A parented hierarchy of 2D bones.
- Up to four bone influences per vertex.
- CPU linear-blend skinning.
- Bone-driven secondary-motion physics.
- Per-component overrides of the physics defaults stored in the rig asset.
- Editing and previewing the rig in a dedicated editor window.

The following are outside the MVP:

- Runtime topology changes, cutting, or tearing.
- Holes or disconnected islands in one polygon.
- Per-vertex soft-body simulation.
- Self-collision.
- GPU skinning.
- IK and animation authoring.
- PSD character import.
- SpriteShape integration.
- Network-deterministic physics.

They may be added later without changing the normalized coordinate convention.

## Canonical coordinate spaces

### Rig space

Serialized geometry and bind-pose data use normalized two-dimensional rig space.

- Bottom-left: `(0, 0)`
- Top-right: `(1, 1)`
- Positive X: right
- Positive Y: up
- Rotation: degrees, positive counter-clockwise
- Bone length: normalized rig-height units
- Z is not serialized; generated mesh vertices use `0`

Vertices and bones may lie outside `[0, 1]`. This is valid and is required for
overshoot handles, bones outside the visible image, and meshes extending beyond
the source Sprite.

Rig space is independent of:

- Sprite pixels-per-unit
- Texture size
- Sprite pivot
- Transform scale
- Atlas placement
- Scene units

### Sprite-local space

The runtime component maps a rig-space point `r` into Sprite-local space:

```text
pixel = (r.x * sprite.rect.width, r.y * sprite.rect.height)
local = (pixel - sprite.pivot) / sprite.pixelsPerUnit
```

This mapping is the MVP's `Stretch` policy. It guarantees that `(0, 0)` and
`(1, 1)` align with the Sprite rect regardless of pivot or PPU.

Future mapping policies may include `PreserveAspectFit`, but must be explicit
component settings. They must not reinterpret serialized rig coordinates.

### UV space

Serialized UVs, when present, use normalized Sprite-rect UV space:

- Bottom-left of the logical Sprite rect: `(0, 0)`
- Top-right of the logical Sprite rect: `(1, 1)`

UVs do not contain texture or atlas coordinates. The runtime renderer converts
them to actual texture UVs for the currently bound Sprite. Consequently, the
same rig asset can be used with multiple Sprites.

For a vertex without an explicitly authored UV, its rig-space XY is used as the
default UV. UV values outside `[0, 1]` are invalid for the MVP.

### Bone-local space

Each bone stores its bind pose relative to its parent:

- Parent index, or `-1` for a root bone
- Local start position
- Local rotation
- Length

Root-bone local values are interpreted in rig space. Child-bone position and
rotation are interpreted in parent-bone space. Runtime pose data uses the same
hierarchy and is converted to matrices only after the physics step.

## Identity and indexing

Array indices are compact runtime references but are not durable identities.

- Every bone has a serialized stable ID.
- Component overrides refer to the stable bone ID, never only to an array index.
- Vertices may gain stable IDs if editor operations need durable selection or
  cross-asset remapping; triangle and edge data use current vertex indices.
- Reordering bones must remap weight indices without changing stable IDs.
- Deleting a bone must either remap its vertex influences or be rejected until
  the user chooses a replacement.

Stable IDs will be serialized as strings generated from GUIDs. They are not
Unity asset GUIDs and are unique only within a rig asset.

## Hard limits and validation

MVP limits:

- Maximum bones per rig: 64
- Maximum influences per vertex: 4
- Minimum polygon vertices: 3
- Triangle indices: counter-clockwise in rig space
- Edge indices: no self-edges
- Bone length: greater than zero
- Parent graph: acyclic

Weight rules:

- Each weight is finite and in `[0, 1]`.
- Active weights sum to `1` within a small serialization tolerance.
- Repeated bone indices on one vertex are merged.
- Zero weights are removed and remaining influences are sorted descending.
- A vertex with no influence is assigned to the nearest bone by the editor, or
  to the first root bone when no better choice is available.
- More than four authored influences are reduced to the four largest and then
  normalized, with an editor warning before the destructive change.

Geometry rules:

- All triangle and edge indices must refer to existing vertices.
- Degenerate triangles are invalid.
- Duplicate edges are invalid regardless of direction.
- A triangle may not reference the same vertex twice.
- MVP assets contain one connected polygon without holes.
- Topology is immutable during Play Mode.

Validation has two levels:

- Error: runtime mesh or skinning cannot be built.
- Warning: runtime is possible, but the asset is probably unintended.

The runtime component must fail safely and preserve the original SpriteRenderer
output when rig data contains an error.

## Sprite compatibility contract

Required for MVP:

- Single-mode and Multiple-mode Sprites.
- Arbitrary pivot and PPU.
- Rectangular, non-rotated texture regions.
- SpriteRenderer color, flip X/Y, sorting layer, and sorting order.
- Sprite replacement at runtime.

Must be detected and reported if not yet implemented:

- Rotated Sprite Atlas packing.
- Tight-packed atlas regions that cannot be mapped as a rectangle.
- SpriteMask interaction.
- Secondary textures and normal maps.
- URP 2D lighting materials.
- SpriteRenderer draw modes other than `Simple`.
- Sprite tiling.

Unsupported input must produce a targeted warning rather than silently rendering
corrupted UVs.

## Physics boundary

Physics operates on bones, not mesh vertices.

- The physics solver outputs a runtime bone pose.
- The skinning system consumes that pose.
- The renderer consumes deformed vertices.
- Physics never edits the rig asset.
- The root may be fixed, animated, or externally driven.
- Each bone may be fixed, animated, or dynamic.
- Asset values are defaults; a scene component may override them by stable ID.

The MVP solver is a fixed-timestep, endpoint-based Verlet chain with length,
angular, and rest-pose constraints. Phase 9 adds endpoint/capsule collision
against Unity `Collider2D` environments. Self-collision remains outside the MVP.

## Update and ownership rules

- The component creates and owns its runtime Mesh instance.
- The source `SpriteRenderer` remains on the component GameObject. The generated
  `MeshFilter` and `MeshRenderer` live on a hidden, non-serialized child because
  Unity does not support this renderer combination as one component stack.
- Runtime meshes and working buffers are never serialized into scenes or assets.
- Static asset arrays are treated as read-only at runtime.
- Physics advances on a fixed timestep.
- The final pose and mesh deformation are applied after animation in LateUpdate.
- Teleport, enable, rig replacement, and Sprite replacement reset simulation.
- Per-frame code must not allocate managed collections after initialization.
- Disabling or destroying the component releases the generated Mesh.

## Editor rules

- Editing requires an explicitly selected `SpriteRigAsset`.
- A Sprite is a preview/binding input and is not written into the asset.
- Preview changes must use the exact runtime coordinate conversion.
- Every edit supports Undo/Redo.
- Destructive normalization or influence reduction requires a visible warning.
- Pixel snapping is optional and derives its step from the preview Sprite PPU;
  snapped values are still stored in normalized rig space.
- Save-time validation must produce the same errors as runtime validation.

## Acceptance criteria for phase 1

- The system boundary and deferred features are explicit.
- Rig, Sprite-local, UV, and bone-local coordinates have one canonical meaning.
- Pivot and PPU conversion is defined by a testable formula.
- Reusable rig assets do not need Sprite references.
- Bone and weight limits are fixed for the first implementation.
- Unsupported Sprite and atlas cases have a detection policy.
- Physics, skinning, and rendering ownership are separated.

Phases 3 through 11 implement Sprite-rect UV mapping, generated mesh rendering,
CPU linear-blend skinning, endpoint Verlet bone physics, stable-ID physics
overrides, fixed-step integration, a dedicated rig editor, automatic grid/Sprite
geometry generation, automatic/mirrored weight authoring, Transform-driven
Animated bones, reference-frame reprojection, teleport reset, and environment
collision.

Phase 10 replaces per-deformation bounds recalculation with a direct,
allocation-free bounds pass and allows offscreen physics simulation to be
paused per component. Phase 11 exposes query/correction diagnostics, selected
rig and collision gizmos, collision-ignore refresh controls, and regression
coverage for both endpoint and capsule modes.
