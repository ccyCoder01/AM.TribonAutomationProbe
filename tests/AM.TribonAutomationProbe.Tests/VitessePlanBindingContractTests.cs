using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class VitessePlanBindingContractTests
{
    [Fact]
    public void WorkerValidatesBindingBeforeFirstLabelWrite()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(
            Path.Combine(
                root,
                "vitesse",
                "AddIns",
                "AMGeometryObjectAutomation",
                "Start.py"));

        var applyStart = worker.IndexOf(
            "def _apply_missing(",
            StringComparison.Ordinal);
        var validation = worker.IndexOf(
            "PLAN_BINDING.validate_against_preflight(",
            applyStart,
            StringComparison.Ordinal);
        var firstWrite = worker.IndexOf(
            "runtime_handle = _create_label(",
            applyStart,
            StringComparison.Ordinal);

        Assert.True(applyStart >= 0);
        Assert.True(validation > applyStart);
        Assert.True(firstWrite > validation);
        Assert.Contains(
            "request_text",
            worker[applyStart..firstWrite],
            StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerPreflightReturnsPlanBinding()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(
            Path.Combine(
                root,
                "vitesse",
                "AddIns",
                "AMGeometryObjectAutomation",
                "Start.py"));

        Assert.Contains(
            "\"planHash\": preflight[\"planHash\"]",
            worker,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"readyOperationIds\":",
            worker,
            StringComparison.Ordinal);
        Assert.Contains(
            "PLAN_BINDING.attach_plan_binding(result)",
            worker,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerIsSingleFileAtRuntimeAndContainsDiagnostics()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(Path.Combine(root, "vitesse", "AddIns", "AMGeometryObjectAutomation", "Start.py"));
        Assert.DoesNotContain("geometry_label_plan_binding.py", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("imp.load_source(\"am_geometry_label_plan_binding", worker, StringComparison.Ordinal);
        Assert.Contains("BEGIN INLINE GEOMETRY LABEL PLAN BINDING", worker, StringComparison.Ordinal);
        Assert.Contains("F2B14D4200E1AC239FBF1CFD28D2F994", worker, StringComparison.Ordinal);
        Assert.Contains("_resolve_addin_root", worker, StringComparison.Ordinal);
        Assert.Contains("_write_worker_diagnostic(\"DIRECT_ENTRY\", \"STARTED\"", worker, StringComparison.Ordinal);
        Assert.Contains("_write_worker_diagnostic(\"DIRECT_ENTRY\", \"NO_REQUEST\"", worker, StringComparison.Ordinal);
        Assert.Contains("_write_worker_diagnostic(\"DIRECT_ENTRY\", \"FAILED\"", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("import json", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("json.loads", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("bytes", worker, StringComparison.Ordinal);
        Assert.Contains("def _bootstrap_status(", worker, StringComparison.Ordinal);
        Assert.Contains("def _string_array_field(", worker, StringComparison.Ordinal);
        Assert.Contains("def _sha256_fallback(", worker, StringComparison.Ordinal);
        Assert.Contains("SHA256_EMPTY_EXPECTED", worker, StringComparison.Ordinal);
        Assert.Contains("SHA256_ABC_EXPECTED", worker, StringComparison.Ordinal);
        Assert.True(worker.IndexOf("STATUS=MODULE_STARTED", StringComparison.Ordinal) < worker.IndexOf("import kcs_draft", StringComparison.Ordinal));
        Assert.True(worker.IndexOf("PLAN_BINDING = _InlinePlanBinding()", StringComparison.Ordinal) < worker.IndexOf("def _resolve_addin_root", StringComparison.Ordinal));
    }

    [Fact]
    public void PythonBindingModuleContainsCrossRuntimeVector()
    {
        var root = FindRepositoryRoot();
        var module = File.ReadAllText(
            Path.Combine(
                root,
                "vitesse",
                "AddIns",
                "AMGeometryObjectAutomation",
                "geometry_label_plan_binding.py"));

        Assert.Contains(
            "geometry-label-plan/v1",
            module,
            StringComparison.Ordinal);
        Assert.Contains(
            "F2B14D4200E1AC239FBF1CFD28D2F994",
            module,
            StringComparison.Ordinal);
        Assert.Contains(
            "39E631EC2D6FA129ECB6A92A841B75F2",
            module,
            StringComparison.Ordinal);
        Assert.Contains(
            "hashlib.sha256",
            module,
            StringComparison.Ordinal);
        Assert.Contains(
            "confirmedOperationIds",
            module,
            StringComparison.Ordinal);
        Assert.Contains(
            "geometry_label_plan_changed",
            module,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(
                    Path.Combine(
                        current.FullName,
                        "vitesse")) &&
                Directory.Exists(
                    Path.Combine(
                        current.FullName,
                        "src")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
