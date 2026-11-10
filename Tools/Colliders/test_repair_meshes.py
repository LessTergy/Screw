import unittest
from unittest.mock import Mock, patch
import numpy as np
from repair_meshes import _repair_full, _transfer_sources, repair, topology


class RepairTests(unittest.TestCase):
    def test_cap_preserves_original_faces_and_closes_render_seams(self):
        points = np.array([[0., 0., 0.], [1., 0., 0.], [0., 1., 0.], [0., 0., 1.]])
        faces = np.array([[0, 2, 1], [0, 1, 3], [1, 2, 3]])
        # Input uses separate vertices for each render face.
        vertices = points[faces].reshape(-1, 3)
        indices = np.arange(9).reshape(-1, 3)
        result = repair({"part": "open", "vertices": vertices.tolist(), "triangles": indices.ravel().tolist()})
        np.testing.assert_array_equal(result["vertices"][:9], vertices)
        closed = np.concatenate((indices.ravel(), result["capTriangles"])).reshape(-1, 3)
        self.assertFalse(any(topology(np.array(result["vertices"]), closed).values()))
        self.assertEqual(result["mode"], "cap-only")
        self.assertEqual(result["sourceFacesRemoved"], 0)
        self.assertGreater(result["addedTriangles"], 0)

    def test_invalid_indices_are_rejected(self):
        with self.assertRaises(ValueError):
            repair({"part": "bad", "vertices": [[0, 0, 0]], "triangles": [0, 1, 9]})

    @staticmethod
    def tetrahedron():
        return (np.array([[0., 0., 0.], [1., 0., 0.], [0., 1., 0.], [0., 0., 1.]]),
                np.array([[0, 2, 1], [0, 1, 3], [1, 2, 3], [0, 3, 2]], dtype=np.int32))

    def test_fallback_reports_component_loss_and_changed_triangle_counts(self):
        vertices, faces = self.tetrahedron()
        original = np.concatenate((vertices, vertices + 5))
        original_faces = np.concatenate((faces, faces + 4))
        full = dict(mode="full", vertices=vertices.tolist(), triangles=faces.ravel().tolist(),
                    attributeSources=list(range(4)), triangleSources=list(range(4)))
        with patch("repair_meshes._repair_caps", side_effect=ValueError("existing faces changed")), \
                patch("repair_meshes._repair_full", return_value=full) as fallback:
            result = repair(dict(part="two components", vertices=original.tolist(), triangles=original_faces.ravel().tolist()))
        fallback.assert_called_once()
        self.assertEqual(result["mode"], "full")
        self.assertEqual(result["fallbackReason"], "existing faces changed")
        self.assertEqual((result["componentCountBefore"], result["componentCountAfter"]), (2, 1))
        self.assertEqual((result["triangleCountBefore"], result["triangleCountAfter"]), (8, 4))

    def test_full_mode_preserves_attribute_seams_and_source_face_labels(self):
        vertices, faces = self.tetrahedron()
        original = vertices[faces].reshape(-1, 3)
        original_faces = np.arange(12).reshape(-1, 3)
        result = _transfer_sources(original, original_faces, vertices, faces)
        np.testing.assert_array_equal(result["triangleSources"], np.arange(4))
        transferred = np.array(result["vertices"])
        indices = np.array(result["triangles"]).reshape(-1, 3)
        np.testing.assert_array_equal(transferred[indices], vertices[faces])
        self.assertEqual(len(transferred), 12)
        for i, triangle in enumerate(indices):
            self.assertEqual(set(np.array(result["attributeSources"])[triangle]), set(original_faces[i]))

    def test_full_mode_cleans_without_explicit_component_removal_or_joining(self):
        vertices, faces = self.tetrahedron()
        mesh = Mock()
        mesh.return_arrays.return_value = vertices, faces
        with patch("repair_meshes._load", return_value=mesh):
            result = _repair_full(vertices, faces)
        self.assertEqual(result["mode"], "full")
        mesh.clean.assert_called_once_with()
        mesh.remove_smallest_components.assert_not_called()
        mesh.join_closest_components.assert_not_called()

    def test_both_failures_are_reported_without_accepting_invalid_geometry(self):
        vertices, faces = self.tetrahedron()
        with patch("repair_meshes._repair_caps", side_effect=ValueError("caps refused")), \
                patch("repair_meshes._repair_full", side_effect=ValueError("full refused")):
            with self.assertRaisesRegex(ValueError, "caps refused.*full refused"):
                repair(dict(part="broken", vertices=vertices.tolist(), triangles=faces.ravel().tolist()))

    def test_topology_detects_inconsistent_winding(self):
        vertices, faces = self.tetrahedron()
        self.assertFalse(any(topology(vertices, faces).values()))
        faces[0] = faces[0, ::-1]
        self.assertGreater(topology(vertices, faces)["windingErrors"], 0)
