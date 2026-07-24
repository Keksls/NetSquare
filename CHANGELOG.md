# Changelog

Notable changes to NetSquare are documented here. Versions follow Semantic Versioning, and all three NuGet packages are released together because the handshake currently requires exact Core version equality.

## [1.0.16] - 2026-07-24

### Changed

- Removed the legacy application-level encryption API, key persistence and unsafe `BinaryFormatter` usage.
- Replaced global GZip/Deflate transforms with optional per-message Deflate compression optimized for realtime latency.
- Compression now applies only to message bodies and retains uncompressed messages unless the configured minimum saving is reached.
- Added an explicit wire flags byte and increased the message wire protocol version to 3.
- Preserved TLS for authenticated TCP confidentiality and left UDP MAC64 authentication unchanged.

### Compatibility

- `NetSquare.Client`, `NetSquare.Server`, and `NetSquare.Core` must all use version `1.0.16`.
- Version 1.0.16 intentionally removes the legacy encryption and compressor-selection public APIs.
- Handshake V2 remains unchanged; message wire version 3 rejects older peers before connection establishment.

## [1.0.15] - 2026-07-24

### Improved

- Replaced per-action scheduler threads with one shared coordinator and a bounded worker pool.
- Reduced allocations in message packing, buffer reuse, UDP sending, world synchronization, and spatialization hot paths.
- Reused UDP socket operation state and routed pending datagrams without scanning every registered message route.
- Reworked synchronized-state snapshots so newer client updates are not removed while an older snapshot is being broadcast.
- Added bounded synchronization-frame retention to prevent unbounded per-client growth.
- Added visibility and chunk hysteresis to reduce spatialization churn near boundaries.
- Expanded diagnostic load scenarios and result reporting for allocation and reliability validation.

### Fixed

- UDP transports now close and unregister before their owning TCP session is disposed.
- UDP receive callbacks no longer restart after shutdown.
- Scheduler frequency changes now affect the next execution without restarting the action.
- Stopping or removing scheduled work waits for active callbacks and detaches the runner cleanly.
- Spatialization and synchronization shutdown now remove their scheduler actions instead of leaving stopped registrations behind.

### Compatibility

- No intentional public API removal.
- `NetSquare.Client`, `NetSquare.Server`, and `NetSquare.Core` must all use version `1.0.15`.
- The wire handshake version remains unchanged.
