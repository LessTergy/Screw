# CoACD collider preparation

Editor-only tooling for the Screw project. Runtime uses saved Unity BoxCollider components.

## Setup (Windows)

The installer uses `uv` and creates a project-local Python 3.12 environment:

```powershell
./Tools/Colliders/Install.ps1
```

Pinned dependencies: CoACD 1.0.14, NumPy 2.2.6, SciPy 1.15.3. The environment, input requests and computed results live in ignored `.tools/`. Installation preserves existing caches.

## Bake

Select the shared model prefab and use **Tools → Screw → Levels → Bake Selected Model Colliders**. The agent invokes `PrimitiveColliderBaker.BakePrefab` through Unity MCP. The first run can take several minutes; each individual native decomposition has a two-minute timeout. Repeated input uses cached results.

CoACD performs convex decomposition of the normalized mesh, with seed 42 and at most eight parts by default. Disconnected shells share that budget and are processed separately; nested shells stay together to preserve cavities. No MeshCollider is created. SciPy fits a box into each convex part by maximizing its volume under the part's face-plane constraints. Unity then checks the original mesh, shrinks boxes where necessary and clips overlaps both within a part and between generated parts in the shared prefab. Clipping only reduces boxes, leaves a small clearance and rechecks each part's coverage before saving.

Preparation tries concavity thresholds 0.05, 0.1 and 0.2 in that order, stopping when the coverage floor is met. CoACD hulls are cached separately from fitted boxes, so changes to fitting do not repeat native decomposition. SLSQP keeps the best feasible iterate even when its line search cannot improve further; Unity still validates the original mesh before accepting it.

Default preparation uses `surfaceInset=0.005`: an inward clearance from the mesh surface equal to 0.5% of its smallest oriented bounding dimension, on each side. The safety check expands each candidate box by that distance against the original triangles, including cavity walls. `surfaceInset=0` disables this clearance. The inset applies to the model surface, rather than every internal box seam.

Preparation starts at **90% of the conservative interior sample centers after the inset**, with a hard limit of **15 boxes per mesh**. If the capped candidate set cannot meet the target, the target falls by five percentage points per part: 90%, 85%, 80%, and so on. The same validated candidates are reused and unnecessary tail boxes removed; native decomposition is not repeated for each lower target. Coverage is checked again after cross-part clipping, lowering the target by the same rule if necessary. A zero target is rejected. `Analyze` is strict by default; `reduceCoverageToFit: true` enables this policy and exposes the selected `TargetCoverage`. `BakePrefab` always enables it. The intentionally empty safety layer is excluded from the denominator. Each step chooses either a validated CoACD proposal or the largest free cuboid of wholly internal cells, whichever adds more covered samples. Grid cells intersecting accepted boxes are excluded; every resulting box is checked against the original triangles. This is a sample coverage floor, not an exact volume percentage or a guarantee of visual accuracy. Assess thin pieces, edges and cavities on the actual level.

Generated boxes share `Assets/Content/Settings/LevelPartsPhysics.physicMaterial`, with zero static friction, dynamic friction and bounciness, and Multiply combination modes. The generator creates the asset when missing and reuses it afterward. Pinned Rigidbody components use None interpolation to follow assembly Transform rotation together with bolts. ReleaseAsync enables Interpolate after detachment and switching to dynamic physics; runtime performs no geometry analysis.

If a cold run times out, pending request files remain available for offline processing:

```powershell
./Tools/Colliders/Prepare.ps1
```

After preparation finishes, repeat the Unity bake. Prefab changes are applied only after all parts pass validation. Existing manual colliders are preserved by default, and rebaking replaces only `_GeneratedPrimitiveColliders`. An explicitly requested manual replacement uses `BakePrefab(path, replaceExisting: true)`; old shapes are removed after analysis and saved together with the validated result. Every saved box has a small inward clearance, and coverage is checked again after all clipping.

## Verification

```powershell
./.tools/coacd/Scripts/python.exe -m unittest discover -s Tools/Colliders -p 'test_*.py' -v
```

Run `PrimitiveColliderBakerTests` and `PreparedLevelRouteTests` in Unity EditMode, followed by relevant PlayMode regression. Cold geometry tests need the installed environment; repeated runs use cached decompositions. Source geometry, material references, GUIDs and transforms must remain intact.

Sources: [CoACD](https://github.com/SarahWeiii/CoACD), [CoACD Python API](https://github.com/SarahWeiii/CoACD/blob/main/python/package/__init__.py), [SciPy constrained optimization](https://docs.scipy.org/doc/scipy-1.15.3/tutorial/optimize.html#constrained-minimization).
