"""Offline CoACD box decomposition; Unity owns validation and prefab writes."""

import json
import importlib.metadata
import hashlib
import sys
from pathlib import Path

import coacd
import numpy as np
from scipy.optimize import Bounds, LinearConstraint, minimize
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components


def inscribed_box(points, faces):
    center = points.mean(axis=0)
    face_points = points[faces]
    normals = np.cross(face_points[:, 1] - face_points[:, 0], face_points[:, 2] - face_points[:, 0])
    lengths = np.linalg.norm(normals, axis=1)
    face_points = face_points[lengths > 1e-12]
    normals = normals[lengths > 1e-12] / lengths[lengths > 1e-12, None]
    normals[np.sum(normals * (center - face_points[:, 0]), axis=1) > 0] *= -1
    distances = np.sum(normals * face_points[:, 0], axis=1)
    matrix = np.column_stack((normals, np.abs(normals)))
    radius = np.min((distances - normals @ center) / np.abs(normals).sum(axis=1)) * 0.5
    if radius <= 1e-9:
        raise ValueError("CoACD produced a degenerate convex part.")
    initial = np.r_[center, np.full(3, radius)]
    best = initial.copy()

    def remember(candidate):
        nonlocal best
        if (np.isfinite(candidate).all() and (candidate[3:] > 0).all()
                and np.max(matrix @ candidate - distances) <= 1e-8
                and np.prod(candidate[3:]) > np.prod(best[3:])):
            best = candidate.copy()

    result = minimize(
        lambda x: -np.log(x[3:]).sum(), initial,
        jac=lambda x: np.r_[np.zeros(3), -1 / x[3:]],
        bounds=Bounds(np.r_[points.min(axis=0), np.full(3, 1e-10)],
                      np.r_[points.max(axis=0), np.ptp(points, axis=0) / 2]),
        constraints=[LinearConstraint(matrix, -np.inf, distances)],
        method="SLSQP", options={"maxiter": 200, "ftol": 1e-10},
        callback=remember,
    )
    remember(result.x)
    center, half = best[:3], best[3:] * 0.99999
    return {"min": (center - half).tolist(), "max": (center + half).tolist()}


def decompose(request):
    if importlib.metadata.version("coacd") != "1.0.14":
        raise RuntimeError("Expected CoACD 1.0.14. Run Install.ps1 again.")
    vertices = np.asarray(request["vertices"], dtype=np.float64)
    triangles = np.asarray(request["triangles"], dtype=np.int32).reshape(-1, 3)
    if vertices.ndim != 2 or vertices.shape[1] != 3 or not np.isfinite(vertices).all():
        raise ValueError("Expected finite 3D vertices.")
    if triangles.size == 0 or triangles.min() < 0 or triangles.max() >= len(vertices):
        raise ValueError("Invalid triangle indices.")
    vertices, remap = np.unique(vertices, axis=0, return_inverse=True)
    triangles = remap[triangles].astype(np.int32)
    edges = triangles.ravel()
    neighbors = np.roll(triangles, 1, axis=1).ravel()
    count, labels = connected_components(coo_matrix((np.ones(len(edges)), (edges, neighbors)),
                                                   shape=(len(vertices), len(vertices))), directed=False)
    groups = [np.flatnonzero(labels == i) for i in range(count)]
    groups = [group for group in groups if np.isin(triangles[:, 0], group).any()]
    bounds = [(vertices[group].min(axis=0), vertices[group].max(axis=0)) for group in groups]
    nested = any(i != j and (a[0] >= b[0]).all() and (a[1] <= b[1]).all()
                 for i, a in enumerate(bounds) for j, b in enumerate(bounds))
    if len(groups) > 1 and not nested:
        if len(groups) > request['maxBoxes']:
            raise ValueError("More separate shells than the collider budget.")
        parts = []
        largest = max(range(len(groups)), key=lambda i: len(groups[i]))
        for i, group in enumerate(groups):
            mask = np.isin(triangles[:, 0], group)
            mapping = np.full(len(vertices), -1, dtype=np.int32)
            mapping[group] = np.arange(len(group))
            budget = 1 + (max(0, min(8, request['maxBoxes']) - len(groups)) if i == largest else 0)
            parts.extend(convex_parts(vertices[group], mapping[triangles[mask]], budget, request['threshold']))
    else:
        parts = convex_parts(vertices, triangles, min(8, request['maxBoxes']), request['threshold'])
    return {"boxes": [inscribed_box(points, faces) for points, faces in parts]}


def convex_parts(vertices, triangles, max_boxes, threshold):
    coacd.set_log_level("error")
    key = hashlib.sha256(json.dumps({"vertices": vertices.tolist(), "triangles": triangles.tolist(),
                                    "threshold": threshold, "maxBoxes": max_boxes,
                                    "backend": "coacd-1.0.14-ch-60-10-2-1000-seed42"}).encode()).hexdigest()
    cache = Path(__file__).resolve().parents[2] / ".tools/coacd/hulls" / (key + ".json")
    if cache.exists():
        parts = [(np.asarray(part[0]), np.asarray(part[1], dtype=np.int32))
                 for part in json.loads(cache.read_text(encoding="utf-8"))]
    else:
        parts = coacd.run_coacd(
            coacd.Mesh(vertices, triangles),
            threshold=threshold,
            max_convex_hull=max_boxes,
            apx_mode="ch",
            preprocess_mode="auto",
            mcts_nodes=10,
            mcts_iterations=60,
            mcts_max_depth=2,
            resolution=1000,
            pca=False,
            seed=42,
        )
        cache.parent.mkdir(parents=True, exist_ok=True)
        temporary = cache.with_suffix(".tmp")
        temporary.write_text(json.dumps([(v.tolist(), f.tolist()) for v, f in parts]), encoding="utf-8")
        temporary.replace(cache)
    return parts


if __name__ == "__main__":
    request = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
    result = decompose(request)
    output = Path(sys.argv[2])
    temporary = output.with_suffix(".tmp")
    temporary.write_text(json.dumps(result, allow_nan=False), encoding="utf-8")
    temporary.replace(output)
