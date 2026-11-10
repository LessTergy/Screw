# CoACD collider preparation

Editor-only tooling for the Screw project. Runtime uses saved Unity BoxCollider components.

## Setup (Windows)

The installer uses `uv` and creates a project-local Python 3.12 environment:

```powershell
./Tools/Colliders/Install.ps1
```

Pinned dependencies: CoACD 1.0.14, NumPy 2.2.6, SciPy 1.15.3, PyMeshFix 0.18.1. The environment, input requests and computed results live in ignored `.tools/`. Installation preserves existing caches. PyMeshFix uses its low-level NumPy API; PyVista/VTK is not installed.

## Mesh analysis and individual repair approval

Mesh repair and collider generation are separate operations.

1. In Project, right-click a level prefab (`Level_XX`, `Level_XX_View`) or shared model prefab and choose **Screw → Analyze Level Meshes...**. The shared model is resolved automatically. No changes are applied to it during analysis.
2. If all mesh topology checks pass, a dialog reports no problems; no review assets or colliders are created.
3. Otherwise, only failed parts are exported together to `repair_meshes.py`. PyMeshFix first fills boundary holes while retaining original vertices, oriented triangles and render seams. If that fails, it starts again from the original arrays and performs full repair (`fill_small_boundaries` then `clean`). Both failures are reported if no valid repair is obtained. No source model is edited by either attempt.
4. A new folder under `Assets/Content/ModelRepairs/` contains `Review.asset`, `Report.json`, repaired mesh assets, individual `Part_N.prefab` previews and `Comparison.unity`. The report contains only problem parts. The scene contains isolated before/after pairs, not the entire level. Only repaired preview parts receive generated colliders, using the baker defaults. Cap-only repair retains source render attributes; full repair maps new triangles to source faces for material groups and maps corners to source attributes for UVs, normals, tangents and colours, retaining separate vertices at render seams. Mapping of changed surfaces is approximate: inspect textures and shading. Large reconstructed meshes use 32-bit indices when necessary.
5. Work in the **Scene** tab, without Play Mode. The review window opens with the comparison scene; the Scene overlay button reopens it. **Показать пару** frames a pair, and **Показывать коллайдеры** toggles green wireframes of the preview boxes. Rotate the Scene view to inspect holes, caps, cavities and filling.
6. **Применить эту деталь** approves just that pair and replaces only its MeshFilter mesh reference in the shared model prefab. The original imported mesh asset and other parts remain untouched. Existing generated colliders on that part are removed as stale; manual colliders are retained. Preview colliders are not transferred. Approval is persisted per part, and the problem-only report is updated. Failed previews, duplicate approvals and meshes changed since analysis are rejected.
7. After approving the required repairs, run collider generation for the level separately. Reopening an existing `Comparison.unity` restores the review controls and saved per-part approvals. If any source or repaired mesh changed since analysis, run a new analysis.

The repair backend uses [PyMeshFix](https://pymeshfix.pyvista.org/). Full repair may move vertices, replace/delete faces or lose connected components. The tool does not explicitly join components or discard all but the largest; `clean()` may still remove defective geometry. Mode (`cap-only`/`full`), fallback reason, triangle counts and component counts before/after are saved in the review/report. Full repairs receive a visible warning and require the same individual approval as other repairs. Closedness, non-manifold edges, winding and degeneracy are checked before import; Unity validates the imported mesh again before preview baking. Comparison assets from earlier attempts are retained and never overwritten.

## Collider generation

### Per-model settings window

In Project, right-click one level or shared model prefab and choose **Screw → Generate Colliders...**. Set boxes per part, initial coverage (%), surface inset (%), coverage reduction step (percentage points), and analysis resolution; then click **Перегенерировать коллайдеры**. Topology problems stop generation for that model and direct you to the separate mesh-analysis command; no repair is performed automatically.

Each opening starts from `PrimitiveColliderBaker` constants; **Вернуть значения по умолчанию** restores them. Settings apply to that run without changing global constants. Defaults: resolution 24, 15 boxes, 90% coverage, 0.5% inset, reduction step 5 percentage points. `DefaultCoverageStep = 0.05f` provides the step; `coverageStep` overrides it in `BakePrefab`, `Analyze`, or `ColliderBatchUtility.Run`. Reports include the actual step and resolution. Invalid/non-finite parameters are rejected before preparation.

### Batch and reports

**Tools → Screw → Levels → Prepare Selected Model Colliders and Report** processes selected prefab owners; **Prepare All Catalog Colliders and Report** processes catalogue models sequentially. It can be cancelled between models. Each model with topology problems is reported and skipped without repair; processing continues with the next model. `Run(paths, reviewCopiesOnly: true)` optionally bakes closed models into independent prefab copies, without creating mesh-repair comparison scenes.

- `.tools/colliders/batch-report.json`: source/target, geometry problems, quality report and status/error.
- `.tools/colliders/reports/<prefab-guid>.json`: saved status, box counts, actual interior sample coverage, selected target, inset, resolution, reduction step, containment validation and generated-box penetration check.
- `Assets/Content/ModelRepairs/<review-folder>/Report.json`: only failed source parts, repair/preview errors, mesh/preview paths and per-part approval status.
- `.tools/colliders/repair/<review-guid>/`: temporary mesh repair input/output arrays.

Coverage measures conservative sample centres, not exact volume. Visual acceptance of repaired geometry remains a per-part user decision. Agent-driven Editor operations use Unity MCP; generation and report calculations need no per-part agent inspection.

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
