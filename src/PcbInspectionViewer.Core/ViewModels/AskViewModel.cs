using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcbInspectionViewer.Core.Sql;

namespace PcbInspectionViewer.Core.ViewModels;

public sealed partial class AskViewModel : ObservableObject
{
    private readonly INlToSqlService _nlToSqlService;
    private readonly ISqlGuard _sqlGuard;

    [ObservableProperty]
    private string _question = string.Empty;

    [ObservableProperty]
    private string _generatedSql = string.Empty;

    [ObservableProperty]
    private string _explanation = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready. Enter a question and click 'Generate SQL'.";

    [ObservableProperty]
    private DataTable? _resultsTable;

    [ObservableProperty]
    private int _resultRowCount;

    [ObservableProperty]
    private double _executionTimeMs;

    public AskViewModel(INlToSqlService nlToSqlService, ISqlGuard sqlGuard)
    {
        _nlToSqlService = nlToSqlService ?? throw new ArgumentNullException(nameof(nlToSqlService));
        _sqlGuard = sqlGuard ?? throw new ArgumentNullException(nameof(sqlGuard));
    }

    [RelayCommand]
    public async Task GenerateSqlAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Question) || IsBusy) return;

        IsBusy = true;
        StatusMessage = "Translating question to safe SQL query...";
        try
        {
            NlToSqlResult result = await _nlToSqlService.TranslateQuestionToSqlAsync(Question, ct);
            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.GeneratedSql))
            {
                GeneratedSql = result.GeneratedSql;
                Explanation = result.Explanation ?? string.Empty;
                StatusMessage = "SQL generated. Review the query and click 'Run Query' to execute.";
            }
            else
            {
                StatusMessage = $"Translation error: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RunQueryAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql) || IsBusy) return;

        IsBusy = true;
        StatusMessage = "Executing guarded read-only query...";
        try
        {
            QueryExecutionResult result = await _sqlGuard.ExecuteGuardedQueryAsync(GeneratedSql, null, ct);
            ExecutionTimeMs = Math.Round(result.ExecutionTimeMs, 2);

            if (result.IsSuccess)
            {
                var dt = new DataTable();
                foreach (string col in result.ColumnNames)
                {
                    dt.Columns.Add(col, typeof(object));
                }

                foreach (var row in result.Rows)
                {
                    dt.Rows.Add(row.ToArray());
                }

                ResultsTable = dt;
                ResultRowCount = result.RowCount;
                StatusMessage = $"Query executed successfully: {result.RowCount} rows returned in {ExecutionTimeMs} ms.";
            }
            else
            {
                ResultsTable = null;
                ResultRowCount = 0;
                StatusMessage = $"Execution rejected by SqlGuard: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Execution error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
