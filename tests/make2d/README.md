# Make2D test suite

Standalone ground-truth tests for the Make2D hidden-line engine. No public collection of geometry with expected Make2D output exists, so the suite builds its own: analytic shapes whose correct drawing can be computed exactly, meshed the way a B-rep faceter (ASM, OCCT) meshes them, and checked from 14 viewpoints each.

## How the ground truth is made

1. **Shapes** (`shapes.py`) carry two descriptions. The engine gets triangle meshes built per B-rep face (`meshing.py`), including deliberate defects: triangle soups, flipped winding, T-junctions, cracked rims where two faces are divided differently. The oracle gets the exact geometry: CSG solids and height-field sheets, analytic B-rep edges, and parametric surfaces.
2. **Reference lines** (`oracle.py`) are the analytic edges plus, per view, the contour generators of every smooth surface (marching squares on the parameter grid of N · (P − eye) = 0).
3. **Visibility** of every reference sample is decided by exact ray casting against the CSG solids (`csg.py`): no mesh, no engine code. Contour samples are tested from points pushed slightly off the surface on both sides, which is well conditioned for convex contours and for cavity contours.
4. **Scoring** (`suite.py score`) compares the engine's visible and hidden polylines with the reference within a screen tolerance δ = 0.4 % of the drawing diagonal: visible recall and precision, hidden recall and precision. Samples near a visibility change (within 3 δ or 1.5 mesh facets) are not required. A metric passes at its threshold (0.99 visible, 0.98 hidden) or when the wrong length is below 0.5 % of the drawing.

## Cases

26 shapes × 14 views = 364 cases. Views: isometric, back isometric, front, plan, below, three seeded random directions, a five-step perspective camera sweep (the pattern of the reported cube-with-cavity failure) and a close wide-angle perspective.

| Group | Shapes |
|---|---|
| Planar | box, box_soup, box_flipped_winding, box_tjunctions, box_rotated, thin_plate, box_far_from_origin, box_tiny, l_bracket, stairs, star |
| Curved | cylinder, cylinder_cap_cracks, cylinder_rotated, sphere, sphere_coarse, cone, torus, wavy_sheet (open surface) |
| Cavities | cube_sphere_cavity, cube_sphere_cavity_cracked_rims, box_through_hole |
| Scenes | two_boxes, box_and_sphere, stacked_boxes (coincident faces), curves_and_solids (free curves piercing and wrapping solids) |

## Running

```powershell
# Python prototype engine (tools/hlr_prototype/hlr.py), needs Python 3 + numpy
python tests\make2d\suite.py all

# C# engine (src/PlotterNodes/Internal/HiddenLine.cs) via tools/Make2DTests, shim build, no Dynamo needed
.\build.ps1 -Make2DTests -DynamoRepo D:\Drawing\Dynamo
.\build.ps1 -Make2DTests -Only cube_sphere_cavity,torus     # subset

# Pictures of individual cases (matplotlib)
python tests\make2d\plot_case.py cube_sphere_cavity sweep2 torus iso --engine csharp
```

Reports: `tests\make2d\results\report_<engine>.html` (table plus an SVG per failing case: green dots expected visible, orange expected hidden, black and grey engine lines, circles mark missing and spurious lines). `results\summary_<engine>.json` holds the metrics. `expected\` (oracle cache) is rebuilt automatically when missing; `cases\` is the engine input and is regenerated with `suite.py generate`.

## Current results (2026-10-27)

| Engine | Pass |
|---|---|
| Prototype before the suite existed | 317 / 364 |
| Prototype now | 363 / 364 (wavy_sheet sweep0: contour cusps, 1 % wrong length, mesh-resolution limited) |
| C# | run `build.ps1 -Make2DTests` |

Engine changes made against the suite (both engines):

1. **Per-face topology.** When a body's input indexing shares vertices inside faces but not across them (how faceters deliver meshes), edges inside a face are never creases and joined edges are creases only if they fold clearly more than the surrounding facets. Fixes interior creases on coarse curved meshes (sphere_coarse) and keeps seams of periodic faces hidden. Soups and fully welded meshes still use the crease angle.
2. **Silhouette self-occlusion.** Triangles sharing a vertex with a silhouette edge no longer hide it (mesh contours zigzag around the true one).
3. **Hidden lines along visible ones** (within 0.3 % of the drawing) are dropped, e.g. the second rim of a cracked mesh.
4. **Overlapping strokes** of the same visibility (within 0.15 %) are drawn once, e.g. the fan of tiny contour edges where a contour runs along the view direction.

The C# reader now prefers `VerticesAsThreeNumbers` + `VertexIndicesByTri` when they reproduce `TrianglesAsNineNumbers` exactly (so per-face topology is available) and falls back to the raw corners otherwise; `Make2D.Diagnostics` reports which was used per body.

## Adding a shape

Write a constructor in `shapes.py` returning `Shape(name, bodies, features, surfaces, solids, sheets, curves)`, add it to `library()`, add its facet size to `FACET` in `suite.py` if it is curved, then `python suite.py generate --only <name>`.

## Checking the ground truth and replaying real Dynamo drawings

1. `python crosscheck.py cube_sphere_cavity` relabels every reference sample with a second, independent method (ray casting against a dense triangle mesh instead of exact CSG) and reports the disagreement per view. Lines that project exactly onto another line are exact ties and are not compared.
2. `samples/Make2D_Verify_ASM.dyn` checks Make2D against ASM itself: every B-rep edge of the solid is sampled and tested with `Geometry.DoesIntersect` against the line to the eye, and the ASM-visible and ASM-hidden points are drawn on top of Make2D's lines.
3. `Make2D.ExportTestCase(folder, name)` writes the exact engine input of a drawing (mesh as read from Dynamo, camera, crease angle) to `captured/`. `python replay_capture.py captured/<name>__*.json --cube-cavity SIDE R` adds the ground truth for a cube minus a centred sphere, scores the Python engine and writes `results/captured.png`; `build.ps1 -Make2DTests -Only <names>` scores the C# engine on the same files.

## Edge classification modes (2026-10-28)

1. **B-rep edges** (solids, surfaces and polysurfaces from Dynamo): Make2D now takes the real edges from ASM (`Topology.Edges`). Only mesh edges lying along them can be creases, filtered for tangency against the local facet angle; the crease angle is not used. Suite cases carry these edges per body (`"edges"` in the case JSON), except the mesh-only variants.
2. **Crease angle** (Dynamo meshes, soups, fully welded meshes): as before.
3. **Per-face topology** (meshes whose faces were tessellated separately) is used only when at least 5 % of shared edges were joined by the weld and no vertex-sharing triangles fold more than 60°. Dynamo meshes are welded across B-rep edges except a few seams, which wrongly switched this mode on (the Difference and Intersection reports).

New cases: `cube_cavity_welded`, `cube_cavity_welded_mesh_only`, `cube_sphere_intersection`, `cube_sphere_intersection_mesh_only` (welded meshes as Dynamo delivers them, sizes of the reported graphs). Line-kind scoring accepts either kind where two reference lines of different kinds coincide on screen (a rim forming part of the outline).

## Outline (2026-10-28)

`Make2D.Outline` returns every visible line that has empty background directly beside it, whatever its kind: the outer contour plus the edges of holes you can see through. `Silhouettes` stays the contour of smooth surfaces only, so where a crease (a disc rim, a box edge) forms the outer contour it is in `Creases` and `Outline`, not in `Silhouettes`. The oracle marks outline samples by casting sight rays just beside each visible sample; the suite scores `outline_recall` and `outline_precision`.

## Tangent edges (2026-10-28)

In B-rep edge mode the crease angle now filters the real edges by how much they fold: the default 20° hides tangent edges (fillet boundaries, corner blends), 0 draws every B-rep edge. Matching a mesh edge to a B-rep edge requires both ends and the midpoint to be near the *same* edge, so chords cutting across a corner between two edges are no longer drawn. New cases: `rounded_box` (crease angle 20, tangent edges not expected) and `rounded_box_tangent_edges` (crease angle 0, expected). Suite: 451 of 453 pass on the prototype.
