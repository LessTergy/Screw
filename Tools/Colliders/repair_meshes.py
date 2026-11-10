"""Repair review copies: preserve source faces first, then try full MeshFix repair."""
import json
import sys
from collections import Counter

import numpy as np
from pymeshfix import PyTMesh
from scipy.spatial import cKDTree
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components


def topology(vertices, faces):
    _, inverse = np.unique(vertices, axis=0, return_inverse=True)
    welded = inverse[faces]
    edges = np.concatenate((welded[:, [0, 1]], welded[:, [1, 2]], welded[:, [2, 0]]))
    counts = Counter(tuple(sorted(edge)) for edge in edges)
    balance = Counter()
    for a, b in edges:
        balance[tuple(sorted((a, b)))] += 1 if a < b else -1
    triangles = vertices[faces]
    return {
        "boundaryEdges": sum(count == 1 for count in counts.values()),
        "nonManifoldEdges": sum(count > 2 for count in counts.values()),
        "windingErrors": sum(count == 2 and balance[edge] != 0 for edge, count in counts.items()),
        "degenerateFaces": int(np.count_nonzero(np.all(np.cross(
            triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0]) == 0, axis=1))),
    }


def component_count(vertices, faces):
    points, inverse = np.unique(vertices, axis=0, return_inverse=True)
    welded = inverse[faces]
    edges = np.concatenate((welded[:, [0, 1]], welded[:, [1, 2]], welded[:, [2, 0]]))
    graph = coo_matrix((np.ones(len(edges)), (edges[:, 0], edges[:, 1])), shape=(len(points), len(points)))
    _, labels = connected_components(graph, directed=False)
    return int(len(np.unique(labels[welded.ravel()])))


def _load(original, faces):
    points, inverse = np.unique(original, axis=0, return_inverse=True)
    mesh = PyTMesh()
    mesh.set_quiet(True)
    mesh.load_array(points, inverse[faces])
    return mesh


def _repair_caps(original, faces):
    mesh = _load(original, faces)
    mesh.fill_small_boundaries(nbe=0, refine=True)
    vertices, triangles = mesh.return_arrays()
    distance, sources = cKDTree(original).query(vertices)
    # Preserve original render seams, UVs, normals and triangles; only append new caps.
    mapping = sources.copy()
    extra = np.flatnonzero(distance > 1e-10)
    mapping[extra] = np.arange(len(original), len(original) + len(extra))
    combined = np.concatenate((original, vertices[extra]))
    unique, weld = np.unique(combined, axis=0, return_inverse=True)

    def oriented(face):
        a, b, c = map(int, face)
        return min((a, b, c), (b, c, a), (c, a, b))

    remaining = Counter(oriented(face) for face in weld[faces])
    caps = []
    for triangle in triangles:
        mapped = mapping[triangle]
        key = oriented(weld[mapped])
        if remaining[key]:
            remaining[key] -= 1
        else:
            caps.extend(map(int, mapped))
    if any(remaining.values()):
        raise ValueError("MeshFix changed existing faces; automatic cap-only repair refused")
    after = topology(combined, np.concatenate((faces.ravel(), caps)).reshape(-1, 3))
    if any(after.values()) or not caps:
        raise ValueError("Repair did not produce a closed manifold with added caps")
    return {
        "mode": "cap-only", "vertices": combined.tolist(), "capTriangles": caps,
        "attributeSources": list(range(len(original))) + sources[extra].tolist(),
        "sourceVerticesMoved": 0, "sourceFacesRemoved": 0, "addedTriangles": len(caps) // 3,
    }


def _transfer_sources(original, faces, vertices, triangles):
    points, inverse = np.unique(original, axis=0, return_inverse=True)
    _, nearest = cKDTree(points).query(vertices)
    source_faces = {tuple(sorted(face)): i for i, face in enumerate(inverse[faces])}
    _, sources = cKDTree(original[faces].mean(axis=1)).query(vertices[triangles].mean(axis=1))
    for i, face in enumerate(nearest[triangles]):
        sources[i] = source_faces.get(tuple(sorted(face)), sources[i])
    source_corners = original[faces[sources]]
    distances = np.sum((vertices[triangles][:, :, None] - source_corners[:, None, :]) ** 2, axis=3)
    attributes = np.take_along_axis(faces[sources], distances.argmin(axis=2), axis=1)
    # Preserve render seams: a geometric vertex can use different source attributes per face.
    keys, indices = np.unique(np.column_stack((triangles.ravel(), attributes.ravel())), axis=0, return_inverse=True)
    return {
        "vertices": vertices[keys[:, 0]].tolist(), "triangles": indices.tolist(),
        "attributeSources": keys[:, 1].tolist(), "triangleSources": sources.tolist(),
    }


def _repair_full(original, faces):
    mesh = _load(original, faces)
    mesh.fill_small_boundaries(nbe=0, refine=True)
    # Do not explicitly join components or keep only the largest one; report losses from clean().
    mesh.clean()
    vertices, triangles = mesh.return_arrays()
    if len(vertices) < 4 or len(triangles) < 4 or not np.isfinite(vertices).all():
        raise ValueError("Full MeshFix repair produced no finite closed volume")
    if any(topology(vertices, triangles).values()):
        raise ValueError("Full MeshFix repair still has topology defects")
    return dict(mode="full", **_transfer_sources(original, faces, vertices, triangles))


def repair(item):
    original = np.asarray(item["vertices"], dtype=np.float64)
    faces = np.asarray(item["triangles"], dtype=np.int32).reshape(-1, 3)
    if (original.ndim != 2 or original.shape[1] != 3 or not np.isfinite(original).all()
            or not len(faces) or faces.min() < 0 or faces.max() >= len(original)):
        raise ValueError("Invalid mesh arrays")
    try:
        result = _repair_caps(original, faces)
    except (ValueError, RuntimeError) as error:
        try:
            result = _repair_full(original, faces)
        except (ValueError, RuntimeError) as full_error:
            raise ValueError(f"Cap-only repair refused: {error}; full repair failed: {full_error}") from full_error
        result["fallbackReason"] = str(error)
    vertices = np.asarray(result["vertices"], dtype=np.float64)
    repaired = (np.asarray(result["triangles"], dtype=np.int32).reshape(-1, 3) if result["mode"] == "full"
                else np.concatenate((faces.ravel(), result["capTriangles"])).reshape(-1, 3))
    after = topology(vertices, repaired)
    if any(after.values()):
        raise ValueError("Repair result still has topology defects")
    result.update(
        part=item["part"], before=topology(original, faces), after=after,
        triangleCountBefore=len(faces), triangleCountAfter=len(repaired),
        componentCountBefore=component_count(original, faces), componentCountAfter=component_count(vertices, repaired),
    )
    return result


def main(source, destination):
    results = []
    for item in json.loads(source.read_text(encoding="utf-8-sig")):
        try:
            results.append(repair(item))
        except Exception as error:
            results.append({"part": item["part"], "error": str(error)})
    destination.write_text(json.dumps(results, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    from pathlib import Path
    main(Path(sys.argv[1]), Path(sys.argv[2]))
