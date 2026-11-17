# PCB Inspection Viewer — Engineering & Agent Guidelines

C# .NET 8 WPF operator station client for the DeepPCB-AOI defect detection system.

## Architecture & Invariants

1. **Deterministic Rule Supremacy**:
   - Board verdicts (`PASS`, `REWORK`, `SCRAP`) come exclusively from the `DeepPCB-AOI` deterministic rule engine and Stage 4 safety guardrails.
   - LLM components (`SummaryCoordinator`, `NlToSqlService`) only summarize structured facts or translate natural-language queries; they never compute or override dispositions.
   - Always validate LLM summary outputs through `SummaryValidator` and fall back to `TemplateSummaryProvider` on validation failure or offline mode.
2. **Defense-in-Depth SQL Security (`SqlGuard`)**:
   - All natural-language SQL queries must execute via `ISqlGuard` with `Mode=ReadOnly` and `PRAGMA query_only = ON`.
   - Queries are restricted to allowlisted read-only views (`v_inspections`, `v_defects`, `v_batches`) with a `LIMIT 500` cap and 2-second timeout.
3. **Build & Test Discipline**:
   - `Nullable=enable` and `TreatWarningsAsErrors=true` are enforced across the solution.
   - Run `dotnet test` on `PcbInspectionViewer.Core.Tests` before committing any core logic changes.

## Graphify Navigation Rules (Token Optimization)

- **When to consult Graphify**: Before implementing cross-module features, refactoring across projects, or tracing unfamiliar dependencies, query the knowledge graph (`graphify-out/GRAPH_REPORT.md` or `graphify query "<question>"` via a lightweight `flash` subagent) to locate exact files and upstream/downstream callers.
- **When to skip Graphify**: For single-file edits, syntax/lint fixes, or errors where the stack trace already provides the exact file and line number, inspect the target file directly without running graph queries.
