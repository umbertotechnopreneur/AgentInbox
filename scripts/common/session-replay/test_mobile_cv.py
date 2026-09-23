"""Focused synthetic tests for the mobile calibration and edge transcription."""

from __future__ import annotations

import math
import sys
import unittest
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

from mobile_cv import (  # noqa: E402
    EdgeParams,
    MobileCvError,
    apply_homography,
    calibrate_intrinsics_robust,
    detect_garment_edges,
    edge_points_full_frame,
    fit_plane_homography,
    fit_scale_locked_rigid_transform,
    laplacian_variance_over_corners,
    rotate_clockwise,
    thin_zhang_suen,
)


# Builds row-major planar board coordinates for deterministic calibration fixtures.
def _board_points(pattern: tuple[int, int], square: float) -> np.ndarray:
    count = pattern[0] * pattern[1]
    points = np.zeros((count, 3), dtype=np.float32)
    index = np.arange(count)
    points[:, 0] = (index % pattern[0]) * square
    points[:, 1] = (index // pattern[0]) * square
    return points


# Applies an inverse plane homography to construct synthetic image coordinates.
def _image_points_from_world(matrix: np.ndarray, world: np.ndarray) -> np.ndarray:
    return apply_homography(np.linalg.inv(matrix), world.astype(np.float32)).astype(np.float32)


class MobileCalibrationTests(unittest.TestCase):
    """Verifies exact geometry and hard-gate behavior without production images."""

    # Verifies clockwise rotations keep the same coordinate convention as CameraX replay.
    def test_rotate_clockwise(self) -> None:
        source = np.arange(18, dtype=np.uint8).reshape(2, 3, 3)
        rotated = rotate_clockwise(source, 90)
        np.testing.assert_array_equal(rotated, cv2.rotate(source, cv2.ROTATE_90_CLOCKWISE))

    # Verifies the scale-locked transform recovers a known rotation and translation.
    def test_scale_locked_rigid_transform(self) -> None:
        source = np.array([[0, 0], [20, 0], [0, 30], [20, 30]], dtype=np.float32)
        angle = math.radians(23.0)
        cosine, sine = math.cos(angle), math.sin(angle)
        destination = np.column_stack(
            (
                cosine * source[:, 0] - sine * source[:, 1] + 41.0,
                sine * source[:, 0] + cosine * source[:, 1] - 12.0,
            )
        )
        actual = fit_scale_locked_rigid_transform(source, destination)
        self.assertAlmostEqual(actual[0], cosine, places=7)
        self.assertAlmostEqual(actual[1], sine, places=7)
        # The source/destination fixtures intentionally use the mobile float32 corner frame.
        self.assertAlmostEqual(actual[2], 41.0, delta=2.0e-6)
        self.assertAlmostEqual(actual[3], -12.0, delta=2.0e-6)

    # Verifies the pooled homography accepts rigidly moved board placements at a strict gate.
    def test_pooled_homography_passes_rigid_placements(self) -> None:
        pattern = (4, 3)
        grid = _board_points(pattern, 25.0)[:, :2]
        truth = np.array(
            [[0.48, 0.02, -30.0], [0.01, 0.51, 18.0], [0.00008, -0.00004, 1.0]],
            dtype=np.float64,
        )
        placements: list[np.ndarray] = []
        for angle_degrees, tx, ty in ((0.0, 0.0, 0.0), (17.0, 140.0, 60.0), (-11.0, -80.0, 120.0)):
            angle = math.radians(angle_degrees)
            c, s = math.cos(angle), math.sin(angle)
            world = np.column_stack(
                (
                    c * grid[:, 0] - s * grid[:, 1] + tx,
                    s * grid[:, 0] + c * grid[:, 1] + ty,
                )
            )
            placements.append(_image_points_from_world(truth, world))
        fit = fit_plane_homography(placements, pattern, 25.0, 0.02)
        self.assertLessEqual(fit.max_placement_median_mm, 0.02)

    # Verifies a non-rigid placement cannot pass by silently changing the physical board scale.
    def test_pooled_homography_rejects_non_rigid_scale(self) -> None:
        pattern = (4, 3)
        grid = _board_points(pattern, 25.0)[:, :2]
        truth = np.array(
            [[0.5, 0.0, -20.0], [0.0, 0.5, 10.0], [0.0, 0.0, 1.0]],
            dtype=np.float64,
        )
        first = _image_points_from_world(truth, grid)
        scaled_world = grid * np.array([1.18, 0.86], dtype=np.float32) + np.array([130, 70])
        second = _image_points_from_world(truth, scaled_world)
        with self.assertRaises(MobileCvError):
            fit_plane_homography([first, second], pattern, 25.0, 0.1)

    # Verifies robust intrinsic fitting keeps the configured view floor and passes clean projections.
    def test_intrinsics_fit_synthetic_views(self) -> None:
        pattern = (5, 4)
        object_points = _board_points(pattern, 20.0)
        camera = np.array(
            [[820.0, 0.0, 320.0], [0.0, 820.0, 240.0], [0.0, 0.0, 1.0]],
            dtype=np.float64,
        )
        distortion = np.array([0.015, -0.004, 0.0, 0.0, 0.0], dtype=np.float64)
        views: list[np.ndarray] = []
        for index in range(8):
            rvec = np.array(
                [0.03 * (index - 3), -0.02 * (index % 4), 0.015 * index],
                dtype=np.float64,
            )
            tvec = np.array(
                [-50.0 + 13.0 * index, -35.0 + 9.0 * (index % 3), 760.0 + 22.0 * index],
                dtype=np.float64,
            )
            projected, _ = cv2.projectPoints(object_points, rvec, tvec, camera, distortion)
            views.append(projected.reshape(-1, 2).astype(np.float32))
        fit = calibrate_intrinsics_robust(views, (640, 480), pattern, 20.0, 0.2, 6)
        self.assertLessEqual(fit.rms_px, 0.2)
        self.assertGreaterEqual(fit.retained_views, 6)
        self.assertAlmostEqual(fit.camera_matrix[0, 0], fit.camera_matrix[1, 1], places=6)

    # Verifies the Java sharpness metric is the population variance over the corner rectangle.
    def test_laplacian_variance_uses_corner_rectangle(self) -> None:
        gray = np.zeros((40, 50), dtype=np.uint8)
        gray[10:30, 15:35] = 255
        corners = np.array([[15.2, 10.1], [34.7, 10.2], [15.3, 29.8], [34.8, 29.7]], dtype=np.float32)
        actual = laplacian_variance_over_corners(gray, corners)
        roi = gray[10:30, 15:35]
        expected = float(np.var(cv2.Laplacian(roi, cv2.CV_64F)))
        self.assertAlmostEqual(actual, expected, places=8)


class MobileEdgeTests(unittest.TestCase):
    """Exercises the shipped colour-gradient, mask, upscale, and thinning path."""

    # Verifies a coloured garment on a neutral table produces usable full-frame edge points.
    def test_colour_garment_produces_edges(self) -> None:
        image = np.full((260, 360, 3), (125, 125, 125), dtype=np.uint8)
        cv2.rectangle(image, (85, 55), (275, 215), (180, 145, 235), thickness=-1)
        cv2.line(image, (100, 180), (260, 180), (140, 105, 190), thickness=3)
        result = detect_garment_edges(image, (85, 55, 191, 161), EdgeParams())
        points = edge_points_full_frame(result)
        self.assertGreater(points.shape[0], 100)
        self.assertGreaterEqual(float(points[:, 0].min()), result.roi[0])
        self.assertGreaterEqual(float(points[:, 1].min()), result.roi[1])

    # Verifies an ordinary filled band becomes one pixel wide without being erased.
    def test_thinning_reduces_solid_band(self) -> None:
        binary = np.zeros((60, 120), dtype=np.uint8)
        cv2.rectangle(binary, (10, 20), (109, 28), 255, thickness=-1)
        cv2.circle(binary, (60, 45), 8, 255, thickness=-1)
        thinned = thin_zhang_suen(binary)
        blocks = (
            (thinned[:-1, :-1] > 0)
            & (thinned[:-1, 1:] > 0)
            & (thinned[1:, :-1] > 0)
            & (thinned[1:, 1:] > 0)
        )
        self.assertFalse(bool(np.any(blocks)))
        self.assertGreater(int(np.count_nonzero(thinned)), 10)

    # Pins edge_core's documented symmetric-X exception where no junction pixel is 8-simple.
    def test_thinning_preserves_symmetric_x_crossing(self) -> None:
        rows = ("......", ".#..#.", "..##..", "..##..", ".#..#.", "......")
        binary = np.zeros((6, 6), dtype=np.uint8)
        for y, row in enumerate(rows):
            for x, value in enumerate(row):
                if value == "#":
                    binary[y, x] = 255
        before_count, _ = cv2.connectedComponents(binary, connectivity=8)
        thinned = thin_zhang_suen(binary)
        after_count, _ = cv2.connectedComponents(thinned, connectivity=8)
        blocks = (
            (thinned[:-1, :-1] > 0)
            & (thinned[:-1, 1:] > 0)
            & (thinned[1:, :-1] > 0)
            & (thinned[1:, 1:] > 0)
        )
        self.assertTrue(bool(np.any(blocks)))
        self.assertEqual(after_count, before_count)
        self.assertEqual(int(np.count_nonzero(thinned)), 8)


if __name__ == "__main__":
    unittest.main()
