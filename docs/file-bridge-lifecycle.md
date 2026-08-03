# File Bridge Lifecycle

The console writes UTF-8 JSON to `inbox` through a `.tmp` file followed by an atomic rename. A Tribon-side script should move the request to `processing`, execute only the three allow-listed actions, and write `{commandId}.result.json` to `output` (or diagnostics to `failed`). The console polls with cancellation and a command timeout, validates the result envelope, then the script may archive the request.
