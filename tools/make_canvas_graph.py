#!/usr/bin/env python3
"""Generates samples/Canvas3D/Canvas3D_Arctic.dyn: a Dynamo scene shown in a Canvas 3D window.

Group 1 builds a small scene from Dynamo solids: a base block, a cylinder, a sphere, a cone, a
block with a spherical cavity (Solid.Difference) and a staircase of boxes, so that the Arctic
display has contact shadows, crevices and a cavity to occlude. Group 2 wires everything into
one Canvas 3D node (Plotter > View > Canvas 3D), whose window opens in Arctic mode, maximized on
another display than Dynamo's.

The graph machinery (node JSON, layout, groups) is shared with make_numeral_graphs.py. Canvas 3D
is a NodeModel (an "ExtensionNode" in the .dyn), so its JSON is written here; the property names
must match Plotter.Canvas.Canvas3D / CanvasSettings in src/PlotterNodes.Canvas.

Usage:
    python tools/make_canvas_graph.py [--schema path/to/Dynamo/doc/dyn-file-spec.json]
"""
import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_numeral_graphs as mg  # noqa: E402

ROOT = mg.ROOT
S_SCENE, S_CANVAS = mg.S_NUMERAL, mg.S_PATTERN

RESOLVER_EXTRA = {
    name: {"Key": f"Autodesk.DesignScript.Geometry.{name}", "Value": "ProtoGeometry.dll"}
    for name in ("Vector", "CoordinateSystem", "Cuboid", "Cylinder", "Sphere", "Cone", "Solid")
}

CANVAS_DESCRIPTION = (
    "Shows the geometry wired into it in a separate 3D window that you can move to another monitor "
    "or make full screen. Switch between Dynamo shading and Arctic display: white materials, soft sky "
    "shadows and ambient occlusion."
)


def default_settings():
    """CanvasSettings as Json.NET writes it (property names = C# names, enums as strings)."""
    return {
        "IsWindowOpen": True,
        "WindowPlacement": None,
        "IsFullScreen": False,
        "IsTopmost": False,
        "DisplayMode": "Arctic",
        "ShowGridShaded": True,
        "ShowGridArctic": False,
        "ShowViewCube": False,
        "FollowBackgroundCamera": False,
        "ShowSettingsPanel": True,
        "Camera": None,
        "Arctic": {
            "MaterialColor": "#EEEEEE",
            "BackgroundColor": "#FFFFFF",
            "UseObjectColors": False,
            "SkyLight": 1.0,
            "GroundBounce": 0.45,
            "FillLight": 0.18,
            "Exposure": 1.0,
            "SkyShadows": True,
            "ShadowQuality": "Medium",
            "AmbientOcclusion": True,
            "AmbientOcclusionStrength": 0.75,
            "AmbientOcclusionRadius": 0.025,
            "GroundPlane": True,
            "GroundShadowStrength": 0.7,
        },
    }


def canvas3d(g, geometry, section=S_CANVAS):
    node = {
        "ConcreteType": "Plotter.Canvas.Canvas3D, PlotterNodes.Canvas",
        "Settings": default_settings(),
        "NodeType": "ExtensionNode",
        "Id": g.gid(),
        "Inputs": [g.port("geometry", "Geometry to show in the canvas window: points, curves, surfaces, solids, meshes, or lists of them.")],
        "Outputs": [g.port("geometry", "The input, passed through unchanged.")],
        "Replication": "Disabled",
        "Description": CANVAS_DESCRIPTION,
    }
    g._add(node, "Canvas 3D", section, sources=[geometry.node["Id"]])
    g.package_nodes.append(node["Id"])
    g.connect(geometry, node, 0)
    return node


def scene(g):
    stmts = [
        ("Cuboid.ByLengths(CoordinateSystem.ByOrigin(0, 0, 10), 60, 60, 20)", "null", 0, "Base block, 60 x 60 x 20"),
        ("Cylinder.ByPointsRadius(Point.ByCoordinates(0, 0, 20), Point.ByCoordinates(0, 0, 75), 15)", "null", 0, "Cylinder standing on the block"),
        ("Sphere.ByCenterPointRadius(Point.ByCoordinates(46, -40, 12), 12)", "null", 0, "Sphere resting on the ground"),
        ("Cone.ByPointsRadius(Point.ByCoordinates(-48, 42, 0), Point.ByCoordinates(-48, 42, 50), 16)", "null", 0, "Cone behind"),
        ("Cuboid.ByLengths(CoordinateSystem.ByOrigin(-48, -42, 14), 28, 28, 28).Difference(Sphere.ByCenterPointRadius(Point.ByCoordinates(-48, -42, 30), 17))",
         "null", 0, "Block with a spherical cavity: occlusion inside the bowl"),
        ("Cuboid.ByLengths(CoordinateSystem.ByOrigin(18 + (0..5) * 8, 48, 2 + (0..5) * 2), 8, 16, 4 + (0..5) * 4)", "null", 1, "Staircase of boxes: contact shadows at every step"),
    ]
    outs = g.code(stmts, S_SCENE)
    return g.list_create(outs, "object", S_SCENE)


def build():
    g = mg.Graph("canvas3d-arctic")
    g.section_titles = {
        S_SCENE: "1. 3D scene from Dynamo solids",
        S_CANVAS: "2. Canvas 3D window",
    }
    g.section_notes = {
        S_SCENE: "Any points, curves, surfaces, solids and meshes work; replace the code block with your own geometry.",
        S_CANVAS: "The window opens maximized on another display. Shaded/Arctic buttons (or the A key) switch the display, F11 is full screen, F zooms to fit.",
    }
    geometry = scene(g)
    canvas3d(g, geometry)
    doc = g.to_dyn("Canvas3D_Arctic", "PlotterNodes sample: Dynamo solids in a Canvas 3D window with Arctic display (sky shadows and ambient occlusion).")
    doc["ElementResolver"]["ResolutionMap"].update(RESOLVER_EXTRA)
    doc["View"]["Camera"] = {"Name": "Background Preview", "EyeX": 160.0, "EyeY": 150.0, "EyeZ": 220.0,
                             "LookX": -160.0, "LookY": -130.0, "LookZ": -220.0, "UpX": 0.0, "UpY": 1.0, "UpZ": 0.0}
    return doc


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--schema", help="Dynamo doc/dyn-file-spec.json for validation")
    args = ap.parse_args()
    doc = build()
    if args.schema:
        import jsonschema  # type: ignore
        jsonschema.validate(doc, json.loads(Path(args.schema).read_text(encoding="utf-8-sig")))
        print("schema validation passed")
    path = ROOT / "samples" / "Canvas3D" / "Canvas3D_Arctic.dyn"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    print(f"wrote {path.relative_to(ROOT)}: {len(doc['Nodes'])} nodes, {len(doc['Connectors'])} wires")


if __name__ == "__main__":
    main()
