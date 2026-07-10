# Gua.Testing.Visual

This opt-in package adds PNG baseline comparison and semantic operation recording/replay.
Semantic assertions remain the primary test path. Missing baselines fail unless
`UpdateBaselines` or `GUA_UPDATE_BASELINES=1` is explicit; variants are caller supplied.
Failures write expected/actual/diff PNG and `comparison.json` below the Gua artifact root.
Pass the artifact path returned by `GuaDiagnosticWriter` as `FailureDirectory` to
place visual output in that same failure directory.

PNG support is isolated here through StbImageSharp and StbImageWriteSharp; their upstream
licenses are included by the NuGet packages. Recording v1 follows
`protocol/schema/recording.schema.json`. Sensitive values are represented only by a
`secretKey` and require a caller-provided resolver during replay.
