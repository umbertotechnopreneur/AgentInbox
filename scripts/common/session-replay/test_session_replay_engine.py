"""Focused contract tests for manifests, calibration artifacts, landmarks, and pipes."""

from __future__ import annotations

import contextlib
import hashlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import session_replay_engine as engine  # noqa: E402


# Returns the exact calibration object expected by qsee.session-replay.extract.v1.
def _calibration_json(lens: list[str], homography: list[str]) -> dict[str, object]:
    return {
        "lensImages": lens,
        "homographyImages": homography,
        "patternColumns": 10,
        "patternRows": 14,
        "squareMm": 50.0,
        "maxIntrinsicRmsPx": 2.0,
        "minIntrinsicViews": 3,
        "maxHomographyErrorMm": 2.0,
        "minHomographyPlacements": 2,
        "maxHomographyPlacements": 12,
        "lensSharpnessThreshold": 100.0,
        "homographySharpnessThreshold": 200.0,
        "coverageGridSize": {"columns": 20, "rows": 20},
        "coverageNewCells": 17,
        "coverageTargetFraction": 0.7,
        "homographyStabilityToleranceFraction": 0.01,
    }


# Writes deterministic JSON for one test fixture.
def _write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


# Returns the SHA-256 of a local test file.
def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


# Writes a self-hashed calibration artifact with an explicit rotated frame and homography.
def _write_calibration_artifact(
    path: Path,
    frame: tuple[int, int],
    rotation: int,
    homography: np.ndarray | None = None,
) -> None:
    identity: dict[str, object] = {
        "rotation": rotation,
        "calibration": _calibration_json(["lens.jpg"], ["homography.jpg"]),
        "lensImages": [
            {"path": "lens.jpg", "sizeBytes": 4, "sha256": "0" * 64}
        ],
        "homographyImages": [
            {"path": "homography.jpg", "sizeBytes": 4, "sha256": "1" * 64}
        ],
    }
    matrix = np.eye(3) if homography is None else homography
    payload: dict[str, object] = {
        "schema": engine.CALIBRATION_ARTIFACT_SCHEMA,
        "calibrationIdentitySha256": hashlib.sha256(
            engine._canonical_json_bytes(identity)
        ).hexdigest(),
        "identity": identity,
        "rotation": rotation,
        "frame": {"width": frame[0], "height": frame[1]},
        "intrinsics": {
            "cameraMatrix": np.eye(3).reshape(-1).tolist(),
            "distortion": [0.0] * 5,
            "rmsPx": 0.2,
            "rejectedViews": 0,
            "retainedViews": 10,
        },
        "homography": {
            "matrix": matrix.reshape(-1).tolist(),
            "maxPlacementMedianMm": 0.3,
            "acceptedPlacements": 3,
            "coverageFraction": 0.75,
        },
        "diagnostics": {"lensImages": [], "homographyImages": []},
    }
    payload["payloadSha256"] = hashlib.sha256(
        engine._canonical_json_bytes(payload)
    ).hexdigest()
    _write_json(path, payload)


# Returns one exact current unversioned historical result fixture.
def _historical_json(client: str = "Kiabi", size: str = "M") -> dict[str, object]:
    measure_name = "HSF" if client == "Kiabi" else "A"
    return {
        "product_code": "TShirt",
        "product_brand": "QSee.ai",
        "timestamp": "2026-08-18T08:18:00.250000000Z",
        "size": size,
        "client": client,
        "measures": [{"measure_name": measure_name, "measure_value": 9.8}],
    }


class ManifestContractTests(unittest.TestCase):
    """Ensures schema drift and dangerous path overlaps fail closed."""

    # Creates a temporary exact manifest plus all files needed for read/hash validation.
    def _fixture(self, root: Path, items: list[dict[str, str]] | None = None) -> Path:
        (root / "lens.jpg").write_bytes(b"lens")
        (root / "homography.jpg").write_bytes(b"homography")
        manifest = {
            "schema": engine.EXTRACT_MANIFEST_SCHEMA,
            "rotation": 90,
            "calibration": _calibration_json(["lens.jpg"], ["homography.jpg"]),
            "items": [] if items is None else items,
        }
        path = root / "manifest.json"
        _write_json(path, manifest)
        return path

    # Verifies exact fields and relative path resolution for calibration-only manifests.
    def test_load_exact_empty_item_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            manifest = engine.load_manifest(self._fixture(root))
            self.assertEqual(manifest.rotation, 90)
            self.assertEqual(manifest.items, ())
            self.assertEqual(manifest.calibration.lens_images[0], (root / "lens.jpg").resolve())

    # Verifies unknown compatibility fields are rejected instead of silently ignored.
    def test_unknown_calibration_field_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            path = self._fixture(root)
            manifest = json.loads(path.read_text(encoding="utf-8"))
            manifest["calibration"]["legacyFallback"] = True
            _write_json(path, manifest)
            with self.assertRaises(engine.SessionReplayError):
                engine.load_manifest(path)

    # Verifies the internal engine accepts only the two explicit tool contracts.
    def test_unapproved_coverage_target_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            path = self._fixture(root)
            manifest = json.loads(path.read_text(encoding="utf-8"))
            manifest["calibration"]["coverageTargetFraction"] = 0.6
            _write_json(path, manifest)
            with self.assertRaisesRegex(engine.SessionReplayError, "exactly 0.5 or 0.7"):
                engine.load_manifest(path)

    # Verifies a pipe path cannot replace its source image or another protected input.
    def test_pipe_input_overlap_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            (root / "garment.jpg").write_bytes(b"image")
            (root / "landmarks.json").write_text("{}", encoding="utf-8")
            item = {
                "id": "one",
                "image": "garment.jpg",
                "landmarks": "landmarks.json",
                "pipe": "garment.jpg",
            }
            with self.assertRaises(engine.SessionReplayError):
                engine.load_manifest(self._fixture(root, [item]))


class ArtifactContractTests(unittest.TestCase):
    """Checks source binding, artifact self-integrity, and pipe compatibility."""

    # Creates one exact normalized landmark artifact bound to a real JPEG.
    def _landmark_fixture(self, root: Path) -> tuple[Path, Path, tuple[int, int]]:
        image_path = root / "capture.jpg"
        image = np.full((600, 800, 3), 180, dtype=np.uint8)
        self.assertTrue(cv2.imwrite(str(image_path), image))
        points = []
        for index in range(engine.EXPECTED_LANDMARKS):
            x = 20.0 + index * 6.0
            y = 25.0 + index * 3.0
            points.append(
                {
                    "index": index,
                    "label": f"K{index + 1}",
                    "x": x,
                    "y": y,
                    "normalized_x": x / 800.0,
                    "normalized_y": y / 600.0,
                    "visibility": 1.0,
                }
            )
        artifact = {
            "schema": engine.LANDMARK_ARTIFACT_SCHEMA,
            "source": {"file_name": image_path.name, "sha256": _sha256(image_path)},
            "request": {
                "expected_model": engine.EXPECTED_MODEL,
                "rotation_clockwise_degrees": 0,
            },
            "coordinate_space": {
                "kind": "image_after_clockwise_rotation",
                "width": 800,
                "height": 600,
            },
            "response_metadata": {"model": engine.EXPECTED_MODEL},
            "landmarks": points,
        }
        artifact_path = root / "capture.landmarks.json"
        _write_json(artifact_path, artifact)
        return image_path, artifact_path, (800, 600)

    # Writes and parses one exact pipe bound to the synthetic landmark fixture.
    def _pipe_fixture(
        self,
        root: Path,
        image: Path,
        landmark_path: Path,
        frame: tuple[int, int],
        points: tuple[engine.LandmarkPoint, ...],
        homography: np.ndarray | None = None,
    ) -> engine.ParsedPipe:
        pipe_path = root / "capture.pipe"
        item = engine.ReplayItem("one", image, landmark_path, pipe_path)
        calibrated = np.array(
            [[point.x + 0.25, point.y - 0.25] for point in points],
            dtype=np.float64,
        )
        text = engine._build_pipe(
            item,
            frame,
            np.eye(3) if homography is None else homography,
            (10, 10, 150, 120),
            points,
            calibrated,
            np.array([[2.0, 3.0], [4.0, 5.0]], dtype=np.float64),
        )
        pipe_path.write_bytes(text.encode("utf-8"))
        return engine.parse_pipe(pipe_path)

    # Builds the exact Java analysis v1 shape for all current client measurements.
    def _analysis_fixture(
        self,
        image: Path,
        frame: tuple[int, int],
        pipe: engine.ParsedPipe,
        calibration_sha256: str,
        client: str = "Kiabi",
        size: str = "M",
    ) -> dict[str, object]:
        keypoints = [
            {
                "index": index + 1,
                "raw": {"x": point.raw_x, "y": point.raw_y},
                "undistorted": {
                    "x": point.undistorted_x,
                    "y": point.undistorted_y,
                },
                "normalized": {
                    "x": point.normalized_x,
                    "y": point.normalized_y,
                },
                "visibility": point.visibility,
            }
            for index, point in enumerate(pipe.keypoints)
        ]
        measurements = []
        for index, measure_id in enumerate(engine.MEASUREMENT_IDS_BY_CLIENT[client]):
            target_mm = 100.0 + index
            measurements.append(
                {
                    "id": measure_id,
                    "mm": target_mm,
                    "cm": target_mm / 10.0,
                    "targetMm": target_mm,
                    "targetCm": target_mm / 10.0,
                    "minimumAllowedMm": target_mm - 10.0,
                    "minimumAllowedCm": (target_mm - 10.0) / 10.0,
                    "maximumAllowedMm": target_mm + 10.0,
                    "maximumAllowedCm": (target_mm + 10.0) / 10.0,
                    "gapMm": 0.0,
                    "gapCm": 0.0,
                    "verified": True,
                    "verdict": "PASS",
                }
            )
        endpoints = {}
        for index, endpoint_id in enumerate(
            sorted(engine.OPTIONAL_ENDPOINT_IDS_BY_CLIENT[client])
        ):
            normalized_x = 0.2 + index * 0.05
            normalized_y = 0.4 + index * 0.05
            endpoints[endpoint_id] = {
                "normalized": {"x": normalized_x, "y": normalized_y},
                "pixel": {
                    "x": frame[0] * normalized_x,
                    "y": frame[1] * normalized_y,
                },
            }
        return {
            "schema": engine.ANALYSIS_SCHEMA,
            "client": client,
            "size": size,
            "source": {
                "pipe": str(pipe.path),
                "image": image.name,
                "pipeSha256": pipe.sha256,
                "calibrationSha256": calibration_sha256,
            },
            "frame": {"width": frame[0], "height": frame[1]},
            "keypoints": keypoints,
            "measurements": measurements,
            "isMeasured": True,
            "isVerified": True,
            "overallVerdict": "PASS",
            "endpoints": endpoints,
        }

    # Builds the exact inferred UTC association artifact consumed by the renderer.
    def _provenance_fixture(
        self,
        image_name: str,
        historical_name: str = "2026-08-18-08-18-00-0123456789abcdef.json",
        historical_sha256: str = "0" * 64,
    ) -> dict[str, object]:
        return {
            "schema": engine.PROVENANCE_SCHEMA,
            "image": {
                "key": f"data_collection/captured/2026-08-18/{image_name}",
                "timestampUtc": "2026-08-18T08:20:30.250000000Z",
            },
            "historicalResult": {
                "key": f"data_collection/results/measures/2026-08-18/{historical_name}",
                "timestampUtc": "2026-08-18T08:18:00.0000000Z",
                "deltaSeconds": 150.25,
                "sha256": historical_sha256,
            },
            "lensCalibration": {
                "groupId": "round-lens-001",
                "referenceTimestampUtc": "2026-08-18T07:50:00.250Z",
                "deltaSeconds": 1830.0,
            },
            "homographyCalibration": {
                "groupId": "session-table-001",
                "referenceTimestampUtc": "2026-08-18T08:10:20.250Z",
                "deltaSeconds": 610.0,
            },
            "association": engine.PROVENANCE_ASSOCIATION,
        }

    # Builds mutually bound image, landmark, pipe, calibration, and analysis evidence.
    def _bound_fixture(
        self,
        root: Path,
        client: str = "Kiabi",
        size: str = "M",
        homography: np.ndarray | None = None,
    ) -> tuple[
        Path,
        Path,
        tuple[int, int],
        tuple[engine.LandmarkPoint, ...],
        engine.ParsedPipe,
        Path,
        dict[str, object],
    ]:
        image, landmark_path, frame = self._landmark_fixture(root)
        points = engine.load_landmarks(landmark_path, image, frame, 0)
        matrix = np.eye(3) if homography is None else homography
        pipe = self._pipe_fixture(
            root,
            image,
            landmark_path,
            frame,
            points,
            matrix,
        )
        calibration_path = root / "calibration.json"
        _write_calibration_artifact(calibration_path, frame, 0, matrix)
        analysis = self._analysis_fixture(
            image,
            frame,
            pipe,
            _sha256(calibration_path),
            client,
            size,
        )
        return image, landmark_path, frame, points, pipe, calibration_path, analysis

    # Verifies all 17 ordered points load when model, frame, rotation, and source hash agree.
    def test_load_bound_landmark_artifact(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, artifact, frame = self._landmark_fixture(root)
            points = engine.load_landmarks(artifact, image, frame, 0)
            self.assertEqual(len(points), engine.EXPECTED_LANDMARKS)
            self.assertEqual(points[-1].index, 16)

    # Verifies a normalized artifact cannot be reused for another image with the same dimensions.
    def test_landmark_source_hash_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, artifact, frame = self._landmark_fixture(root)
            replacement = root / "replacement.jpg"
            replacement.write_bytes(image.read_bytes() + b"trailing-change")
            with self.assertRaises(engine.SessionReplayError):
                engine.load_landmarks(artifact, replacement, frame, 0)

    # Verifies serialized pipes retain all coordinate representations and explicit edge counts.
    def test_pipe_writer_is_panko_batch_compatible(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            item = engine.ReplayItem(
                "one",
                root / "capture.jpg",
                root / "capture.landmarks.json",
                root / "capture.pipe",
            )
            points = tuple(
                engine.LandmarkPoint(index, 10 + index, 20 + index, 0.1, 0.2, 1.0)
                for index in range(engine.EXPECTED_LANDMARKS)
            )
            calibrated = np.array([[point.x + 0.5, point.y - 0.5] for point in points])
            edges = np.array([[2.0, 3.0], [4.0, 5.0]], dtype=np.float32)
            text = engine._build_pipe(
                item,
                (100, 200),
                np.eye(3),
                (1, 2, 30, 40),
                points,
                calibrated,
                edges,
            )
            self.assertIn("kp_count 17\n", text)
            self.assertEqual(text.count("\nkp "), 17)
            self.assertIn("edge_count 2\n", text)
            self.assertEqual(text.count("\ne "), 2)
            item.pipe.write_bytes(text.encode("utf-8"))
            parsed = engine.parse_pipe(item.pipe)
            self.assertEqual(parsed.image_name, "capture.jpg")
            self.assertEqual(parsed.frame_size, (100, 200))
            self.assertEqual(len(parsed.keypoints), 17)
            self.assertEqual(parsed.edges.shape, (2, 2))

    # Verifies reordered or aliased pipe records fail instead of being normalized.
    def test_pipe_parser_rejects_reordered_records(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, landmark_path, frame = self._landmark_fixture(root)
            points = engine.load_landmarks(landmark_path, image, frame, 0)
            parsed = self._pipe_fixture(root, image, landmark_path, frame, points)
            lines = parsed.path.read_text(encoding="utf-8").splitlines()
            lines[1], lines[2] = lines[2], lines[1]
            parsed.path.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))
            with self.assertRaises(engine.SessionReplayError):
                engine.parse_pipe(parsed.path)

    # Verifies a truncated pipe fails as a contract error before indexed parsing.
    def test_pipe_parser_rejects_truncated_file(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            path = Path(raw) / "short.pipe"
            path.write_bytes(b"image capture.jpg\n")
            with self.assertRaises(engine.SessionReplayError):
                engine.parse_pipe(path)

    # Verifies valid homographies are tested independently of arbitrary projective scale.
    def test_pipe_and_calibration_accept_scaled_invertible_homography(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            scaled = np.eye(3, dtype=np.float64) * 1.0e-8
            image, _, frame, _, pipe, calibration, _ = self._bound_fixture(
                root,
                homography=scaled,
            )
            self.assertEqual(pipe.frame_size, frame)
            digest, coverage_target = engine._validate_render_calibration(
                calibration,
                0,
                frame,
                pipe,
            )
            self.assertEqual(digest, _sha256(calibration))
            self.assertEqual(coverage_target, 0.7)
            self.assertEqual(image.name, pipe.image_name)

    # Verifies an invertible H that sends a calibrated keypoint to infinity is rejected.
    def test_pipe_parser_rejects_homography_point_at_infinity(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, landmark_path, frame = self._landmark_fixture(root)
            points = engine.load_landmarks(landmark_path, image, frame, 0)
            homography = np.array(
                [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [1.0, 0.0, -20.25]],
                dtype=np.float64,
            )
            with self.assertRaises(engine.SessionReplayError):
                self._pipe_fixture(
                    root,
                    image,
                    landmark_path,
                    frame,
                    points,
                    homography,
                )

    # Verifies exact historical fields, UTC timestamp, client, size, and finite values pass.
    def test_historical_contract_accepts_current_unversioned_payload(self) -> None:
        historical = engine.validate_historical_artifact(
            _historical_json(),
            "Kiabi",
            "M",
        )
        self.assertEqual(historical.timestamp_utc, "2026-08-18T08:18:00.250000000Z")
        self.assertEqual(historical.measures[0].name, "HSF")
        self.assertEqual(historical.measures[0].value, 9.8)

    # Verifies historical aliases and unknown compatibility keys are rejected.
    def test_historical_contract_rejects_unknown_root_field(self) -> None:
        payload = _historical_json()
        payload["brand"] = payload["product_brand"]
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_historical_artifact(payload, "Kiabi", "M")

    # Verifies historical timestamps must use the exact UTC-Z representation.
    def test_historical_contract_rejects_non_utc_timestamp(self) -> None:
        payload = _historical_json()
        payload["timestamp"] = "2026-08-18T15:18:00.250+07:00"
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_historical_artifact(payload, "Kiabi", "M")

    # Verifies a historical result cannot cross the explicitly selected client or size.
    def test_historical_contract_rejects_client_or_size_mismatch(self) -> None:
        payload = _historical_json()
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_historical_artifact(payload, "Panko", "M")
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_historical_artifact(payload, "Kiabi", "L")

    # Verifies duplicate names and non-finite historical values fail closed.
    def test_historical_contract_rejects_duplicate_or_nonfinite_measure(self) -> None:
        duplicate = _historical_json()
        duplicate["measures"].append(
            {"measure_name": "HSF", "measure_value": 10.0}
        )
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_historical_artifact(duplicate, "Kiabi", "M")
        nonfinite = _historical_json()
        nonfinite["measures"][0]["measure_value"] = float("nan")
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_historical_artifact(nonfinite, "Kiabi", "M")

    # Verifies a canonical ordered subset remains explicit incomplete comparison evidence.
    def test_historical_contract_accepts_ordered_incomplete_panko_subset(self) -> None:
        payload = _historical_json("Panko", "M")
        payload["measures"] = [
            {"measure_name": name, "measure_value": float(index + 1)}
            for index, name in enumerate(("A", "C", "D", "E", "F", "H", "I", "L", "Q"))
        ]
        historical = engine.validate_historical_artifact(payload, "Panko", "M")
        self.assertEqual(tuple(measure.name for measure in historical.measures), ("A", "C", "D", "E", "F", "H", "I", "L", "Q"))

    # Verifies unknown IDs and out-of-order subsets cannot enter historical comparison rows.
    def test_historical_contract_rejects_unknown_or_reordered_measurements(self) -> None:
        unknown = _historical_json()
        unknown["measures"] = [{"measure_name": "LEGACY", "measure_value": 1.0}]
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_historical_artifact(unknown, "Kiabi", "M")
        reordered = _historical_json("Panko", "M")
        reordered["measures"] = [
            {"measure_name": "C", "measure_value": 1.0},
            {"measure_name": "A", "measure_value": 2.0},
        ]
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_historical_artifact(reordered, "Panko", "M")

    # Verifies pre-remote loading binds the payload timestamp to its result filename minute and bytes.
    def test_historical_file_binds_filename_minute_and_exact_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            path = Path(raw) / "2026-08-18-08-18-59-0123456789abcdef.json"
            payload = _historical_json()
            payload["timestamp"] = "2026-08-18T08:18:00.999999999Z"
            _write_json(path, payload)
            historical = engine.load_historical_artifact(path, "Kiabi", "M")
            self.assertEqual(historical.path, path.resolve())
            self.assertEqual(historical.sha256, _sha256(path))

    # Verifies a noncanonical result name or payload in another minute fails before remote work.
    def test_historical_file_rejects_name_or_payload_minute_drift(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            noncanonical = root / "historical.json"
            _write_json(noncanonical, _historical_json())
            with self.assertRaises(engine.SessionReplayError):
                engine.load_historical_artifact(noncanonical, "Kiabi", "M")
            drifted = root / "2026-08-18-08-19-00-0123456789abcdef.json"
            _write_json(drifted, _historical_json())
            with self.assertRaises(engine.SessionReplayError):
                engine.load_historical_artifact(drifted, "Kiabi", "M")

    # Verifies caught deterministic evidence failures use the reserved exit code 23.
    def test_validate_historical_cli_uses_deterministic_rejection_exit_code(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            path = Path(raw) / "historical.json"
            _write_json(path, _historical_json())
            error = io.StringIO()
            with contextlib.redirect_stderr(error):
                exit_code = engine.main(
                    [
                        "validate-historical",
                        "--historical-json",
                        str(path),
                        "--client",
                        "Kiabi",
                        "--size",
                        "M",
                    ]
                )
            self.assertEqual(exit_code, engine.DETERMINISTIC_REJECTION_EXIT_CODE)
            self.assertIn("session replay failed:", error.getvalue())

    # Verifies argparse failures keep their native exit code instead of using cacheable code 23.
    def test_validate_historical_cli_keeps_argparse_exit_code(self) -> None:
        error = io.StringIO()
        with contextlib.redirect_stderr(error), self.assertRaises(SystemExit) as raised:
            engine.main(["validate-historical"])
        self.assertEqual(raised.exception.code, 2)

    # Verifies strict analysis binding yields current rows plus validated historical values.
    def test_analysis_contract_yields_replay_and_historical_rows(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(root)
            historical = engine.validate_historical_artifact(
                _historical_json(),
                "Kiabi",
                "M",
            )
            rows, overall = engine.validate_analysis_artifact(
                analysis,
                expected_client="Kiabi",
                expected_size="M",
                image_path=image,
                frame_size=frame,
                pipe=pipe,
                calibration_sha256=_sha256(calibration),
                landmarks=points,
                historical=historical,
            )
            self.assertEqual(overall, "PASS")
            self.assertEqual(len(rows), len(engine.MEASUREMENT_IDS_BY_CLIENT["Kiabi"]))
            self.assertEqual(rows[0]["name"], "HSF")
            self.assertEqual(rows[0]["value"], 10.0)
            self.assertEqual(rows[0]["historical"], 9.8)

    # Verifies a root PASS/FAIL value cannot contradict the Java isVerified flag.
    def test_analysis_overall_contradiction_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(root)
            analysis["overallVerdict"] = "FAIL"
            with self.assertRaises(engine.SessionReplayError):
                engine.validate_analysis_artifact(
                    analysis,
                    expected_client="Kiabi",
                    expected_size="M",
                    image_path=image,
                    frame_size=frame,
                    pipe=pipe,
                    calibration_sha256=_sha256(calibration),
                    landmarks=points,
                    historical=None,
                )

    # Verifies analysis cannot substitute a different exact pipe SHA-256.
    def test_analysis_pipe_hash_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(root)
            analysis["source"]["pipeSha256"] = "f" * 64
            with self.assertRaises(engine.SessionReplayError):
                engine.validate_analysis_artifact(
                    analysis,
                    expected_client="Kiabi",
                    expected_size="M",
                    image_path=image,
                    frame_size=frame,
                    pipe=pipe,
                    calibration_sha256=_sha256(calibration),
                    landmarks=points,
                    historical=None,
                )

    # Verifies analysis cannot substitute a different calibration file digest.
    def test_analysis_calibration_hash_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(root)
            analysis["source"]["calibrationSha256"] = "f" * 64
            with self.assertRaises(engine.SessionReplayError):
                engine.validate_analysis_artifact(
                    analysis,
                    expected_client="Kiabi",
                    expected_size="M",
                    image_path=image,
                    frame_size=frame,
                    pipe=pipe,
                    calibration_sha256=_sha256(calibration),
                    landmarks=points,
                    historical=None,
                )

    # Verifies Java undistorted keypoints must remain byte-bound to the selected pipe.
    def test_analysis_undistorted_keypoint_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(root)
            analysis["keypoints"][0]["undistorted"]["x"] += 0.1
            with self.assertRaises(engine.SessionReplayError):
                engine.validate_analysis_artifact(
                    analysis,
                    expected_client="Kiabi",
                    expected_size="M",
                    image_path=image,
                    frame_size=frame,
                    pipe=pipe,
                    calibration_sha256=_sha256(calibration),
                    landmarks=points,
                    historical=None,
                )

    # Verifies selected client, size, source, frame, and landmark fields are all hard bindings.
    def test_analysis_selection_and_landmark_bindings_are_required(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, base = self._bound_fixture(root)
            for case in (
                "client",
                "size",
                "pipe-path",
                "image",
                "frame",
                "raw",
                "normalized",
                "visibility",
            ):
                analysis = json.loads(json.dumps(base))
                if case == "client":
                    analysis["client"] = "Panko"
                elif case == "size":
                    analysis["size"] = "L"
                elif case == "pipe-path":
                    analysis["source"]["pipe"] = str((root / "other.pipe").resolve())
                elif case == "image":
                    analysis["source"]["image"] = "other.jpg"
                elif case == "frame":
                    analysis["frame"]["width"] += 1
                elif case == "raw":
                    analysis["keypoints"][0]["raw"]["x"] += 0.1
                elif case == "normalized":
                    analysis["keypoints"][0]["normalized"]["x"] += 0.1
                else:
                    analysis["keypoints"][0]["visibility"] = 0.5
                with self.subTest(case=case), self.assertRaises(engine.SessionReplayError):
                    engine.validate_analysis_artifact(
                        analysis,
                        expected_client="Kiabi",
                        expected_size="M",
                        image_path=image,
                        frame_size=frame,
                        pipe=pipe,
                        calibration_sha256=_sha256(calibration),
                        landmarks=points,
                        historical=None,
                    )

    # Verifies the exact current Panko measurement order is accepted as a complete set.
    def test_analysis_accepts_exact_panko_measurement_order(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(
                root,
                client="Panko",
                size="L",
            )
            rows, overall = engine.validate_analysis_artifact(
                analysis,
                expected_client="Panko",
                expected_size="L",
                image_path=image,
                frame_size=frame,
                pipe=pipe,
                calibration_sha256=_sha256(calibration),
                landmarks=points,
                historical=None,
            )
            self.assertEqual(tuple(row["name"] for row in rows), engine.MEASUREMENT_IDS_BY_CLIENT["Panko"])
            self.assertEqual(overall, "PASS")

    # Verifies current client measurement IDs cannot be missing or reordered.
    def test_analysis_measurement_order_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(root)
            analysis["measurements"][0], analysis["measurements"][1] = (
                analysis["measurements"][1],
                analysis["measurements"][0],
            )
            with self.assertRaises(engine.SessionReplayError):
                engine.validate_analysis_artifact(
                    analysis,
                    expected_client="Kiabi",
                    expected_size="M",
                    image_path=image,
                    frame_size=frame,
                    pipe=pipe,
                    calibration_sha256=_sha256(calibration),
                    landmarks=points,
                    historical=None,
                )

    # Verifies endpoint pixels must equal their normalized coordinates times the frame.
    def test_analysis_endpoint_pixel_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(root)
            analysis["endpoints"]["HSF"]["pixel"]["x"] += 1.0
            with self.assertRaises(engine.SessionReplayError):
                engine.validate_analysis_artifact(
                    analysis,
                    expected_client="Kiabi",
                    expected_size="M",
                    image_path=image,
                    frame_size=frame,
                    pipe=pipe,
                    calibration_sha256=_sha256(calibration),
                    landmarks=points,
                    historical=None,
                )

    # Verifies an endpoint omitted by a current mobile calculator fallback remains valid evidence.
    def test_analysis_missing_optional_mobile_endpoint_is_accepted(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, _, frame, points, pipe, calibration, analysis = self._bound_fixture(root)
            del analysis["endpoints"]["CF1"]
            rows, overall = engine.validate_analysis_artifact(
                analysis,
                expected_client="Kiabi",
                expected_size="M",
                image_path=image,
                frame_size=frame,
                pipe=pipe,
                calibration_sha256=_sha256(calibration),
                landmarks=points,
                historical=None,
            )
            self.assertEqual(len(rows), 11)
            self.assertEqual(overall, "PASS")

    # Verifies signed image-minus-reference deltas render as explicit inferred UTC evidence.
    def test_provenance_contract_validates_and_formats_deltas(self) -> None:
        provenance = self._provenance_fixture("capture.jpg")
        lines = engine.validate_provenance_artifact(provenance, "capture.jpg")
        self.assertEqual(lines[0], "Image UTC: 2026-08-18T08:20:30.250000000Z")
        self.assertIn("+02m 30.25s", lines[1])
        self.assertIn("+30m 30.00s", lines[2])
        self.assertIn("+10m 10.00s", lines[3])

    # Verifies a stored temporal delta cannot disagree with its two UTC timestamps.
    def test_provenance_delta_contradiction_is_rejected(self) -> None:
        provenance = self._provenance_fixture("capture.jpg")
        provenance["lensCalibration"]["deltaSeconds"] = -1830.0
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_provenance_artifact(provenance, "capture.jpg")

    # Verifies the former historical provenance shape without SHA-256 is no longer accepted.
    def test_provenance_historical_sha256_is_required(self) -> None:
        provenance = self._provenance_fixture("capture.jpg")
        del provenance["historicalResult"]["sha256"]
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_provenance_artifact(provenance, "capture.jpg")

    # Verifies the strict renderer produces one combined image, table, and inferred UTC box.
    def test_render_overlay_writes_combined_review_image(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            (
                image,
                landmark_path,
                frame,
                _,
                pipe,
                calibration_path,
                analysis,
            ) = self._bound_fixture(root)
            analysis_path = root / "capture.analysis.json"
            provenance_path = root / "capture.provenance.json"
            historical_path = root / "2026-08-18-08-18-00-0123456789abcdef.json"
            output_path = root / "capture.review.jpg"
            _write_json(analysis_path, analysis)
            _write_json(historical_path, _historical_json())
            _write_json(
                provenance_path,
                self._provenance_fixture(
                    image.name,
                    historical_path.name,
                    _sha256(historical_path),
                ),
            )
            engine.render_overlay(
                image,
                landmark_path,
                analysis_path,
                pipe_path=pipe.path,
                calibration_path=calibration_path,
                output_path=output_path,
                rotation=0,
                expected_client="Kiabi",
                expected_size="M",
                provenance_path=provenance_path,
                historical_path=historical_path,
                overwrite=False,
            )
            rendered = cv2.imread(str(output_path), cv2.IMREAD_COLOR)
            self.assertIsNotNone(rendered)
            self.assertEqual(rendered.shape[0], frame[1])
            self.assertGreater(rendered.shape[1], frame[0])

    # Verifies render rejects a pipe whose homography differs from verified calibration.
    def test_render_overlay_rejects_pipe_homography_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, landmark_path, frame = self._landmark_fixture(root)
            points = engine.load_landmarks(landmark_path, image, frame, 0)
            pipe_matrix = np.array([[1.0, 0.0, 3.0], [0.0, 1.0, 4.0], [0.0, 0.0, 1.0]])
            pipe = self._pipe_fixture(
                root,
                image,
                landmark_path,
                frame,
                points,
                pipe_matrix,
            )
            calibration_path = root / "calibration.json"
            _write_calibration_artifact(calibration_path, frame, 0, np.eye(3))
            analysis_path = root / "capture.analysis.json"
            _write_json(
                analysis_path,
                self._analysis_fixture(
                    image,
                    frame,
                    pipe,
                    _sha256(calibration_path),
                ),
            )
            provenance_path = root / "capture.provenance.json"
            _write_json(provenance_path, self._provenance_fixture(image.name))
            with self.assertRaises(engine.SessionReplayError):
                engine.render_overlay(
                    image,
                    landmark_path,
                    analysis_path,
                    pipe_path=pipe.path,
                    calibration_path=calibration_path,
                    output_path=root / "review.jpg",
                    rotation=0,
                    expected_client="Kiabi",
                    expected_size="M",
                    provenance_path=provenance_path,
                    historical_path=None,
                    overwrite=False,
                )

    # Verifies the provenance box cannot claim historical evidence without its validated file.
    def test_render_overlay_rejects_historical_presence_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            image, landmark_path, _, _, pipe, calibration, analysis = self._bound_fixture(root)
            analysis_path = root / "capture.analysis.json"
            provenance_path = root / "capture.provenance.json"
            _write_json(analysis_path, analysis)
            _write_json(provenance_path, self._provenance_fixture(image.name))
            with self.assertRaises(engine.SessionReplayError):
                engine.render_overlay(
                    image,
                    landmark_path,
                    analysis_path,
                    pipe_path=pipe.path,
                    calibration_path=calibration,
                    output_path=root / "review.jpg",
                    rotation=0,
                    expected_client="Kiabi",
                    expected_size="M",
                    provenance_path=provenance_path,
                    historical_path=None,
                    overwrite=False,
                )

    # Verifies render provenance binds historical basename and exact file SHA-256.
    def test_render_historical_binding_rejects_basename_or_sha_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            historical_path = root / "2026-08-18-08-18-00-0123456789abcdef.json"
            _write_json(historical_path, _historical_json())
            historical = engine.load_historical_artifact(historical_path, "Kiabi", "M")
            valid = self._provenance_fixture(
                "capture.jpg",
                historical_path.name,
                _sha256(historical_path),
            )
            engine.validate_provenance_artifact(valid, "capture.jpg")
            engine._validate_historical_provenance_binding(valid, historical)

            wrong_name = json.loads(json.dumps(valid))
            wrong_name["historicalResult"]["key"] = (
                "data_collection/results/measures/2026-08-18/"
                "2026-08-18-08-18-00-fedcba9876543210.json"
            )
            with self.assertRaises(engine.SessionReplayError):
                engine._validate_historical_provenance_binding(wrong_name, historical)

            wrong_sha = json.loads(json.dumps(valid))
            wrong_sha["historicalResult"]["sha256"] = "f" * 64
            with self.assertRaises(engine.SessionReplayError):
                engine._validate_historical_provenance_binding(wrong_sha, historical)

    # Verifies provenance cannot substitute an alternate UTC second for its result filename.
    def test_provenance_rejects_alternate_result_timestamp(self) -> None:
        provenance = self._provenance_fixture("capture.jpg")
        provenance["historicalResult"]["timestampUtc"] = "2026-08-18T08:18:01Z"
        provenance["historicalResult"]["deltaSeconds"] = 149.25
        with self.assertRaises(engine.SessionReplayError):
            engine.validate_provenance_artifact(provenance, "capture.jpg")


class FrameValidationTests(unittest.TestCase):
    """Exercises the read-only pre-upload frame gate and standalone artifact integrity."""

    # Writes a self-hashed calibration artifact with an explicit rotated frame.
    def _calibration_artifact(self, path: Path, frame: tuple[int, int], rotation: int) -> None:
        _write_calibration_artifact(path, frame, rotation)

    # Verifies a garment whose rotated frame equals calibration passes without emitting a file.
    def test_validate_frames_passes_exact_rotated_dimensions(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            calibration = root / "calibration.json"
            self._calibration_artifact(calibration, (120, 160), 90)
            image = root / "capture.jpg"
            self.assertTrue(cv2.imwrite(str(image), np.zeros((120, 160, 3), dtype=np.uint8)))
            result = engine.validate_frames(calibration, 90, [image])
            self.assertEqual((result[0]["width"], result[0]["height"]), (120, 160))
            self.assertEqual(set(root.iterdir()), {calibration, image})

    # Verifies a preview-size image cannot proceed to the remote request phase.
    def test_validate_frames_rejects_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            calibration = root / "calibration.json"
            self._calibration_artifact(calibration, (120, 160), 90)
            image = root / "preview.jpg"
            self.assertTrue(cv2.imwrite(str(image), np.zeros((80, 100, 3), dtype=np.uint8)))
            with self.assertRaises(engine.SessionReplayError):
                engine.validate_frames(calibration, 90, [image])


if __name__ == "__main__":
    unittest.main()
