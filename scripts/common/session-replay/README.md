# Session replay Python engine

This folder is an internal implementation detail of `scripts/QSee.ps1`. The
PowerShell launcher owns source selection, date/time correlation, local/S3
settings, credentials, and calls to the authoritative remote landmark model.
This Python engine never runs local inference and never calls an endpoint.

The public runner fixes the remote endpoint to
`https://testqsee.dopikai.com/panko/api/v1/landmark-estimation/estimate`, the
model to `tshirt-panko-detr-pose`, the selected client's bundled table, and the
calibration contract below. It accepts no endpoint, model, table, or calibration
implementation substitution. The settings JSON selects exactly one approved
coverage fraction: `0.50` for relaxed historical recovery or `0.70` for the
current mobile contract. The Python engine supplies the strict local
calibration, frame validation, geometry extraction, and rendering stages.
Before any remote submission, PowerShell preflight also requires the explicit
size to occur exactly once as a case-sensitive top-level key in that bundled
table.

Run executes that strict preflight before Local/S3 inventory or negative-cache
lookup. The runner then selects the explicit calibration group or the latest
prior same-day group for each kind and applies readiness gates; it never tries
an older group when the latest prior group is underfilled. Selected Local files
are SHA-256-bound and staged through verified copies, while selected S3 objects
are staged with their listed ETags as `If-Match` conditions.

Calibration runs first and must pass every selected strict gate before the
launcher may submit any garment image. It never retries with another coverage
target:

```powershell
python session_replay_engine.py calibrate `
  --manifest calibration-manifest.json `
  --output calibration.json
```

The calibration manifest uses schema `qsee.session-replay.extract.v1` and must
have an empty `items` array. Before any upload, the launcher must also prove
that every selected garment has the exact calibrated frame after rotation:

```powershell
python session_replay_engine.py validate-frames `
  --calibration calibration.json `
  --rotation 90 `
  --image capture-001.jpg `
  --image capture-002.jpg
```

This read-only command validates the calibration schema, payload hash, identity
hash, matrices, rotation, and frame, then decodes every selected image. It emits
no files. After it passes, the launcher creates normalized remote artifacts
with `qsee-ai-core/scripts/replay_mobile_landmarks.py`; extraction validates the
exact calibration identity and emits the requested `.pipe` files:

```powershell
python session_replay_engine.py extract `
  --manifest extract-manifest.json `
  --calibration calibration.json
```

The manifest has this exact shape. Unknown fields and aliases are rejected.
Paths are resolved relative to the manifest file, and array order is the
chronological capture order used by the mobile coverage gate.

```json
{
  "schema": "qsee.session-replay.extract.v1",
  "rotation": 90,
  "calibration": {
    "lensImages": ["lens/2026-08-18-08-00-00.jpg"],
    "homographyImages": ["homography/2026-08-18-08-10-00.jpg"],
    "patternColumns": 10,
    "patternRows": 14,
    "squareMm": 50.0,
    "maxIntrinsicRmsPx": 2.0,
    "minIntrinsicViews": 10,
    "maxHomographyErrorMm": 2.0,
    "minHomographyPlacements": 2,
    "maxHomographyPlacements": 12,
    "lensSharpnessThreshold": 100.0,
    "homographySharpnessThreshold": 200.0,
    "coverageGridSize": {"columns": 20, "rows": 20},
    "coverageNewCells": 17,
    "coverageTargetFraction": 0.5,
    "homographyStabilityToleranceFraction": 0.01
  },
  "items": [
    {
      "id": "capture-001",
      "image": "garments/2026-08-18-08-20-00.jpg",
      "landmarks": "landmarks/capture-001.landmarks.json",
      "pipe": "pipes/capture-001.pipe"
    }
  ]
}
```

Every landmark file must be `qsee.landmarks.v1`, contain exactly K1..K17 from
`tshirt-panko-detr-pose`, match the source JPEG SHA-256, and describe the same
rotated frame as the calibration. Missing calibration, an over-gate fit, a
frame mismatch, a landmark mismatch, or an empty garment edge fails the item
and the overall extraction. No replay-added raw-pixel, uncalibrated,
local-model, or chord fallback exists. The current mobile calculators retain
their production fallback behavior because replay analysis invokes them unchanged.

Checkerboard detection follows one fixed current-mobile sequence for both lens
and homography inputs. `findChessboardCornersSB` runs first. If it does not find
the complete grid, classic `findChessboardCorners` runs second and its result is
refined with `cornerSubPix`. The classic second step is part of the current
algorithm; it is not a legacy or replay-compatibility route and is not
operator-configurable.

Before analysis or rendering, every selected historical comparison file is
validated independently. This command is read-only, accepts repeatable files,
and rejects the first unknown field, alias, duplicate measure, non-finite value,
non-UTC timestamp, or client/size mismatch:

```powershell
python session_replay_engine.py validate-historical `
  --historical-json historical-result-001.json `
  --historical-json historical-result-002.json `
  --client Kiabi `
  --size M
```

The unversioned historical root must contain exactly `product_code`,
`product_brand`, `timestamp`, `size`, `client`, and `measures`. Each measure must
contain exactly `measure_name` and finite numeric `measure_value`. Historical
measurement IDs must be a canonical-order subset of the selected client's
current IDs. Such incomplete subsets remain comparison evidence; they are not
substituted for the complete current client measurement contract. Every file
must be named exactly `YYYY-MM-DD-HH-mm-ss-<16 hex>.json`, and its strict UTC-Z
payload timestamp must fall within that filename's encoded UTC minute. The
filename second remains the correlation event time used for displayed deltas. This check
runs before any remote garment request.

For each image, PowerShell writes a one-item
`analysis-manifests/<item-id>.json` using
`qsee.session-replay.manifest.v1`. Its exact root fields are `schema`, `client`,
`size`, `calibrationSha256`, and `items`; the calibration hash binds Java
analysis to the exact `calibration.json` bytes. The runner completes remote
landmarks, extraction, this focused Java analysis, strict rendering, and the
item summary before it may send the next image remotely.

An optional review renderer appends a summary panel without changing evidence.
The panel marks a 50% artifact as replay-tool calibration and states that the
current mobile target is 70%; a 70% artifact is marked as current mobile.
It accepts only `qsee.session-replay.analysis.v1`. Its source must contain
exactly `pipe`, `image`, `pipeSha256`, and `calibrationSha256`, where both hashes
are lowercase SHA-256 values of exact file bytes. The renderer strictly parses
the fixed pipe record order, verifies its homography against the selected
calibration artifact, and binds frame plus all raw, undistorted, normalized,
and visibility keypoint values back to the pipe and landmark artifact. It also
requires the exact current measurement ID order for the selected client.
Calculator endpoints are optional renderer hints exactly as they are in the
app; every endpoint that is present is still bound to normalized coordinates
times the frame. Homography invertibility is scale-independent, while any
calibrated keypoint or edge mapped to infinity is rejected.
`--provenance` is required; `--historical-json` is optional and must be omitted
when no uniquely matched measures JSON exists:

```powershell
python session_replay_engine.py render `
  --image capture.jpg `
  --landmarks capture.landmarks.json `
  --analysis capture.analysis.json `
  --pipe capture.pipe `
  --calibration calibration.json `
  --expected-client Kiabi `
  --expected-size M `
  --provenance capture.provenance.json `
  --historical-json historical-result.json `
  --rotation 90 `
  --output capture.review.jpg
```

The required provenance file uses schema `qsee.session-replay.provenance.v1`:

```json
{
  "schema": "qsee.session-replay.provenance.v1",
  "image": {"key": "captured/capture.jpg", "timestampUtc": "2026-08-18T08:20:30Z"},
  "historicalResult": {
    "key": "results/2026-08-18-08-18-00-0123456789abcdef.json",
    "timestampUtc": "2026-08-18T08:18:00Z",
    "deltaSeconds": 150.0,
    "sha256": "0000000000000000000000000000000000000000000000000000000000000000"
  },
  "lensCalibration": {
    "groupId": "round-lens-001",
    "referenceTimestampUtc": "2026-08-18T07:50:00Z",
    "deltaSeconds": 1830.0
  },
  "homographyCalibration": {
    "groupId": "session-table-001",
    "referenceTimestampUtc": "2026-08-18T08:10:20Z",
    "deltaSeconds": 610.0
  },
  "association": "inferred-from-UTC-time-not-database-identity"
}
```

The signed convention is always `image UTC - reference UTC`: positive means the
image is later, and negative means it is earlier. The historical reference is
the matched result event UTC. The lens and checkerboard/homography references
are each the last frame by `LastModifiedUtc` in the selected calibration group.
The renderer recomputes every delta, rejects a difference greater than 1 ms,
and labels the per-image box `INFERRED UTC ASSOCIATIONS`. A selected historical
file must have the same basename as `historicalResult.key`, its exact file-byte
SHA-256 must equal `historicalResult.sha256`, and the provenance timestamp must
fall within the UTC minute encoded in that basename. The filename second remains
the exact correlation reference used by provenance. The old three-field historical
provenance shape is rejected.

`historicalResult` may be `null` when no measures JSON candidate was selected.
In that valid case the launcher omits `--historical-json`, and the box displays
`Measures JSON: none selected`; image and calibration provenance remain
required.

A historical JSON file is comparison evidence only. It supplies posted
product/client/size/timestamp metadata and `measure_name` / `measure_value`
rows; it has no landmarks, calibration, tolerance, verdict, or database session
identity. The renderer displays matching stored values without inferring a
missing row or allowing any historical value to affect current PASS/FAIL.

The PowerShell runner is fail-fast for the selected set. The first remote,
extraction, Java analysis, or rendering failure terminates the run and writes
`evidence.md`; `COMPLETE` is emitted only after every selected image has a
completed overlay. Failed calibration and frame validation also create a
content-addressed negative entry under `evidence-cache`; successful calibration
is not cached or reused.

Every run directory also receives a plain-language `README.md` before
calibration processing begins. It explains the original-photo starting point,
lens-distortion calibration, checkerboard metric calibration, remote K1..K17
detection, current mobile measurement, and the exact UTC windows used to select
and correlate the retained files. This guide is intentionally separate from the
technical manifests and exists for successful and failed runs.

After every selected item reaches `COMPLETE`, the runner invokes the strict
spreadsheet generator and writes
`<client>-<UTC-day>-session-replay-evidence.xlsx` directly in the run root.
The workbook contains Overview, Images, Measurements, Calibration Frames, and
Sources & Timing sheets. Microsoft Excel desktop is the only workbook runtime;
its absence fails preflight. The generator records native local-file hyperlinks,
the exact UTC selection windows, and each photo's signed deltas to historical
JSON, lens calibration, and checkerboard calibration. Excel saves to a temporary
file, reopens it, recalculates the overview formulas, verifies the exact sheet
set, and rejects formula errors before the file can replace the final output.
The runner then inspects the OOXML worksheet cells and rejects cached error cells
or worksheet `HYPERLINK` formulas. Any workbook failure changes the run to
`FAILED`; there is no alternate spreadsheet writer or output location.
`overlays` contains only the annotated JPEGs.

The fingerprint binds selected byte identities, timestamps, measurement-table
bytes, replay/mobile source hashes, configuration, and exact Python, OpenCV,
and NumPy runtime-binary identities. Only the engine's reserved deterministic
rejection code may create a cache entry; ordinary process or operational
failures do not.

Every caught deterministic `SessionReplayError` or `MobileCvError` exits with
reserved code `23`. Argument parsing and Python startup failures retain their
native exit codes, so callers must negative-cache only code `23`.

Use `QSee.ps1 Setup` to create or update the launcher-managed private Python
environment. Run the focused tests through the QSee launcher; this internal
folder does not define a separate public setup or direct pip workflow.
