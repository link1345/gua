# Gua.Runtime

Managed `net10.0` and `netstandard2.1` wrapper over the stable Gua runtime C
ABI. Engine adapters use it to publish semantic frames, consume actions,
complete screenshot requests, expose adapter versions, and run the Inspector
WebSocket bridge without duplicating P/Invoke declarations.

The native `gua_runtime` library must be deployed for the current platform.
