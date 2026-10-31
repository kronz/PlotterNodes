#!/usr/bin/env python3
"""Generates samples/Make2D_Scene.dyn: a Dynamo graph that draws solids with hidden lines removed.

Group 1 builds a small scene from Dynamo solids (cuboid, cylinder, sphere, cone) plus a free
line. Group 2 makes two Make2D drawings of it: a parallel isometric (Make2D.ByDirection) and a
perspective (Make2D.ByCamera). Group 3 puts visible and hidden lines on separate pens and
exports one AxiDraw SVG per view.

The graph machinery (node JSON, layout, groups) is shared with make_numeral_graphs.py. The
Make2D node signatures are declared here as well, so the graph can be generated before the
DLL has been rebuilt; they must match the C# methods in Make2D.cs / DynamoGeometry.cs
(tools/ApiDump rewrites tools/plotter_api.json from the compiled DLL).

Usage:
    python tools/make_make2d_graph.py [--schema path/to/Dynamo/doc/dyn-file-spec.json]
                                      [--svg-dir D:\\Drawing\\PlotterNodes\\output]
"""
import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_numeral_graphs as mg  # noqa: E402

ROOT = mg.ROOT
PAGE_W, PAGE_H = mg.PAGE_W, mg.PAGE_H
S_SCENE, S_VIEWS, S_PENS = mg.S_NUMERAL, mg.S_PATTERN, mg.S_PENS

VECTOR = "Autodesk.DesignScript.Geometry.Vector"
POINT = "Autodesk.DesignScript.Geometry.Point"


def param(name, ds, default=None, cs=None):
    return {"name": name, "ds": ds, "cs": cs or ds, "hasDefault": default is not None, "default": None if default in (None, "null") else default}


MAKE2D_API = {
    "Plotter.Make2D.ByDirection": [{
        "signature": f"Plotter.Make2D.ByDirection@var[]..[],{VECTOR},{VECTOR},double,double,double",
        "static": True, "class": "Plotter.Make2D", "method": "ByDirection", "thisName": None,
        "params": [
            param("geometry", "var[]..[]", cs="System.Collections.Generic.IList<System.Object>"),
            param("viewDirection", VECTOR, "Vector.ByCoordinates(0, 0, -1)"),
            param("upVector", VECTOR, "null"),
            param("meshTolerance", "double", "-1"),
            param("creaseAngle", "double", "20"),
            param("mergeTolerance", "double", "0"),
        ],
        "returnDs": "Plotter.Make2D", "returnCs": "Plotter.Make2D", "multiReturn": None,
    }],
    "Plotter.Make2D.ByCamera": [{
        "signature": f"Plotter.Make2D.ByCamera@var[]..[],{POINT},{POINT},{VECTOR},double,double,double,double,double",
        "static": True, "class": "Plotter.Make2D", "method": "ByCamera", "thisName": None,
        "params": [
            param("geometry", "var[]..[]", cs="System.Collections.Generic.IList<System.Object>"),
            param("eyePoint", POINT),
            param("targetPoint", POINT),
            param("upVector", VECTOR, "null"),
            param("fieldOfView", "double", "60"),
            param("imagePlaneDistance", "double", "0"),
            param("meshTolerance", "double", "-1"),
            param("creaseAngle", "double", "20"),
            param("mergeTolerance", "double", "0"),
        ],
        "returnDs": "Plotter.Make2D", "returnCs": "Plotter.Make2D", "multiReturn": None,
    }],
}
for prop in ("VisibleLines", "HiddenLines", "Silhouettes", "Creases", "Curves"):
    MAKE2D_API[f"Plotter.Make2D.{prop}"] = [{
        "signature": f"Plotter.Make2D.{prop}", "static": False, "class": "Plotter.Make2D", "method": prop,
        "thisName": "make2D", "params": [], "returnDs": "Plotter.Polyline[]",
        "returnCs": "System.Collections.Generic.IList<Plotter.Polyline>", "multiReturn": None,
    }]
MAKE2D_API["Plotter.Make2D.Frame"] = [{
    "signature": "Plotter.Make2D.Frame", "static": False, "class": "Plotter.Make2D", "method": "Frame",
    "thisName": "make2D", "params": [], "returnDs": "Plotter.Polyline", "returnCs": "Plotter.Polyline", "multiReturn": None,
}]
for key, value in MAKE2D_API.items():
    mg.API.setdefault(key, value)

RESOLVER_EXTRA = {
    name: {"Key": f"Autodesk.DesignScript.Geometry.{name}", "Value": "ProtoGeometry.dll"}
    for name in ("Vector", "CoordinateSystem", "Cuboid", "Cylinder", "Sphere", "Cone", "Solid")
}


def scene(g):
    """Dynamo solids and one free line; every statement is one output of the code block."""
    stmts = [
        ("Cuboid.ByLengths(CoordinateSystem.ByOrigin(0, 0, 10), 60, 60, 20)", "null", 0, "Base block, 60 x 60 x 20"),
        ("Cylinder.ByPointsRadius(Point.ByCoordinates(0, 0, 20), Point.ByCoordinates(0, 0, 75), 15)", "null", 0, "Cylinder standing on the block"),
        ("Sphere.ByCenterPointRadius(Point.ByCoordinates(42, -38, 12), 12)", "null", 0, "Sphere beside it"),
        ("Cone.ByPointsRadius(Point.ByCoordinates(-45, 40, 0), Point.ByCoordinates(-45, 40, 50), 16)", "null", 0, "Cone behind"),
        ("Line.ByStartPointEndPoint(Point.ByCoordinates(-80, -80, 5), Point.ByCoordinates(80, 80, 5))", "null", 0, "A free curve: projected and hidden-line tested, but it hides nothing"),
    ]
    outs = g.code(stmts, S_SCENE)
    return g.list_create(outs, "object", S_SCENE)


def pens(g, name, view, svg_dir, frame=None):
    visible = g.call("Plotter.Make2D.VisibleLines", this=view, section=S_PENS)
    hidden = g.call("Plotter.Make2D.HiddenLines", this=view, section=S_PENS)
    # Drawings are centred on the projection of the world origin / camera target: move them to the page centre.
    vis_t = g.call("Plotter.Polyline.Translate", this=visible, dx=PAGE_W / 2, dy=PAGE_H / 2, section=S_PENS)
    hid_t = g.call("Plotter.Polyline.Translate", this=hidden, dx=PAGE_W / 2, dy=PAGE_H / 2, section=S_PENS)
    layers = [
        g.call("Plotter.Layer.ByGeometry", name="visible", geometry=vis_t, color="#111111", strokeWidth=0.5, penNumber=1, section=S_PENS),
        g.call("Plotter.Layer.ByGeometry", name="hidden", geometry=hid_t, color="#9ca3af", strokeWidth=0.3, penNumber=2, section=S_PENS),
    ]
    if frame is not None:
        frame_t = g.call("Plotter.Polyline.Translate", this=frame, dx=PAGE_W / 2, dy=PAGE_H / 2, section=S_PENS)
        layers.append(g.call("Plotter.Layer.ByGeometry", name="frame", geometry=frame_t, color="#c2410c", strokeWidth=0.3, penNumber=3, section=S_PENS))
    lst = g.list_create(layers, "Plotter.Layer")
    drawing = g.call("Plotter.Drawing.ByLayers", layers=lst, width=PAGE_W, height=PAGE_H, section=S_PENS)
    opt = g.call("Plotter.Drawing.Optimize", this=drawing, section=S_PENS)
    path = g.string_input(f"{svg_dir}\\make2d_{name}.svg", "null")
    g.call("Plotter.Drawing.ExportSvg", this=opt, filePath=path, includePageOutline=True, section=S_PENS)
    stats = g.call("Plotter.Drawing.Statistics", this=opt, section=S_PENS)
    g.watch(stats["pathCount"])


def build(svg_dir):
    g = mg.Graph("make2d-scene")
    g.section_titles = {
        S_SCENE: "1. 3D scene from Dynamo solids",
        S_VIEWS: "2. Make2D: parallel and perspective",
        S_PENS: "3. Pens, page and AxiDraw SVG",
    }
    g.section_notes = {
        S_SCENE: "Any solids, surfaces, meshes and curves work; replace the code block with your own geometry.",
        S_VIEWS: "ByDirection looks along a vector (plan view is 0,0,-1). ByCamera is a perspective from an eye point towards a target; the field of view clips to a square window.",
        S_PENS: "Visible and hidden lines go to separate pens. Set the SVG paths, then press Run.",
    }
    geom = scene(g)

    direction = g.code([("Vector.ByCoordinates(-1, -1, -0.8)", "null", 0, "Isometric-ish view direction (from the viewer into the scene)")], S_VIEWS)[0]
    iso = g.call("Plotter.Make2D.ByDirection", geometry=geom, viewDirection=direction, meshTolerance=0.1, creaseAngle=20, section=S_VIEWS)

    cam = g.code([
        ("Point.ByCoordinates(150, -170, 95)", "null", 0, "Camera eye"),
        ("Point.ByCoordinates(0, 0, 25)", "null", 0, "Camera target (projects to the drawing centre)"),
    ], S_VIEWS)
    persp = g.call("Plotter.Make2D.ByCamera", geometry=geom, eyePoint=cam[0], targetPoint=cam[1], fieldOfView=40, imagePlaneDistance=230,
                   meshTolerance=0.1, section=S_VIEWS)
    frame = g.call("Plotter.Make2D.Frame", this=persp, section=S_VIEWS)

    pens(g, "iso", iso, svg_dir)
    pens(g, "perspective", persp, svg_dir, frame)
    doc = g.to_dyn("Make2D_Scene", "PlotterNodes sample: hidden-line drawings (Make2D) of Dynamo solids, parallel and perspective, exported as AxiDraw SVG.")
    doc["ElementResolver"]["ResolutionMap"].update(RESOLVER_EXTRA)
    doc["View"]["Camera"] = {"Name": "Background Preview", "EyeX": 160.0, "EyeY": -220.0, "EyeZ": 150.0,
                             "LookX": -160.0, "LookY": 220.0, "LookZ": -130.0, "UpX": 0.0, "UpY": 0.0, "UpZ": 1.0}
    return doc


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--schema", help="Dynamo doc/dyn-file-spec.json for validation")
    ap.add_argument("--svg-dir", default=r"D:\Drawing\PlotterNodes\output", help="folder written to the SVG path nodes")
    args = ap.parse_args()
    doc = build(args.svg_dir)
    if args.schema:
        import jsonschema  # type: ignore
        jsonschema.validate(doc, json.loads(Path(args.schema).read_text(encoding="utf-8-sig")))
        print("schema validation passed")
    path = ROOT / "samples" / "Make2D_Scene.dyn"
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    print(f"wrote {path.relative_to(ROOT)}: {len(doc['Nodes'])} nodes, {len(doc['Connectors'])} wires")


if __name__ == "__main__":
    main()
