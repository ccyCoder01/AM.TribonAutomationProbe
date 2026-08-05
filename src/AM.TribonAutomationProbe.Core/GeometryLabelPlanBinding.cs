using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AM.TribonAutomationProbe.Core;

public static class GeometryLabelPlanBinding
{
    public const string ContractVersion = "geometry-label-plan/v1";

    public static GeometryLabelPreflightResult Attach(
        GeometryLabelPreflightResult value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var readyOperationIds = value.Items
            .Where(x => string.Equals(
                x.Decision,
                "READY_TO_CREATE",
                StringComparison.Ordinal))
            .Select(x => x.OperationId)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return value with
        {
            PlanHash = ComputeHash(value),
            ReadyOperationIds = readyOperationIds
        };
    }

    public static void ValidateRawPlanHash(
        GeometryLabelPreflightResult value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsSha256(value.PlanHash))
        {
            throw PlanHashMismatch("The Vitesse planHash is not a 64-character SHA-256 value.");
        }

        var recomputed = ComputeHash(value);
        if (!string.Equals(value.PlanHash, recomputed, StringComparison.Ordinal))
        {
            throw PlanHashMismatch("The Vitesse planHash does not match the recomputed plan hash.");
        }
    }

    public static string ComputeHash(
        GeometryLabelPreflightResult value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(value.Items);

        var canonical = new StringBuilder();
        AppendField(canonical, ContractVersion);
        AppendField(canonical, value.Status);
        AppendField(
            canonical,
            value.PreAlreadyPresentCount.ToString(
                CultureInfo.InvariantCulture));
        AppendField(
            canonical,
            value.PreMissingCount.ToString(
                CultureInfo.InvariantCulture));
        AppendField(
            canonical,
            value.PreDuplicateTextCount.ToString(
                CultureInfo.InvariantCulture));
        AppendField(
            canonical,
            value.PreTextConflictCount.ToString(
                CultureInfo.InvariantCulture));
        AppendField(
            canonical,
            value.PreInspectionErrorCount.ToString(
                CultureInfo.InvariantCulture));

        foreach (var item in value.Items
                     .OrderBy(x => x.OperationId, StringComparer.Ordinal)
                     .ThenBy(x => x.StableObjectId, StringComparer.Ordinal))
        {
            AppendField(canonical, item.OperationId);
            AppendField(canonical, item.SourceObjectId);
            AppendField(canonical, item.StableObjectId);
            AppendField(canonical, item.ExpectedText);
            AppendField(
                canonical,
                item.MatchCount.ToString(
                    CultureInfo.InvariantCulture));
            AppendField(canonical, item.Decision);
            AppendField(canonical, item.MatchHandle ?? string.Empty);
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    canonical.ToString())));
    }

    public static void ValidateAuthorization(
        GeometryLabelApplyMissingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.AllowWrite)
        {
            throw Invalid(
                "allowWrite must be true.");
        }

        if (!request.WriteConfirmed)
        {
            throw Invalid(
                "writeConfirmed must be true.");
        }

        if (string.IsNullOrWhiteSpace(
                request.ConfirmedPreflightOperationId))
        {
            throw Invalid(
                "confirmedPreflightOperationId is required.");
        }

        if (!IsSha256(
                request.ConfirmedPlanHash))
        {
            throw Invalid(
                "confirmedPlanHash must be a 64-character SHA-256 value.");
        }

        var operationIds =
            request.ConfirmedOperationIds ??
            Array.Empty<string>();

        if (operationIds.Count == 0)
        {
            throw Invalid(
                "confirmedOperationIds must contain at least one operation.");
        }

        if (operationIds.Any(
                x => string.IsNullOrWhiteSpace(x)))
        {
            throw Invalid(
                "confirmedOperationIds cannot contain blank values.");
        }

        if (operationIds
            .Distinct(StringComparer.Ordinal)
            .Count() != operationIds.Count)
        {
            throw Invalid(
                "confirmedOperationIds cannot contain duplicates.");
        }
    }

    public static void ValidateAgainstPreflight(
        GeometryLabelApplyMissingRequest request,
        GeometryLabelPreflightResult currentPreflight)
    {
        ValidateAuthorization(request);
        ArgumentNullException.ThrowIfNull(currentPreflight);

        if (!string.Equals(
                request.ConfirmedPreflightOperationId,
                currentPreflight.OperationId,
                StringComparison.Ordinal))
        {
            throw PlanChanged(
                "The confirmed preflight operation does not match the verification preflight.");
        }

        var attached = Attach(currentPreflight);

        if (!string.Equals(
                request.ConfirmedPlanHash,
                attached.PlanHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw PlanChanged(
                "The current label plan hash differs from the confirmed plan.");
        }

        var confirmed = request.ConfirmedOperationIds!
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var current = (
                attached.ReadyOperationIds ??
                Array.Empty<string>())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (!confirmed.SequenceEqual(
                current,
                StringComparer.Ordinal))
        {
            throw PlanChanged(
                "The current missing-label operation set differs from the confirmed set.");
        }
    }

    private static void AppendField(
        StringBuilder builder,
        string? value)
    {
        value ??= string.Empty;
        builder.Append(
            Encoding.UTF8.GetByteCount(value)
                .ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }

    private static bool IsSha256(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 64)
        {
            return false;
        }

        return value.All(
            x =>
                (x >= '0' && x <= '9') ||
                (x >= 'a' && x <= 'f') ||
                (x >= 'A' && x <= 'F'));
    }

    private static ProbeException Invalid(
        string message) =>
        new(
            ProbeErrorCodes.InvalidMessage,
            message,
            "validation");

    private static ProbeException PlanChanged(
        string message) =>
        new(
            ProbeErrorCodes.VerificationFailed,
            message,
            "safety");

    private static ProbeException PlanHashMismatch(
        string message) =>
        new(
            "GEOMETRY_LABEL_PLAN_HASH_MISMATCH",
            message,
            "safety");
}
