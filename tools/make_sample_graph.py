#!/usr/bin/env python3
"""Generates samples/PlotterNodes_Numeral8.dyn, a Dynamo graph that exercises the package.

The graph builds a numeral 8 from four Dynamo circles, turns it into a Shape, radiates
concentric halo rings from it, clips them to the page, hatches the numeral, assigns pens
and exports an AxiDraw-ready SVG.

Usage:  python tools/make_sample_graph.py [path/to/Dynamo/doc/dyn-file-spec.json]
The optional schema path enables validation (requires the jsonschema package).
"""
import json
import sys
import uuid
from pathlib import Path

PACKAGE_NAME = "PlotterNodes"
PACKAGE_VERSION = "0.1.0"


def gid():
    return uuid.uuid4().hex


def port(name, desc, default=False):
    return {
        "Id": gid(),
        "Name": name,
        "Description": desc,
        "UsingDefaultValue": default,
        "Level": 2,
        "UseLevels": False,
        "KeepListStructure": False,
    }


class Graph:
    def __init__(self):
        self.nodes = []
        self.views = []
        self.connectors = []
        self.package_nodes = []
        self.group_nodes = []   # ids of nodes created since the last group() call

    def zt(self, signature, inputs, output, x, y, description="", package=True):
        """inputs: list of (name, type, default-or-None). output: (name, type)."""
        node_id = gid()
        in_ports = []
        for name, typ, default in inputs:
            if default is None:
                in_ports.append(port(name, typ))
            else:
                in_ports.append(port(name, f"{typ}\nDefault value : {default}", True))
        node = {
            "ConcreteType": "Dynamo.Graph.Nodes.ZeroTouch.DSFunction, DynamoCore",
            "NodeType": "FunctionNode",
            "FunctionSignature": signature,
            "Id": node_id,
            "Inputs": in_ports,
            "Outputs": [port(output[0], output[1])],
            "Replication": "Auto",
            "Description": description,
        }
        self.nodes.append(node)
        self.group_nodes.append(node_id)
        short = signature.split("@")[0]
        short = ".".join(short.split(".")[-2:])
        self.views.append(self._view(short, node_id, x, y))
        if package:
            self.package_nodes.append(node_id)
        return node

    def code_block(self, code, output_names, x, y):
        node_id = gid()
        node = {
            "ConcreteType": "Dynamo.Graph.Nodes.CodeBlockNodeModel, DynamoCore",
            "Id": node_id,
            "NodeType": "CodeBlockNode",
            "Inputs": [],
            "Outputs": [port(n, f"Value of expression at line {i + 1}") for i, n in enumerate(output_names)],
            "Replication": "Disabled",
            "Description": "Allows for DesignScript code to be authored directly",
            "Code": code,
        }
        self.nodes.append(node)
        self.group_nodes.append(node_id)
        self.views.append(self._view("Code Block", node_id, x, y))
        return node

    def string_input(self, value, x, y, name="String"):
        node_id = gid()
        node = {
            "ConcreteType": "CoreNodeModels.Input.StringInput, CoreNodeModels",
            "NodeType": "StringInputNode",
            "InputValue": value,
            "Id": node_id,
            "Inputs": [],
            "Outputs": [port("", "String")],
            "Replication": "Disabled",
            "Description": "Creates a string",
        }
        self.nodes.append(node)
        self.group_nodes.append(node_id)
        self.views.append(self._view(name, node_id, x, y))
        return node

    def slider(self, value, minimum, maximum, step, x, y, name="Number Slider"):
        node_id = gid()
        node = {
            "ConcreteType": "CoreNodeModels.Input.DoubleSlider, CoreNodeModels",
            "NodeType": "NumberInputNode",
            "NumberType": "Double",
            "MaximumValue": maximum,
            "MinimumValue": minimum,
            "StepValue": step,
            "InputValue": value,
            "Id": node_id,
            "Inputs": [],
            "Outputs": [port("", "Double")],
            "Replication": "Disabled",
            "Description": "A slider that produces numeric values.",
        }
        self.nodes.append(node)
        self.group_nodes.append(node_id)
        self.views.append(self._view(name, node_id, x, y))
        return node

    def list_create(self, count, x, y):
        node_id = gid()
        node = {
            "ConcreteType": "CoreNodeModels.CreateList, CoreNodeModels",
            "VariableInputPorts": True,
            "Id": node_id,
            "NodeType": "ExtensionNode",
            "Inputs": [port(f"item{i}", f"Item Index #{i}") for i in range(count)],
            "Outputs": [port("list", "A list (type: var[]..[])")],
            "Replication": "Disabled",
            "Description": "Makes a new list from the given inputs",
        }
        self.nodes.append(node)
        self.group_nodes.append(node_id)
        self.views.append(self._view("List Create", node_id, x, y))
        return node

    def watch(self, x, y):
        node_id = gid()
        node = {
            "ConcreteType": "CoreNodeModels.Watch, CoreNodeModels",
            "WatchWidth": 200.0,
            "WatchHeight": 200.0,
            "Id": node_id,
            "NodeType": "ExtensionNode",
            "Inputs": [port("", "Node to show output from")],
            "Outputs": [port("", "Node output")],
            "Replication": "Disabled",
            "Description": "Visualizes a node's output",
        }
        self.nodes.append(node)
        self.group_nodes.append(node_id)
        self.views.append(self._view("Watch", node_id, x, y))
        return node

    def connect(self, src_node, src_index, dst_node, dst_index):
        self.connectors.append({
            "Start": src_node["Outputs"][src_index]["Id"],
            "End": dst_node["Inputs"][dst_index]["Id"],
            "Id": gid(),
        })

    @staticmethod
    def _view(name, node_id, x, y):
        return {
            "ShowGeometry": True,
            "Name": name,
            "Id": node_id,
            "IsSetAsInput": False,
            "IsSetAsOutput": False,
            "Excluded": False,
            "X": float(x),
            "Y": float(y),
        }

    def group(self, text, x, y, width=360.0, height=120.0):
        """Creates a group around every node created since the previous group() call."""
        members = list(self.group_nodes)
        self.group_nodes = []
        return {
            "Id": gid(),
            "Title": text,
            "DescriptionText": "",
            "IsExpanded": True,
            "WidthAdjustment": 0.0,
            "HeightAdjustment": 0.0,
            "Nodes": members,
            "HasNestedGroups": False,
            "Left": float(x),
            "Top": float(y),
            "Width": width,
            "Height": height,
            "FontSize": 36.0,
            "GroupStyleId": "00000000-0000-0000-0000-000000000000",
            "InitialTop": float(y),
            "InitialHeight": height,
            "TextblockHeight": 0.0,
            "Background": "#FFC1D676",
        }


def build():
    g = Graph()
    S = "Plotter.Shape"
    L = "Plotter.Layer"

    # --- Input curves: four Dynamo circles making an 8 (page is A4 landscape, mm) ---
    curves = g.code_block(
        "// Outer lobes and counters of the numeral, in page millimetres.\n"
        "outer = [Circle.ByCenterPointRadius(Point.ByCoordinates(148.5, 129), 24),\n"
        "         Circle.ByCenterPointRadius(Point.ByCoordinates(148.5, 81), 28)];\n"
        "holes = [Circle.ByCenterPointRadius(Point.ByCoordinates(148.5, 130), 9),\n"
        "         Circle.ByCenterPointRadius(Point.ByCoordinates(148.5, 80), 12)];",
        ["outer", "holes"], -900, 0)

    outer_shapes = g.zt(f"{S}.ByCurves@Autodesk.DesignScript.Geometry.Curve[],double,double",
                        [("curves", "Curve[]", None), ("segmentLength", "double", 0.5), ("joinTolerance", "double", 0.01)],
                        ("Shape", "Shape"), -450, -60,
                        "Creates a shape from Dynamo curves.")
    hole_shapes = g.zt(f"{S}.ByCurves@Autodesk.DesignScript.Geometry.Curve[],double,double",
                       [("curves", "Curve[]", None), ("segmentLength", "double", 0.5), ("joinTolerance", "double", 0.01)],
                       ("Shape", "Shape"), -450, 180,
                       "Creates a shape from Dynamo curves.")
    g.connect(curves, 0, outer_shapes, 0)
    g.connect(curves, 1, hole_shapes, 0)

    # Overlapping lobes must be fused (even-odd would otherwise punch a hole where they overlap).
    body = g.zt(f"{S}.Union@Plotter.Shape[],double",
                [("shapes", "Shape[]", None), ("resolution", "double", 0.25)],
                ("Shape", "Shape"), -50, -60, "Boolean union of shapes.")
    g.connect(outer_shapes, 0, body, 0)

    combine_list = g.list_create(2, 250, 20)
    g.connect(body, 0, combine_list, 0)
    g.connect(hole_shapes, 0, combine_list, 1)

    numeral = g.zt(f"{S}.Combine@Plotter.Shape[]", [("shapes", "Shape[]", None)],
                   ("Shape", "Shape"), 500, 20, "Merges loops; inner loops become holes.")
    g.connect(combine_list, 0, numeral, 0)

    group1 = g.group("1. Numeral from Dynamo curves", -920, -120, 1700, 420)

    # --- Halo rings ---
    ring_params = g.code_block("24; // ring count\n4;  // first offset (mm)", ["count", "firstOffset"], 500, 300)
    spacing = g.slider(5.5, 2.0, 12.0, 0.5, 500, 420, "Ring spacing")
    rings = g.zt(f"{S}.Rings@int,double,double,double",
                 [("shape", "Shape", None), ("count", "int", 5), ("spacing", "double", 3), ("firstOffset", "double", 3), ("resolution", "double", 0.25)],
                 ("Shape[]", "Shape[]"), 850, 240, "Concentric offsets around the shape.")
    g.connect(numeral, 0, rings, 0)
    g.connect(ring_params, 0, rings, 1)
    g.connect(spacing, 0, rings, 2)
    g.connect(ring_params, 1, rings, 3)

    page = g.zt("Plotter.Page.Region@double,double,double",
                [("width", "double", 297), ("height", "double", 210), ("margin", "double", 10)],
                ("Shape", "Shape"), 850, 480, "Drawable page region.")
    page_margin = g.code_block("8; // page margin (mm)", ["margin"], 500, 560)
    g.connect(page_margin, 0, page, 2)

    clipped = g.zt(f"{S}.Clip@var[]..[],bool,double,double",
                   [("shape", "Shape", None), ("geometry", "var[]..[]", None), ("keepInside", "bool", "false"), ("margin", "double", 0), ("resolution", "double", 0.25)],
                   ("Polyline[]", "Polyline[]"), 1250, 300, "Clips geometry against the shape.")
    keep_inside = g.code_block("true;", ["keepInside"], 850, 640)
    g.connect(page, 0, clipped, 0)
    g.connect(rings, 0, clipped, 1)
    g.connect(keep_inside, 0, clipped, 2)

    group2 = g.group("2. Halo rings, clipped to the page", 480, 220, 1100, 520)

    # --- Numeral outline and fill ---
    wobble = g.zt(f"{S}.Wobble@double,double,int,int",
                  [("shape", "Shape", None), ("amplitude", "double", 0.5), ("wavelength", "double", 10), ("seed", "int", 0), ("octaves", "int", 2)],
                  ("Shape", "Shape"), 850, -120, "Hand-drawn wobble.")
    g.connect(numeral, 0, wobble, 0)

    hatch = g.zt(f"{S}.Hatch@double,double,double,double",
                 [("shape", "Shape", None), ("spacing", "double", 2), ("angle", "double", 45), ("inset", "double", 0), ("offset", "double", 0)],
                 ("Polyline[]", "Polyline[]"), 850, 60, "Parallel line fill.")
    hatch_params = g.code_block("1.6; // hatch spacing\n65;  // hatch angle\n1.5; // inset from outline", ["spacing", "angle", "inset"], 500, 150)
    g.connect(numeral, 0, hatch, 0)
    g.connect(hatch_params, 0, hatch, 1)
    g.connect(hatch_params, 1, hatch, 2)
    g.connect(hatch_params, 2, hatch, 3)

    # --- Pens ---
    def layer(x, y, name, color, width, pen):
        n = g.zt(f"{L}.ByGeometry@string,var[]..[],string,double,int",
                 [("name", "string", None), ("geometry", "var[]..[]", None), ("color", "string", "\"#000000\""), ("strokeWidth", "double", 0.35), ("penNumber", "int", 1)],
                 ("Layer", "Layer"), x, y, "Pen layer.")
        params = g.code_block(f"\"{name}\";\n\"{color}\";\n{width};\n{pen};", ["name", "color", "width", "pen"], x - 300, y + 40)
        g.connect(params, 0, n, 0)
        g.connect(params, 1, n, 2)
        g.connect(params, 2, n, 3)
        g.connect(params, 3, n, 4)
        return n

    pink = layer(1650, -140, "pink outline", "#ff6fae", 0.6, 1)
    fill = layer(1650, 60, "light fill", "#c7d2fe", 0.3, 2)
    blue = layer(1650, 300, "blue rings", "#1d4ed8", 0.5, 3)
    g.connect(wobble, 0, pink, 1)
    g.connect(hatch, 0, fill, 1)
    g.connect(clipped, 0, blue, 1)

    layers = g.list_create(3, 2050, 60)
    g.connect(pink, 0, layers, 0)
    g.connect(fill, 0, layers, 1)
    g.connect(blue, 0, layers, 2)

    drawing = g.zt("Plotter.Drawing.ByLayers@Plotter.Layer[],double,double,string",
                   [("layers", "Layer[]", None), ("width", "double", 297), ("height", "double", 210), ("units", "string", "\"mm\"")],
                   ("Drawing", "Drawing"), 2300, 60, "Page plus pen layers.")
    g.connect(layers, 0, drawing, 0)

    optimized = g.zt("Plotter.Drawing.Optimize@double",
                     [("drawing", "Drawing", None), ("joinTolerance", "double", 0.01)],
                     ("Drawing", "Drawing"), 2650, 60, "Reorders strokes to cut pen-up travel.")
    g.connect(drawing, 0, optimized, 0)

    path = g.string_input("C:\\Temp\\PlotterNodes_eight.svg", 2650, 260, "SVG file path")
    export = g.zt("Plotter.Drawing.ExportSvg@string,int,bool",
                  [("drawing", "Drawing", None), ("filePath", "string", None), ("decimals", "int", 3), ("includePageOutline", "bool", "false")],
                  ("string", "string"), 3000, 60, "Writes the AxiDraw-ready SVG.")
    g.connect(optimized, 0, export, 0)
    g.connect(path, 0, export, 1)

    stats = g.zt("Plotter.Drawing.Statistics",
                 [("drawing", "Drawing", None)],
                 ("layerCount", "var"), 3000, 300, "Plot statistics.")
    # Statistics is a multi-return node: replace the single output with the keyed outputs.
    stats["Outputs"] = [port(k, "var") for k in ("layerCount", "pathCount", "drawLength", "travelLength", "outsidePage")]
    g.connect(optimized, 0, stats, 0)
    w = g.watch(3350, 300)
    g.connect(stats, 2, w, 0)

    group3 = g.group("3. Outline, fill, pens and AxiDraw SVG export", 480, -220, 3100, 640)

    doc = {
        "Uuid": str(uuid.uuid4()),
        "IsCustomNode": False,
        "Description": "PlotterNodes sample: a numeral built from Dynamo circles, radiating concentric rings, hatch fill, pen layers and AxiDraw SVG export.",
        "Name": "PlotterNodes_Numeral8",
        "ElementResolver": {
            "ResolutionMap": {
                "Circle": {"Key": "Autodesk.DesignScript.Geometry.Circle", "Value": "ProtoGeometry.dll"},
                "Point": {"Key": "Autodesk.DesignScript.Geometry.Point", "Value": "ProtoGeometry.dll"},
            }
        },
        "Inputs": [],
        "Outputs": [],
        "Nodes": g.nodes,
        "Connectors": g.connectors,
        "Dependencies": [],
        "NodeLibraryDependencies": [
            {
                "Name": PACKAGE_NAME,
                "Version": PACKAGE_VERSION,
                "ReferenceType": "Package",
                "Nodes": g.package_nodes,
            }
        ],
        "Thumbnail": "",
        "GraphDocumentationURL": None,
        "ExtensionWorkspaceData": [],
        "Author": "",
        "Linting": {"activeLinter": "None", "activeLinterId": "7b75fb44-43fd-4631-a878-29f4d5d8399a", "warningCount": 0, "errorCount": 0},
        "Bindings": [],
        "View": {
            "Dynamo": {
                "ScaleFactor": 1.0,
                "HasRunWithoutCrash": False,
                "IsVisibleInDynamoLibrary": True,
                "Version": "4.0.0.0",
                "RunType": "Manual",
                "RunPeriod": "1000",
            },
            "Camera": {
                "Name": "Background Preview",
                "EyeX": 150.0, "EyeY": 260.0, "EyeZ": 300.0,
                "LookX": 0.0, "LookY": -260.0, "LookZ": -300.0,
                "UpX": 0.0, "UpY": 1.0, "UpZ": 0.0,
            },
            "ConnectorPins": [],
            "NodeViews": g.views,
            "Annotations": [group1, group2, group3],
            "X": 200.0,
            "Y": 300.0,
            "Zoom": 0.45,
        },
    }
    return doc


def main():
    out = Path(__file__).resolve().parent.parent / "samples" / "PlotterNodes_Numeral8.dyn"
    out.parent.mkdir(parents=True, exist_ok=True)
    doc = build()
    out.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    print(f"wrote {out} ({len(doc['Nodes'])} nodes, {len(doc['Connectors'])} connectors)")
    if len(sys.argv) > 1:
        import jsonschema  # type: ignore
        schema = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
        jsonschema.validate(doc, schema)
        print("schema validation passed")


if __name__ == "__main__":
    main()
