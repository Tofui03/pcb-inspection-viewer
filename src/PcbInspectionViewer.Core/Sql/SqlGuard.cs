using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Data.Entities;

namespace PcbInspectionViewer.Core.Sql;

public sealed class SqlGuard : ISqlGuard
{
    private readonly string _dbPath;
    private readonly Func<InspectionDbContext> _auditDbContextFactory;

    private static readonly HashSet<string> AllowedViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "v_inspections",
        "v_defects",
        "v_batches"
    };

    // Disallowed keywords, extensions, and SQLite system catalog tables
    private static readonly Regex DisallowedKeywordsRegex = new(
        @"\b(attach|detach|pragma|insert|update|delete|drop|alter|create|replace|load_extension|reindex|vacuum|transaction|commit|rollback|savepoint|release|sqlite_master|sqlite_schema|sqlite_temp_master|sqlite_temp_schema)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Comment indicators -- or /* */
    private static readonly Regex CommentRegex = new(
        @"(--|/\*|\*/)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ClauseBoundaryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "GROUP", "HAVING", "WINDOW", "ORDER", "LIMIT", "OFFSET",
        "UNION", "INTERSECT", "EXCEPT", "JOIN", "INNER", "LEFT", "RIGHT",
        "FULL", "CROSS", "NATURAL", "OUTER", "ON", "USING", "FOR", "SET",
        "VALUES", "INTO", "RETURNING"
    };

    public SqlGuard(string dbPath, Func<InspectionDbContext> auditDbContextFactory)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            throw new ArgumentException("Database path must be provided", nameof(dbPath));
        }

        _dbPath = dbPath;
        _auditDbContextFactory = auditDbContextFactory ?? throw new ArgumentNullException(nameof(auditDbContextFactory));
    }

    public SqlValidationResult ValidateQuery(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlValidationResult(false, null, "Query cannot be empty.");
        }

        string trimmed = sql.Trim();

        // 1. Reject SQL comments (-- or /* */)
        if (CommentRegex.IsMatch(trimmed))
        {
            return new SqlValidationResult(false, null, "SQL comments are strictly forbidden.");
        }

        // 2. Must start with SELECT or WITH ... SELECT
        bool startsWithSelect = trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
        bool startsWithWith = trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

        if (!startsWithSelect && !startsWithWith)
        {
            return new SqlValidationResult(false, null, "Query must start with SELECT or WITH ... SELECT.");
        }

        if (startsWithWith && !Regex.IsMatch(trimmed, @"\bSELECT\b", RegexOptions.IgnoreCase))
        {
            return new SqlValidationResult(false, null, "WITH CTE must conclude with a SELECT statement.");
        }

        // 3. Single statement only: reject semicolons followed by non-whitespace characters
        int firstSemicolon = trimmed.IndexOf(';');
        if (firstSemicolon >= 0)
        {
            string after = trimmed[(firstSemicolon + 1)..].Trim();
            if (!string.IsNullOrEmpty(after))
            {
                return new SqlValidationResult(false, null, "Multiple SQL statements are strictly forbidden.");
            }
            // Strip trailing semicolon
            trimmed = trimmed[..firstSemicolon].Trim();
        }

        // 4. Reject disallowed keywords (DDL/DML/Extensions/PRAGMA)
        Match keywordMatch = DisallowedKeywordsRegex.Match(trimmed);
        if (keywordMatch.Success)
        {
            return new SqlValidationResult(false, null, $"Disallowed keyword detected: '{keywordMatch.Value.ToUpperInvariant()}'.");
        }

        // 5. Allowlisted views only: verify table/view references
        // If WITH clause is used, collect declared CTE names as allowed references
        var allowedInQuery = new HashSet<string>(AllowedViews, StringComparer.OrdinalIgnoreCase);
        if (startsWithWith)
        {
            var cteMatches = Regex.Matches(trimmed, @"\b(?:with|,)\s+([a-zA-Z0-9_]+)\s+as\b", RegexOptions.IgnoreCase);
            foreach (Match cm in cteMatches)
            {
                allowedInQuery.Add(cm.Groups[1].Value);
            }
        }

        if (!ValidateTableReferences(trimmed, allowedInQuery, out string? tableRejectionReason))
        {
            return new SqlValidationResult(false, null, tableRejectionReason);
        }

        // 6. Parameter limits: max 10, string length <= 100
        if (parameters is not null)
        {
            if (parameters.Count > 10)
            {
                return new SqlValidationResult(false, null, "Exceeded maximum allowed parameters (max 10).");
            }

            foreach (var kvp in parameters)
            {
                if (kvp.Value is string s && s.Length > 100)
                {
                    return new SqlValidationResult(false, null, $"Parameter '{kvp.Key}' exceeds maximum string length (100 characters).");
                }
            }
        }

        // Wrap query inside LIMIT 500
        string sanitizedSql = $"SELECT * FROM ({trimmed}) LIMIT 500";
        return new SqlValidationResult(true, sanitizedSql, null);
    }

    public async Task<QueryExecutionResult> ExecuteGuardedQueryAsync(
        string rawSql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        SqlValidationResult validation = ValidateQuery(rawSql, parameters);

        if (!validation.IsAllowed || string.IsNullOrWhiteSpace(validation.SanitizedSql))
        {
            sw.Stop();
            await AuditLogAsync(rawSql, validation.SanitizedSql ?? string.Empty, isSuccess: false, validation.RejectionReason, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);
            return new QueryExecutionResult(
                IsSuccess: false,
                ColumnNames: Array.Empty<string>(),
                Rows: Array.Empty<IReadOnlyList<object?>>(),
                RowCount: 0,
                ExecutionTimeMs: sw.Elapsed.TotalMilliseconds,
                ErrorMessage: validation.RejectionReason);
        }

        // Open read-only SQLite connection with query_only = ON
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 2 // 2-second timeout
        };

        try
        {
            await using var conn = new SqliteConnection(builder.ConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            // Enforce SQLite engine-level read-only pragma
            await using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA query_only = ON;";
                await pragmaCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = validation.SanitizedSql;
            cmd.CommandTimeout = 2; // 2 seconds execution timeout

            if (parameters is not null)
            {
                foreach (var kvp in parameters)
                {
                    cmd.Parameters.AddWithValue(kvp.Key.StartsWith('@') ? kvp.Key : $"@{kvp.Key}", kvp.Value ?? DBNull.Value);
                }
            }

            // Execute with linked CTS for 2s timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await using var reader = await cmd.ExecuteReaderAsync(linkedCts.Token).ConfigureAwait(false);

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var rows = new List<IReadOnlyList<object?>>();
            while (await reader.ReadAsync(linkedCts.Token).ConfigureAwait(false))
            {
                var row = new object?[reader.FieldCount];
                reader.GetValues(row);
                rows.Add(row);
            }

            sw.Stop();
            await AuditLogAsync(rawSql, validation.SanitizedSql, isSuccess: true, null, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);

            return new QueryExecutionResult(
                IsSuccess: true,
                ColumnNames: columns,
                Rows: rows,
                RowCount: rows.Count,
                ExecutionTimeMs: sw.Elapsed.TotalMilliseconds,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            string error = ex is OperationCanceledException ? "Query execution timed out after 2 seconds." : ex.Message;
            await AuditLogAsync(rawSql, validation.SanitizedSql, isSuccess: false, error, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);

            return new QueryExecutionResult(
                IsSuccess: false,
                ColumnNames: Array.Empty<string>(),
                Rows: Array.Empty<IReadOnlyList<object?>>(),
                RowCount: 0,
                ExecutionTimeMs: sw.Elapsed.TotalMilliseconds,
                ErrorMessage: error);
        }
    }

    private async Task AuditLogAsync(string promptOrRawSql, string executedSql, bool isSuccess, string? errorMessage, double latencyMs)
    {
        try
        {
            await using InspectionDbContext db = _auditDbContextFactory();
            db.QueryLogs.Add(new QueryLogEntity
            {
                UserPrompt = promptOrRawSql,
                GeneratedSql = executedSql,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                ExecutionLatencyMs = Math.Round(latencyMs, 2),
                QueriedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch
        {
            // Logging failure should never take down query pipeline
        }
    }

    private enum SqlTokenKind
    {
        Word,
        QuotedIdentifier,
        StringLiteral,
        Symbol
    }

    private readonly record struct SqlToken(SqlTokenKind Kind, string Value)
    {
        public bool IsKeyword(string keyword) =>
            Kind == SqlTokenKind.Word && Value.Equals(keyword, StringComparison.OrdinalIgnoreCase);

        public bool IsSymbol(string sym) =>
            Kind == SqlTokenKind.Symbol && Value == sym;
    }

    private static bool ValidateTableReferences(string sql, ISet<string> allowedInQuery, out string? rejectionReason)
    {
        List<SqlToken> tokens = TokenizeSql(sql);
        return ValidateTokenRange(tokens, 0, tokens.Count, allowedInQuery, out rejectionReason);
    }

    private static bool ValidateTokenRange(
        IReadOnlyList<SqlToken> tokens,
        int start,
        int end,
        ISet<string> allowedInQuery,
        out string? rejectionReason)
    {
        int i = start;
        while (i < end)
        {
            SqlToken token = tokens[i];
            if (token.IsKeyword("FROM"))
            {
                // Ignore SQLite `IS [NOT] DISTINCT FROM <expr>`
                bool isDistinctFrom = i > start && tokens[i - 1].IsKeyword("DISTINCT");
                if (isDistinctFrom)
                {
                    i++;
                    continue;
                }

                i++;
                if (!ValidateFromList(tokens, ref i, end, allowedInQuery, out rejectionReason))
                {
                    return false;
                }
                continue;
            }

            if (token.IsKeyword("JOIN"))
            {
                i++;
                if (!ValidateTableFactor(tokens, ref i, end, allowedInQuery, out rejectionReason))
                {
                    return false;
                }
                continue;
            }

            i++;
        }

        rejectionReason = null;
        return true;
    }

    private static bool ValidateFromList(
        IReadOnlyList<SqlToken> tokens,
        ref int index,
        int end,
        ISet<string> allowedInQuery,
        out string? rejectionReason)
    {
        while (index < end)
        {
            if (!ValidateTableFactor(tokens, ref index, end, allowedInQuery, out rejectionReason))
            {
                return false;
            }

            if (index < end && tokens[index].IsSymbol(","))
            {
                index++;
                continue;
            }

            break;
        }

        rejectionReason = null;
        return true;
    }

    private static bool ValidateTableFactor(
        IReadOnlyList<SqlToken> tokens,
        ref int index,
        int end,
        ISet<string> allowedInQuery,
        out string? rejectionReason)
    {
        if (index >= end)
        {
            rejectionReason = "Malformed FROM/JOIN clause.";
            return false;
        }

        if (tokens[index].IsSymbol("("))
        {
            int closeIndex = FindMatchingParen(tokens, index, end);
            if (closeIndex < 0)
            {
                rejectionReason = "Unmatched parenthesis in FROM/JOIN clause.";
                return false;
            }

            int innerStart = index + 1;
            if (innerStart < closeIndex && (tokens[innerStart].IsKeyword("SELECT") || tokens[innerStart].IsKeyword("WITH")))
            {
                if (!ValidateTokenRange(tokens, innerStart, closeIndex, allowedInQuery, out rejectionReason))
                {
                    return false;
                }
            }
            else if (innerStart < closeIndex)
            {
                int innerIdx = innerStart;
                if (!ValidateFromList(tokens, ref innerIdx, closeIndex, allowedInQuery, out rejectionReason))
                {
                    return false;
                }
                if (innerIdx < closeIndex && !ValidateTokenRange(tokens, innerIdx, closeIndex, allowedInQuery, out rejectionReason))
                {
                    return false;
                }
            }

            index = closeIndex + 1;
            SkipOptionalAlias(tokens, ref index, end);
            rejectionReason = null;
            return true;
        }

        SqlToken targetToken = tokens[index];
        if (targetToken.Kind != SqlTokenKind.Word && targetToken.Kind != SqlTokenKind.QuotedIdentifier)
        {
            rejectionReason = $"Unexpected token '{targetToken.Value}' in FROM/JOIN clause.";
            return false;
        }

        if (index + 1 < end && tokens[index + 1].IsSymbol("."))
        {
            string qualifiedTarget = index + 2 < end ? $"{targetToken.Value}.{tokens[index + 2].Value}" : targetToken.Value;
            rejectionReason = $"Schema-qualified access to '{qualifiedTarget}' is not allowed. Only allowlisted views ({string.Join(", ", AllowedViews)}) may be queried.";
            return false;
        }

        string tableName = targetToken.Value;
        if (!allowedInQuery.Contains(tableName))
        {
            rejectionReason = $"Access to table or view '{tableName}' is not allowed. Only allowlisted views ({string.Join(", ", AllowedViews)}) may be queried.";
            return false;
        }

        index++;
        SkipOptionalAlias(tokens, ref index, end);
        rejectionReason = null;
        return true;
    }

    private static void SkipOptionalAlias(IReadOnlyList<SqlToken> tokens, ref int index, int end)
    {
        if (index >= end)
        {
            return;
        }

        if (tokens[index].IsKeyword("AS"))
        {
            index++;
            if (index < end && (tokens[index].Kind == SqlTokenKind.Word || tokens[index].Kind == SqlTokenKind.QuotedIdentifier))
            {
                index++;
            }
            return;
        }

        SqlToken next = tokens[index];
        if (next.Kind == SqlTokenKind.QuotedIdentifier ||
            (next.Kind == SqlTokenKind.Word && !ClauseBoundaryKeywords.Contains(next.Value)))
        {
            index++;
        }
    }

    private static int FindMatchingParen(IReadOnlyList<SqlToken> tokens, int openIndex, int end)
    {
        int depth = 0;
        for (int i = openIndex; i < end; i++)
        {
            if (tokens[i].IsSymbol("("))
            {
                depth++;
            }
            else if (tokens[i].IsSymbol(")"))
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static List<SqlToken> TokenizeSql(string sql)
    {
        var tokens = new List<SqlToken>();
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Single-quoted string literal
            if (c == '\'')
            {
                int start = ++i;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'')
                    {
                        break;
                    }
                    i++;
                }
                tokens.Add(new SqlToken(SqlTokenKind.StringLiteral, sql[start..Math.Min(i, sql.Length)]));
                if (i < sql.Length && sql[i] == '\'')
                {
                    i++;
                }
                continue;
            }

            // Quoted identifier ("...", `...`, or [...])
            if (c == '"' || c == '`' || c == '[')
            {
                char closeChar = c == '[' ? ']' : c;
                int start = ++i;
                while (i < sql.Length)
                {
                    if (c != '[' && sql[i] == closeChar && i + 1 < sql.Length && sql[i + 1] == closeChar)
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == closeChar)
                    {
                        break;
                    }
                    i++;
                }
                string rawIdent = sql[start..Math.Min(i, sql.Length)];
                if (c != '[')
                {
                    rawIdent = rawIdent.Replace($"{closeChar}{closeChar}", $"{closeChar}");
                }
                tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, rawIdent));
                if (i < sql.Length && sql[i] == closeChar)
                {
                    i++;
                }
                continue;
            }

            // Unquoted word / identifier
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                {
                    i++;
                }
                tokens.Add(new SqlToken(SqlTokenKind.Word, sql[start..i]));
                continue;
            }

            tokens.Add(new SqlToken(SqlTokenKind.Symbol, c.ToString()));
            i++;
        }

        return tokens;
    }
}
