# Session replay inventory module

`SessionReplay.Inventory.psm1` provides the configuration, local/S3 inventory,
download, and timestamp-correlation boundary used by the session replay launcher.
It does not run calibration, inference, or rendering.

## Public functions

- `Read-SessionReplayConfiguration [-RepositoryRoot <absolute path>]` reads the
  fixed `scripts/session-replay.settings.local.json` file as inert UTF-8 JSON
  string data.
- `Get-SessionReplayDayInventory -Configuration <object> -Source <Local|S3>
  -UtcDay <yyyy-MM-dd>` returns normalized images, matching result JSON files,
  and lens/homography calibration groups for one exact UTC day.
- `Test-SessionReplayS3Access -Configuration <object>` runs the credential-
  scrubbed, named-profile STS identity preflight and returns `$true` or throws.
- `Save-SessionReplayS3Object -Configuration <object> -Record <object>
  -Destination <absolute path>` downloads one listed S3 object without overwrite
  with the listed ETag as an `If-Match` condition, then verifies its nonzero
  byte size against list metadata.
- `Resolve-SessionReplayCorrelation -Captures <object[]> -Results <object[]>
  -MaxDeltaSeconds <0..60>` creates deterministic one-to-one matches inside each
  UTC minute, reporting `Matched`, `Unmatched`, or true equal-delta `Ambiguous`
  states.

Every normalized local or S3 object has exactly these ordered properties:
`Key`, `LocalPath`, `LastModifiedUtc`, `Size`, and `ContentIdentity`. S3 records
have a null `LocalPath` until downloaded and carry the exact listed ETag as
their content identity. Local inventory records acquire a lowercase SHA-256
identity when the runner freezes the selected plan. During Run, both source
types are staged under `source/`; Local files are size/hash checked before and
after their temporary copy, while S3 downloads are ETag/size checked.

## Configuration contract

The launcher creates the ignored
`scripts/session-replay.settings.local.json` from the tracked, non-secret
`scripts/session-replay.settings.example.json` when it is missing. The inventory
module itself requires the local file to exist.

The JSON root must be one object containing exactly these 20 required,
case-sensitive keys. Every value is a non-empty JSON string.

```text
QSEE_REPLAY_AWS_EXE
QSEE_REPLAY_AWS_PROFILE
QSEE_REPLAY_AWS_REGION
QSEE_REPLAY_S3_BUCKET
QSEE_REPLAY_MIRROR_ROOT
QSEE_REPLAY_SOURCE
QSEE_REPLAY_IMAGE_SET
QSEE_REPLAY_CLIENT
QSEE_REPLAY_SIZE
QSEE_REPLAY_ROTATION
QSEE_REPLAY_MAX_IMAGES
QSEE_REPLAY_COVERAGE_TARGET_FRACTION
QSEE_REPLAY_CORRELATION_SECONDS
QSEE_REPLAY_MODEL
QSEE_REPLAY_TABLE_PATH
QSEE_REPLAY_CV_ENDPOINT
QSEE_REPLAY_CORE_ROOT
QSEE_REPLAY_OUTPUT_ROOT
QSEE_REPLAY_PYTHON_EXE
QSEE_REPLAY_ENV_LOCAL
```

Missing, duplicate, unknown, aliased, differently cased, non-string, empty, and
malformed fields fail. The file must be valid UTF-8 JSON, no larger than 64 KiB,
and not a symbolic link or reparse point. The source is exactly `Local` or `S3`;
the image set is exactly `Captured`, `PreviewDetected`, or
`PreviewNotDetected`; rotation is `0`, `90`, `180`, or `270`.
`QSEE_REPLAY_COVERAGE_TARGET_FRACTION` is exactly `0.50` or `0.70`; `0.50`
selects the replay-tool relaxed contract and `0.70` selects the current mobile
contract. No intermediate value or automatic retry is accepted.
`QSEE_REPLAY_PYTHON_EXE` must be an absolute path but may not exist until Setup;
execution preflight owns its existence check.

The JSON contains configuration paths and named-profile identifiers, never AWS
access keys, session tokens, the remote API key, or passwords. The public
runner separately requires the exact fixed remote endpoint/model, the selected
client's current bundled measurement table, and an exact existing size row
before remote submission.

S3 operations use only the configured absolute AWS executable, named profile,
region, and bucket. Their child process removes ambient AWS credential/profile
variables, disables instance metadata, runs STS first, and permits only listing
or one explicit `get-object` download. Successful command output is consumed
internally; failure exit code and stderr are preserved as diagnostics without
printing credentials.

Correlation requires every result basename to match the exact timestamped
contract, for example `2026-08-18-03-27-59-<16 hex>.json`, and uses that encoded
UTC second. A nonconforming result key fails the inventory/correlation flow;
there is no `LastModifiedUtc` compatibility fallback for results. Captures use
`LastModifiedUtc`. Candidate edges are sorted by delta, result key, and capture
key before deterministic one-to-one assignment; equal-delta connected
conflicts remain `Ambiguous` rather than being guessed.

The module groups calibration objects but does not choose a usable fallback.
The runner selects the explicit group or the latest prior same-day group for
each calibration kind, then applies readiness gates. An underfilled latest
group blocks replay; it does not cause selection of an older group.

Run the focused local validation without Pester:

```powershell
pwsh -NoProfile -NonInteractive -File `
  .\scripts\common\session-replay\SessionReplay.Inventory.Tests.ps1
```
