#!/usr/bin/env python3
"""Generates samples/Make2D_Verify_ASM.dyn: checks Make2D against ASM itself, inside Dynamo.

One code block builds the cube-with-spherical-cavity solid, then
  1. ground truth from the geometry kernel: every B-rep edge (solid.Edges) is sampled and a line
     from each sample to the camera eye is intersected with the solid (Geometry.DoesIntersect);
  2. the samples are projected exactly as Make2D.ByCamera projects (eye, target, image distance =
     eye-target distance) and split into ASM-visible and ASM-hidden points;
  3. Make2D.ByCamera runs on the same solid; its VisibleLines, Creases and Silhouettes are drawn
     at the same place as the points, Diagnostics reports how the mesh was read;
  4. Make2D.ExportTestCase writes the exact engine input (ASM mesh as read, camera, crease angle) to
     tests/make2d/captured, for replay in the test suite (python tests/make2d/replay_capture.py).

Usage: python tools/make_make2d_verify_graph.py
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_numeral_graphs as mg  # noqa: E402

ROOT = mg.ROOT
OUT = "D:/Drawing/PlotterNodes/tests/make2d/captured"

LINES = [
    ("Inputs: edit these. side and R match the test suite shape (cube 10, sphere radius 6.8).", None),
    (None, "side = 10"),
    (None, "R = 6.8"),
    (None, "eye = Point.ByCoordinates(28, -38, 22)"),
    (None, "target = Point.ByCoordinates(0, 0, 0)"),
    (None, "fov = 40"),
    (None, "meshTol = -1"),
    (None, f'captureFolder = "{OUT}"'),
    ("The solid", None),
    (None, "cube = Cuboid.ByLengths(Point.Origin(), side, side, side)"),
    (None, "ball = Sphere.ByCenterPointRadius(Point.Origin(), R)"),
    (None, "solid = cube.Difference(ball)"),
    ("Ground truth from ASM: sample every B-rep edge and cast a line to the eye", None),
    (None, "edgeCurves = solid.Edges.CurveGeometry"),
    (None, "samples = edgeCurves.PointsAtEqualChordLength(60)"),
    (None, "toEye = Vector.ByTwoPoints(samples, eye).Normalized()"),
    (None, "sightLines = Line.ByStartPointEndPoint(samples.Translate(toEye, side * 0.002), eye)"),
    (None, "asmHidden = sightLines.DoesIntersect(solid)"),
    ("Project the samples the way Make2D.ByCamera does", None),
    (None, "fwd = Vector.ByTwoPoints(eye, target).Normalized()"),
    (None, "rgt = fwd.Cross(Vector.ZAxis()).Normalized()"),
    (None, "upv = rgt.Cross(fwd)"),
    (None, "dist = eye.DistanceTo(target)"),
    (None, "offset = 2 * side"),
    (None, "rel = Vector.ByTwoPoints(eye, samples)"),
    (None, "depth = rel.Dot(fwd)"),
    (None, "drawn = Point.ByCoordinates(rel.Dot(rgt) * dist / depth + offset, rel.Dot(upv) * dist / depth, 0)"),
    (None, "flatPts = List.Flatten(drawn, -1)"),
    (None, "flatHidden = List.Flatten(asmHidden, -1)"),
    (None, 'asmVisiblePoints = List.FilterByBoolMask(flatPts, flatHidden)["out"]'),
    (None, 'asmHiddenPoints = List.FilterByBoolMask(flatPts, flatHidden)["in"]'),
    ("Make2D on the same solid and camera, drawn at the same place (Creases and Silhouettes repeated below)", None),
    (None, "m = Plotter.Make2D.ByCamera([solid], eye, target, null, fov, 0, meshTol)"),
    (None, "diagnostics = m.Diagnostics"),
    (None, "make2dVisible = m.VisibleLines.ToPolyCurve().Translate(offset, 0, 0)"),
    (None, "make2dCreases = m.Creases.ToPolyCurve().Translate(offset, -1.6 * side, 0)"),
    (None, "make2dSilhouettes = m.Silhouettes.ToPolyCurve().Translate(offset + 1.6 * side, -1.6 * side, 0)"),
    (None, "counts = [List.Count(asmVisiblePoints), List.Count(asmHiddenPoints), List.Count(m.Creases), List.Count(m.Silhouettes)]"),
    ("Exact engine input written for replay in the test suite (python tests/make2d/replay_capture.py)", None),
    (None, 'captured = m.ExportTestCase(captureFolder, "verify")'),
]


def build():
    g = mg.Graph("make2d-verify-asm")
    lines, stmts = [], []
    for comment, stmt in LINES:
        if comment:
            lines.append(f"// {comment}")
        if stmt:
            lines.append(f"{stmt};")
            stmts.append(stmt)
    node = {
        "ConcreteType": "Dynamo.Graph.Nodes.CodeBlockNodeModel, DynamoCore",
        "Id": g.gid(),
        "NodeType": "CodeBlockNode",
        "Inputs": [],
        "Outputs": [g.port(s.split("=")[0].strip(), f"Value of expression at line {i + 1}") for i, s in enumerate(stmts)],
        "Replication": "Disabled",
        "Description": "Allows for DesignScript code to be authored directly",
        "Code": "\n".join(lines),
    }
    g.section_titles = {mg.S_NUMERAL: "Make2D versus ASM: same solid, same camera", mg.S_PATTERN: "", mg.S_PENS: ""}
    g.section_notes = {mg.S_NUMERAL: "Green-check: ASM-visible points should lie on Make2D visible lines; ASM-hidden points should not. Run once; the captured case lands in tests/make2d/captured."}
    g._add(node, "Make2D vs ASM", mg.S_NUMERAL, len(lines))
    g.package_nodes.append(node["Id"])
    doc = g.to_dyn("Make2D_Verify_ASM", "Checks Make2D.ByCamera against ASM ray intersections on a cube with a spherical cavity and captures the ASM mesh for the test suite.")
    doc["View"]["Camera"] = {"Name": "Background Preview", "EyeX": 10.0, "EyeY": -60.0, "EyeZ": 40.0,
                             "LookX": 0.0, "LookY": 60.0, "LookZ": -40.0, "UpX": 0.0, "UpY": 0.0, "UpZ": 1.0}
    doc["View"]["Dynamo"]["RunType"] = "Manual"
    return doc


def main():
    doc = build()
    path = ROOT / "samples" / "Make2D_Verify_ASM.dyn"
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    (ROOT / "tests" / "make2d" / "captured").mkdir(parents=True, exist_ok=True)
    print(f"wrote {path.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
