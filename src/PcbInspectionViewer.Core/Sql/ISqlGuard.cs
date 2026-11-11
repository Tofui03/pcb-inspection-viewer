namespace PcbInspectionViewer.Core.Sql;

public sealed record SqlValidationResult(
    bool IsAllowed,
    string? SanitizedSql,
    string? RejectionReason);

public sealed record QueryExecutionResult(
    bool IsSuccess,
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int RowCount,
    double ExecutionTimeMs,
    string? ErrorMessage);

public interface ISqlGuard
{
    SqlValidationResult ValidateQuery(string sql, IReadOnlyDictionary<string, object?>? parameters = null);
    Task<QueryExecutionResult> ExecuteGuardedQueryAsync(
        string rawSql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);
}
