#!/usr/bin/env python3
"""Calibrate and extract strict, explicitly configured session replay artifacts.

The public PowerShell launcher owns inventory, S3/local selection, credentials,
date/time correlation, and remote landmark submission.  This engine starts only
after those choices are explicit.  Its two production phases are intentionally
separate: calibration must pass before any caller submits garment images, and
extraction consumes only previously generated ``qsee.landmarks.v1`` artifacts.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
import os
import re
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Sequence

import cv2
import numpy as np

from mobile_cv import (
    EdgeParams,
    HomographyFit,
    IntrinsicsFit,
    MobileCvError,
    calibrate_intrinsics_robust,
    detect_garment_edges,
    edge_points_full_frame,
    find_board_corners,
    fit_plane_homography,
    landmark_bounding_box,
    laplacian_variance_over_corners,
    read_rotated_bgr,
    undistort_to_pixel_frame,
)


EXTRACT_MANIFEST_SCHEMA = "qsee.session-replay.extract.v1"
CALIBRATION_ARTIFACT_SCHEMA = "qsee.session-replay.calibration.v1"
LANDMARK_ARTIFACT_SCHEMA = "qsee.landmarks.v1"
EXPECTED_MODEL = "tshirt-panko-detr-pose"
EXPECTED_LANDMARKS = 17
ROOT_KEYS = {"schema", "rotation", "calibration", "items"}
CALIBRATION_KEYS = {
    "lensImages",
    "homographyImages",
    "patternColumns",
    "patternRows",
    "squareMm",
    "maxIntrinsicRmsPx",
    "minIntrinsicViews",
    "maxHomographyErrorMm",
    "minHomographyPlacements",
    "maxHomographyPlacements",
    "lensSharpnessThreshold",
    "homographySharpnessThreshold",
    "coverageGridSize",
    "coverageNewCells",
    "coverageTargetFraction",
    "homographyStabilityToleranceFraction",
}
GRID_KEYS = {"columns", "rows"}
ITEM_KEYS = {"id", "image", "landmarks", "pipe"}
CALIBRATION_ARTIFACT_KEYS = {
    "schema",
    "calibrationIdentitySha256",
    "identity",
    "rotation",
    "frame",
    "intrinsics",
    "homography",
    "diagnostics",
    "payloadSha256",
}
INTRINSIC_ARTIFACT_KEYS = {
    "cameraMatrix",
    "distortion",
    "rmsPx",
    "rejectedViews",
    "retainedViews",
}
HOMOGRAPHY_ARTIFACT_KEYS = {
    "matrix",
    "maxPlacementMedianMm",
    "acceptedPlacements",
    "coverageFraction",
}
ANALYSIS_SCHEMA = "qsee.session-replay.analysis.v1"
ANALYSIS_REQUIRED_KEYS = {
    "schema",
    "client",
    "size",
    "source",
    "frame",
    "keypoints",
    "measurements",
    "isMeasured",
    "isVerified",
    "overallVerdict",
    "endpoints",
}
ANALYSIS_OPTIONAL_KEYS = {"historicalResult"}
ANALYSIS_SOURCE_KEYS = {
    "pipe",
    "image",
    "pipeSha256",
    "calibrationSha256",
}
ANALYSIS_KEYPOINT_KEYS = {
    "index",
    "raw",
    "undistorted",
    "normalized",
    "visibility",
}
ANALYSIS_COORDINATE_KEYS = {"x", "y"}
ANALYSIS_MEASUREMENT_KEYS = {
    "id",
    "mm",
    "cm",
    "targetMm",
    "targetCm",
    "minimumAllowedMm",
    "minimumAllowedCm",
    "maximumAllowedMm",
    "maximumAllowedCm",
    "gapMm",
    "gapCm",
    "verified",
    "verdict",
}
MEASUREMENT_IDS_BY_CLIENT = {
    "Kiabi": ("HSF", "AA", "VV", "EA", "EH", "CF", "ND", "NF", "NHR", "SLS", "SWB"),
    "Panko": ("A", "C", "D", "E", "F", "H", "I", "L", "Q", "R"),
}
OPTIONAL_ENDPOINT_IDS_BY_CLIENT = {
    "Kiabi": frozenset({"HSF", "CF1", "CF2", "NF"}),
    "Panko": frozenset({"A", "C1", "C2"}),
}
HISTORICAL_ROOT_KEYS = {
    "product_code",
    "product_brand",
    "timestamp",
    "size",
    "client",
    "measures",
}
HISTORICAL_MEASURE_KEYS = {"measure_name", "measure_value"}
CALIBRATION_IDENTITY_KEYS = {
    "rotation",
    "calibration",
    "lensImages",
    "homographyImages",
}
CALIBRATION_INPUT_IDENTITY_KEYS = {"path", "sizeBytes", "sha256"}
PROVENANCE_SCHEMA = "qsee.session-replay.provenance.v1"
PROVENANCE_ASSOCIATION = "inferred-from-UTC-time-not-database-identity"
PROVENANCE_ROOT_KEYS = {
    "schema",
    "image",
    "historicalResult",
    "lensCalibration",
    "homographyCalibration",
    "association",
}
PROVENANCE_IMAGE_KEYS = {"key", "timestampUtc"}
PROVENANCE_HISTORICAL_KEYS = {"key", "timestampUtc", "deltaSeconds", "sha256"}
PROVENANCE_CALIBRATION_KEYS = {
    "groupId",
    "referenceTimestampUtc",
    "deltaSeconds",
}
UTC_TIMESTAMP_PATTERN = re.compile(
    r"^(?P<base>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(?P<fraction>\d{1,9}))?Z$"
)
RESULT_FILENAME_PATTERN = re.compile(
    r"^(?P<timestamp>\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2})-[0-9A-Fa-f]{16}\.json$"
)
PROVENANCE_DELTA_TOLERANCE_SECONDS = 0.001
PIPE_COORDINATE_TOLERANCE = 1.0e-8
PIPE_HOMOGRAPHY_TOLERANCE = 1.0e-8
ENDPOINT_COORDINATE_TOLERANCE = 1.0e-6
LOWERCASE_SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
DETERMINISTIC_REJECTION_EXIT_CODE = 23
MOBILE_COVERAGE_TARGET_FRACTION = 0.7
RELAXED_REPLAY_COVERAGE_TARGET_FRACTION = 0.5
ALLOWED_COVERAGE_TARGET_FRACTIONS = frozenset(
    {
        MOBILE_COVERAGE_TARGET_FRACTION,
        RELAXED_REPLAY_COVERAGE_TARGET_FRACTION,
    }
)


class SessionReplayError(RuntimeError):
    """Raised when evidence cannot satisfy the selected strict replay contract."""


@dataclass(frozen=True)
class CalibrationSpec:
    """The exact calibration section of an extraction manifest."""

    lens_images: tuple[Path, ...]
    homography_images: tuple[Path, ...]
    pattern_columns: int
    pattern_rows: int
    square_mm: float
    max_intrinsic_rms_px: float
    min_intrinsic_views: int
    max_homography_error_mm: float
    min_homography_placements: int
    max_homography_placements: int
    lens_sharpness_threshold: float
    homography_sharpness_threshold: float
    coverage_columns: int
    coverage_rows: int
    coverage_new_cells: int
    coverage_target_fraction: float
    homography_stability_tolerance_fraction: float


@dataclass(frozen=True)
class ReplayItem:
    """One explicit garment-to-landmark-to-pipe binding."""

    item_id: str
    image: Path
    landmarks: Path
    pipe: Path


@dataclass(frozen=True)
class ExtractManifest:
    """A resolved, strictly validated extraction manifest."""

    path: Path
    rotation: int
    calibration: CalibrationSpec
    items: tuple[ReplayItem, ...]
    raw_calibration: dict[str, Any]


@dataclass(frozen=True)
class LandmarkPoint:
    """One raw remote landmark in the rotated endpoint frame."""

    index: int
    x: float
    y: float
    normalized_x: float
    normalized_y: float
    visibility: float


@dataclass(frozen=True)
class LoadedCalibration:
    """A verified calibration artifact ready for item extraction."""

    rotation: int
    frame_size: tuple[int, int]
    intrinsics: IntrinsicsFit
    homography: HomographyFit
    identity_sha256: str


@dataclass(frozen=True)
class ExtractedItem:
    """All content needed to commit one compatible ``.pipe`` file."""

    item: ReplayItem
    pipe_text: str
    edge_pixels: int
    bbox: tuple[int, int, int, int]


@dataclass(frozen=True)
class PipeKeypoint:
    """One exact keypoint row parsed from a generated replay pipe."""

    undistorted_x: float
    undistorted_y: float
    raw_x: float
    raw_y: float
    normalized_x: float
    normalized_y: float
    visibility: float


@dataclass(frozen=True)
class ParsedPipe:
    """A strict replay pipe bound to its exact file bytes."""

    path: Path
    sha256: str
    image_name: str
    frame_size: tuple[int, int]
    homography: np.ndarray
    bbox: tuple[int, int, int, int]
    keypoints: tuple[PipeKeypoint, ...]
    edges: np.ndarray


@dataclass(frozen=True)
class HistoricalMeasure:
    """One validated unversioned historical measurement value."""

    name: str
    value: float


@dataclass(frozen=True)
class ValidatedHistorical:
    """A strict current historical result bound to client and size."""

    path: Path | None
    sha256: str | None
    product_code: str
    product_brand: str
    timestamp_utc: str
    client: str
    size: str
    measures: tuple[HistoricalMeasure, ...]


# Builds JSON objects while rejecting duplicate field names at every nesting level.
def _unique_json_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise SessionReplayError(f"JSON contains duplicate field {key!r}")
        result[key] = value
    return result


# Rejects non-standard JSON numeric constants before field-level validation.
def _reject_json_constant(value: str) -> None:
    raise SessionReplayError(f"JSON contains non-standard numeric constant {value!r}")


# Reads one JSON document and exact bytes while converting failures into one contract error.
def _read_json_with_bytes(path: Path) -> tuple[dict[str, Any], bytes]:
    try:
        content = path.read_bytes()
        text = content.decode("utf-8")
        value = json.loads(
            text,
            object_pairs_hook=_unique_json_object,
            parse_constant=_reject_json_constant,
        )
    except OSError as exc:
        raise SessionReplayError(f"cannot read JSON file {path}: {exc}") from exc
    except UnicodeDecodeError as exc:
        raise SessionReplayError(f"JSON file is not strict UTF-8: {path}") from exc
    except json.JSONDecodeError as exc:
        raise SessionReplayError(f"invalid JSON in {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise SessionReplayError(f"JSON root must be an object: {path}")
    return value, content


# Reads one strict JSON object without exposing its raw byte representation.
def _read_json(path: Path) -> dict[str, Any]:
    value, _ = _read_json_with_bytes(path)
    return value


# Rejects missing and unknown object fields so producer/consumer drift never becomes implicit.
def _require_exact_keys(value: dict[str, Any], expected: set[str], field: str) -> None:
    actual = set(value)
    if actual != expected:
        missing = sorted(expected - actual)
        unknown = sorted(actual - expected)
        details: list[str] = []
        if missing:
            details.append(f"missing={missing}")
        if unknown:
            details.append(f"unknown={unknown}")
        raise SessionReplayError(f"{field} has the wrong fields ({', '.join(details)})")


# Requires a JSON string and rejects empty/whitespace-only values.
def _require_string(value: Any, field: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise SessionReplayError(f"{field} must be a non-empty string")
    return value


# Requires a JSON integer while rejecting booleans, which Python otherwise treats as integers.
def _require_integer(value: Any, field: str, minimum: int | None = None) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise SessionReplayError(f"{field} must be an integer")
    if minimum is not None and value < minimum:
        raise SessionReplayError(f"{field} must be at least {minimum}")
    return value


# Requires a finite JSON number with an optional inclusive lower bound.
def _require_number(value: Any, field: str, minimum: float | None = None) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise SessionReplayError(f"{field} must be numeric")
    result = float(value)
    if not math.isfinite(result):
        raise SessionReplayError(f"{field} must be finite")
    if minimum is not None and result < minimum:
        raise SessionReplayError(f"{field} must be at least {minimum}")
    return result


# Resolves one manifest path relative to the manifest document itself.
def _resolve_manifest_path(base: Path, value: Any, field: str) -> Path:
    raw = _require_string(value, field)
    path = Path(raw)
    if not path.is_absolute():
        path = base / path
    return path.resolve()


# Resolves a non-empty ordered image list and rejects duplicate identities.
def _resolve_image_list(base: Path, value: Any, field: str) -> tuple[Path, ...]:
    if not isinstance(value, list) or not value:
        raise SessionReplayError(f"{field} must be a non-empty array")
    paths = tuple(
        _resolve_manifest_path(base, item, f"{field}[{index}]")
        for index, item in enumerate(value)
    )
    if len(set(paths)) != len(paths):
        raise SessionReplayError(f"{field} must not contain duplicate paths")
    return paths


# Parses the calibration object without accepting compatibility aliases.
def _parse_calibration(
    base: Path,
    value: Any,
) -> tuple[CalibrationSpec, dict[str, Any]]:
    if not isinstance(value, dict):
        raise SessionReplayError("calibration must be an object")
    _require_exact_keys(value, CALIBRATION_KEYS, "calibration")
    grid = value["coverageGridSize"]
    if not isinstance(grid, dict):
        raise SessionReplayError("calibration.coverageGridSize must be an object")
    _require_exact_keys(grid, GRID_KEYS, "calibration.coverageGridSize")

    pattern_columns = _require_integer(
        value["patternColumns"], "calibration.patternColumns", 1
    )
    pattern_rows = _require_integer(value["patternRows"], "calibration.patternRows", 1)
    min_intrinsic_views = _require_integer(
        value["minIntrinsicViews"], "calibration.minIntrinsicViews", 3
    )
    min_homography = _require_integer(
        value["minHomographyPlacements"],
        "calibration.minHomographyPlacements",
        1,
    )
    max_homography = _require_integer(
        value["maxHomographyPlacements"],
        "calibration.maxHomographyPlacements",
        1,
    )
    if min_homography > max_homography:
        raise SessionReplayError(
            "calibration.minHomographyPlacements must not exceed "
            "calibration.maxHomographyPlacements"
        )
    coverage_columns = _require_integer(
        grid["columns"], "calibration.coverageGridSize.columns", 1
    )
    coverage_rows = _require_integer(
        grid["rows"], "calibration.coverageGridSize.rows", 1
    )
    coverage_new_cells = _require_integer(
        value["coverageNewCells"], "calibration.coverageNewCells", 1
    )
    if coverage_new_cells > coverage_columns * coverage_rows:
        raise SessionReplayError(
            "calibration.coverageNewCells exceeds the coverage grid cell count"
        )
    coverage_target = _require_number(
        value["coverageTargetFraction"],
        "calibration.coverageTargetFraction",
        0.0,
    )
    if coverage_target not in ALLOWED_COVERAGE_TARGET_FRACTIONS:
        raise SessionReplayError(
            "calibration.coverageTargetFraction must be exactly 0.5 or 0.7"
        )
    stability = _require_number(
        value["homographyStabilityToleranceFraction"],
        "calibration.homographyStabilityToleranceFraction",
        0.0,
    )
    if stability > 1.0:
        raise SessionReplayError(
            "calibration.homographyStabilityToleranceFraction must not exceed 1.0"
        )

    spec = CalibrationSpec(
        lens_images=_resolve_image_list(base, value["lensImages"], "calibration.lensImages"),
        homography_images=_resolve_image_list(
            base,
            value["homographyImages"],
            "calibration.homographyImages",
        ),
        pattern_columns=pattern_columns,
        pattern_rows=pattern_rows,
        square_mm=_require_number(value["squareMm"], "calibration.squareMm", 1.0e-12),
        max_intrinsic_rms_px=_require_number(
            value["maxIntrinsicRmsPx"],
            "calibration.maxIntrinsicRmsPx",
            1.0e-12,
        ),
        min_intrinsic_views=min_intrinsic_views,
        max_homography_error_mm=_require_number(
            value["maxHomographyErrorMm"],
            "calibration.maxHomographyErrorMm",
            1.0e-12,
        ),
        min_homography_placements=min_homography,
        max_homography_placements=max_homography,
        lens_sharpness_threshold=_require_number(
            value["lensSharpnessThreshold"],
            "calibration.lensSharpnessThreshold",
            0.0,
        ),
        homography_sharpness_threshold=_require_number(
            value["homographySharpnessThreshold"],
            "calibration.homographySharpnessThreshold",
            0.0,
        ),
        coverage_columns=coverage_columns,
        coverage_rows=coverage_rows,
        coverage_new_cells=coverage_new_cells,
        coverage_target_fraction=coverage_target,
        homography_stability_tolerance_fraction=stability,
    )
    return spec, value


# Parses item bindings and protects all declared inputs from accidental output replacement.
def _parse_items(base: Path, value: Any, manifest_path: Path) -> tuple[ReplayItem, ...]:
    if not isinstance(value, list):
        raise SessionReplayError("items must be an array")
    items: list[ReplayItem] = []
    ids: set[str] = set()
    pipes: set[Path] = set()
    for index, raw in enumerate(value):
        if not isinstance(raw, dict):
            raise SessionReplayError(f"items[{index}] must be an object")
        _require_exact_keys(raw, ITEM_KEYS, f"items[{index}]")
        item_id = _require_string(raw["id"], f"items[{index}].id")
        if item_id in ids:
            raise SessionReplayError(f"duplicate item id: {item_id}")
        image = _resolve_manifest_path(base, raw["image"], f"items[{index}].image")
        landmarks = _resolve_manifest_path(
            base,
            raw["landmarks"],
            f"items[{index}].landmarks",
        )
        pipe = _resolve_manifest_path(base, raw["pipe"], f"items[{index}].pipe")
        if pipe.suffix.lower() != ".pipe":
            raise SessionReplayError(f"items[{index}].pipe must end in .pipe")
        if pipe in pipes:
            raise SessionReplayError(f"duplicate pipe output: {pipe}")
        if pipe in {manifest_path, image, landmarks}:
            raise SessionReplayError(f"items[{index}].pipe overlaps a protected input")
        ids.add(item_id)
        pipes.add(pipe)
        items.append(ReplayItem(item_id, image, landmarks, pipe))
    return tuple(items)


# Loads the exact extraction manifest schema and resolves every path deterministically.
def load_manifest(path: Path) -> ExtractManifest:
    """Return a strict manifest; item cardinality is validated by the selected CLI phase."""

    resolved = path.resolve()
    root = _read_json(resolved)
    _require_exact_keys(root, ROOT_KEYS, "manifest")
    if root["schema"] != EXTRACT_MANIFEST_SCHEMA:
        raise SessionReplayError(
            f"manifest schema must be exactly {EXTRACT_MANIFEST_SCHEMA!r}"
        )
    rotation = _require_integer(root["rotation"], "rotation")
    if rotation not in (0, 90, 180, 270):
        raise SessionReplayError("rotation must be 0, 90, 180, or 270")
    calibration, raw_calibration = _parse_calibration(resolved.parent, root["calibration"])
    items = _parse_items(resolved.parent, root["items"], resolved)
    protected = {
        resolved,
        *calibration.lens_images,
        *calibration.homography_images,
        *(item.image for item in items),
        *(item.landmarks for item in items),
    }
    for item in items:
        if item.pipe in protected:
            raise SessionReplayError(f"pipe output overlaps a protected manifest input: {item.pipe}")
    return ExtractManifest(
        path=resolved,
        rotation=rotation,
        calibration=calibration,
        items=items,
        raw_calibration=raw_calibration,
    )


# Streams a SHA-256 without loading multi-megabyte calibration photos into Python bytes at once.
def _sha256_file(path: Path) -> str:
    if not path.is_file():
        raise SessionReplayError(f"required file does not exist: {path}")
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
    except OSError as exc:
        raise SessionReplayError(f"cannot hash required file {path}: {exc}") from exc
    return digest.hexdigest()


# Produces stable UTF-8 JSON bytes for hashes and durable artifact output.
def _canonical_json_bytes(value: Any) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=True,
        allow_nan=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


# Computes a content-bound identity for the ordered calibration inputs and every gate value.
def _calibration_identity(manifest: ExtractManifest) -> tuple[str, dict[str, Any]]:
    spec = manifest.calibration
    lens = [
        {
            "path": str(path),
            "sizeBytes": path.stat().st_size if path.is_file() else None,
            "sha256": _sha256_file(path),
        }
        for path in spec.lens_images
    ]
    homography = [
        {
            "path": str(path),
            "sizeBytes": path.stat().st_size if path.is_file() else None,
            "sha256": _sha256_file(path),
        }
        for path in spec.homography_images
    ]
    identity = {
        "rotation": manifest.rotation,
        "calibration": manifest.raw_calibration,
        "lensImages": lens,
        "homographyImages": homography,
    }
    return hashlib.sha256(_canonical_json_bytes(identity)).hexdigest(), identity


# Converts a rotated BGR frame to the same grayscale representation used by the Android JNI path.
def _to_gray(image: np.ndarray) -> np.ndarray:
    return cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)


# Detects and sharpness-gates all ordered lens inputs before fitting the robust intrinsic model.
def _fit_intrinsics(
    manifest: ExtractManifest,
) -> tuple[IntrinsicsFit, tuple[int, int], list[dict[str, Any]]]:
    spec = manifest.calibration
    pattern = spec.pattern_columns, spec.pattern_rows
    frame_size: tuple[int, int] | None = None
    views: list[np.ndarray] = []
    diagnostics: list[dict[str, Any]] = []
    for path in spec.lens_images:
        image = read_rotated_bgr(path, manifest.rotation)
        current_size = image.shape[1], image.shape[0]
        if frame_size is None:
            frame_size = current_size
        elif current_size != frame_size:
            raise SessionReplayError(
                f"lens frame {path} is {current_size}, expected {frame_size}"
            )
        gray = _to_gray(image)
        corners = find_board_corners(gray, pattern, allow_classic_fallback=True)
        if corners is None:
            diagnostics.append({"path": str(path), "accepted": False, "reason": "board-not-found"})
            continue
        sharpness = laplacian_variance_over_corners(gray, corners)
        accepted = sharpness >= spec.lens_sharpness_threshold
        diagnostics.append(
            {
                "path": str(path),
                "accepted": accepted,
                "sharpnessLaplacianVariance": sharpness,
                "reason": None if accepted else "below-sharpness-gate",
            }
        )
        if accepted:
            views.append(corners)
    if frame_size is None:
        raise SessionReplayError("no lens image could establish a calibration frame")
    fit = calibrate_intrinsics_robust(
        views,
        frame_size,
        pattern,
        spec.square_mm,
        spec.max_intrinsic_rms_px,
        spec.min_intrinsic_views,
    )
    return fit, frame_size, diagnostics


# Converts one full board grid into the set of occupied mobile coverage cells.
def _placement_cells(
    corners: np.ndarray,
    frame_size: tuple[int, int],
    columns: int,
    rows: int,
) -> set[int]:
    width, height = frame_size
    points = np.asarray(corners, dtype=np.float64).reshape(-1, 2)
    cell_columns = np.floor((points[:, 0] / width) * columns).astype(np.int64)
    cell_rows = np.floor((points[:, 1] / height) * rows).astype(np.int64)
    cell_columns = np.clip(cell_columns, 0, columns - 1)
    cell_rows = np.clip(cell_rows, 0, rows - 1)
    return set((cell_rows * columns + cell_columns).tolist())


# Returns a board centroid normalized by the full rotated camera frame.
def _placement_centroid(
    corners: np.ndarray,
    frame_size: tuple[int, int],
) -> tuple[float, float]:
    mean = np.asarray(corners, dtype=np.float64).reshape(-1, 2).mean(axis=0)
    return float(mean[0] / frame_size[0]), float(mean[1] / frame_size[1])


# Replays the chronological mobile coverage gate and stops at the first passing joint fit.
def _fit_homography_chronologically(
    manifest: ExtractManifest,
    intrinsics: IntrinsicsFit,
    frame_size: tuple[int, int],
) -> tuple[HomographyFit, list[np.ndarray], list[dict[str, Any]], float]:
    spec = manifest.calibration
    pattern = spec.pattern_columns, spec.pattern_rows
    covered: set[int] = set()
    accepted: list[np.ndarray] = []
    diagnostics: list[dict[str, Any]] = []
    last_centroid: tuple[float, float] | None = None
    last_fit_error: str | None = None
    total_cells = spec.coverage_columns * spec.coverage_rows

    for path in spec.homography_images:
        image = read_rotated_bgr(path, manifest.rotation)
        current_size = image.shape[1], image.shape[0]
        if current_size != frame_size:
            raise SessionReplayError(
                f"homography frame {path} is {current_size}, expected {frame_size}"
            )
        gray = _to_gray(image)
        corners = find_board_corners(gray, pattern, allow_classic_fallback=True)
        if corners is None:
            diagnostics.append({"path": str(path), "accepted": False, "reason": "board-not-found"})
            continue
        sharpness = laplacian_variance_over_corners(gray, corners)
        if sharpness < spec.homography_sharpness_threshold:
            diagnostics.append(
                {
                    "path": str(path),
                    "accepted": False,
                    "sharpnessLaplacianVariance": sharpness,
                    "reason": "below-sharpness-gate",
                }
            )
            continue

        cells = _placement_cells(
            corners,
            frame_size,
            spec.coverage_columns,
            spec.coverage_rows,
        )
        new_cells = len(cells - covered)
        coverage_before = len(covered) / total_cells
        centroid = _placement_centroid(corners, frame_size)
        if not accepted:
            eligible = True
        elif coverage_before >= spec.coverage_target_fraction:
            eligible = last_centroid is None or math.hypot(
                centroid[0] - last_centroid[0],
                centroid[1] - last_centroid[1],
            ) > spec.homography_stability_tolerance_fraction
        else:
            eligible = new_cells >= spec.coverage_new_cells
        if not eligible:
            diagnostics.append(
                {
                    "path": str(path),
                    "accepted": False,
                    "sharpnessLaplacianVariance": sharpness,
                    "newCoverageCells": new_cells,
                    "reason": "coverage-position-rejected",
                }
            )
            continue

        undistorted = undistort_to_pixel_frame(
            corners,
            intrinsics.camera_matrix,
            intrinsics.distortion,
        )
        accepted.append(undistorted)
        covered.update(cells)
        last_centroid = centroid
        coverage_after = len(covered) / total_cells
        diagnostic: dict[str, Any] = {
            "path": str(path),
            "accepted": True,
            "sharpnessLaplacianVariance": sharpness,
            "newCoverageCells": new_cells,
            "coverageFraction": coverage_after,
            "placementNumber": len(accepted),
        }
        diagnostics.append(diagnostic)

        if (
            len(accepted) >= spec.min_homography_placements
            and coverage_after >= spec.coverage_target_fraction
        ):
            try:
                fit = fit_plane_homography(
                    accepted,
                    pattern,
                    spec.square_mm,
                    spec.max_homography_error_mm,
                )
                diagnostic["fitAttempt"] = "passed"
                diagnostic["fitMaxPlacementMedianMm"] = fit.max_placement_median_mm
                return fit, accepted, diagnostics, coverage_after
            except MobileCvError as exc:
                last_fit_error = str(exc)
                diagnostic["fitAttempt"] = "failed"
                diagnostic["fitFailure"] = last_fit_error

        if len(accepted) >= spec.max_homography_placements:
            break

    coverage = len(covered) / total_cells
    reason = last_fit_error or (
        f"coverage stopped at {coverage:.6f} with {len(accepted)} accepted placements"
    )
    raise SessionReplayError(
        "homography calibration did not reach a passing chronological mobile fit: " + reason
    )


# Builds a self-checking JSON calibration artifact after every hard gate has passed.
def build_calibration_artifact(manifest: ExtractManifest) -> dict[str, Any]:
    """Run calibration only; this function performs no landmark request or item extraction."""

    identity_sha256, identity = _calibration_identity(manifest)
    try:
        intrinsics, frame_size, lens_diagnostics = _fit_intrinsics(manifest)
        homography, placements, homography_diagnostics, coverage = (
            _fit_homography_chronologically(manifest, intrinsics, frame_size)
        )
    except MobileCvError as exc:
        raise SessionReplayError(str(exc)) from exc

    payload: dict[str, Any] = {
        "schema": CALIBRATION_ARTIFACT_SCHEMA,
        "calibrationIdentitySha256": identity_sha256,
        "identity": identity,
        "rotation": manifest.rotation,
        "frame": {"width": frame_size[0], "height": frame_size[1]},
        "intrinsics": {
            "cameraMatrix": intrinsics.camera_matrix.reshape(-1).tolist(),
            "distortion": intrinsics.distortion.reshape(-1).tolist(),
            "rmsPx": intrinsics.rms_px,
            "rejectedViews": intrinsics.rejected_views,
            "retainedViews": intrinsics.retained_views,
        },
        "homography": {
            "matrix": homography.matrix.reshape(-1).tolist(),
            "maxPlacementMedianMm": homography.max_placement_median_mm,
            "acceptedPlacements": len(placements),
            "coverageFraction": coverage,
        },
        "diagnostics": {
            "lensImages": lens_diagnostics,
            "homographyImages": homography_diagnostics,
        },
    }
    payload["payloadSha256"] = hashlib.sha256(_canonical_json_bytes(payload)).hexdigest()
    return payload


# Writes UTF-8 content through a same-directory temporary file and an atomic replacement.
def _write_atomic(path: Path, content: bytes, overwrite: bool) -> None:
    if path.exists() and not overwrite:
        raise SessionReplayError(f"output already exists: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=path.parent,
            prefix=f".{path.name}.",
            suffix=".tmp",
            delete=False,
        ) as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
            temporary = Path(stream.name)
        os.replace(temporary, path)
    except OSError as exc:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
        raise SessionReplayError(f"cannot write output {path}: {exc}") from exc


# Saves one calibration artifact with stable readable formatting.
def write_calibration_artifact(
    manifest: ExtractManifest,
    output: Path,
    overwrite: bool,
) -> dict[str, Any]:
    """Calibrate and atomically save the reusable result."""

    if manifest.items:
        raise SessionReplayError("calibrate requires a manifest whose items array is empty")
    destination = output.resolve()
    protected = {
        manifest.path,
        *manifest.calibration.lens_images,
        *manifest.calibration.homography_images,
    }
    if destination in protected:
        raise SessionReplayError("calibration output overlaps a protected input")
    artifact = build_calibration_artifact(manifest)
    rendered = (json.dumps(artifact, indent=2, ensure_ascii=True, allow_nan=False) + "\n").encode(
        "utf-8"
    )
    _write_atomic(destination, rendered, overwrite)
    return artifact


# Reads a finite numeric vector from a calibration artifact.
def _numeric_vector(value: Any, length: int, field: str) -> np.ndarray:
    if not isinstance(value, list) or len(value) != length:
        raise SessionReplayError(f"{field} must contain exactly {length} numbers")
    numbers = np.array(
        [_require_number(item, f"{field}[{index}]") for index, item in enumerate(value)],
        dtype=np.float64,
    )
    return numbers


# Validates homography invertibility independently of its arbitrary projective scale.
def _validate_homography_matrix(matrix: np.ndarray, field: str) -> None:
    if matrix.shape != (3, 3) or not np.isfinite(matrix).all():
        raise SessionReplayError(f"{field} must be a finite 3x3 matrix")
    scale = float(np.max(np.abs(matrix)))
    if scale == 0.0 or not math.isfinite(scale):
        raise SessionReplayError(f"{field} must not be the zero matrix")
    determinant = float(np.linalg.det(matrix / scale))
    if not math.isfinite(determinant) or abs(determinant) <= 1.0e-12:
        raise SessionReplayError(f"{field} is singular or numerically invalid")


# Rejects points that the current Java homography mapper sends to infinity.
def _validate_homography_points(
    matrix: np.ndarray,
    points: Sequence[tuple[float, float]],
    field: str,
) -> None:
    for position, point in enumerate(points):
        x = float(np.float32(point[0]))
        y = float(np.float32(point[1]))
        denominator = matrix[2, 0] * x + matrix[2, 1] * y + matrix[2, 2]
        if not math.isfinite(float(denominator)) or denominator == 0.0:
            raise SessionReplayError(f"{field}[{position}] maps to infinity")
        mapped_x = (matrix[0, 0] * x + matrix[0, 1] * y + matrix[0, 2]) / denominator
        mapped_y = (matrix[1, 0] * x + matrix[1, 1] * y + matrix[1, 2]) / denominator
        if not math.isfinite(float(mapped_x)) or not math.isfinite(float(mapped_y)):
            raise SessionReplayError(f"{field}[{position}] maps to a non-finite point")


# Verifies calibration schema and integrity while hashing the exact artifact bytes.
def _load_verified_calibration_document(path: Path) -> tuple[dict[str, Any], str]:
    raw, content = _read_json_with_bytes(path.resolve())
    _require_exact_keys(raw, CALIBRATION_ARTIFACT_KEYS, "calibration artifact")
    if raw.get("schema") != CALIBRATION_ARTIFACT_SCHEMA:
        raise SessionReplayError(
            f"calibration schema must be exactly {CALIBRATION_ARTIFACT_SCHEMA!r}"
        )
    payload_hash = _require_string(raw.get("payloadSha256"), "payloadSha256")
    unhashed = dict(raw)
    del unhashed["payloadSha256"]
    actual_payload_hash = hashlib.sha256(_canonical_json_bytes(unhashed)).hexdigest()
    if payload_hash != actual_payload_hash:
        raise SessionReplayError("calibration artifact payload hash does not match its content")
    artifact_identity = _require_string(
        raw.get("calibrationIdentitySha256"),
        "calibrationIdentitySha256",
    )
    identity_value = raw.get("identity")
    if not isinstance(identity_value, dict):
        raise SessionReplayError("calibration identity must be an object")
    _require_exact_keys(
        identity_value,
        CALIBRATION_IDENTITY_KEYS,
        "calibration identity",
    )
    if identity_value["rotation"] != raw.get("rotation"):
        raise SessionReplayError(
            "calibration identity rotation contradicts the artifact rotation"
        )
    identity_calibration = identity_value["calibration"]
    if not isinstance(identity_calibration, dict):
        raise SessionReplayError("calibration identity.calibration must be an object")
    _require_exact_keys(
        identity_calibration,
        CALIBRATION_KEYS,
        "calibration identity.calibration",
    )
    for list_name in ("lensImages", "homographyImages"):
        entries = identity_value[list_name]
        if not isinstance(entries, list) or not entries:
            raise SessionReplayError(f"calibration identity.{list_name} must be non-empty")
        for position, entry in enumerate(entries):
            if not isinstance(entry, dict):
                raise SessionReplayError(
                    f"calibration identity.{list_name}[{position}] must be an object"
                )
            _require_exact_keys(
                entry,
                CALIBRATION_INPUT_IDENTITY_KEYS,
                f"calibration identity.{list_name}[{position}]",
            )
            _require_string(
                entry["path"],
                f"calibration identity.{list_name}[{position}].path",
            )
            _require_integer(
                entry["sizeBytes"],
                f"calibration identity.{list_name}[{position}].sizeBytes",
                1,
            )
            digest = _require_string(
                entry["sha256"],
                f"calibration identity.{list_name}[{position}].sha256",
            )
            if len(digest) != 64 or any(character not in "0123456789abcdef" for character in digest):
                raise SessionReplayError(
                    f"calibration identity.{list_name}[{position}].sha256 is invalid"
                )
    embedded_identity = hashlib.sha256(_canonical_json_bytes(identity_value)).hexdigest()
    if embedded_identity != artifact_identity:
        raise SessionReplayError(
            "calibration identity hash does not match the embedded identity"
        )
    return raw, hashlib.sha256(content).hexdigest()


# Verifies the artifact identity against a manifest and loads its calibrated matrices.
def load_calibration_artifact(
    path: Path,
    manifest: ExtractManifest,
) -> LoadedCalibration:
    """Load calibrated matrices without refitting any input image."""

    raw, _ = _load_verified_calibration_document(path)
    expected_identity, _ = _calibration_identity(manifest)
    artifact_identity = _require_string(
        raw.get("calibrationIdentitySha256"),
        "calibrationIdentitySha256",
    )
    if artifact_identity != expected_identity:
        raise SessionReplayError(
            "calibration artifact identity does not match the manifest inputs and gates"
        )
    rotation = _require_integer(raw.get("rotation"), "calibration.rotation")
    if rotation != manifest.rotation:
        raise SessionReplayError("calibration rotation does not match the extraction manifest")
    frame = raw.get("frame")
    if not isinstance(frame, dict) or set(frame) != {"width", "height"}:
        raise SessionReplayError("calibration.frame must contain exactly width and height")
    frame_size = (
        _require_integer(frame["width"], "calibration.frame.width", 1),
        _require_integer(frame["height"], "calibration.frame.height", 1),
    )
    intrinsic = raw.get("intrinsics")
    homography = raw.get("homography")
    if not isinstance(intrinsic, dict) or not isinstance(homography, dict):
        raise SessionReplayError("calibration intrinsics and homography must be objects")
    _require_exact_keys(intrinsic, INTRINSIC_ARTIFACT_KEYS, "calibration.intrinsics")
    _require_exact_keys(homography, HOMOGRAPHY_ARTIFACT_KEYS, "calibration.homography")
    camera_matrix = _numeric_vector(
        intrinsic.get("cameraMatrix"), 9, "intrinsics.cameraMatrix"
    ).reshape(3, 3)
    distortion = _numeric_vector(
        intrinsic.get("distortion"), 5, "intrinsics.distortion"
    ).reshape(1, 5)
    homography_matrix = _numeric_vector(
        homography.get("matrix"), 9, "homography.matrix"
    ).reshape(3, 3)
    _validate_homography_matrix(homography_matrix, "calibration homography")
    rms = _require_number(intrinsic.get("rmsPx"), "intrinsics.rmsPx", 0.0)
    max_median = _require_number(
        homography.get("maxPlacementMedianMm"),
        "homography.maxPlacementMedianMm",
        0.0,
    )
    if rms > manifest.calibration.max_intrinsic_rms_px:
        raise SessionReplayError("calibration artifact exceeds the manifest intrinsic RMS gate")
    if max_median > manifest.calibration.max_homography_error_mm:
        raise SessionReplayError("calibration artifact exceeds the manifest homography gate")
    return LoadedCalibration(
        rotation=rotation,
        frame_size=frame_size,
        intrinsics=IntrinsicsFit(
            camera_matrix=camera_matrix,
            distortion=distortion,
            rms_px=rms,
            rejected_views=_require_integer(
                intrinsic.get("rejectedViews"),
                "intrinsics.rejectedViews",
                0,
            ),
            retained_views=_require_integer(
                intrinsic.get("retainedViews"),
                "intrinsics.retainedViews",
                manifest.calibration.min_intrinsic_views,
            ),
        ),
        homography=HomographyFit(
            matrix=homography_matrix,
            max_placement_median_mm=max_median,
        ),
        identity_sha256=artifact_identity,
    )


# Validates standalone calibration fields needed by frame and render gates.
def _standalone_calibration_frame_from_document(
    raw: dict[str, Any],
    rotation: int,
) -> tuple[int, int]:
    identity = raw["identity"]
    gates = identity["calibration"]
    artifact_rotation = _require_integer(raw.get("rotation"), "calibration.rotation")
    if artifact_rotation not in (0, 90, 180, 270):
        raise SessionReplayError("calibration rotation is unsupported")
    if artifact_rotation != rotation:
        raise SessionReplayError(
            "requested frame-validation rotation does not match calibration"
        )
    frame = raw.get("frame")
    if not isinstance(frame, dict):
        raise SessionReplayError("calibration.frame must be an object")
    _require_exact_keys(frame, {"width", "height"}, "calibration.frame")
    frame_size = (
        _require_integer(frame["width"], "calibration.frame.width", 1),
        _require_integer(frame["height"], "calibration.frame.height", 1),
    )
    intrinsic = raw.get("intrinsics")
    homography = raw.get("homography")
    if not isinstance(intrinsic, dict) or not isinstance(homography, dict):
        raise SessionReplayError("calibration intrinsics and homography must be objects")
    _require_exact_keys(intrinsic, INTRINSIC_ARTIFACT_KEYS, "calibration.intrinsics")
    _require_exact_keys(homography, HOMOGRAPHY_ARTIFACT_KEYS, "calibration.homography")
    camera = _numeric_vector(
        intrinsic["cameraMatrix"], 9, "intrinsics.cameraMatrix"
    ).reshape(3, 3)
    _numeric_vector(intrinsic["distortion"], 5, "intrinsics.distortion")
    plane = _numeric_vector(homography["matrix"], 9, "homography.matrix").reshape(3, 3)
    if abs(float(np.linalg.det(camera))) <= 1.0e-12:
        raise SessionReplayError("calibration camera matrix is singular")
    _validate_homography_matrix(plane, "calibration homography")
    rms = _require_number(intrinsic["rmsPx"], "intrinsics.rmsPx", 0.0)
    _require_integer(intrinsic["rejectedViews"], "intrinsics.rejectedViews", 0)
    retained = _require_integer(intrinsic["retainedViews"], "intrinsics.retainedViews", 3)
    max_median = _require_number(
        homography["maxPlacementMedianMm"],
        "homography.maxPlacementMedianMm",
        0.0,
    )
    accepted = _require_integer(
        homography["acceptedPlacements"],
        "homography.acceptedPlacements",
        1,
    )
    coverage = _require_number(
        homography["coverageFraction"],
        "homography.coverageFraction",
        0.0,
    )
    if coverage > 1.0:
        raise SessionReplayError("homography.coverageFraction must not exceed 1.0")
    max_rms = _require_number(
        gates["maxIntrinsicRmsPx"],
        "calibration identity.calibration.maxIntrinsicRmsPx",
        1.0e-12,
    )
    min_views = _require_integer(
        gates["minIntrinsicViews"],
        "calibration identity.calibration.minIntrinsicViews",
        3,
    )
    max_homography_error = _require_number(
        gates["maxHomographyErrorMm"],
        "calibration identity.calibration.maxHomographyErrorMm",
        1.0e-12,
    )
    min_placements = _require_integer(
        gates["minHomographyPlacements"],
        "calibration identity.calibration.minHomographyPlacements",
        1,
    )
    max_placements = _require_integer(
        gates["maxHomographyPlacements"],
        "calibration identity.calibration.maxHomographyPlacements",
        min_placements,
    )
    target_coverage = _require_number(
        gates["coverageTargetFraction"],
        "calibration identity.calibration.coverageTargetFraction",
        0.0,
    )
    if target_coverage not in ALLOWED_COVERAGE_TARGET_FRACTIONS:
        raise SessionReplayError(
            "calibration identity coverage target must be exactly 0.5 or 0.7"
        )
    if rms > max_rms or retained < min_views:
        raise SessionReplayError("standalone calibration does not satisfy its intrinsic gates")
    if max_median > max_homography_error:
        raise SessionReplayError("standalone calibration does not satisfy its homography error gate")
    if not min_placements <= accepted <= max_placements:
        raise SessionReplayError("standalone calibration placement count is outside its gates")
    if coverage < target_coverage:
        raise SessionReplayError("standalone calibration does not satisfy its coverage gate")
    return frame_size


# Loads one integrity-checked calibration and returns its validated rotated frame.
def _standalone_calibration_frame(path: Path, rotation: int) -> tuple[int, int]:
    raw, _ = _load_verified_calibration_document(path)
    return _standalone_calibration_frame_from_document(raw, rotation)


# Decodes selected garments and refuses any after-rotation frame that differs from calibration.
def validate_frames(
    calibration_path: Path,
    rotation: int,
    images: Sequence[Path],
) -> list[dict[str, Any]]:
    """Run a read-only pre-upload frame gate and emit no files."""

    if not images:
        raise SessionReplayError("validate-frames requires at least one image")
    frame_size = _standalone_calibration_frame(calibration_path.resolve(), rotation)
    validated: list[dict[str, Any]] = []
    for raw_path in images:
        path = raw_path.resolve()
        try:
            image = read_rotated_bgr(path, rotation)
        except MobileCvError as exc:
            raise SessionReplayError(str(exc)) from exc
        actual = image.shape[1], image.shape[0]
        if actual != frame_size:
            raise SessionReplayError(
                f"rotated garment frame {actual} does not match calibration {frame_size}: {path}"
            )
        validated.append(
            {
                "image": str(path),
                "width": actual[0],
                "height": actual[1],
            }
        )
    return validated


# Validates one normalized landmark coordinate against its pixel coordinate and frame.
def _validate_landmark_point(
    raw: Any,
    position: int,
    frame_size: tuple[int, int],
) -> LandmarkPoint:
    if not isinstance(raw, dict):
        raise SessionReplayError(f"landmarks[{position}] must be an object")
    index = _require_integer(raw.get("index"), f"landmarks[{position}].index", 0)
    if index != position or raw.get("label") != f"K{position + 1}":
        raise SessionReplayError(
            f"landmarks[{position}] must be index {position} with label K{position + 1}"
        )
    x = _require_number(raw.get("x"), f"landmarks[{position}].x")
    y = _require_number(raw.get("y"), f"landmarks[{position}].y")
    nx = _require_number(raw.get("normalized_x"), f"landmarks[{position}].normalized_x")
    ny = _require_number(raw.get("normalized_y"), f"landmarks[{position}].normalized_y")
    visibility = _require_number(
        raw.get("visibility"),
        f"landmarks[{position}].visibility",
    )
    width, height = frame_size
    if not (0.0 <= x <= width and 0.0 <= y <= height):
        raise SessionReplayError(f"landmarks[{position}] is outside the rotated frame")
    if not (0.0 <= nx <= 1.0 and 0.0 <= ny <= 1.0):
        raise SessionReplayError(f"landmarks[{position}] normalized values are outside [0,1]")
    if abs(x - nx * width) > 1.0 or abs(y - ny * height) > 1.0:
        raise SessionReplayError(
            f"landmarks[{position}] pixel and normalized coordinates disagree"
        )
    if not 0.0 <= visibility <= 1.0:
        raise SessionReplayError(f"landmarks[{position}].visibility is outside [0,1]")
    return LandmarkPoint(position, x, y, nx, ny, visibility)


# Loads only the qsee.landmarks.v1 artifact generated by the authoritative CV replay script.
def load_landmarks(
    path: Path,
    image_path: Path,
    frame_size: tuple[int, int],
    rotation: int,
) -> tuple[LandmarkPoint, ...]:
    """Validate model, image hash, frame, rotation, order, and all 17 points."""

    artifact = _read_json(path)
    if artifact.get("schema") != LANDMARK_ARTIFACT_SCHEMA:
        raise SessionReplayError(
            f"landmark artifact schema must be exactly {LANDMARK_ARTIFACT_SCHEMA!r}: {path}"
        )
    source = artifact.get("source")
    request = artifact.get("request")
    coordinate = artifact.get("coordinate_space")
    metadata = artifact.get("response_metadata")
    if not all(isinstance(value, dict) for value in (source, request, coordinate, metadata)):
        raise SessionReplayError(f"landmark artifact metadata is incomplete: {path}")
    if source.get("sha256") != _sha256_file(image_path):
        raise SessionReplayError(f"landmark artifact image hash does not match {image_path}")
    if request.get("rotation_clockwise_degrees") != rotation:
        raise SessionReplayError(f"landmark artifact rotation does not match manifest: {path}")
    if request.get("expected_model") != EXPECTED_MODEL or metadata.get("model") != EXPECTED_MODEL:
        raise SessionReplayError(f"landmark artifact model is not {EXPECTED_MODEL!r}: {path}")
    if (
        coordinate.get("width") != frame_size[0]
        or coordinate.get("height") != frame_size[1]
    ):
        raise SessionReplayError(f"landmark artifact frame does not match calibration: {path}")
    if coordinate.get("kind") != "image_after_clockwise_rotation":
        raise SessionReplayError(f"landmark artifact coordinate space is unsupported: {path}")
    raw_points = artifact.get("landmarks")
    if not isinstance(raw_points, list) or len(raw_points) != EXPECTED_LANDMARKS:
        raise SessionReplayError(
            f"landmark artifact must contain exactly {EXPECTED_LANDMARKS} points: {path}"
        )
    return tuple(
        _validate_landmark_point(raw, index, frame_size)
        for index, raw in enumerate(raw_points)
    )


# Formats one finite float exactly as the desktop Panko pipe writer does.
def _pipe_number(value: float) -> str:
    if not math.isfinite(value):
        raise SessionReplayError("cannot serialize a non-finite pipe coordinate")
    return f"{value:.10f}"


# Serializes one mobile-compatible Panko pipe after calibration, edge extraction, and undistortion.
def _build_pipe(
    item: ReplayItem,
    frame_size: tuple[int, int],
    homography: np.ndarray,
    bbox: tuple[int, int, int, int],
    raw_points: Sequence[LandmarkPoint],
    undistorted_points: np.ndarray,
    undistorted_edges: np.ndarray,
) -> str:
    if any(character.isspace() for character in item.image.name):
        raise SessionReplayError(
            f"pipe-compatible image names must not contain whitespace: {item.image.name}"
        )
    lines = [
        f"image {item.image.name}",
        f"frame {frame_size[0]} {frame_size[1]}",
        "H " + " ".join(_pipe_number(float(value)) for value in homography.reshape(-1)),
        f"bbox {bbox[0]} {bbox[1]} {bbox[2]} {bbox[3]}",
        f"kp_count {len(raw_points)}",
    ]
    for raw, calibrated in zip(raw_points, undistorted_points, strict=True):
        values = (
            float(calibrated[0]),
            float(calibrated[1]),
            raw.x,
            raw.y,
            raw.normalized_x,
            raw.normalized_y,
            raw.visibility,
        )
        lines.append("kp " + " ".join(_pipe_number(value) for value in values))
    lines.append(f"edge_count {undistorted_edges.shape[0]}")
    lines.extend(
        "e " + _pipe_number(float(point[0])) + " " + _pipe_number(float(point[1]))
        for point in undistorted_edges
    )
    return "\n".join(lines) + "\n"


# Parses one non-negative base-10 integer token from an exact pipe line.
def _parse_pipe_integer(token: str, field: str, minimum: int = 0) -> int:
    if re.fullmatch(r"0|[1-9][0-9]*", token) is None:
        raise SessionReplayError(f"{field} must be a canonical non-negative integer")
    value = int(token)
    if value < minimum:
        raise SessionReplayError(f"{field} must be at least {minimum}")
    return value


# Parses one finite fixed-decimal token emitted by the exact pipe writer.
def _parse_pipe_float(token: str, field: str) -> float:
    if re.fullmatch(r"-?[0-9]+\.[0-9]{10}", token) is None:
        raise SessionReplayError(f"{field} must use the exact ten-decimal pipe format")
    value = float(token)
    if not math.isfinite(value):
        raise SessionReplayError(f"{field} must be finite")
    return value


# Splits one pipe line using the writer's exact single-space delimiter.
def _pipe_tokens(line: str, field: str, expected_count: int) -> list[str]:
    tokens = line.split(" ")
    if len(tokens) != expected_count or any(token == "" for token in tokens):
        raise SessionReplayError(
            f"{field} must contain exactly {expected_count} single-space-delimited tokens"
        )
    return tokens


# Parses a replay pipe without accepting reordered, missing, aliased, or trailing records.
def parse_pipe(path: Path) -> ParsedPipe:
    """Bind exact pipe bytes to strict frame, homography, keypoint, and edge records."""

    resolved = path.resolve()
    try:
        content = resolved.read_bytes()
        text = content.decode("utf-8")
    except OSError as exc:
        raise SessionReplayError(f"cannot read pipe file {resolved}: {exc}") from exc
    except UnicodeDecodeError as exc:
        raise SessionReplayError(f"pipe file is not strict UTF-8: {resolved}") from exc
    if not content or b"\r" in content or not content.endswith(b"\n"):
        raise SessionReplayError("pipe must use LF line endings and end with exactly one newline")
    lines = text[:-1].split("\n")
    if not lines or any(line == "" for line in lines):
        raise SessionReplayError("pipe must not contain blank records")
    minimum_record_count = 5 + EXPECTED_LANDMARKS + 1 + 1
    if len(lines) < minimum_record_count:
        raise SessionReplayError(
            f"pipe must contain at least {minimum_record_count} exact records"
        )

    cursor = 0
    image_tokens = _pipe_tokens(lines[cursor], "pipe.image", 2)
    cursor += 1
    if image_tokens[0] != "image":
        raise SessionReplayError("pipe record 1 must be exactly image")
    image_name = _require_string(image_tokens[1], "pipe.image")
    if image_name in {".", ".."} or "/" in image_name or "\\" in image_name:
        raise SessionReplayError("pipe.image must be a basename")

    frame_tokens = _pipe_tokens(lines[cursor], "pipe.frame", 3)
    cursor += 1
    if frame_tokens[0] != "frame":
        raise SessionReplayError("pipe record 2 must be exactly frame")
    frame_size = (
        _parse_pipe_integer(frame_tokens[1], "pipe.frame.width", 1),
        _parse_pipe_integer(frame_tokens[2], "pipe.frame.height", 1),
    )

    homography_tokens = _pipe_tokens(lines[cursor], "pipe.H", 10)
    cursor += 1
    if homography_tokens[0] != "H":
        raise SessionReplayError("pipe record 3 must be exactly H")
    homography = np.array(
        [
            _parse_pipe_float(token, f"pipe.H[{index}]")
            for index, token in enumerate(homography_tokens[1:])
        ],
        dtype=np.float64,
    ).reshape(3, 3)
    _validate_homography_matrix(homography, "pipe homography")

    bbox_tokens = _pipe_tokens(lines[cursor], "pipe.bbox", 5)
    cursor += 1
    if bbox_tokens[0] != "bbox":
        raise SessionReplayError("pipe record 4 must be exactly bbox")
    bbox = tuple(
        _parse_pipe_integer(token, f"pipe.bbox[{index}]", 1 if index >= 2 else 0)
        for index, token in enumerate(bbox_tokens[1:])
    )
    if bbox[0] + bbox[2] > frame_size[0] or bbox[1] + bbox[3] > frame_size[1]:
        raise SessionReplayError("pipe.bbox extends outside pipe.frame")

    count_tokens = _pipe_tokens(lines[cursor], "pipe.kp_count", 2)
    cursor += 1
    if count_tokens[0] != "kp_count":
        raise SessionReplayError("pipe record 5 must be exactly kp_count")
    keypoint_count = _parse_pipe_integer(count_tokens[1], "pipe.kp_count", 1)
    if keypoint_count != EXPECTED_LANDMARKS:
        raise SessionReplayError(
            f"pipe.kp_count must be exactly {EXPECTED_LANDMARKS}"
        )
    if len(lines) < cursor + keypoint_count + 1:
        raise SessionReplayError("pipe ends before all keypoint records")
    keypoints: list[PipeKeypoint] = []
    for position in range(keypoint_count):
        tokens = _pipe_tokens(lines[cursor], f"pipe.kp[{position}]", 8)
        cursor += 1
        if tokens[0] != "kp":
            raise SessionReplayError(f"pipe keypoint record {position} must be exactly kp")
        values = [
            _parse_pipe_float(token, f"pipe.kp[{position}][{index}]")
            for index, token in enumerate(tokens[1:])
        ]
        keypoint = PipeKeypoint(*values)
        if not (
            0.0 <= keypoint.raw_x <= frame_size[0]
            and 0.0 <= keypoint.raw_y <= frame_size[1]
            and 0.0 <= keypoint.undistorted_x <= frame_size[0]
            and 0.0 <= keypoint.undistorted_y <= frame_size[1]
        ):
            raise SessionReplayError(f"pipe.kp[{position}] coordinate is outside frame")
        if not (
            0.0 <= keypoint.normalized_x <= 1.0
            and 0.0 <= keypoint.normalized_y <= 1.0
            and 0.0 <= keypoint.visibility <= 1.0
        ):
            raise SessionReplayError(
                f"pipe.kp[{position}] normalized coordinate or visibility is outside [0,1]"
            )
        keypoints.append(keypoint)

    edge_count_tokens = _pipe_tokens(lines[cursor], "pipe.edge_count", 2)
    cursor += 1
    if edge_count_tokens[0] != "edge_count":
        raise SessionReplayError("pipe record after keypoints must be exactly edge_count")
    edge_count = _parse_pipe_integer(edge_count_tokens[1], "pipe.edge_count", 1)
    if len(lines) != cursor + edge_count:
        raise SessionReplayError("pipe edge_count does not match the exact remaining record count")
    edges: list[tuple[float, float]] = []
    for position in range(edge_count):
        tokens = _pipe_tokens(lines[cursor], f"pipe.e[{position}]", 3)
        cursor += 1
        if tokens[0] != "e":
            raise SessionReplayError(f"pipe edge record {position} must be exactly e")
        edge = (
            _parse_pipe_float(tokens[1], f"pipe.e[{position}].x"),
            _parse_pipe_float(tokens[2], f"pipe.e[{position}].y"),
        )
        if not (0.0 <= edge[0] <= frame_size[0] and 0.0 <= edge[1] <= frame_size[1]):
            raise SessionReplayError(f"pipe.e[{position}] is outside frame")
        edges.append(edge)
    mapping_points = [
        (point.undistorted_x, point.undistorted_y) for point in keypoints
    ]
    mapping_points.extend(edges)
    _validate_homography_points(homography, mapping_points, "pipe calibrated point")
    return ParsedPipe(
        path=resolved,
        sha256=hashlib.sha256(content).hexdigest(),
        image_name=image_name,
        frame_size=frame_size,
        homography=homography,
        bbox=(bbox[0], bbox[1], bbox[2], bbox[3]),
        keypoints=tuple(keypoints),
        edges=np.asarray(edges, dtype=np.float64),
    )


# Runs exact edge and undistortion stages for one already-remote-inferred garment.
def _extract_item(
    manifest: ExtractManifest,
    calibration: LoadedCalibration,
    item: ReplayItem,
) -> ExtractedItem:
    try:
        image = read_rotated_bgr(item.image, manifest.rotation)
    except MobileCvError as exc:
        raise SessionReplayError(f"item {item.item_id}: {exc}") from exc
    current_size = image.shape[1], image.shape[0]
    if current_size != calibration.frame_size:
        raise SessionReplayError(
            f"item {item.item_id}: rotated frame {current_size} does not match "
            f"calibration {calibration.frame_size}"
        )
    points = load_landmarks(
        item.landmarks,
        item.image,
        calibration.frame_size,
        manifest.rotation,
    )
    try:
        bbox = landmark_bounding_box(points, calibration.frame_size)
        edge_result = detect_garment_edges(image, bbox, EdgeParams())
        raw_edges = edge_points_full_frame(edge_result)
        raw_keypoints = np.array([(point.x, point.y) for point in points], dtype=np.float32)
        undistorted_points = undistort_to_pixel_frame(
            raw_keypoints,
            calibration.intrinsics.camera_matrix,
            calibration.intrinsics.distortion,
        )
        undistorted_edges = undistort_to_pixel_frame(
            raw_edges,
            calibration.intrinsics.camera_matrix,
            calibration.intrinsics.distortion,
        )
    except MobileCvError as exc:
        raise SessionReplayError(f"item {item.item_id}: {exc}") from exc
    if undistorted_edges.shape[0] == 0:
        raise SessionReplayError(f"item {item.item_id}: calibrated garment edge is empty")
    pipe_text = _build_pipe(
        item,
        calibration.frame_size,
        calibration.homography.matrix,
        bbox,
        points,
        undistorted_points,
        undistorted_edges,
    )
    return ExtractedItem(item, pipe_text, int(raw_edges.shape[0]), bbox)


# Commits all pipes only after every item has passed, so a failed batch emits no new partial set.
def extract_manifest_items(
    manifest: ExtractManifest,
    calibration_path: Path,
    overwrite: bool,
) -> list[ExtractedItem]:
    """Extract all items without fitting calibration or invoking a remote model."""

    if not manifest.items:
        raise SessionReplayError("extract requires at least one manifest item")
    if not overwrite:
        existing = [item.pipe for item in manifest.items if item.pipe.exists()]
        if existing:
            raise SessionReplayError(f"pipe output already exists: {existing[0]}")
    calibration = load_calibration_artifact(calibration_path, manifest)
    extracted = [_extract_item(manifest, calibration, item) for item in manifest.items]

    temporary_files: list[tuple[Path, Path]] = []
    try:
        for result in extracted:
            destination = result.item.pipe
            destination.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(
                mode="w",
                encoding="utf-8",
                newline="\n",
                dir=destination.parent,
                prefix=f".{destination.name}.",
                suffix=".tmp",
                delete=False,
            ) as stream:
                stream.write(result.pipe_text)
                stream.flush()
                os.fsync(stream.fileno())
                temporary_files.append((Path(stream.name), destination))
        for temporary, destination in temporary_files:
            os.replace(temporary, destination)
    except OSError as exc:
        for temporary, _ in temporary_files:
            temporary.unlink(missing_ok=True)
        raise SessionReplayError(f"cannot commit pipe outputs: {exc}") from exc
    return extracted


# Requires exactly the declared required keys plus the one documented optional analysis field.
def _require_analysis_root_keys(value: dict[str, Any]) -> None:
    actual = set(value)
    missing = ANALYSIS_REQUIRED_KEYS - actual
    unknown = actual - ANALYSIS_REQUIRED_KEYS - ANALYSIS_OPTIONAL_KEYS
    if missing or unknown:
        details: list[str] = []
        if missing:
            details.append(f"missing={sorted(missing)}")
        if unknown:
            details.append(f"unknown={sorted(unknown)}")
        raise SessionReplayError(
            "analysis artifact has the wrong fields (" + ", ".join(details) + ")"
        )


# Reads one exact x/y object and rejects non-finite or unknown coordinate fields.
def _analysis_xy(value: Any, field: str) -> tuple[float, float]:
    if not isinstance(value, dict):
        raise SessionReplayError(f"{field} must be an object")
    _require_exact_keys(value, ANALYSIS_COORDINATE_KEYS, field)
    return (
        _require_number(value["x"], f"{field}.x"),
        _require_number(value["y"], f"{field}.y"),
    )


# Validates Java keypoints against both the exact pipe and remote landmark evidence.
def _validate_analysis_keypoints(
    value: Any,
    landmarks: Sequence[LandmarkPoint],
    pipe_keypoints: Sequence[PipeKeypoint],
) -> None:
    if not isinstance(value, list) or len(value) != EXPECTED_LANDMARKS:
        raise SessionReplayError(
            f"analysis.keypoints must contain exactly {EXPECTED_LANDMARKS} entries"
        )
    if len(landmarks) != EXPECTED_LANDMARKS or len(pipe_keypoints) != EXPECTED_LANDMARKS:
        raise SessionReplayError("keypoint evidence must contain exactly 17 ordered entries")
    for position, raw in enumerate(value):
        if not isinstance(raw, dict):
            raise SessionReplayError(f"analysis.keypoints[{position}] must be an object")
        _require_exact_keys(raw, ANALYSIS_KEYPOINT_KEYS, f"analysis.keypoints[{position}]")
        index = _require_integer(
            raw["index"],
            f"analysis.keypoints[{position}].index",
            1,
        )
        if index != position + 1:
            raise SessionReplayError(
                f"analysis.keypoints[{position}].index must be {position + 1}"
            )
        raw_xy = _analysis_xy(raw["raw"], f"analysis.keypoints[{position}].raw")
        undistorted = _analysis_xy(
            raw["undistorted"],
            f"analysis.keypoints[{position}].undistorted",
        )
        normalized = _analysis_xy(
            raw["normalized"],
            f"analysis.keypoints[{position}].normalized",
        )
        visibility = _require_number(
            raw["visibility"],
            f"analysis.keypoints[{position}].visibility",
        )
        expected = landmarks[position]
        pipe_point = pipe_keypoints[position]
        pipe_landmark_pairs = (
            (pipe_point.raw_x, expected.x),
            (pipe_point.raw_y, expected.y),
            (pipe_point.normalized_x, expected.normalized_x),
            (pipe_point.normalized_y, expected.normalized_y),
            (pipe_point.visibility, expected.visibility),
        )
        if any(
            abs(actual - landmark) > PIPE_COORDINATE_TOLERANCE
            for actual, landmark in pipe_landmark_pairs
        ):
            raise SessionReplayError(
                f"pipe keypoint {position} does not match the landmark artifact"
            )
        analysis_pipe_pairs = (
            (raw_xy[0], pipe_point.raw_x),
            (raw_xy[1], pipe_point.raw_y),
            (undistorted[0], pipe_point.undistorted_x),
            (undistorted[1], pipe_point.undistorted_y),
            (normalized[0], pipe_point.normalized_x),
            (normalized[1], pipe_point.normalized_y),
            (visibility, pipe_point.visibility),
        )
        if any(
            abs(actual - pipe_value) > PIPE_COORDINATE_TOLERANCE
            for actual, pipe_value in analysis_pipe_pairs
        ):
            raise SessionReplayError(
                f"analysis.keypoints[{position}] does not match the exact pipe row"
            )


# Parses the exact client measurement order and enforces unit, range, and verdict consistency.
def _validate_analysis_measurements(
    value: Any,
    expected_ids: Sequence[str],
) -> list[dict[str, Any]]:
    if not isinstance(value, list) or len(value) != len(expected_ids):
        raise SessionReplayError(
            f"analysis.measurements must contain exactly {len(expected_ids)} ordered entries"
        )
    rows: list[dict[str, Any]] = []
    seen: set[str] = set()
    for position, raw in enumerate(value):
        if not isinstance(raw, dict):
            raise SessionReplayError(f"analysis.measurements[{position}] must be an object")
        _require_exact_keys(
            raw,
            ANALYSIS_MEASUREMENT_KEYS,
            f"analysis.measurements[{position}]",
        )
        measure_id = _require_string(raw["id"], f"analysis.measurements[{position}].id")
        if measure_id != expected_ids[position]:
            raise SessionReplayError(
                f"analysis.measurements[{position}].id must be exactly "
                f"{expected_ids[position]!r}"
            )
        if measure_id in seen:
            raise SessionReplayError(f"duplicate analysis measurement id: {measure_id}")
        seen.add(measure_id)
        numeric_names = (
            "mm",
            "cm",
            "targetMm",
            "targetCm",
            "minimumAllowedMm",
            "minimumAllowedCm",
            "maximumAllowedMm",
            "maximumAllowedCm",
            "gapMm",
            "gapCm",
        )
        numbers = {
            name: _require_number(
                raw[name],
                f"analysis.measurements[{position}].{name}",
            )
            for name in numeric_names
        }
        for mm_name, cm_name in (
            ("mm", "cm"),
            ("targetMm", "targetCm"),
            ("minimumAllowedMm", "minimumAllowedCm"),
            ("maximumAllowedMm", "maximumAllowedCm"),
            ("gapMm", "gapCm"),
        ):
            if abs(numbers[mm_name] / 10.0 - numbers[cm_name]) > 0.001:
                raise SessionReplayError(
                    f"analysis.measurements[{position}] has inconsistent {mm_name}/{cm_name} units"
                )
        if numbers["minimumAllowedMm"] > numbers["maximumAllowedMm"]:
            raise SessionReplayError(
                f"analysis.measurements[{position}] has an inverted allowed range"
            )
        if abs((numbers["mm"] - numbers["targetMm"]) - numbers["gapMm"]) > 1.0e-6:
            raise SessionReplayError(
                f"analysis.measurements[{position}] gapMm contradicts value minus target"
            )
        verified = raw["verified"]
        if not isinstance(verified, bool):
            raise SessionReplayError(
                f"analysis.measurements[{position}].verified must be boolean"
            )
        verdict = raw["verdict"]
        expected_verdict = "PASS" if verified else "FAIL"
        if verdict != expected_verdict:
            raise SessionReplayError(
                f"analysis.measurements[{position}] verdict contradicts verified"
            )
        inside_range = (
            numbers["minimumAllowedMm"] - 1.0e-6
            <= numbers["mm"]
            <= numbers["maximumAllowedMm"] + 1.0e-6
        )
        if verified != inside_range:
            raise SessionReplayError(
                f"analysis.measurements[{position}] verified contradicts its allowed range"
            )
        rows.append(
            {
                "name": measure_id,
                "value": numbers["cm"],
                "target": numbers["targetCm"],
                "minimum": numbers["minimumAllowedCm"],
                "maximum": numbers["maximumAllowedCm"],
                "verdict": verdict,
            }
        )
    return rows


# Validates one exact unversioned historical result against the selected client and size.
def validate_historical_artifact(
    historical: dict[str, Any],
    expected_client: str,
    expected_size: str,
    path: Path | None = None,
    sha256: str | None = None,
) -> ValidatedHistorical:
    """Reject aliases, schema drift across UTC minutes, duplicates, and non-finite values."""

    if expected_client not in MEASUREMENT_IDS_BY_CLIENT:
        raise SessionReplayError("expected historical client must be exactly Kiabi or Panko")
    if (path is None) != (sha256 is None):
        raise SessionReplayError("historical path and exact-byte SHA-256 must be supplied together")
    bound_sha256 = (
        _require_lowercase_sha256(sha256, "historical SHA-256")
        if sha256 is not None
        else None
    )
    expected_size_value = _require_string(expected_size, "expected historical size")
    _require_exact_keys(historical, HISTORICAL_ROOT_KEYS, "historical result")
    product_code = _require_string(historical["product_code"], "historical.product_code")
    product_brand = _require_string(historical["product_brand"], "historical.product_brand")
    timestamp_utc, _ = _parse_utc_timestamp(
        historical["timestamp"],
        "historical.timestamp",
    )
    if path is not None:
        _, filename_second, _ = _parse_result_filename_timestamp(
            path.name,
            "historical filename",
        )
        if _utc_timestamp_minute(timestamp_utc) != _utc_timestamp_minute(filename_second):
            raise SessionReplayError(
                "historical payload timestamp is outside the UTC minute encoded in its filename"
            )
    client = _require_string(historical["client"], "historical.client")
    size = _require_string(historical["size"], "historical.size")
    if client != expected_client:
        raise SessionReplayError("historical.client does not match the selected client")
    if size != expected_size_value:
        raise SessionReplayError("historical.size does not match the selected size")
    raw_measures = historical["measures"]
    if not isinstance(raw_measures, list) or not raw_measures:
        raise SessionReplayError("historical.measures must be a non-empty array")
    measures: list[HistoricalMeasure] = []
    seen: set[str] = set()
    canonical_ids = MEASUREMENT_IDS_BY_CLIENT[client]
    canonical_positions = {
        measure_id: position for position, measure_id in enumerate(canonical_ids)
    }
    previous_position = -1
    for position, raw in enumerate(raw_measures):
        if not isinstance(raw, dict):
            raise SessionReplayError(f"historical.measures[{position}] must be an object")
        _require_exact_keys(
            raw,
            HISTORICAL_MEASURE_KEYS,
            f"historical.measures[{position}]",
        )
        name = _require_string(
            raw["measure_name"],
            f"historical.measures[{position}].measure_name",
        )
        if name in seen:
            raise SessionReplayError(f"duplicate historical measurement: {name}")
        seen.add(name)
        canonical_position = canonical_positions.get(name)
        if canonical_position is None:
            raise SessionReplayError(
                f"historical measurement {name!r} is not valid for client {client}"
            )
        if canonical_position <= previous_position:
            raise SessionReplayError(
                "historical measurements must follow the exact canonical client order"
            )
        previous_position = canonical_position
        measures.append(
            HistoricalMeasure(
                name=name,
                value=_require_number(
                    raw["measure_value"],
                    f"historical.measures[{position}].measure_value",
                ),
            )
        )
    return ValidatedHistorical(
        path=path.resolve() if path is not None else None,
        sha256=bound_sha256,
        product_code=product_code,
        product_brand=product_brand,
        timestamp_utc=timestamp_utc,
        client=client,
        size=size,
        measures=tuple(measures),
    )


# Loads one historical result and applies the same strict contract used by rendering.
def load_historical_artifact(
    path: Path,
    expected_client: str,
    expected_size: str,
) -> ValidatedHistorical:
    resolved = path.resolve()
    raw, content = _read_json_with_bytes(resolved)
    return validate_historical_artifact(
        raw,
        expected_client,
        expected_size,
        resolved,
        hashlib.sha256(content).hexdigest(),
    )


# Validates a non-empty ordered historical file list without emitting any artifact.
def validate_historical_files(
    paths: Sequence[Path],
    expected_client: str,
    expected_size: str,
) -> list[ValidatedHistorical]:
    if not paths:
        raise SessionReplayError("validate-historical requires at least one JSON file")
    resolved = tuple(path.resolve() for path in paths)
    if len(set(resolved)) != len(resolved):
        raise SessionReplayError("validate-historical paths must be unique")
    return [
        load_historical_artifact(path, expected_client, expected_size)
        for path in resolved
    ]


# Binds optional historical S3 result values by measurement name without deriving a verdict.
def _merge_historical_rows(
    rows: list[dict[str, Any]],
    historical: ValidatedHistorical | None,
) -> None:
    if historical is None:
        return
    by_name = {row["name"]: row for row in rows}
    for measure in historical.measures:
        if measure.name in by_name:
            by_name[measure.name]["historical"] = measure.value


# Requires one lowercase SHA-256 string produced from exact file bytes.
def _require_lowercase_sha256(value: Any, field: str) -> str:
    digest = _require_string(value, field)
    if LOWERCASE_SHA256_PATTERN.fullmatch(digest) is None:
        raise SessionReplayError(f"{field} must be a lowercase 64-character SHA-256")
    return digest


# Validates the full analysis document against every selected render input.
def validate_analysis_artifact(
    analysis: dict[str, Any],
    *,
    expected_client: str,
    expected_size: str,
    image_path: Path,
    frame_size: tuple[int, int],
    pipe: ParsedPipe,
    calibration_sha256: str,
    landmarks: Sequence[LandmarkPoint],
    historical: ValidatedHistorical | None,
) -> tuple[list[dict[str, Any]], str]:
    """Fail closed on schema, source, geometry, or PASS/FAIL contradictions."""

    _require_analysis_root_keys(analysis)
    if analysis["schema"] != ANALYSIS_SCHEMA:
        raise SessionReplayError(f"analysis schema must be exactly {ANALYSIS_SCHEMA!r}")
    if expected_client not in MEASUREMENT_IDS_BY_CLIENT:
        raise SessionReplayError("expected client must be exactly Kiabi or Panko")
    expected_size_value = _require_string(expected_size, "expected size")
    if _require_string(analysis["client"], "analysis.client") != expected_client:
        raise SessionReplayError("analysis.client does not match the expected client")
    if _require_string(analysis["size"], "analysis.size") != expected_size_value:
        raise SessionReplayError("analysis.size does not match the expected size")
    image = image_path.resolve()
    if pipe.image_name != image.name:
        raise SessionReplayError("pipe.image does not match the rendered image basename")
    source = analysis["source"]
    if not isinstance(source, dict):
        raise SessionReplayError("analysis.source must be an object")
    _require_exact_keys(source, ANALYSIS_SOURCE_KEYS, "analysis.source")
    pipe_reference = _require_string(source["pipe"], "analysis.source.pipe")
    source_pipe = Path(pipe_reference)
    if not source_pipe.is_absolute() or source_pipe.resolve() != pipe.path:
        raise SessionReplayError("analysis.source.pipe does not match the selected pipe")
    if _require_string(source["image"], "analysis.source.image") != image.name:
        raise SessionReplayError("analysis.source.image does not match the rendered image")
    pipe_sha256 = _require_lowercase_sha256(
        source["pipeSha256"],
        "analysis.source.pipeSha256",
    )
    if pipe_sha256 != pipe.sha256:
        raise SessionReplayError("analysis.source.pipeSha256 does not match exact pipe bytes")
    expected_calibration_sha256 = _require_lowercase_sha256(
        calibration_sha256,
        "selected calibration SHA-256",
    )
    source_calibration_sha256 = _require_lowercase_sha256(
        source["calibrationSha256"],
        "analysis.source.calibrationSha256",
    )
    if source_calibration_sha256 != expected_calibration_sha256:
        raise SessionReplayError(
            "analysis.source.calibrationSha256 does not match exact calibration bytes"
        )
    frame = analysis["frame"]
    if not isinstance(frame, dict):
        raise SessionReplayError("analysis.frame must be an object")
    _require_exact_keys(frame, {"width", "height"}, "analysis.frame")
    analysis_frame = (
        _require_integer(frame["width"], "analysis.frame.width", 1),
        _require_integer(frame["height"], "analysis.frame.height", 1),
    )
    if analysis_frame != frame_size or pipe.frame_size != frame_size:
        raise SessionReplayError(
            "analysis.frame, pipe.frame, and the rotated image must match exactly"
        )
    _validate_analysis_keypoints(analysis["keypoints"], landmarks, pipe.keypoints)
    rows = _validate_analysis_measurements(
        analysis["measurements"],
        MEASUREMENT_IDS_BY_CLIENT[expected_client],
    )
    if analysis["isMeasured"] is not True:
        raise SessionReplayError("analysis.isMeasured must be true")
    is_verified = analysis["isVerified"]
    if not isinstance(is_verified, bool):
        raise SessionReplayError("analysis.isVerified must be boolean")
    all_verified = all(row["verdict"] == "PASS" for row in rows)
    if is_verified != all_verified:
        raise SessionReplayError(
            "analysis.isVerified contradicts the per-measurement verdicts"
        )
    overall = analysis["overallVerdict"]
    expected_overall = "PASS" if is_verified else "FAIL"
    if overall != expected_overall:
        raise SessionReplayError("analysis.overallVerdict contradicts analysis.isVerified")
    endpoints = analysis["endpoints"]
    if not isinstance(endpoints, dict):
        raise SessionReplayError("analysis.endpoints must be an object")
    for endpoint_id, endpoint in endpoints.items():
        _require_string(endpoint_id, "analysis endpoint id")
        if not isinstance(endpoint, dict):
            raise SessionReplayError(f"analysis.endpoints.{endpoint_id} must be an object")
        _require_exact_keys(
            endpoint,
            {"normalized", "pixel"},
            f"analysis.endpoints.{endpoint_id}",
        )
        normalized = _analysis_xy(
            endpoint["normalized"],
            f"analysis.endpoints.{endpoint_id}.normalized",
        )
        pixel = _analysis_xy(
            endpoint["pixel"],
            f"analysis.endpoints.{endpoint_id}.pixel",
        )
        if not (0.0 <= normalized[0] <= 1.0 and 0.0 <= normalized[1] <= 1.0):
            raise SessionReplayError(
                f"analysis.endpoints.{endpoint_id}.normalized is outside [0,1]"
            )
        expected_pixel = (
            normalized[0] * frame_size[0],
            normalized[1] * frame_size[1],
        )
        if (
            abs(pixel[0] - expected_pixel[0]) > ENDPOINT_COORDINATE_TOLERANCE
            or abs(pixel[1] - expected_pixel[1]) > ENDPOINT_COORDINATE_TOLERANCE
        ):
            raise SessionReplayError(
                f"analysis.endpoints.{endpoint_id}.pixel does not equal normalized*frame"
            )
    if historical is not None and (
        historical.client != expected_client or historical.size != expected_size_value
    ):
        raise SessionReplayError("historical evidence is not bound to the expected client and size")
    _merge_historical_rows(rows, historical)
    return rows, overall


# Verifies calibration bytes, gates, frame, and homography before rendering analysis evidence.
def _validate_render_calibration(
    calibration_path: Path,
    rotation: int,
    frame_size: tuple[int, int],
    pipe: ParsedPipe,
) -> tuple[str, float]:
    raw, file_sha256 = _load_verified_calibration_document(calibration_path.resolve())
    calibration_frame = _standalone_calibration_frame_from_document(raw, rotation)
    if calibration_frame != frame_size or pipe.frame_size != frame_size:
        raise SessionReplayError(
            "calibration.frame, pipe.frame, and the rotated image must match exactly"
        )
    homography = raw["homography"]
    if not isinstance(homography, dict):
        raise SessionReplayError("calibration.homography must be an object")
    matrix = _numeric_vector(
        homography["matrix"],
        9,
        "calibration.homography.matrix",
    ).reshape(3, 3)
    if not np.allclose(
        pipe.homography,
        matrix,
        rtol=0.0,
        atol=PIPE_HOMOGRAPHY_TOLERANCE,
    ):
        raise SessionReplayError("pipe homography does not match the calibration artifact")
    coverage_target = _require_number(
        raw["identity"]["calibration"]["coverageTargetFraction"],
        "calibration.identity.calibration.coverageTargetFraction",
    )
    if coverage_target not in ALLOWED_COVERAGE_TARGET_FRACTIONS:
        raise SessionReplayError("render calibration coverage target must be 0.5 or 0.7")
    return file_sha256, coverage_target


# Parses one strict ISO-8601 UTC timestamp with up to nanosecond source precision.
def _parse_utc_timestamp(value: Any, field: str) -> tuple[str, float]:
    raw = _require_string(value, field)
    match = UTC_TIMESTAMP_PATTERN.fullmatch(raw)
    if match is None:
        raise SessionReplayError(
            f"{field} must be an ISO-8601 UTC timestamp ending in Z"
        )
    try:
        base = dt.datetime.strptime(match.group("base"), "%Y-%m-%dT%H:%M:%S").replace(
            tzinfo=dt.timezone.utc
        )
    except ValueError as exc:
        raise SessionReplayError(f"{field} is not a valid UTC date/time") from exc
    fraction_text = match.group("fraction") or ""
    fraction = int(fraction_text) / (10 ** len(fraction_text)) if fraction_text else 0.0
    return raw, base.timestamp() + fraction


# Returns the whole-second UTC identity from an already validated UTC-Z timestamp.
def _utc_timestamp_second(value: str) -> str:
    match = UTC_TIMESTAMP_PATTERN.fullmatch(value)
    if match is None:
        raise SessionReplayError("internal UTC timestamp validation was bypassed")
    return match.group("base") + "Z"


# Returns the UTC-minute identity from an already validated UTC-Z timestamp.
def _utc_timestamp_minute(value: str) -> str:
    match = UTC_TIMESTAMP_PATTERN.fullmatch(value)
    if match is None:
        raise SessionReplayError("internal UTC timestamp validation was bypassed")
    return match.group("base")[:16] + "Z"


# Reports whether an already validated timestamp is exactly its whole second.
def _utc_timestamp_has_zero_fraction(value: str) -> bool:
    match = UTC_TIMESTAMP_PATTERN.fullmatch(value)
    if match is None:
        raise SessionReplayError("internal UTC timestamp validation was bypassed")
    fraction = match.group("fraction")
    return fraction is None or int(fraction) == 0


# Parses the exact timestamped result basename and returns its UTC-second identity.
def _parse_result_filename_timestamp(
    key: str,
    field: str,
) -> tuple[str, str, float]:
    basename = key.replace("\\", "/").rsplit("/", 1)[-1]
    match = RESULT_FILENAME_PATTERN.fullmatch(basename)
    if match is None:
        raise SessionReplayError(
            f"{field} basename must match YYYY-MM-DD-HH-MM-SS-<16 hex>.json"
        )
    try:
        timestamp = dt.datetime.strptime(
            match.group("timestamp"),
            "%Y-%m-%d-%H-%M-%S",
        ).replace(tzinfo=dt.timezone.utc)
    except ValueError as exc:
        raise SessionReplayError(f"{field} contains an invalid UTC second") from exc
    canonical = timestamp.strftime("%Y-%m-%dT%H:%M:%SZ")
    return basename, canonical, timestamp.timestamp()


# Validates one stored signed delta against image UTC minus its reference UTC.
def _validate_provenance_delta(
    value: Any,
    expected_seconds: float,
    field: str,
) -> float:
    actual = _require_number(value, field)
    if abs(actual - expected_seconds) > PROVENANCE_DELTA_TOLERANCE_SECONDS:
        raise SessionReplayError(
            f"{field}={actual:.6f} does not equal image-minus-reference "
            f"{expected_seconds:.6f} seconds"
        )
    return actual


# Formats a signed UTC delta as total minutes and seconds for the compact evidence box.
def _format_provenance_delta(seconds: float) -> str:
    sign = "+" if seconds >= 0.0 else "-"
    absolute = abs(seconds)
    minutes = int(absolute // 60.0)
    remainder = absolute - minutes * 60.0
    return f"{sign}{minutes:02d}m {remainder:05.2f}s"


# Validates the exact date/time-only association schema and returns compact display lines.
def validate_provenance_artifact(
    provenance: dict[str, Any],
    image_name: str,
) -> list[str]:
    """Prove every displayed delta while labelling all associations as inferred."""

    _require_exact_keys(provenance, PROVENANCE_ROOT_KEYS, "provenance")
    if provenance["schema"] != PROVENANCE_SCHEMA:
        raise SessionReplayError(
            f"provenance schema must be exactly {PROVENANCE_SCHEMA!r}"
        )
    if provenance["association"] != PROVENANCE_ASSOCIATION:
        raise SessionReplayError(
            f"provenance.association must be exactly {PROVENANCE_ASSOCIATION!r}"
        )
    image = provenance["image"]
    if not isinstance(image, dict):
        raise SessionReplayError("provenance.image must be an object")
    _require_exact_keys(image, PROVENANCE_IMAGE_KEYS, "provenance.image")
    image_key = _require_string(image["key"], "provenance.image.key")
    normalized_name = image_key.replace("\\", "/").rsplit("/", 1)[-1]
    if normalized_name != image_name:
        raise SessionReplayError(
            "provenance.image.key basename does not match the rendered image"
        )
    image_timestamp, image_epoch = _parse_utc_timestamp(
        image["timestampUtc"],
        "provenance.image.timestampUtc",
    )

    lines = [f"Image UTC: {image_timestamp}"]
    historical = provenance["historicalResult"]
    if historical is None:
        lines.append("Measures JSON: none selected")
    else:
        if not isinstance(historical, dict):
            raise SessionReplayError("provenance.historicalResult must be null or an object")
        _require_exact_keys(
            historical,
            PROVENANCE_HISTORICAL_KEYS,
            "provenance.historicalResult",
        )
        historical_key = _require_string(
            historical["key"],
            "provenance.historicalResult.key",
        )
        _, filename_timestamp, filename_epoch = _parse_result_filename_timestamp(
            historical_key,
            "provenance.historicalResult.key",
        )
        historical_timestamp, _ = _parse_utc_timestamp(
            historical["timestampUtc"],
            "provenance.historicalResult.timestampUtc",
        )
        if (
            _utc_timestamp_second(historical_timestamp) != filename_timestamp
            or not _utc_timestamp_has_zero_fraction(historical_timestamp)
        ):
            raise SessionReplayError(
                "provenance historical timestamp must equal the result filename UTC second "
                f"{filename_timestamp} (received {historical_timestamp})"
            )
        _require_lowercase_sha256(
            historical["sha256"],
            "provenance.historicalResult.sha256",
        )
        delta = _validate_provenance_delta(
            historical["deltaSeconds"],
            image_epoch - filename_epoch,
            "provenance.historicalResult.deltaSeconds",
        )
        lines.append(
            "Measures JSON: "
            f"{_format_provenance_delta(delta)} | {historical_key}"
        )

    for field_name, label in (
        ("lensCalibration", "Lens calibration"),
        ("homographyCalibration", "Table calibration"),
    ):
        calibration = provenance[field_name]
        if not isinstance(calibration, dict):
            raise SessionReplayError(f"provenance.{field_name} must be an object")
        _require_exact_keys(
            calibration,
            PROVENANCE_CALIBRATION_KEYS,
            f"provenance.{field_name}",
        )
        group_id = _require_string(
            calibration["groupId"],
            f"provenance.{field_name}.groupId",
        )
        _, reference_epoch = _parse_utc_timestamp(
            calibration["referenceTimestampUtc"],
            f"provenance.{field_name}.referenceTimestampUtc",
        )
        delta = _validate_provenance_delta(
            calibration["deltaSeconds"],
            image_epoch - reference_epoch,
            f"provenance.{field_name}.deltaSeconds",
        )
        lines.append(f"{label}: {_format_provenance_delta(delta)} | {group_id}")
    return lines


# Binds selected historical provenance to its exact local bytes and payload UTC minute.
def _validate_historical_provenance_binding(
    provenance: dict[str, Any],
    historical: ValidatedHistorical | None,
) -> None:
    provenance_historical = provenance["historicalResult"]
    if historical is None:
        if provenance_historical is not None:
            raise SessionReplayError(
                "provenance historicalResult must be null without --historical-json"
            )
        return
    if provenance_historical is None:
        raise SessionReplayError(
            "provenance historicalResult is required with --historical-json"
        )
    if not isinstance(provenance_historical, dict):
        raise SessionReplayError("provenance.historicalResult must be an object")
    if historical.path is None or historical.sha256 is None:
        raise SessionReplayError("historical evidence is not bound to exact local file bytes")
    historical_key = _require_string(
        provenance_historical["key"],
        "provenance.historicalResult.key",
    )
    result_basename, result_second, _ = _parse_result_filename_timestamp(
        historical_key,
        "provenance.historicalResult.key",
    )
    if historical.path.name != result_basename:
        raise SessionReplayError(
            "historical file basename does not match provenance historicalResult.key"
        )
    provenance_sha256 = _require_lowercase_sha256(
        provenance_historical["sha256"],
        "provenance.historicalResult.sha256",
    )
    if provenance_sha256 != historical.sha256:
        raise SessionReplayError(
            "provenance historicalResult SHA-256 does not match exact historical file bytes"
        )
    payload_timestamp, _ = _parse_utc_timestamp(
        historical.timestamp_utc,
        "historical.timestamp",
    )
    if _utc_timestamp_minute(payload_timestamp) != _utc_timestamp_minute(result_second):
        raise SessionReplayError(
            "historical payload timestamp is outside the UTC minute encoded in its filename"
        )


# Draws ordered K1..K17 landmarks and an appended PASS/FAIL evidence table.
def render_overlay(
    image_path: Path,
    landmark_path: Path,
    analysis_path: Path,
    *,
    pipe_path: Path,
    calibration_path: Path,
    output_path: Path,
    rotation: int,
    expected_client: str,
    expected_size: str,
    provenance_path: Path,
    historical_path: Path | None,
    overwrite: bool,
) -> None:
    """Create one review JPEG; rendering never changes measurement evidence."""

    image = read_rotated_bgr(image_path.resolve(), rotation)
    frame_size = image.shape[1], image.shape[0]
    pipe = parse_pipe(pipe_path)
    calibration_sha256, coverage_target = _validate_render_calibration(
        calibration_path,
        rotation,
        frame_size,
        pipe,
    )
    points = load_landmarks(landmark_path.resolve(), image_path.resolve(), frame_size, rotation)
    analysis = _read_json(analysis_path.resolve())
    provenance = _read_json(provenance_path.resolve())
    historical = (
        load_historical_artifact(historical_path, expected_client, expected_size)
        if historical_path is not None
        else None
    )
    rows, overall = validate_analysis_artifact(
        analysis,
        expected_client=expected_client,
        expected_size=expected_size,
        image_path=image_path,
        frame_size=frame_size,
        pipe=pipe,
        calibration_sha256=calibration_sha256,
        landmarks=points,
        historical=historical,
    )
    provenance_lines = validate_provenance_artifact(provenance, image_path.name)
    _validate_historical_provenance_binding(provenance, historical)

    scale = max(0.55, min(2.0, min(frame_size) / 1000.0))
    radius = max(4, round(min(frame_size) / 300.0))
    thickness = max(1, round(scale * 2))
    for point in points:
        x, y = int(round(point.x)), int(round(point.y))
        cv2.circle(image, (x, y), radius + 2, (255, 255, 255), -1, cv2.LINE_AA)
        cv2.circle(image, (x, y), radius, (0, 0, 255), -1, cv2.LINE_AA)
        label_position = (min(frame_size[0] - 1, x + radius * 2), max(20, y - radius * 2))
        cv2.putText(
            image,
            f"K{point.index + 1}",
            label_position,
            cv2.FONT_HERSHEY_SIMPLEX,
            scale,
            (0, 0, 0),
            thickness + 3,
            cv2.LINE_AA,
        )
        cv2.putText(
            image,
            f"K{point.index + 1}",
            label_position,
            cv2.FONT_HERSHEY_SIMPLEX,
            scale,
            (0, 0, 255),
            thickness,
            cv2.LINE_AA,
        )

    panel_width = max(700, min(1050, frame_size[0] // 3))
    canvas = np.full((frame_size[1], frame_size[0] + panel_width, 3), 245, dtype=np.uint8)
    canvas[:, : frame_size[0]] = image
    colour = (40, 150, 40) if overall == "PASS" else (40, 40, 210)
    x0 = frame_size[0] + 24
    cv2.putText(
        canvas,
        overall,
        (x0, 64),
        cv2.FONT_HERSHEY_SIMPLEX,
        1.2,
        colour,
        3,
        cv2.LINE_AA,
    )
    coverage_label = (
        "REPLAY TOOL CALIBRATION: 50% COVERAGE (MOBILE: 70%)"
        if coverage_target == RELAXED_REPLAY_COVERAGE_TARGET_FRACTION
        else "CURRENT MOBILE CALIBRATION: 70% COVERAGE"
    )
    coverage_colour = (
        (20, 110, 210)
        if coverage_target == RELAXED_REPLAY_COVERAGE_TARGET_FRACTION
        else (40, 120, 40)
    )
    cv2.putText(
        canvas,
        coverage_label,
        (x0, 100),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.48,
        coverage_colour,
        2,
        cv2.LINE_AA,
    )
    cv2.putText(
        canvas,
        "Measure  Replay  Target  Allowed range  Historic  Verdict",
        (x0, 136),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.56,
        (20, 20, 20),
        1,
        cv2.LINE_AA,
    )
    y = 174
    for row in rows:
        replay = f"{row['value']:.2f}" if isinstance(row.get("value"), (int, float)) else "-"
        historic = (
            f"{row['historical']:.2f}"
            if isinstance(row.get("historical"), (int, float))
            else "-"
        )
        verdict = str(row.get("verdict", "N/A"))
        target = f"{row['target']:.2f}"
        allowed = f"{row['minimum']:.2f}..{row['maximum']:.2f}"
        line = (
            f"{row['name']:<7} {replay:>7} {target:>7} "
            f"{allowed:>14} {historic:>9}  {verdict}"
        )
        cv2.putText(
            canvas,
            line,
            (x0, y),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            (20, 20, 20),
            1,
            cv2.LINE_AA,
        )
        y += 34
        if y >= frame_size[1] - 20:
            break
    provenance_y = min(frame_size[1] - 180, y + 24)
    if provenance_y < 12:
        raise SessionReplayError("the rotated frame is too short for the provenance box")
    box_bottom = min(frame_size[1] - 12, provenance_y + 154)
    cv2.rectangle(
        canvas,
        (x0 - 10, provenance_y - 24),
        (frame_size[0] + panel_width - 14, box_bottom),
        (225, 225, 225),
        -1,
    )
    cv2.putText(
        canvas,
        "INFERRED UTC ASSOCIATIONS",
        (x0, provenance_y),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.56,
        (40, 40, 190),
        2,
        cv2.LINE_AA,
    )
    line_y = provenance_y + 32
    for line in provenance_lines:
        compact = line if len(line) <= 82 else line[:79] + "..."
        cv2.putText(
            canvas,
            compact,
            (x0, line_y),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.43,
            (35, 35, 35),
            1,
            cv2.LINE_AA,
        )
        line_y += 27
    success, encoded = cv2.imencode(".jpg", canvas, [cv2.IMWRITE_JPEG_QUALITY, 94])
    if not success:
        raise SessionReplayError("OpenCV could not encode the overlay JPEG")
    _write_atomic(output_path.resolve(), encoded.tobytes(), overwrite)


# Registers common overwrite behavior on file-producing commands.
def _add_overwrite_argument(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Atomically replace an existing output",
    )


# Builds the strict two-phase extraction CLI plus the independent review renderer.
def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Run strict selected calibration and pipe extraction from explicit local evidence. "
            "This program never invokes a landmark endpoint."
        )
    )
    commands = parser.add_subparsers(dest="command", required=True)
    calibrate = commands.add_parser(
        "calibrate",
        help="Hard-gate calibration before any remote garment submission",
    )
    calibrate.add_argument("--manifest", required=True, type=Path)
    calibrate.add_argument("--output", required=True, type=Path)
    _add_overwrite_argument(calibrate)

    extract = commands.add_parser(
        "extract",
        help="Use an existing calibration and qsee.landmarks.v1 files to emit pipes",
    )
    extract.add_argument("--manifest", required=True, type=Path)
    extract.add_argument("--calibration", required=True, type=Path)
    _add_overwrite_argument(extract)

    validate = commands.add_parser(
        "validate-frames",
        help="Verify rotated garment dimensions before any remote request",
    )
    validate.add_argument("--calibration", required=True, type=Path)
    validate.add_argument(
        "--rotation",
        required=True,
        type=int,
        choices=(0, 90, 180, 270),
    )
    validate.add_argument(
        "--image",
        required=True,
        action="append",
        type=Path,
        help="Garment image to decode and validate; repeat for multiple images",
    )

    validate_historical = commands.add_parser(
        "validate-historical",
        help="Validate current unversioned historical JSON before replay",
    )
    validate_historical.add_argument(
        "--historical-json",
        required=True,
        action="append",
        type=Path,
        help="Historical measures JSON to validate; repeat for multiple files",
    )
    validate_historical.add_argument(
        "--client",
        required=True,
        choices=tuple(MEASUREMENT_IDS_BY_CLIENT),
    )
    validate_historical.add_argument("--size", required=True)

    render = commands.add_parser(
        "render",
        help="Draw K1..K17 and an evidence-only PASS/FAIL table",
    )
    render.add_argument("--image", required=True, type=Path)
    render.add_argument("--landmarks", required=True, type=Path)
    render.add_argument("--analysis", required=True, type=Path)
    render.add_argument("--pipe", required=True, type=Path)
    render.add_argument("--calibration", required=True, type=Path)
    render.add_argument(
        "--expected-client",
        required=True,
        choices=tuple(MEASUREMENT_IDS_BY_CLIENT),
    )
    render.add_argument("--expected-size", required=True)
    render.add_argument("--provenance", required=True, type=Path)
    render.add_argument("--output", required=True, type=Path)
    render.add_argument("--rotation", required=True, type=int, choices=(0, 90, 180, 270))
    render.add_argument("--historical-json", type=Path)
    _add_overwrite_argument(render)
    return parser


# Dispatches one CLI phase and prints machine-readable non-secret completion evidence.
def main(argv: Sequence[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    try:
        if args.command == "calibrate":
            manifest = load_manifest(args.manifest)
            artifact = write_calibration_artifact(
                manifest,
                args.output,
                args.overwrite,
            )
            print(
                json.dumps(
                    {
                        "status": "calibrated",
                        "output": str(args.output.resolve()),
                        "calibrationIdentitySha256": artifact["calibrationIdentitySha256"],
                        "intrinsicRmsPx": artifact["intrinsics"]["rmsPx"],
                        "homographyMaxPlacementMedianMm": artifact["homography"][
                            "maxPlacementMedianMm"
                        ],
                    },
                    sort_keys=True,
                )
            )
            return 0
        if args.command == "extract":
            manifest = load_manifest(args.manifest)
            results = extract_manifest_items(
                manifest,
                args.calibration,
                args.overwrite,
            )
            print(
                json.dumps(
                    {
                        "status": "extracted",
                        "items": [
                            {
                                "id": result.item.item_id,
                                "pipe": str(result.item.pipe),
                                "edgePixels": result.edge_pixels,
                            }
                            for result in results
                        ],
                    },
                    sort_keys=True,
                )
            )
            return 0
        if args.command == "validate-frames":
            validated = validate_frames(
                args.calibration,
                args.rotation,
                args.image,
            )
            print(
                json.dumps(
                    {"status": "frames-valid", "items": validated},
                    sort_keys=True,
                )
            )
            return 0
        if args.command == "validate-historical":
            historical_results = validate_historical_files(
                args.historical_json,
                args.client,
                args.size,
            )
            print(
                json.dumps(
                    {
                        "status": "historical-valid",
                        "items": [
                            {
                                "path": str(result.path),
                                "client": result.client,
                                "size": result.size,
                                "timestampUtc": result.timestamp_utc,
                                "measureCount": len(result.measures),
                                "sha256": result.sha256,
                            }
                            for result in historical_results
                        ],
                    },
                    sort_keys=True,
                )
            )
            return 0
        render_overlay(
            args.image,
            args.landmarks,
            args.analysis,
            pipe_path=args.pipe,
            calibration_path=args.calibration,
            output_path=args.output,
            rotation=args.rotation,
            expected_client=args.expected_client,
            expected_size=args.expected_size,
            provenance_path=args.provenance,
            historical_path=args.historical_json,
            overwrite=args.overwrite,
        )
        print(json.dumps({"status": "rendered", "output": str(args.output.resolve())}))
        return 0
    except (SessionReplayError, MobileCvError) as exc:
        print(f"session replay failed: {exc}", file=sys.stderr)
        return DETERMINISTIC_REJECTION_EXIT_CODE


if __name__ == "__main__":
    raise SystemExit(main())
