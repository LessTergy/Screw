import itertools
import unittest
from types import SimpleNamespace
from unittest.mock import patch

import numpy as np
from scipy.spatial import ConvexHull

from coacd_boxes import decompose, inscribed_box


class CoacdBoxTests(unittest.TestCase):
    def test_separate_shells_keep_the_empty_gap_and_share_the_budget(self):
        tetrahedron = np.array([[0., 0., 0.], [1., 0., 0.], [0., 1., 0.], [0., 0., 1.]])
        points = np.vstack((tetrahedron, tetrahedron + [3, 0, 0]))
        faces = np.array([[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]])
        triangles = np.vstack((faces, faces + 4))
        result = decompose(dict(vertices=points.tolist(), triangles=triangles.ravel().tolist(), threshold=.2, maxBoxes=2))
        self.assertEqual(len(result['boxes']), 2)
        for box in result['boxes']:
            self.assertTrue(box['max'][0] <= 1 or box['min'][0] >= 3)
            self.assertGreater(np.prod(np.array(box['max']) - box['min']), .036)

    def test_failed_optimizer_cannot_return_an_exterior_box(self):
        points = np.array([[0., 0., 0.], [1., 0., 0.], [0., 1., 0.], [0., 0., 1.]])
        invalid = SimpleNamespace(x=np.array([10., 10., 10., 2., 2., 2.]))
        with patch('coacd_boxes.minimize', return_value=invalid):
            box = inscribed_box(points, ConvexHull(points).simplices)
        corners = np.array(list(itertools.product(*zip(box['min'], box['max']))))
        self.assertTrue((corners >= 0).all())
        self.assertTrue((corners.sum(axis=1) <= 1).all())

    def test_tetrahedron_box_stays_inside_and_uses_a_nonzero_volume(self):
        points = np.array([[0., 0., 0.], [1., 0., 0.], [0., 1., 0.], [0., 0., 1.]])
        box = inscribed_box(points, ConvexHull(points).simplices)
        corners = np.array(list(itertools.product(*zip(box['min'], box['max']))))
        self.assertTrue((corners >= -1e-8).all())
        self.assertTrue((corners.sum(axis=1) <= 1 + 1e-8).all())
        self.assertGreater(np.prod(np.array(box['max']) - box['min']), 0.036)

    def test_real_coacd_cube_decomposition_preserves_input_and_fits_one_box(self):
        points = np.array(list(itertools.product([0., 1.], repeat=3)))
        hull = ConvexHull(points)
        faces = hull.simplices.copy()
        for index, face in enumerate(faces):
            normal = np.cross(points[face[1]] - points[face[0]], points[face[2]] - points[face[0]])
            if np.dot(normal, points[face].mean(axis=0) - 0.5) < 0:
                faces[index] = face[::-1]
        request = dict(vertices=points.tolist(), triangles=faces.ravel().tolist(), threshold=.05, maxBoxes=8)
        original = points.copy()
        result = decompose(request)
        self.assertEqual(len(result['boxes']), 1)
        box = result['boxes'][0]
        self.assertTrue((np.array(box['min']) >= 0).all())
        self.assertTrue((np.array(box['max']) <= 1).all())
        self.assertGreater(np.prod(np.array(box['max']) - box['min']), .99)
        np.testing.assert_array_equal(original, points)

    def test_nonfinite_vertices_are_rejected_before_native_decomposition(self):
        with self.assertRaises(ValueError):
            decompose(dict(vertices=[[float('nan'), 0, 0]], triangles=[0, 0, 0], threshold=.05, maxBoxes=8))


if __name__ == '__main__':
    unittest.main()
