"""OpenCV stages shared with the QSee Android measurement pipeline.

This module is a Python transcription of ``calib_core.cpp`` and the shipped
``edge_core.cpp`` path used by ``PhotoAnalyser``.  It deliberately exposes no
uncalibrated or chord-based measurement path: callers either receive calibrated
points and a non-empty garment edge map or an exception.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

import cv2
import numpy as np


class MobileCvError(RuntimeError):
    """Raised when an input cannot satisfy the mobile CV contract."""


@dataclass(frozen=True)
class IntrinsicsFit:
    """A hard-gated intrinsic calibration in the rotated camera frame."""

    camera_matrix: np.ndarray
    distortion: np.ndarray
    rms_px: float
    rejected_views: int
    retained_views: int


@dataclass(frozen=True)
class HomographyFit:
    """A hard-gated image-to-table homography."""

    matrix: np.ndarray
    max_placement_median_mm: float


@dataclass(frozen=True)
class EdgeParams:
    """The values currently shipped in ``qsee_config.yaml``."""

    roi_pad_frac: float = 0.04
    working_max_side: float = 1024.0
    use_clahe: bool = False
    clahe_clip: float = 2.0
    clahe_grid: int = 8
    denoise_kernel: int = 3
    denoise_sigma: float = 0.8
    mode: str = "fixed"
    low_threshold: float = 25.0
    high_threshold: float = 75.0
    otsu_low_ratio: float = 0.5
    median_sigma: float = 0.33
    sobel_aperture: int = 3
    l2_gradient: bool = True
    colour_gradient: bool = True
    mask_non_garment: bool = True
    mask_morph_frac: float = 0.008
    mask_dilate_px: int = 6
    morph_close_kernel: int = 0
    min_component_px: int = 0
    thin: bool = True


@dataclass(frozen=True)
class EdgeResult:
    """A garment edge map stored only over the full-resolution working ROI."""

    roi: tuple[int, int, int, int]
    binary_roi: np.ndarray
    working_scale: float


# Rotates a decoded camera frame clockwise exactly as the Android capture path does.
def rotate_clockwise(image: np.ndarray, degrees: int) -> np.ndarray:
    """Return ``image`` in the endpoint/calibration coordinate frame."""

    normalized = degrees % 360
    if normalized == 0:
        return image.copy()
    if normalized == 90:
        return cv2.rotate(image, cv2.ROTATE_90_CLOCKWISE)
    if normalized == 180:
        return cv2.rotate(image, cv2.ROTATE_180)
    if normalized == 270:
        return cv2.rotate(image, cv2.ROTATE_90_COUNTERCLOCKWISE)
    raise MobileCvError("rotation must be 0, 90, 180, or 270 degrees")


# Reads and rotates one image while preserving the BGR channel contract used by cv::imread.
def read_rotated_bgr(path: Path, degrees: int) -> np.ndarray:
    """Decode a local image as BGR and rotate it into the mobile server frame."""

    image = cv2.imread(str(path), cv2.IMREAD_COLOR)
    if image is None or image.size == 0:
        raise MobileCvError(f"cannot decode image: {path}")
    return rotate_clockwise(image, degrees)


# Refines corners on the full-resolution grayscale frame.
def _refine_subpixel(gray: np.ndarray, corners: np.ndarray) -> np.ndarray:
    criteria = (
        cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_MAX_ITER,
        30,
        0.001,
    )
    return cv2.cornerSubPix(
        gray,
        corners.astype(np.float32),
        (11, 11),
        (-1, -1),
        criteria,
    )


# Finds a complete chessboard grid with the same SB-first/classic-second policy as calib_core.cpp.
def find_board_corners(
    gray: np.ndarray,
    pattern: tuple[int, int],
    allow_classic_fallback: bool = True,
) -> np.ndarray | None:
    """Return ``N x 2`` float32 full-resolution corners, or ``None``."""

    if gray is None or gray.size == 0 or gray.ndim != 2:
        raise MobileCvError("chessboard detection requires a non-empty grayscale image")
    if pattern[0] <= 0 or pattern[1] <= 0:
        raise MobileCvError("chessboard pattern dimensions must be positive")

    long_side = max(gray.shape[1], gray.shape[0])
    scale = 1280.0 / long_side if long_side > 1280 else 1.0
    find_image = (
        cv2.resize(gray, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        if scale < 1.0
        else gray
    )
    found, raw = cv2.findChessboardCornersSB(
        find_image,
        pattern,
        flags=cv2.CALIB_CB_NORMALIZE_IMAGE | cv2.CALIB_CB_ACCURACY,
    )
    if found:
        corners = raw.astype(np.float32)
        if scale < 1.0:
            corners *= np.float32(1.0 / scale)
            corners = _refine_subpixel(gray, corners)
        return corners.reshape(-1, 2)

    if not allow_classic_fallback:
        return None
    found, raw = cv2.findChessboardCorners(
        find_image,
        pattern,
        flags=(
            cv2.CALIB_CB_ADAPTIVE_THRESH
            | cv2.CALIB_CB_NORMALIZE_IMAGE
            | cv2.CALIB_CB_FILTER_QUADS
        ),
    )
    if not found:
        return None
    corners = raw.astype(np.float32)
    if scale < 1.0:
        corners *= np.float32(1.0 / scale)
    return _refine_subpixel(gray, corners).reshape(-1, 2)


# Builds the planar chessboard object points in the row-major order returned by OpenCV.
def _board_object_points(
    pattern: tuple[int, int],
    square_mm: float,
) -> np.ndarray:
    count = pattern[0] * pattern[1]
    points = np.zeros((count, 3), dtype=np.float32)
    indices = np.arange(count, dtype=np.int32)
    points[:, 0] = (indices % pattern[0]).astype(np.float32) * np.float32(square_mm)
    points[:, 1] = (indices // pattern[0]).astype(np.float32) * np.float32(square_mm)
    return points


# Fits intrinsics and removes only the worst per-view error until the configured floor is reached.
def calibrate_intrinsics_robust(
    views: Sequence[np.ndarray],
    image_size: tuple[int, int],
    pattern: tuple[int, int],
    square_mm: float,
    max_rms_px: float,
    min_views: int,
) -> IntrinsicsFit:
    """Reproduce ``calibrateIntrinsicsRobust`` and enforce its RMS result as a hard gate."""

    expected = pattern[0] * pattern[1]
    if min_views < 3:
        raise MobileCvError("minIntrinsicViews must be at least 3")
    if len(views) < min_views:
        raise MobileCvError(
            f"intrinsic calibration has {len(views)} usable views; {min_views} are required"
        )
    if image_size[0] <= 0 or image_size[1] <= 0:
        raise MobileCvError("calibration image dimensions must be positive")
    if square_mm <= 0.0 or max_rms_px <= 0.0:
        raise MobileCvError("intrinsic square size and RMS gate must be positive")

    retained = [np.asarray(view, dtype=np.float32).reshape(-1, 2) for view in views]
    if any(view.shape != (expected, 2) for view in retained):
        raise MobileCvError("an intrinsic view does not contain the complete chessboard grid")

    object_one = _board_object_points(pattern, square_mm)
    object_sets = [object_one.copy() for _ in retained]
    image_sets = [view.reshape(-1, 1, 2) for view in retained]
    camera_matrix = cv2.initCameraMatrix2D(
        object_sets,
        image_sets,
        image_size,
        aspectRatio=1.0,
    )
    distortion: np.ndarray | None = None
    flags = (
        cv2.CALIB_USE_INTRINSIC_GUESS
        | cv2.CALIB_FIX_ASPECT_RATIO
        | cv2.CALIB_ZERO_TANGENT_DIST
        | cv2.CALIB_FIX_K3
    )
    rejected = 0

    while True:
        object_sets = [object_one.copy() for _ in retained]
        image_sets = [view.reshape(-1, 1, 2) for view in retained]
        result = cv2.calibrateCameraExtended(
            object_sets,
            image_sets,
            image_size,
            camera_matrix,
            distortion,
            flags=flags,
        )
        rms = float(result[0])
        camera_matrix = np.asarray(result[1], dtype=np.float64)
        distortion = np.asarray(result[2], dtype=np.float64)
        per_view_errors = np.asarray(result[7], dtype=np.float64).reshape(-1)
        if rms <= max_rms_px or len(retained) <= min_views:
            break
        worst = int(np.argmax(per_view_errors))
        del retained[worst]
        rejected += 1

    dist5 = np.zeros((1, 5), dtype=np.float64)
    flattened = distortion.reshape(-1) if distortion is not None else np.empty(0)
    dist5[0, : min(5, flattened.size)] = flattened[:5]
    if not math.isfinite(rms) or rms > max_rms_px:
        raise MobileCvError(
            f"intrinsic RMS {rms:.6f} px exceeds the {max_rms_px:.6f} px gate"
        )
    return IntrinsicsFit(
        camera_matrix=camera_matrix,
        distortion=dist5,
        rms_px=rms,
        rejected_views=rejected,
        retained_views=len(retained),
    )


# Maps points through a 3x3 homography without changing their numeric precision first.
def apply_homography(matrix: np.ndarray, points: np.ndarray) -> np.ndarray:
    """Apply ``matrix`` to ``N x 2`` points and return float64 plane coordinates."""

    h = np.asarray(matrix, dtype=np.float64).reshape(3, 3)
    p = np.asarray(points)
    flat = p.reshape(-1, 2)
    x = flat[:, 0].astype(np.float64)
    y = flat[:, 1].astype(np.float64)
    w = h[2, 0] * x + h[2, 1] * y + h[2, 2]
    if np.any(np.abs(w) <= 1.0e-15):
        raise MobileCvError("homography maps a calibration point to infinity")
    return np.column_stack(
        (
            (h[0, 0] * x + h[0, 1] * y + h[0, 2]) / w,
            (h[1, 0] * x + h[1, 1] * y + h[1, 2]) / w,
        )
    )


# Fits the scale-locked 2D Kabsch rotation and translation used between board placements.
def fit_scale_locked_rigid_transform(
    source: np.ndarray,
    destination: np.ndarray,
) -> tuple[float, float, float, float]:
    """Return ``cosine, sine, translation_x, translation_y``."""

    src = np.asarray(source, dtype=np.float64).reshape(-1, 2)
    dst = np.asarray(destination, dtype=np.float64).reshape(-1, 2)
    if src.shape[0] == 0 or src.shape != dst.shape:
        raise MobileCvError("rigid-transform point sets must be non-empty and equally sized")
    src_mean = src.mean(axis=0)
    dst_mean = dst.mean(axis=0)
    a = src - src_mean
    b = dst - dst_mean
    sxx = float(np.sum(a[:, 0] * b[:, 0] + a[:, 1] * b[:, 1]))
    sxy = float(np.sum(a[:, 0] * b[:, 1] - a[:, 1] * b[:, 0]))
    angle = math.atan2(sxy, sxx)
    cosine = math.cos(angle)
    sine = math.sin(angle)
    tx = float(dst_mean[0] - (cosine * src_mean[0] - sine * src_mean[1]))
    ty = float(dst_mean[1] - (sine * src_mean[0] + cosine * src_mean[1]))
    return cosine, sine, tx, ty


# Alternates rigid board placement poses and a pooled LMEDS homography exactly three times.
def fit_plane_homography(
    placements: Sequence[np.ndarray],
    pattern: tuple[int, int],
    square_mm: float,
    max_error_mm: float,
) -> HomographyFit:
    """Reproduce ``fitPlaneHomography`` and enforce its median-error gate."""

    count = pattern[0] * pattern[1]
    if not placements:
        raise MobileCvError("homography calibration has no accepted placements")
    points = [np.asarray(p, dtype=np.float32).reshape(-1, 2) for p in placements]
    if any(p.shape != (count, 2) for p in points):
        raise MobileCvError("a homography placement does not contain the complete board")
    if square_mm <= 0.0 or max_error_mm <= 0.0:
        raise MobileCvError("homography square size and error gate must be positive")

    grid = _board_object_points(pattern, square_mm)[:, :2].astype(np.float32)
    matrix, _ = cv2.findHomography(points[0], grid, method=cv2.LMEDS)
    if matrix is None or matrix.size == 0:
        raise MobileCvError("the anchor homography is degenerate")
    matrix = np.asarray(matrix, dtype=np.float64)
    world = [np.empty_like(grid) for _ in points]
    world[0] = grid.copy()

    for _ in range(3):
        for index in range(1, len(points)):
            mapped = apply_homography(matrix, points[index])
            cosine, sine, tx, ty = fit_scale_locked_rigid_transform(grid, mapped)
            posed = np.empty_like(grid)
            posed[:, 0] = (
                cosine * grid[:, 0] - sine * grid[:, 1] + tx
            ).astype(np.float32)
            posed[:, 1] = (
                sine * grid[:, 0] + cosine * grid[:, 1] + ty
            ).astype(np.float32)
            world[index] = posed
        all_image = np.concatenate(points, axis=0)
        all_world = np.concatenate(world, axis=0)
        refined, _ = cv2.findHomography(all_image, all_world, method=cv2.LMEDS)
        if refined is None or refined.size == 0:
            raise MobileCvError("the pooled homography refit is degenerate")
        matrix = np.asarray(refined, dtype=np.float64)
        if len(points) == 1:
            break

    worst_median = 0.0
    for image_points, world_points in zip(points, world, strict=True):
        mapped = apply_homography(matrix, image_points)
        errors = np.hypot(
            mapped[:, 0] - world_points[:, 0],
            mapped[:, 1] - world_points[:, 1],
        )
        upper_median = float(np.partition(errors, errors.size // 2)[errors.size // 2])
        worst_median = max(worst_median, upper_median)
    if not math.isfinite(worst_median) or worst_median > max_error_mm:
        raise MobileCvError(
            "homography max placement median "
            f"{worst_median:.6f} mm exceeds the {max_error_mm:.6f} mm gate"
        )
    return HomographyFit(matrix=matrix, max_placement_median_mm=worst_median)


# Undistorts points back into the same pixel frame by passing P=K, as the Android JNI does.
def undistort_to_pixel_frame(
    points: np.ndarray,
    camera_matrix: np.ndarray,
    distortion: np.ndarray,
) -> np.ndarray:
    """Return an ``N x 2`` float32 array in the calibrated pixel frame."""

    source = np.asarray(points, dtype=np.float32).reshape(-1, 1, 2)
    if source.shape[0] == 0:
        return np.empty((0, 2), dtype=np.float32)
    result = cv2.undistortPoints(
        source,
        np.asarray(camera_matrix, dtype=np.float64),
        np.asarray(distortion, dtype=np.float64),
        P=np.asarray(camera_matrix, dtype=np.float64),
    )
    return np.asarray(result, dtype=np.float32).reshape(-1, 2)


# Computes the exact board-bounding-box Laplacian variance used by Sharpness.java.
def laplacian_variance_over_corners(gray: np.ndarray, corners: np.ndarray) -> float:
    """Return the population variance of the CV_64F Laplacian over the corner box."""

    points = np.asarray(corners, dtype=np.float32).reshape(-1, 2)
    if points.shape[0] == 0:
        return 0.0
    min_x = float(np.min(points[:, 0]))
    min_y = float(np.min(points[:, 1]))
    max_x = float(np.max(points[:, 0]))
    max_y = float(np.max(points[:, 1]))
    x = max(0, int(min_x))
    y = max(0, int(min_y))
    width = min(gray.shape[1], int(math.ceil(max_x))) - x
    height = min(gray.shape[0], int(math.ceil(max_y))) - y
    if width <= 0 or height <= 0:
        return 0.0
    roi = gray[y : y + height, x : x + width]
    laplacian = cv2.Laplacian(roi, cv2.CV_64F)
    _, stddev = cv2.meanStdDev(laplacian)
    value = float(stddev[0, 0])
    return value * value


# Implements positive std::round/std::lround semantics without Python's ties-to-even behavior.
def _round_positive(value: float) -> int:
    return int(math.floor(value + 0.5))


# Grows a detector box by a fraction per side and clamps it to the frame.
def _padded_roi(
    bbox: tuple[int, int, int, int],
    image_size: tuple[int, int],
    pad_frac: float,
) -> tuple[int, int, int, int]:
    x, y, width, height = bbox
    frame_width, frame_height = image_size
    pad_x = min(1_000_000, max(0, _round_positive(width * max(0.0, pad_frac))))
    pad_y = min(1_000_000, max(0, _round_positive(height * max(0.0, pad_frac))))
    x0 = max(0.0, float(x) - pad_x)
    y0 = max(0.0, float(y) - pad_y)
    x1 = min(float(frame_width), float(x) + width + pad_x)
    y1 = min(float(frame_height), float(y) + height + pad_y)
    if x1 <= x0 or y1 <= y0:
        return 0, 0, 0, 0
    return int(x0), int(y0), int(x1 - x0), int(y1 - y0)


# Selects the exact working crop size, including the two-pixel short-side floor.
def _working_size(crop_size: tuple[int, int], working_max_side: float) -> tuple[int, int]:
    width, height = crop_size
    long_side = max(width, height)
    maximum = max(0.0, working_max_side)
    if maximum <= 0.0 or long_side <= maximum:
        return width, height
    scale = maximum / long_side
    return max(2, _round_positive(width * scale)), max(2, _round_positive(height * scale))


# Returns the upper median of an 8-bit image, matching edge_core.cpp's histogram walk.
def _upper_median_u8(image: np.ndarray) -> float:
    flat = np.asarray(image, dtype=np.uint8).reshape(-1)
    if flat.size == 0:
        raise MobileCvError("cannot compute a threshold from an empty image")
    return float(np.partition(flat, flat.size // 2)[flat.size // 2])


# Resolves the configured Canny thresholds and prevents a degenerate equal pair.
def _resolve_thresholds(gray: np.ndarray | None, params: EdgeParams) -> tuple[float, float]:
    if params.mode == "fixed":
        low, high = params.low_threshold, params.high_threshold
    elif params.mode == "otsu":
        if gray is None:
            raise MobileCvError("OTSU edge thresholds require a grayscale working image")
        high, _ = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY | cv2.THRESH_OTSU)
        low = params.otsu_low_ratio * high
    elif params.mode == "median":
        if gray is None:
            raise MobileCvError("median edge thresholds require a grayscale working image")
        median = _upper_median_u8(gray)
        low = max(0.0, (1.0 - params.median_sigma) * median)
        high = min(255.0, (1.0 + params.median_sigma) * median)
    else:
        raise MobileCvError(f"unsupported edge threshold mode: {params.mode}")
    if high < low:
        low, high = high, low
    if high - low < 1.0:
        high = low + 1.0
    return float(low), float(high)


# Converts float gradients with the saturating signed-16-bit behavior of cv::Mat::convertTo.
def _saturating_int16(values: np.ndarray) -> np.ndarray:
    rounded = np.rint(values)
    return np.clip(rounded, -32768, 32767).astype(np.int16)


# Computes the Di Zenzo CIELab gradient accepted by OpenCV's two-gradient Canny overload.
def _di_zenzo_gradient(lab8: np.ndarray, aperture: int) -> tuple[np.ndarray, np.ndarray]:
    lab = lab8.astype(np.float32)
    channels = cv2.split(lab)
    gxx = np.zeros(lab.shape[:2], dtype=np.float32)
    gyy = np.zeros(lab.shape[:2], dtype=np.float32)
    gxy = np.zeros(lab.shape[:2], dtype=np.float32)
    for channel in channels:
        dx = cv2.Sobel(channel, cv2.CV_32F, 1, 0, ksize=aperture)
        dy = cv2.Sobel(channel, cv2.CV_32F, 0, 1, ksize=aperture)
        gxx += dx * dx
        gyy += dy * dy
        gxy += dx * dy
    difference = gxx - gyy
    two_gxy = np.float32(2.0) * gxy
    discriminant = np.sqrt(difference * difference + two_gxy * two_gxy)
    lambda_max = np.maximum(np.float32(0.5) * (gxx + gyy + discriminant), 0.0)
    magnitude = np.sqrt(lambda_max)
    theta = np.float32(0.5) * cv2.phase(difference, two_gxy, angleInDegrees=False)
    fx, fy = cv2.polarToCart(magnitude, theta, angleInDegrees=False)
    return _saturating_int16(fx), _saturating_int16(fy)


# Samples a sparse set of frame regions and returns its per-channel upper-median Lab colour.
def _sample_median_lab(
    image: np.ndarray,
    stride: int,
    regions: Sequence[tuple[int, int, int, int]],
) -> np.ndarray | None:
    samples: list[np.ndarray] = []
    frame_height, frame_width = image.shape[:2]
    for x, y, width, height in regions:
        x0 = max(0, x)
        y0 = max(0, y)
        x1 = min(frame_width, x + width)
        y1 = min(frame_height, y + height)
        if x1 <= x0 or y1 <= y0:
            continue
        region = image[y0:y1:stride, x0:x1:stride]
        if region.size:
            samples.append(region.reshape(-1, 3))
    if not samples:
        return None
    bgr = np.concatenate(samples, axis=0).astype(np.uint8)
    if bgr.shape[0] < 32:
        return None
    lab = cv2.cvtColor(bgr.reshape(1, -1, 3), cv2.COLOR_BGR2LAB).reshape(-1, 3)
    middle = lab.shape[0] // 2
    return np.array(
        [np.partition(lab[:, channel], middle)[middle] for channel in range(3)],
        dtype=np.float32,
    )


# Models the table and garment colours and returns the largest hole-filled garment region.
def _build_garment_mask(
    image: np.ndarray,
    bbox: tuple[int, int, int, int],
    working_crop: np.ndarray,
    params: EdgeParams,
) -> np.ndarray | None:
    x, y, width, height = bbox
    frame_height, frame_width = image.shape[:2]
    margin = max(16, max(width, height) // 24)
    ex0 = max(0, x - margin)
    ey0 = max(0, y - margin)
    ex1 = min(frame_width, x + width + margin)
    ey1 = min(frame_height, y + height + margin)
    exclude = (ex0, ey0, max(0, ex1 - ex0), max(0, ey1 - ey0))
    stride = max(8, max(frame_width, frame_height) // 90)
    band = max(16, max(width, height) // 30)
    ring = (
        (exclude[0] - band, exclude[1], band, exclude[3]),
        (exclude[0] + exclude[2], exclude[1], band, exclude[3]),
        (exclude[0], exclude[1] - band, exclude[2], band),
        (exclude[0], exclude[1] + exclude[3], exclude[2], band),
    )
    table = _sample_median_lab(image, stride, ring)
    if table is None:
        return None

    core = (
        x + width * 3 // 8,
        y + height * 3 // 8,
        max(1, width // 4),
        max(1, height // 4),
    )
    cx0 = max(0, core[0])
    cy0 = max(0, core[1])
    cx1 = min(frame_width, core[0] + core[2])
    cy1 = min(frame_height, core[1] + core[3])
    clamped_core = (cx0, cy0, max(0, cx1 - cx0), max(0, cy1 - cy0))
    if clamped_core[2] < 4 or clamped_core[3] < 4:
        return None
    garment = _sample_median_lab(image, max(1, stride // 2), (clamped_core,))
    if garment is None:
        return None

    small = working_crop
    long_side = max(small.shape[1], small.shape[0])
    if long_side > 256:
        scale = 256.0 / long_side
        small = cv2.resize(
            small,
            (
                max(2, _round_positive(small.shape[1] * scale)),
                max(2, _round_positive(small.shape[0] * scale)),
            ),
            interpolation=cv2.INTER_AREA,
        )
    lab = cv2.cvtColor(small, cv2.COLOR_BGR2LAB).astype(np.float32)
    garment_distance = np.linalg.norm(lab - garment.reshape(1, 1, 3), axis=2)
    table_distance = np.linalg.norm(lab - table.reshape(1, 1, 3), axis=2)
    mask = np.where(garment_distance < table_distance, 255, 0).astype(np.uint8)

    kernel_size = max(3, int(min(mask.shape[1], mask.shape[0]) * params.mask_morph_frac)) | 1
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (kernel_size, kernel_size))
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(mask, connectivity=8)
    if count <= 1:
        return None
    best = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    mask = np.where(labels == best, 255, 0).astype(np.uint8)

    bordered = cv2.copyMakeBorder(mask, 1, 1, 1, 1, cv2.BORDER_CONSTANT, value=0)
    flood_mask = np.zeros((bordered.shape[0] + 2, bordered.shape[1] + 2), dtype=np.uint8)
    cv2.floodFill(bordered, flood_mask, (0, 0), 255)
    flood = bordered[1:-1, 1:-1]
    mask = cv2.bitwise_or(mask, cv2.bitwise_not(flood))
    if mask.shape != working_crop.shape[:2]:
        mask = cv2.resize(
            mask,
            (working_crop.shape[1], working_crop.shape[0]),
            interpolation=cv2.INTER_NEAREST,
        )
    return mask


# Executes one vectorized Zhang-Suen sub-iteration with the mobile endpoint-preservation rule.
def _thinning_pass(padded: np.ndarray, step: int) -> int:
    center = padded[1:-1, 1:-1]
    p2 = padded[:-2, 1:-1]
    p3 = padded[:-2, 2:]
    p4 = padded[1:-1, 2:]
    p5 = padded[2:, 2:]
    p6 = padded[2:, 1:-1]
    p7 = padded[2:, :-2]
    p8 = padded[1:-1, :-2]
    p9 = padded[:-2, :-2]
    neighbors = [p2, p3, p4, p5, p6, p7, p8, p9]
    degree = sum(neighbor.astype(np.uint8) for neighbor in neighbors)
    transitions = sum(
        ((~neighbors[index]) & neighbors[(index + 1) % 8]).astype(np.uint8)
        for index in range(8)
    )
    if step == 0:
        directional = (~(p2 & p4 & p6)) & (~(p4 & p6 & p8))
    else:
        directional = (~(p2 & p4 & p8)) & (~(p2 & p6 & p8))
    remove = center & (degree >= 3) & (degree <= 6) & (transitions == 1) & directional
    removed = int(np.count_nonzero(remove))
    center[remove] = False
    return removed


# Tests whether one set pixel is locally 8-simple and is not a line endpoint.
def _is_eight_simple(padded: np.ndarray, y: int, x: int) -> bool:
    neighbors = (
        bool(padded[y - 1, x]),
        bool(padded[y - 1, x + 1]),
        bool(padded[y, x + 1]),
        bool(padded[y + 1, x + 1]),
        bool(padded[y + 1, x]),
        bool(padded[y + 1, x - 1]),
        bool(padded[y, x - 1]),
        bool(padded[y - 1, x - 1]),
    )
    if sum(neighbors) < 2:
        return False
    components = sum(
        neighbors[index] and not neighbors[(index + 7) % 8] for index in range(8)
    )
    return components == 1


# Removes parity-created 2x2 blocks without changing local 8-connectivity.
def _remove_two_by_two_blocks(padded: np.ndarray) -> None:
    while True:
        candidates = (
            padded[1:-2, 1:-2]
            & padded[1:-2, 2:-1]
            & padded[2:-1, 1:-2]
            & padded[2:-1, 2:-1]
        )
        progress = False
        for raw_y, raw_x in np.argwhere(candidates):
            y = int(raw_y) + 1
            x = int(raw_x) + 1
            if not (
                padded[y, x]
                and padded[y, x + 1]
                and padded[y + 1, x]
                and padded[y + 1, x + 1]
            ):
                continue
            for dy, dx in ((0, 0), (0, 1), (1, 0), (1, 1)):
                if _is_eight_simple(padded, y + dy, x + dx):
                    padded[y + dy, x + dx] = False
                    progress = True
                    break
        if not progress:
            return


# Thins a binary map to the same fixed point and crossing policy as edge_core.cpp.
def thin_zhang_suen(binary: np.ndarray) -> np.ndarray:
    """Return an independent uint8 edge map with values exactly 0 or 255."""

    padded = np.pad(np.asarray(binary) > 0, 1, mode="constant", constant_values=False)
    while True:
        removed_a = _thinning_pass(padded, 0)
        removed_b = _thinning_pass(padded, 1)
        if removed_a + removed_b == 0:
            break
    _remove_two_by_two_blocks(padded)
    return np.where(padded[1:-1, 1:-1], 255, 0).astype(np.uint8)


# Drops connected components below the configured pixel floor.
def _prune_small_components(binary: np.ndarray, minimum_pixels: int) -> np.ndarray:
    count, labels, stats, _ = cv2.connectedComponentsWithStats(binary, connectivity=8)
    if count <= 1:
        return binary
    keep = np.zeros(count, dtype=bool)
    keep[1:] = stats[1:, cv2.CC_STAT_AREA] >= minimum_pixels
    return np.where(keep[labels] & (binary > 0), 255, 0).astype(np.uint8)


# Runs the shipped BGR desktop equivalent of edge_core::detectGarmentEdges over one landmark box.
def detect_garment_edges(
    image: np.ndarray,
    garment_bbox: tuple[int, int, int, int],
    params: EdgeParams = EdgeParams(),
) -> EdgeResult:
    """Return the full-resolution ROI edge map or raise when no edge is available."""

    if image is None or image.size == 0 or image.dtype != np.uint8 or image.ndim != 3:
        raise MobileCvError("garment edge detection requires a non-empty uint8 BGR image")
    if image.shape[2] != 3:
        raise MobileCvError("garment edge detection requires exactly three BGR channels")
    if params.sobel_aperture not in (3, 5, 7):
        raise MobileCvError("edge Sobel aperture must be 3, 5, or 7")

    frame_size = (image.shape[1], image.shape[0])
    roi = _padded_roi(garment_bbox, frame_size, params.roi_pad_frac)
    x, y, width, height = roi
    if width < 2 or height < 2:
        raise MobileCvError("the landmark bounding box has no usable garment ROI")
    full_crop = image[y : y + height, x : x + width]
    working_size = _working_size((width, height), params.working_max_side)
    crop = (
        cv2.resize(full_crop, working_size, interpolation=cv2.INTER_AREA)
        if working_size != (width, height)
        else full_crop
    )
    working_scale = max(working_size) / max(width, height)

    use_colour = params.colour_gradient
    need_gray = (not use_colour) or params.mode != "fixed"
    gray: np.ndarray | None = None
    if need_gray:
        gray = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
        if params.use_clahe:
            clahe = cv2.createCLAHE(
                clipLimit=params.clahe_clip,
                tileGridSize=(max(1, params.clahe_grid), max(1, params.clahe_grid)),
            )
            gray = clahe.apply(gray)
        if params.denoise_kernel >= 3:
            kernel = params.denoise_kernel | 1
            gray = cv2.GaussianBlur(
                gray,
                (kernel, kernel),
                sigmaX=params.denoise_sigma,
            )
    low, high = _resolve_thresholds(gray, params)

    if use_colour:
        lab = cv2.cvtColor(crop, cv2.COLOR_BGR2LAB)
        if params.use_clahe:
            channels = list(cv2.split(lab))
            channels[0] = cv2.createCLAHE(
                clipLimit=params.clahe_clip,
                tileGridSize=(max(1, params.clahe_grid), max(1, params.clahe_grid)),
            ).apply(channels[0])
            lab = cv2.merge(channels)
        if params.denoise_kernel >= 3:
            kernel = params.denoise_kernel | 1
            lab = cv2.GaussianBlur(
                lab,
                (kernel, kernel),
                sigmaX=params.denoise_sigma,
            )
        dx, dy = _di_zenzo_gradient(lab, params.sobel_aperture)
        edges = cv2.Canny(dx, dy, low, high, L2gradient=params.l2_gradient)
    else:
        if gray is None:
            raise MobileCvError("the luma edge path has no grayscale image")
        edges = cv2.Canny(
            gray,
            low,
            high,
            apertureSize=params.sobel_aperture,
            L2gradient=params.l2_gradient,
        )

    if params.mask_non_garment:
        mask = _build_garment_mask(image, garment_bbox, crop, params)
        if mask is not None:
            if params.mask_dilate_px > 0:
                diameter = 2 * params.mask_dilate_px + 1
                grown = cv2.dilate(
                    mask,
                    cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (diameter, diameter)),
                )
            else:
                grown = mask
            edges = cv2.bitwise_and(edges, grown)

    if params.morph_close_kernel > 0:
        kernel = cv2.getStructuringElement(
            cv2.MORPH_ELLIPSE,
            (params.morph_close_kernel, params.morph_close_kernel),
        )
        edges = cv2.morphologyEx(edges, cv2.MORPH_CLOSE, kernel)
    if params.min_component_px > 0:
        edges = _prune_small_components(edges, params.min_component_px)
    if edges.shape != (height, width):
        edges = cv2.resize(edges, (width, height), interpolation=cv2.INTER_NEAREST)
    if params.thin:
        edges = thin_zhang_suen(edges)
    if int(np.count_nonzero(edges)) == 0:
        raise MobileCvError("the shipped garment edge detector produced no edge pixels")
    return EdgeResult(roi=roi, binary_roi=edges, working_scale=working_scale)


# Converts a visibility-gated landmark set into PhotoAnalyser's padded garment box.
def landmark_bounding_box(
    points: Sequence[object],
    frame_size: tuple[int, int],
    pad_fraction: float = 0.06,
    minimum_visibility: float = 0.5,
) -> tuple[int, int, int, int]:
    """Return the exact ``EdgeGarmentOutline.boundingBoxOf`` rectangle."""

    usable: list[tuple[float, float]] = []
    for point in points:
        x = float(getattr(point, "x"))
        y = float(getattr(point, "y"))
        visibility = float(getattr(point, "visibility"))
        if visibility >= minimum_visibility and math.isfinite(x) and math.isfinite(y):
            usable.append((x, y))
    if not usable:
        raise MobileCvError("no landmark clears the mobile outline visibility gate")
    values = np.asarray(usable, dtype=np.float64)
    min_x, min_y = np.min(values, axis=0)
    max_x, max_y = np.max(values, axis=0)
    pad_x = (max_x - min_x) * pad_fraction
    pad_y = (max_y - min_y) * pad_fraction
    frame_width, frame_height = frame_size
    x0 = int(max(0.0, math.floor(min_x - pad_x)))
    y0 = int(max(0.0, math.floor(min_y - pad_y)))
    x1 = int(min(float(frame_width), math.ceil(max_x + pad_x)))
    y1 = int(min(float(frame_height), math.ceil(max_y + pad_y)))
    bbox = x0, y0, max(0, x1 - x0), max(0, y1 - y0)
    if bbox[2] <= 0 or bbox[3] <= 0:
        raise MobileCvError("the visibility-gated landmarks produce an empty garment box")
    return bbox


# Converts an ROI-local nonzero map into full-frame float32 edge coordinates in row-major order.
def edge_points_full_frame(result: EdgeResult) -> np.ndarray:
    """Return ``N x 2`` full-frame coordinates compatible with cv::findNonZero."""

    rows, columns = np.nonzero(result.binary_roi)
    if rows.size == 0:
        raise MobileCvError("the garment edge map is empty")
    x, y, _, _ = result.roi
    return np.column_stack((columns + x, rows + y)).astype(np.float32)
