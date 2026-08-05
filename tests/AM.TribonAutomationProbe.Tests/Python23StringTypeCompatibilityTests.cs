using System;
using System.IO;
using Xunit;

namespace AM.TribonAutomationProbe.Tests
{
    public sealed class Python23StringTypeCompatibilityTests
    {
        [Fact]
        public void VitesseWorkerAcceptsBothStrAndUnicodeForBindingStrings()
        {
            var root = FindRepositoryRoot();
            var startPath = Path.Combine(
                root,
                "vitesse",
                "AddIns",
                "AMGeometryObjectAutomation",
                "Start.py");

            var text = File.ReadAllText(startPath);

            Assert.Contains(
                "STRING_TYPES = (str, unicode)",
                text,
                StringComparison.Ordinal);
            Assert.Contains(
                "STRING_TYPES = (str,)",
                text,
                StringComparison.Ordinal);

            Assert.Contains(
                "preflight_operation_id,\r\n            STRING_TYPES",
                text,
                StringComparison.Ordinal);
            Assert.Contains(
                "not isinstance(plan_hash, STRING_TYPES)",
                text,
                StringComparison.Ordinal);
            Assert.Contains(
                "not isinstance(operation_id, STRING_TYPES)",
                text,
                StringComparison.Ordinal);

            Assert.DoesNotContain(
                "preflight_operation_id,\r\n            TEXT_TYPE",
                text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "not isinstance(plan_hash, TEXT_TYPE)",
                text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "not isinstance(operation_id, TEXT_TYPE)",
                text,
                StringComparison.Ordinal);

            Assert.Contains(
                "inline parsed binding string-type ",
                text,
                StringComparison.Ordinal);
            Assert.Contains(
                "_inline_validate_authorization(\r\n        parsed_binding",
                text,
                StringComparison.Ordinal);
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(
                AppContext.BaseDirectory);

            while (current is not null)
            {
                var candidate = Path.Combine(
                    current.FullName,
                    "vitesse",
                    "AddIns",
                    "AMGeometryObjectAutomation",
                    "Start.py");

                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                "Repository root containing Start.py was not found.");
        }
    }
}