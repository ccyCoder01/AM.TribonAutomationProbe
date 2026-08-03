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
        Assert.Contains("ADDIN_ROOT_SOURCE", worker, StringComparison.Ordinal);
        Assert.Contains("_valid_addin_root", worker, StringComparison.Ordinal);
        Assert.Contains("ADDIN_ROOT, ADDIN_ROOT_SOURCE = _resolve_addin_root()", worker, StringComparison.Ordinal);
        Assert.Contains("except SystemExit, error", worker, StringComparison.Ordinal);
        Assert.Contains("_write_failure_result_for_selected", worker, StringComparison.Ordinal);
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
    public void WorkerPrefersCwdDeploymentBeforeFileStagingFallback()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(Path.Combine(root, "vitesse", "AddIns", "AMGeometryObjectAutomation", "Start.py"));
        var cwd = worker.IndexOf("candidates.append((cwd, \"CWD\"))", StringComparison.Ordinal);
        var file = worker.IndexOf("candidates.append((file_root, \"FILE\"))", StringComparison.Ordinal);
        Assert.True(cwd >= 0);
        Assert.True(file > cwd);
        Assert.Contains("return candidate, source", worker, StringComparison.Ordinal);
        Assert.Contains("if _valid_addin_root(candidate):", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerContainsPreflightKcsStageTraceBoundaries()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(Path.Combine(root, "vitesse", "AddIns", "AMGeometryObjectAutomation", "Start.py"));
        Assert.Contains("geometry-object-worker-stage-trace.txt", worker, StringComparison.Ordinal);
        Assert.Contains("open(os.path.join(DIAGNOSTICS, \"geometry-object-worker-stage-trace.txt\"), \"ab\")", worker, StringComparison.Ordinal);
        Assert.Contains("handle.flush()", worker, StringComparison.Ordinal);
        foreach (var marker in new[] { "REQUEST_SELECTED", "TEXT_CAPTURE_START", "TEXT_CAPTURE_RETURNED", "TEXT_PROPERTIES_GET_START", "TEXT_PROPERTIES_GET_RETURNED", "ELEMENT_EXTENT_GET_START", "ELEMENT_EXTENT_GET_RETURNED", "DETECTOR_START", "DETECTOR_RETURNED", "PLAN_READ_START", "PLAN_READ_RETURNED", "TARGET_RESOLVE_START", "TARGET_RESOLVE_RETURNED", "LABEL_INDEX_START", "LABEL_INDEX_RETURNED", "PREFLIGHT_EVALUATE_START", "PREFLIGHT_EVALUATE_RETURNED", "PLAN_BINDING_START", "PLAN_BINDING_RETURNED", "RESULT_WRITE_START", "RESULT_WRITE_RETURNED", "REQUEST_ARCHIVE_START", "REQUEST_ARCHIVE_RETURNED", "PROCESS_SUCCESS", "PROCESS_EXCEPTION", "FAILURE_RESULT_WRITE_START", "FAILURE_RESULT_WRITE_RETURNED", "FAILURE_ARCHIVE_START", "FAILURE_ARCHIVE_RETURNED" })
        {
            Assert.Contains("\"" + marker + "\"", worker, StringComparison.Ordinal);
        }
        Assert.Contains("SetBoundaryInfinite()", worker, StringComparison.Ordinal);
        Assert.Contains("def _create_label(", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("SAVEWORK", worker, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"(?m)^\s*(?:return\s+)?[^#\r\n:]+?\s+if\s+[^:\r\n]+?\s+else\s+[^#\r\n]+$",
            worker);
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
