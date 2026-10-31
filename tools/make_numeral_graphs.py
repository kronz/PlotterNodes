#!/usr/bin/env python3
"""Generates samples/Numerals/Numeral_0.dyn ... Numeral_9.dyn and a matching C# preview program.

Each numeral is written once as a *recipe* (the functions digit_0 ... digit_9 below). A recipe
builds a small dataflow graph out of PlotterNodes calls. The graph is compiled twice:

  1. to a Dynamo .dyn file: DesignScript code blocks for the numeral curves and literal values,
     zero-touch nodes for every PlotterNodes call, List Create nodes, a String node for the SVG
     path, groups and a tidy left-to-right layout;
  2. to tools/NumeralPreview/Program.cs: the same calls in C#, with Dynamo's auto-replication
     written out as Select(...). Running it renders exactly what the graphs compute, without Dynamo.

Node signatures, parameter names and defaults come from tools/plotter_api.json (written by
tools/ApiDump from the compiled DLL), so a graph can only reference nodes that really exist.

Usage:
    python tools/make_numeral_graphs.py [--schema path/to/Dynamo/doc/dyn-file-spec.json]
                                        [--svg-dir D:\\Drawing\\PlotterNodes\\output]
"""
import argparse
import json
import math
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
API = json.loads((ROOT / "tools" / "plotter_api.json").read_text(encoding="utf-8"))["functions"]
PACKAGE_NAME = "PlotterNodes"
PACKAGE_VERSION = "0.1.0"
PAGE_W, PAGE_H = 297.0, 210.0
CX, CY = PAGE_W / 2, PAGE_H / 2
ID_NAMESPACE = uuid.UUID("6f1d3c52-6a0e-4c55-9d0b-1f8a8f1c2d77")

S_NUMERAL = "numeral"
S_PATTERN = "pattern"
S_PENS = "pens"
SECTION_ORDER = [S_NUMERAL, S_PATTERN, S_PENS]
SECTION_COLORS = {S_NUMERAL: "#FFC1D676", S_PATTERN: "#FFB9F9E1", S_PENS: "#FFFFB8D8"}


# ----------------------------------------------------------------------------------------------
# Formatting helpers
# ----------------------------------------------------------------------------------------------

def fmt(x):
    """Number literal valid in both DesignScript and C#."""
    if isinstance(x, bool):
        return "true" if x else "false"
    if isinstance(x, int):
        return str(x)
    if abs(x - round(x)) < 1e-9:
        return str(int(round(x)))
    return f"{x:.4f}".rstrip("0").rstrip(".")


def ds_rank(ds_type):
    """Expected list rank of a DesignScript type; None means arbitrary (var[]..[])."""
    if ds_type is None or "..[]" in ds_type:
        return None
    return ds_type.count("[]")


# ----------------------------------------------------------------------------------------------
# Curve primitives (DesignScript for the .dyn, C# for the preview)
# ----------------------------------------------------------------------------------------------

def P(x, y):
    return f"Point.ByCoordinates({fmt(x)}, {fmt(y)})"


class Line:
    def __init__(self, a, b):
        self.a, self.b = a, b

    def ds(self):
        return f"Line.ByStartPointEndPoint({P(*self.a)}, {P(*self.b)})"

    def cs(self):
        (x1, y1), (x2, y2) = self.a, self.b
        return f"Plotter.Polyline.Line({fmt(x1)}, {fmt(y1)}, {fmt(x2)}, {fmt(y2)})"


class Arc3:
    """Arc through three points, in order (Arc.ByThreePoints)."""

    def __init__(self, p1, p2, p3):
        self.p = (p1, p2, p3)

    def ds(self):
        return f"Arc.ByThreePoints({P(*self.p[0])}, {P(*self.p[1])}, {P(*self.p[2])})"

    def cs(self):
        (ax, ay), (bx, by), (cx, cy) = self.p
        d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by))
        ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d
        uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d
        r = math.hypot(ax - ux, ay - uy)
        a1 = math.degrees(math.atan2(ay - uy, ax - ux))
        a3 = math.degrees(math.atan2(cy - uy, cx - ux))
        ccw = ((bx - ax) * (cy - by) - (by - ay) * (cx - bx)) > 0
        if ccw:
            while a3 <= a1:
                a3 += 360
        else:
            while a3 >= a1:
                a3 -= 360
        segs = max(16, int(abs(a3 - a1) / 3))
        return f"Plotter.Polyline.Arc({fmt(ux)}, {fmt(uy)}, {fmt(r)}, {fmt(a1)}, {fmt(a3)}, {segs})"


class Circle:
    def __init__(self, c, r):
        self.c, self.r = c, r

    def ds(self):
        return f"Circle.ByCenterPointRadius({P(*self.c)}, {fmt(self.r)})"

    def cs(self):
        return f"Plotter.Shape.Circle({fmt(self.c[0])}, {fmt(self.c[1])}, {fmt(self.r)}, 128).Loops[0]"


class Ellipse:
    def __init__(self, c, rx, ry):
        self.c, self.rx, self.ry = c, rx, ry

    def ds(self):
        return f"Ellipse.ByOriginRadii({P(*self.c)}, {fmt(self.rx)}, {fmt(self.ry)})"

    def cs(self):
        return f"Plotter.Shape.Ellipse({fmt(self.c[0])}, {fmt(self.c[1])}, {fmt(self.rx)}, {fmt(self.ry)}, 0, 160).Loops[0]"


class Poly:
    """Closed polygon (PolyCurve.ByPoints with connectLastToFirst = true)."""

    def __init__(self, pts):
        self.pts = pts

    def ds(self):
        return "PolyCurve.ByPoints([" + ", ".join(P(x, y) for x, y in self.pts) + "], true)"

    def cs(self):
        xs = ", ".join(fmt(x) for x, _ in self.pts)
        ys = ", ".join(fmt(y) for _, y in self.pts)
        return f"Plotter.Polyline.ByCoordinates(new List<double> {{ {xs} }}, new List<double> {{ {ys} }}, true)"


def rect(x0, y0, x1, y1):
    return Poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)])


# ----------------------------------------------------------------------------------------------
# Graph builder
# ----------------------------------------------------------------------------------------------

class Val:
    """An output port: which node/port produces it, its list rank and its C# expression."""

    def __init__(self, node, port, rank, cs):
        self.node, self.port, self.rank, self.cs = node, port, rank, cs


class Graph:
    def __init__(self, key):
        self.key = key
        self.counter = 0
        self.nodes = []          # Dynamo node JSON
        self.meta = {}           # node id -> layout info
        self.connectors = []
        self.package_nodes = []
        self.cs_lines = []
        self.cs_count = 0

    # -- ids -------------------------------------------------------------------------------
    def gid(self):
        self.counter += 1
        return uuid.uuid5(ID_NAMESPACE, f"{self.key}:{self.counter}").hex

    def csvar(self):
        self.cs_count += 1
        return f"v{self.cs_count}"

    def port(self, name, desc, default=False):
        return {"Id": self.gid(), "Name": name, "Description": desc, "UsingDefaultValue": default,
                "Level": 2, "UseLevels": False, "KeepListStructure": False}

    def _add(self, node, name, section, lines=1, sources=()):
        self.nodes.append(node)
        self.meta[node["Id"]] = {"name": name, "section": section, "sources": list(sources), "consumers": [],
                                 "height": self._height(node, lines), "index": len(self.nodes)}
        for s in sources:
            self.meta[s]["consumers"].append(node["Id"])

    @staticmethod
    def _height(node, lines):
        if node.get("NodeType") == "CodeBlockNode":
            return 60 + 19 * lines
        ports = max(len(node["Inputs"]), len(node["Outputs"]), 1)
        return 70 + 30 * ports

    def connect(self, val, node, index):
        self.connectors.append({"Start": val.port["Id"], "End": node["Inputs"][index]["Id"], "Id": self.gid()})

    # -- code blocks -------------------------------------------------------------------------
    def code(self, statements, section):
        """statements: list of (ds_statement, cs_expr, rank, comment). One output per statement."""
        lines = []
        outs = []
        for ds, _, _, comment in statements:
            if comment:
                lines.append(f"// {comment}")
            lines.append(f"{ds};")
        node = {
            "ConcreteType": "Dynamo.Graph.Nodes.CodeBlockNodeModel, DynamoCore",
            "Id": self.gid(),
            "NodeType": "CodeBlockNode",
            "Inputs": [],
            "Outputs": [],
            "Replication": "Disabled",
            "Description": "Allows for DesignScript code to be authored directly",
            "Code": "\n".join(lines),
        }
        for i, (_, cs, rank, _) in enumerate(statements):
            p = self.port("", f"Value of expression at line {i + 1}")
            node["Outputs"].append(p)
            var = self.csvar()
            self.cs_lines.append(f"var {var} = {cs};")
            outs.append(Val(node, p, rank, var))
        total_lines = sum(l.count("\n") + 1 for l in lines)
        self._add(node, "Code Block", section, total_lines)
        return outs

    def curves(self, prims, comment, section=S_NUMERAL):
        body = ",\n ".join(p.ds() for p in prims)
        cs = "new List<Plotter.Polyline> { " + ", ".join(p.cs() for p in prims) + " }"
        return self.code([(f"[{body}]", cs, 1, comment)], section)[0]

    def curve_groups(self, groups, comment, section=S_NUMERAL):
        body = ",\n ".join("[" + ", ".join(p.ds() for p in g) + "]" for g in groups)
        cs = "new List<List<Plotter.Polyline>> { " + ", ".join(
            "new List<Plotter.Polyline> { " + ", ".join(p.cs() for p in g) + " }" for g in groups) + " }"
        return self.code([(f"[{body}]", cs, 2, comment)], section)[0]

    # -- zero-touch calls ---------------------------------------------------------------------
    def call(self, qname, this=None, section=S_PENS, cs_override=None, **kwargs):
        overloads = API[qname]
        if len(overloads) != 1:
            raise ValueError(f"{qname} is overloaded; extend the generator to choose")
        fn = overloads[0]
        params = fn["params"]
        names = {p["name"] for p in params}
        for k in kwargs:
            if k not in names:
                raise ValueError(f"{qname} has no parameter {k}")
        if not fn["static"] and this is None:
            raise ValueError(f"{qname} is an instance method; pass this=")

        # Literal inputs go into one code block per call, one statement per literal.
        literal_params = [p for p in params if p["name"] in kwargs and not isinstance(kwargs[p["name"]], Val)]
        literal_vals = {}
        if literal_params:
            stmts = []
            for p in literal_params:
                ds, cs, rank = self.literal(kwargs[p["name"]], p["ds"])
                stmts.append((ds, cs, rank, p["name"]))
            for p, v in zip(literal_params, self.code(stmts, section)):
                literal_vals[p["name"]] = v

        inputs = []   # (port json, Val or None)
        if not fn["static"]:
            inputs.append((self.port(fn["thisName"], fn["class"]), this, 0, fn["class"]))
        for p in params:
            v = kwargs.get(p["name"])
            if p["name"] in literal_vals:
                v = literal_vals[p["name"]]
            if v is None and not p["hasDefault"]:
                raise ValueError(f"{qname}: required parameter {p['name']} not supplied")
            desc = p["ds"] if v is not None or not p["hasDefault"] else f"{p['ds']}\nDefault value : {p['default'] if p['default'] is not None else 'null'}"
            port = self.port(p["name"], desc, default=v is None)
            inputs.append((port, v, ds_rank(p["ds"]), p["ds"]))

        multi = fn["multiReturn"]
        outputs = [self.port(k, "var") for k in multi] if multi else [self.port(fn["returnDs"].split(".")[-1], fn["returnDs"])]
        node = {
            "ConcreteType": "Dynamo.Graph.Nodes.ZeroTouch.DSFunction, DynamoCore",
            "NodeType": "FunctionNode",
            "FunctionSignature": fn["signature"],
            "Id": self.gid(),
            "Inputs": [i[0] for i in inputs],
            "Outputs": outputs,
            "Replication": "Auto",
            "Description": "",
        }
        sources = [v.node["Id"] for _, v, _, _ in inputs if v is not None]
        self._add(node, f"{fn['class'].split('.')[-1]}.{fn['method']}", section, sources=sources)
        self.package_nodes.append(node["Id"])
        for idx, (_, v, _, _) in enumerate(inputs):
            if v is not None:
                self.connect(v, node, idx)

        # ---- C# ------------------------------------------------------------------------------
        replicated = []
        args = {}
        for idx, (port, v, expected, ds) in enumerate(inputs):
            if v is None:
                continue
            name = "this" if (idx == 0 and not fn["static"]) else port["Name"]
            expr = v.cs
            if expected is not None and v.rank is not None and v.rank > expected:
                if v.rank != expected + 1:
                    raise ValueError(f"{qname}: input {name} is rank {v.rank}, expected {expected}; only one level of replication is supported")
                replicated.append((name, v))
                expr = "e"
            if expected is None:
                expr = f"Cs.Obj({expr})"
            args[name] = expr
        if len(replicated) > 1:
            raise ValueError(f"{qname}: more than one replicated input")

        if cs_override is not None:
            call_expr = cs_override(args)
        else:
            named = ", ".join(f"{k}: {v}" for k, v in args.items() if k != "this")
            target = args["this"] if not fn["static"] else fn["class"]
            call_expr = f"{target}.{fn['method']}({named})"
        ret_rank = ds_rank(fn["returnDs"])
        var = self.csvar()
        if replicated:
            self.cs_lines.append(f"var {var} = {replicated[0][1].cs}.Select(e => {call_expr}).ToList();")
            rank = None if ret_rank is None else ret_rank + 1
        else:
            self.cs_lines.append(f"var {var} = {call_expr};")
            rank = ret_rank
        if multi:
            return {k: Val(node, p, 0, f'{var}["{k}"]') for k, p in zip(multi, outputs)}
        return Val(node, outputs[0], rank, var)

    @staticmethod
    def literal(value, ds_type):
        base = ds_type.replace("[]", "").replace("..", "")
        if ds_rank(ds_type) == 1 and isinstance(value, (list, tuple)):
            items = [fmt(float(x)) if base == "double" else fmt(int(x)) for x in value]
            cs_t = "double" if base == "double" else "int"
            return "[" + ", ".join(items) + "]", f"new List<{cs_t}> {{ {', '.join(items)} }}", 1
        if base == "string":
            if "\\" in value or '"' in value:
                raise ValueError("keep backslashes and quotes out of code-block strings; use a String node")
            return f'"{value}"', f'"{value}"', 0
        if base == "bool":
            return fmt(bool(value)), fmt(bool(value)), 0
        if base == "int":
            return fmt(int(value)), fmt(int(value)), 0
        if base == "double":
            return fmt(float(value)), fmt(float(value)), 0
        raise ValueError(f"cannot write a literal for {ds_type}")

    # -- other core nodes ------------------------------------------------------------------------
    def list_create(self, items, cs_elem, section=S_PENS):
        node = {
            "ConcreteType": "CoreNodeModels.CreateList, CoreNodeModels",
            "VariableInputPorts": True,
            "Id": self.gid(),
            "NodeType": "ExtensionNode",
            "Inputs": [self.port(f"item{i}", f"Item Index #{i}") for i in range(len(items))],
            "Outputs": [self.port("list", "A list (type: var[]..[])")],
            "Replication": "Disabled",
            "Description": "Makes a new list from the given inputs",
        }
        self._add(node, "List Create", section, sources=[v.node["Id"] for v in items])
        for i, v in enumerate(items):
            self.connect(v, node, i)
        var = self.csvar()
        self.cs_lines.append(f"var {var} = new List<{cs_elem}> {{ {', '.join(v.cs for v in items)} }};")
        rank = 1 if all(v.rank == 0 for v in items) else None
        return Val(node, node["Outputs"][0], rank, var)

    def string_input(self, value, cs, section=S_PENS):
        node = {
            "ConcreteType": "CoreNodeModels.Input.StringInput, CoreNodeModels",
            "NodeType": "StringInputNode",
            "InputValue": value,
            "Id": self.gid(),
            "Inputs": [],
            "Outputs": [self.port("", "String")],
            "Replication": "Disabled",
            "Description": "Creates a string",
        }
        self._add(node, "SVG file path", section)
        var = self.csvar()
        self.cs_lines.append(f"var {var} = {cs};")
        return Val(node, node["Outputs"][0], 0, var)

    def watch(self, val, section=S_PENS):
        node = {
            "ConcreteType": "CoreNodeModels.Watch, CoreNodeModels",
            "WatchWidth": 160.0,
            "WatchHeight": 60.0,
            "Id": self.gid(),
            "NodeType": "ExtensionNode",
            "Inputs": [self.port("", "Node to show output from")],
            "Outputs": [self.port("", "Node output")],
            "Replication": "Disabled",
            "Description": "Visualizes a node's output",
        }
        self._add(node, "Watch", section, sources=[val.node["Id"]])
        self.connect(val, node, 0)

    # -- layout ------------------------------------------------------------------------------------
    def layout(self):
        meta = self.meta
        depth = {}

        def d(nid):
            if nid in depth:
                return depth[nid]
            srcs = meta[nid]["sources"]
            depth[nid] = 0 if not srcs else 1 + max(d(s) for s in srcs)
            return depth[nid]

        for nid in meta:
            d(nid)
        # Pull pure inputs (code blocks, strings) right next to their first consumer.
        for nid, m in meta.items():
            if not m["sources"] and m["consumers"]:
                depth[nid] = max(0, min(depth[c] for c in m["consumers"]) - 1)

        col_w, gap = 400.0, 40.0
        views, groups = [], []
        band_top = 0.0
        for si, section in enumerate(SECTION_ORDER):
            members = [nid for nid in meta if meta[nid]["section"] == section]
            if not members:
                continue
            col_y = {}
            positions = {}
            for nid in sorted(members, key=lambda n: (depth[n], meta[n]["index"])):
                c = depth[nid]
                y = col_y.get(c, band_top + 80.0)
                positions[nid] = (c * col_w, y)
                col_y[c] = y + meta[nid]["height"] + gap
            band_bottom = max(col_y.values())
            for nid, (x, y) in positions.items():
                views.append({"ShowGeometry": True, "Name": meta[nid]["name"], "Id": nid, "IsSetAsInput": False,
                              "IsSetAsOutput": False, "Excluded": False, "X": x, "Y": y})
            xs = [positions[n][0] for n in members]
            groups.append({
                "Id": self.gid(),
                "Title": self.section_titles[section],
                "DescriptionText": self.section_notes.get(section, ""),
                "IsExpanded": True,
                "WidthAdjustment": 0.0,
                "HeightAdjustment": 0.0,
                "Nodes": members,
                "HasNestedGroups": False,
                "Left": min(xs) - 20.0,
                "Top": band_top,
                "Width": max(xs) - min(xs) + 340.0,
                "Height": band_bottom - band_top,
                "FontSize": 36.0,
                "GroupStyleId": "00000000-0000-0000-0000-000000000000",
                "InitialTop": band_top + 70.0,
                "InitialHeight": band_bottom - band_top - 70.0,
                "TextblockHeight": 60.0,
                "Background": SECTION_COLORS[section],
            })
            band_top = band_bottom + 120.0
        return views, groups

    # -- output ------------------------------------------------------------------------------------
    def to_dyn(self, name, description):
        views, groups = self.layout()
        return {
            "Uuid": str(uuid.uuid5(ID_NAMESPACE, self.key)),
            "IsCustomNode": False,
            "Description": description,
            "Name": name,
            "ElementResolver": {"ResolutionMap": {
                "Point": {"Key": "Autodesk.DesignScript.Geometry.Point", "Value": "ProtoGeometry.dll"},
                "Line": {"Key": "Autodesk.DesignScript.Geometry.Line", "Value": "ProtoGeometry.dll"},
                "Arc": {"Key": "Autodesk.DesignScript.Geometry.Arc", "Value": "ProtoGeometry.dll"},
                "Circle": {"Key": "Autodesk.DesignScript.Geometry.Circle", "Value": "ProtoGeometry.dll"},
                "Ellipse": {"Key": "Autodesk.DesignScript.Geometry.Ellipse", "Value": "ProtoGeometry.dll"},
                "PolyCurve": {"Key": "Autodesk.DesignScript.Geometry.PolyCurve", "Value": "ProtoGeometry.dll"},
            }},
            "Inputs": [],
            "Outputs": [],
            "Nodes": self.nodes,
            "Connectors": self.connectors,
            "Dependencies": [],
            "NodeLibraryDependencies": [{"Name": PACKAGE_NAME, "Version": PACKAGE_VERSION,
                                         "ReferenceType": "Package", "Nodes": self.package_nodes}],
            "Thumbnail": "",
            "GraphDocumentationURL": None,
            "ExtensionWorkspaceData": [],
            "Author": "PlotterNodes samples",
            "Linting": {"activeLinter": "None", "activeLinterId": "7b75fb44-43fd-4631-a878-29f4d5d8399a",
                        "warningCount": 0, "errorCount": 0},
            "Bindings": [],
            "View": {
                "Dynamo": {"ScaleFactor": 1.0, "HasRunWithoutCrash": False, "IsVisibleInDynamoLibrary": True,
                           "Version": "4.0.0.0", "RunType": "Manual", "RunPeriod": "1000"},
                "Camera": {"Name": "Background Preview", "EyeX": CX, "EyeY": CY, "EyeZ": 330.0,
                           "LookX": 0.0, "LookY": 0.0, "LookZ": -330.0, "UpX": 0.0, "UpY": 1.0, "UpZ": 0.0},
                "ConnectorPins": [],
                "NodeViews": views,
                "Annotations": groups,
                "X": 40.0,
                "Y": 40.0,
                "Zoom": 0.4,
            },
        }


# ----------------------------------------------------------------------------------------------
# Shared building blocks for the recipes
# ----------------------------------------------------------------------------------------------

def stroke_numeral(g, prims, width, comment):
    """Numeral from stroke centre lines: Dynamo curves -> polylines -> distance field -> contour."""
    curves = g.curves(prims, comment)
    pls = g.call("Plotter.Polyline.ByCurve", curve=curves, section=S_NUMERAL, cs_override=lambda a: a["curve"])
    field = g.call("Plotter.DistanceField.ByPolylines", polylines=pls, resolution=0.3, padding=width, section=S_NUMERAL)
    return g.call("Plotter.DistanceField.Contour", this=field, level=width / 2, section=S_NUMERAL)


def outline_numeral(g, groups, comment):
    """Numeral from closed outline curves: one Shape per group, fused with a union, corners eased."""
    curves = g.curve_groups(groups, comment)
    parts = g.call("Plotter.Shape.ByCurves", curves=curves, section=S_NUMERAL,
                   cs_override=lambda a: f"Plotter.Shape.ByPolylines({a['curves']})")
    fused = g.call("Plotter.Shape.Union", shapes=parts, resolution=0.3, section=S_NUMERAL)
    return g.call("Plotter.Shape.Smooth", this=fused, iterations=2, ratio=0.2, section=S_NUMERAL)


def offset(g, shape, d, section=S_NUMERAL):
    return g.call("Plotter.Shape.Offset", this=shape, distance=d, resolution=0.3, section=section)


def wobble(g, val, amplitude, wavelength, seed, section=S_PENS, polyline=False):
    q = "Plotter.Polyline.Wobble" if polyline else "Plotter.Shape.Wobble"
    return g.call(q, this=val, amplitude=amplitude, wavelength=wavelength, seed=seed, octaves=2, section=section)


def region(g, margin=8):
    return g.call("Plotter.Page.Region", width=PAGE_W, height=PAGE_H, margin=margin, section=S_PATTERN)


def rings(g, cells, start, step=2):
    return g.call("Plotter.Pattern.SelectRings", cells=cells, start=start, step=step, section=S_PATTERN)


def clip_outside(g, mask, geometry, section=S_PATTERN):
    return g.call("Plotter.Shape.Clip", this=mask, geometry=geometry, keepInside=False, resolution=0.3, section=section)


def clip_inside(g, mask, geometry, section=S_PATTERN):
    return g.call("Plotter.Shape.Clip", this=mask, geometry=geometry, keepInside=True, resolution=0.4, section=section)


def hatch(g, shape, spacing=1.8, angle=60):
    return g.call("Plotter.Shape.Hatch", this=shape, spacing=spacing, angle=angle, inset=1.5, section=S_PENS)


def layer(g, name, color, width, pen, items):
    geom = items[0] if len(items) == 1 else g.list_create(items, "object")
    return g.call("Plotter.Layer.ByGeometry", name=name, geometry=geom, color=color,
                  strokeWidth=width, penNumber=pen, section=S_PENS)


def finish(g, digit, layers, svg_dir):
    lst = g.list_create(layers, "Plotter.Layer")
    drawing = g.call("Plotter.Drawing.ByLayers", layers=lst, section=S_PENS)
    opt = g.call("Plotter.Drawing.Optimize", this=drawing, section=S_PENS)
    path = g.string_input(f"{svg_dir}\\numeral_{digit}.svg", f'System.IO.Path.Combine(outDir, "numeral_{digit}.svg")')
    g.call("Plotter.Drawing.ExportSvg", this=opt, filePath=path, includePageOutline=True, section=S_PENS)
    stats = g.call("Plotter.Drawing.Statistics", this=opt, section=S_PENS)
    g.watch(stats["drawLength"])
    g.cs_lines.append(f'Report("{digit}", {stats["drawLength"].cs.split("[")[0]});')


# ----------------------------------------------------------------------------------------------
# Recipes: one per numeral, each echoing the matching reference image
# ----------------------------------------------------------------------------------------------

def digit_0(g):
    """Bull's-eye circles around an oval 0; the counter is filled with concentric ovals."""
    shape = stroke_numeral(g, [Ellipse((CX, CY), 40, 64)], 26, "Centre line of the 0: one ellipse, thickened to 26 mm below")
    halo = offset(g, shape, 7)
    # Avoid the filled silhouette, otherwise circles would also be placed inside the counter.
    solid = g.call("Plotter.Shape.FillHoles", this=halo, section=S_NUMERAL)
    reg = region(g)
    cells = g.call("Plotter.Pattern.Circles", region=reg, minRadius=5, maxRadius=18, spacing=2.5, rings=4, ringGap=3.2,
                   seed=5, avoid=solid, avoidMargin=1, attempts=5000, section=S_PATTERN)
    outer = wobble(g, rings(g, cells, 0), 0.5, 8, 1)
    inner = wobble(g, rings(g, cells, 1), 0.5, 8, 2)
    counter = g.call("Plotter.Shape.HolesOnly", this=shape, section=S_NUMERAL)
    counter_rings = g.call("Plotter.Shape.InnerRings", this=counter, spacing=4.5, firstOffset=4, maxCount=20, resolution=0.3, section=S_NUMERAL)
    return [
        layer(g, "orange", "#e5572b", 0.6, 1, [outer]),
        layer(g, "pink", "#f0a0d0", 0.6, 2, [inner, wobble(g, counter_rings, 0.4, 10, 3)]),
        layer(g, "numeral", "#b8401f", 0.6, 3, [wobble(g, shape, 0.5, 14, 4), wobble(g, halo, 0.6, 16, 5)]),
        layer(g, "fill", "#cfe8ff", 0.3, 4, [hatch(g, shape, 1.8, 45)]),
    ]


def digit_1(g):
    """A tall bar framed by a grid of squares with nested rectangles (blue and green)."""
    shape = outline_numeral(g, [[rect(129, 30, 168, 180)]], "Outline of the 1: a closed PolyCurve rectangle (any closed curves work)")
    halo = offset(g, shape, 6)
    reg = region(g)
    cells = g.call("Plotter.Pattern.Grid", region=reg, cellWidth=30, cellHeight=26, gap=6, rings=3, ringGap=4.5,
                   innerShape="rectangle", jitter=1.2, seed=11, avoid=halo, avoidMargin=0, section=S_PATTERN)
    blue = clip_outside(g, halo, wobble(g, rings(g, cells, 0), 0.5, 9, 1, S_PATTERN))
    green = clip_outside(g, halo, wobble(g, rings(g, cells, 1), 0.5, 9, 2, S_PATTERN))
    inner_line = offset(g, shape, 2.5)
    return [
        layer(g, "blue", "#3fa7e0", 0.6, 1, [blue]),
        layer(g, "green", "#7bb662", 0.6, 2, [green]),
        layer(g, "pink", "#ff6fae", 0.6, 3, [wobble(g, shape, 0.4, 14, 3), wobble(g, inner_line, 0.4, 14, 4), wobble(g, halo, 0.5, 16, 5)]),
        layer(g, "fill", "#dde3ff", 0.3, 4, [hatch(g, shape)]),
    ]


def digit_2(g):
    """Big hand-cut patches of orange and pink behind a 2 with a green outline."""
    shape = stroke_numeral(g, [
        Arc3((112, 136), (149, 171), (180, 120)),
        Line((180, 120), (117, 40)),
        Line((117, 40), (188, 40)),
    ], 24, "Centre lines of the 2: bowl arc, diagonal and base; thickened to 24 mm below")
    halo = offset(g, shape, 7)
    reg = region(g)
    cells = g.call("Plotter.Pattern.NestedRectangles", region=reg, minCell=34, maxCell=80, gap=5, ringGap=6,
                   minRings=2, maxRings=3, jitter=3, seed=2, avoid=halo, avoidMargin=0, section=S_PATTERN)
    orange = clip_outside(g, halo, wobble(g, rings(g, cells, 0), 1.2, 18, 1, S_PATTERN))
    pink = clip_outside(g, halo, wobble(g, rings(g, cells, 1), 1.0, 14, 2, S_PATTERN))
    return [
        layer(g, "orange", "#f28c38", 0.7, 1, [orange]),
        layer(g, "pink", "#e05fae", 0.6, 2, [pink]),
        layer(g, "green", "#4cc96b", 0.6, 3, [wobble(g, shape, 0.5, 14, 3), wobble(g, halo, 0.6, 16, 4)]),
        layer(g, "fill", "#d6e4ff", 0.3, 4, [hatch(g, shape)]),
    ]


def digit_3(g):
    """Green and blue bull's-eyes around a 3 wrapped in a scalloped, flower-like halo."""
    shape = stroke_numeral(g, [
        Arc3((118.4, 156.5), (178, 151.3), (141.3, 107.5)),
        Arc3((141, 108.4), (183.4, 65.6), (115, 53.5)),
    ], 22, "Centre lines of the 3: two arcs; thickened to 22 mm below")
    flower = g.call("Plotter.Shape.Scallop", this=offset(g, shape, 9), amplitude=4, wavelength=18, scallop=True, section=S_NUMERAL)
    reg = region(g)
    cells = g.call("Plotter.Pattern.Circles", region=reg, minRadius=6, maxRadius=20, spacing=3, rings=3, ringGap=3.5,
                   seed=3, avoid=flower, avoidMargin=3, attempts=5000, section=S_PATTERN)
    return [
        layer(g, "green", "#8bc34a", 0.7, 1, [wobble(g, rings(g, cells, 0), 0.6, 9, 1)]),
        layer(g, "blue", "#1e88e5", 0.7, 2, [wobble(g, rings(g, cells, 1), 0.5, 8, 2)]),
        layer(g, "halo", "#cddc39", 0.6, 3, [wobble(g, flower, 0.4, 12, 3)]),
        layer(g, "numeral", "#e86fc4", 0.6, 4, [wobble(g, shape, 0.5, 14, 4)]),
        layer(g, "fill", "#ece4ff", 0.3, 5, [hatch(g, shape)]),
    ]


def digit_4(g):
    """Patchwork of nested rectangles (yellow and red) around a blocky 4 built from rectangles."""
    shape = outline_numeral(g, [
        [rect(150, 30, 176, 178)],
        [rect(104, 80, 198, 106)],
        [rect(104, 80, 130, 170)],
    ], "Outline of the 4: three overlapping closed rectangles, fused by Shape.Union below")
    halo = offset(g, shape, 5)
    # Closing (grow 15, shrink 6): a 9 mm clearance that also fills the narrow notch between the stems,
    # so no slivers of pattern are left stranded there.
    mask = offset(g, offset(g, shape, 15), -6)
    reg = region(g, 6)
    cells = g.call("Plotter.Pattern.NestedRectangles", region=reg, minCell=22, maxCell=55, gap=4, ringGap=3.2,
                   minRings=2, maxRings=5, jitter=1.2, seed=11, avoid=shape, avoidMargin=2, section=S_PATTERN)
    yellow = clip_outside(g, mask, wobble(g, rings(g, cells, 0), 0.5, 9, 1, S_PATTERN))
    red = clip_outside(g, mask, wobble(g, rings(g, cells, 1), 0.5, 9, 2, S_PATTERN))
    return [
        layer(g, "yellow", "#e3b23c", 0.5, 1, [yellow]),
        layer(g, "red", "#c8402f", 0.5, 2, [red]),
        layer(g, "blue", "#2563eb", 0.6, 3, [wobble(g, shape, 0.5, 14, 3), wobble(g, halo, 0.6, 14, 4), wobble(g, offset(g, shape, 2.5), 0.5, 14, 5)]),
        layer(g, "fill", "#dbeafe", 0.3, 4, [hatch(g, shape, 1.5, 30)]),
    ]


def digit_5(g):
    """A field of small framed ovals around a 5 wrapped in green and red outlines."""
    shape = stroke_numeral(g, [
        Line((182, 172), (122, 172)),
        Line((122, 172), (120.3, 108.6)),
        Arc3((120.3, 108.6), (185.8, 74.5), (115.4, 52.3)),
    ], 22, "Centre lines of the 5: top bar, upright and bowl arc; thickened to 22 mm below")
    green_ring = offset(g, shape, 4.5)
    red_ring = offset(g, shape, 9)
    mask = offset(g, shape, 14)
    reg = region(g, 6)
    cells = g.call("Plotter.Pattern.Grid", region=reg, cellWidth=15, cellHeight=12, gap=4, rings=2, ringGap=3.5,
                   innerShape="ellipse", jitter=0.8, seed=5, avoid=mask, avoidMargin=0, section=S_PATTERN)
    frames = clip_outside(g, mask, wobble(g, rings(g, cells, 0, 0), 0.4, 7, 1, S_PATTERN))
    dots = clip_outside(g, mask, wobble(g, rings(g, cells, 1, 0), 0.3, 5, 2, S_PATTERN))
    return [
        layer(g, "pink", "#ff8fd8", 0.6, 1, [frames]),
        layer(g, "red", "#e63946", 0.6, 2, [dots, wobble(g, red_ring, 0.6, 16, 3), wobble(g, mask, 0.6, 16, 4)]),
        layer(g, "green", "#2a9d8f", 0.6, 3, [wobble(g, green_ring, 0.5, 14, 5)]),
        layer(g, "numeral", "#8ecae6", 0.6, 4, [wobble(g, shape, 0.4, 14, 6)]),
        layer(g, "fill", "#e3f2fd", 0.3, 5, [hatch(g, shape)]),
    ]


def digit_6(g):
    """Thick spiral scrolls swirling around a 6."""
    shape = stroke_numeral(g, [
        Circle((150, 76), 38),
        Arc3((112.2, 80), (124, 142), (178, 170)),
    ], 24, "Centre lines of the 6: bowl circle and rising tail arc; thickened to 24 mm below")
    halo = offset(g, shape, 7)
    solid = g.call("Plotter.Shape.FillHoles", this=halo, section=S_NUMERAL)
    reg = region(g, 6)
    spirals = g.call("Plotter.Pattern.Spirals", region=reg, minRadius=14, maxRadius=32, turns=2.3, spacing=6, seed=6,
                     avoid=solid, avoidMargin=3, attempts=4000, section=S_PATTERN)
    fat = g.call("Plotter.Polyline.Outline", this=spirals, width=7, resolution=0.3, section=S_PATTERN)
    counter = g.call("Plotter.Shape.InnerRings", this=g.call("Plotter.Shape.HolesOnly", this=shape, section=S_NUMERAL),
                     spacing=4, firstOffset=3, maxCount=3, resolution=0.3, section=S_NUMERAL)
    return [
        layer(g, "blue", "#2a7fd4", 0.6, 1, [wobble(g, fat, 0.5, 12, 1), wobble(g, counter, 0.4, 10, 6)]),
        layer(g, "light", "#9dd6ff", 0.4, 2, [wobble(g, spirals, 0.4, 10, 2, polyline=True)]),
        layer(g, "numeral", "#1e40af", 0.6, 3, [wobble(g, shape, 0.5, 14, 3), wobble(g, halo, 0.6, 16, 4), wobble(g, offset(g, shape, 2.5), 0.5, 14, 5)]),
        layer(g, "fill", "#e0e7ff", 0.3, 4, [hatch(g, shape)]),
    ]


def digit_7(g):
    """A grid of square frames in pink, yellow and red around a 7 with a green halo."""
    shape = stroke_numeral(g, [
        Line((108, 170), (188, 170)),
        Line((188, 170), (136, 36)),
    ], 24, "Centre lines of the 7: top bar and diagonal; thickened to 24 mm below")
    halo = offset(g, shape, 6)
    mask = offset(g, shape, 10)
    reg = region(g, 6)
    cells = g.call("Plotter.Pattern.Grid", region=reg, cellWidth=30, cellHeight=30, gap=4, rings=3, ringGap=5,
                   innerShape="rectangle", jitter=1.5, seed=7, avoid=mask, avoidMargin=0, section=S_PATTERN)
    pink = clip_outside(g, mask, wobble(g, rings(g, cells, 0, 0), 0.6, 10, 1, S_PATTERN))
    yellow = clip_outside(g, mask, wobble(g, rings(g, cells, 1, 0), 0.5, 9, 2, S_PATTERN))
    red = clip_outside(g, mask, wobble(g, rings(g, cells, 2, 0), 0.4, 8, 3, S_PATTERN))
    return [
        layer(g, "pink", "#e879c6", 0.7, 1, [pink, wobble(g, shape, 0.5, 14, 4)]),
        layer(g, "yellow", "#f2c14e", 0.6, 2, [yellow]),
        layer(g, "red", "#d64933", 0.6, 3, [red]),
        layer(g, "green", "#9ad94f", 0.6, 4, [wobble(g, halo, 0.6, 16, 5), wobble(g, mask, 0.6, 16, 6)]),
        layer(g, "fill", "#eef0ff", 0.3, 5, [hatch(g, shape)]),
    ]


def digit_8(g):
    """Wobbling concentric contours radiating across the whole page from an 8."""
    shape = stroke_numeral(g, [
        Circle((CX, 136), 26),
        Circle((CX, 71), 34),
    ], 22, "Centre lines of the 8: two circles; thickened to 22 mm below")
    field = g.call("Plotter.DistanceField.ByShape", shape=shape, resolution=0.4, padding=180, section=S_PATTERN)
    wavy = g.call("Plotter.DistanceField.Perturb", this=field, amplitude=1.5, wavelength=40, seed=8, octaves=2, section=S_PATTERN)
    blue_levels = g.call("Plotter.DistanceField.Contours", this=wavy, levels=[4 + 12 * i for i in range(15)], section=S_PATTERN)
    green_levels = g.call("Plotter.DistanceField.Contours", this=wavy, levels=[10 + 12 * i for i in range(15)], section=S_PATTERN)
    reg = region(g, 6)
    counters = g.call("Plotter.Shape.InnerRings", this=g.call("Plotter.Shape.HolesOnly", this=shape, section=S_NUMERAL),
                      spacing=3, firstOffset=2.5, maxCount=10, resolution=0.3, section=S_NUMERAL)
    return [
        layer(g, "blue", "#1d4ed8", 0.6, 1, [clip_inside(g, reg, blue_levels)]),
        layer(g, "green", "#a3c93a", 0.6, 2, [clip_inside(g, reg, green_levels)]),
        layer(g, "teal", "#14b8a6", 0.6, 3, [wobble(g, shape, 0.4, 14, 3), wobble(g, counters, 0.3, 8, 4)]),
        layer(g, "fill", "#e6ecff", 0.3, 4, [hatch(g, shape)]),
    ]


def digit_9(g):
    """Columns of framed ovals separated by thick bars, behind a 9 with a yellow outline."""
    shape = stroke_numeral(g, [
        Circle((146, 130), 30),
        Line((176, 134), (176, 38)),
    ], 24, "Centre lines of the 9: bowl circle and stem; thickened to 24 mm below")
    halo = offset(g, shape, 5)
    mask = g.call("Plotter.Shape.FillHoles", this=halo, section=S_NUMERAL)
    reg = region(g)
    cell_w, cell_h, gap = 16, 26, 7
    cells = g.call("Plotter.Pattern.Grid", region=reg, cellWidth=cell_w, cellHeight=cell_h, gap=gap, rings=2, ringGap=4,
                   innerShape="ellipse", jitter=0.8, seed=21, avoid=halo, avoidMargin=0, section=S_PATTERN)
    # Bars sit midway between the grid columns (same layout rule as Pattern.Grid: columns centred in the region).
    m = 8
    width = PAGE_W - 2 * m
    pitch = cell_w + gap
    nx = int((width + gap) // pitch)
    off_x = m + (width - (nx * pitch - gap)) / 2
    xs = [off_x + i * pitch + cell_w + gap / 2 for i in range(nx - 1)]
    bars = g.curves([Line((x, m + 3), (x, PAGE_H - m - 3)) for x in xs],
                    "Vertical bars midway between the pattern columns", section=S_PATTERN)
    bar_pl = g.call("Plotter.Polyline.ByCurve", curve=bars, section=S_PATTERN, cs_override=lambda a: a["curve"])
    bar_shapes = g.call("Plotter.Polyline.Outline", this=bar_pl, width=5, resolution=0.3, section=S_PATTERN)
    return [
        layer(g, "navy", "#1e3a8a", 0.6, 1, [clip_outside(g, mask, wobble(g, bar_shapes, 0.6, 20, 1, S_PATTERN))]),
        layer(g, "green", "#5fb04a", 0.6, 2, [clip_outside(g, mask, wobble(g, rings(g, cells, 0, 0), 0.5, 8, 2, S_PATTERN))]),
        layer(g, "blue", "#1f6fd8", 0.6, 3, [clip_outside(g, mask, wobble(g, rings(g, cells, 1, 0), 0.4, 7, 3, S_PATTERN))]),
        layer(g, "yellow", "#f4c430", 0.6, 4, [wobble(g, shape, 0.5, 14, 4), wobble(g, halo, 0.6, 16, 5)]),
        layer(g, "fill", "#eef2ff", 0.3, 5, [hatch(g, shape)]),
    ]


RECIPES = [digit_0, digit_1, digit_2, digit_3, digit_4, digit_5, digit_6, digit_7, digit_8, digit_9]


# ----------------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------------

CS_HEADER = """// <auto-generated>
// Generated by tools/make_numeral_graphs.py from the same recipes as samples/Numerals/*.dyn.
// Each NumeralN method performs the graph's node calls in order; Dynamo replication is written
// out as Select(...). Do not edit by hand.
// </auto-generated>
using System;
using System.Collections.Generic;
using System.Linq;

namespace NumeralPreview
{
    internal static class Cs
    {
        public static IList<object> Obj(object o)
            => o is System.Collections.IEnumerable e && !(o is string) ? e.Cast<object>().ToList() : new List<object> { o };
    }

    internal static partial class Program
    {
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--schema", help="Dynamo doc/dyn-file-spec.json for validation")
    ap.add_argument("--svg-dir", default=r"D:\Drawing\PlotterNodes\output", help="folder written to the SVG path nodes")
    args = ap.parse_args()

    out_dir = ROOT / "samples" / "Numerals"
    out_dir.mkdir(parents=True, exist_ok=True)
    schema = None
    if args.schema:
        import jsonschema  # type: ignore
        schema = json.loads(Path(args.schema).read_text(encoding="utf-8-sig"))

    methods = []
    for digit, recipe in enumerate(RECIPES):
        g = Graph(f"numeral-{digit}")
        g.section_titles = {
            S_NUMERAL: f"1. Numeral {digit} from Dynamo curves",
            S_PATTERN: "2. Background pattern",
            S_PENS: "3. Pens, page and AxiDraw SVG",
        }
        g.section_notes = {
            S_NUMERAL: "Edit the curves in the first code block; any closed or open curves work.",
            S_PATTERN: "Seeds, cell sizes and ring gaps live in the code blocks next to each node.",
            S_PENS: "One Layer per pen. Set the SVG path, then press Run.",
        }
        layers = recipe(g)
        finish(g, digit, layers, args.svg_dir)
        doc = g.to_dyn(f"Numeral_{digit}", f"PlotterNodes sample: {recipe.__doc__.strip()}")
        if schema is not None:
            jsonschema.validate(doc, schema)
        path = out_dir / f"Numeral_{digit}.dyn"
        path.write_text(json.dumps(doc, indent=2), encoding="utf-8")
        print(f"wrote {path.relative_to(ROOT)}: {len(doc['Nodes'])} nodes, {len(doc['Connectors'])} wires")
        body = "\n".join("            " + line for line in g.cs_lines)
        methods.append(f"        // {recipe.__doc__.strip()}\n        public static void Numeral{digit}(string outDir)\n        {{\n{body}\n        }}\n")

    cs = CS_HEADER + "\n".join(methods) + "    }\n}\n"
    cs_path = ROOT / "tools" / "NumeralPreview" / "Numerals.g.cs"
    cs_path.parent.mkdir(parents=True, exist_ok=True)
    cs_path.write_text(cs, encoding="utf-8")
    print(f"wrote {cs_path.relative_to(ROOT)}")
    if schema is not None:
        print("schema validation passed for all graphs")


if __name__ == "__main__":
    main()
