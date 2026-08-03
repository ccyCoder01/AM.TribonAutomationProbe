# Tribon Script Contract

The real Tribon integration is intentionally not guessed. The script must implement `context.get`, `annotation.export`, and `annotation.move`, preserve `commandId`/`correlationId`, return Bridge 0.1 result JSON, and distinguish write, refresh, re-read, and verification failures using the documented error codes.
