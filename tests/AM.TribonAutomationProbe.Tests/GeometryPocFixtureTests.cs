using System.Text.Json;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class GeometryPocFixtureTests
{
    private static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "GeometryObjectPoc", name);

    [Fact]
    public void PocFixtureContainsTwelveObjectsAnd113UniqueGeometryHandles()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathOf("geometry-object-snapshot.legacy.json")));
        var root = document.RootElement; var objects = root.GetProperty("objects"); var handles = new HashSet<string>(StringComparer.Ordinal); var categories = new Dictionary<string, int>(StringComparer.Ordinal);
        Assert.Equal(12, objects.GetArrayLength());
        foreach (var item in objects.EnumerateArray()) { var category = item.GetProperty("category").GetString()!; categories[category] = categories.TryGetValue(category, out var count) ? count + 1 : 1; var geometry = item.GetProperty("geometryHandles"); Assert.Equal(item.GetProperty("geometryCount").GetInt32(), geometry.GetArrayLength()); foreach (var handle in geometry.EnumerateArray()) Assert.True(handles.Add(handle.GetString()!)); }
        Assert.Equal(113, handles.Count); Assert.Equal(2, categories["LIFTING_BEAM"]); Assert.Equal(3, categories["LIFTING_LUG"]); Assert.Equal(3, categories["PIPE_FLANGE_FRONT"]); Assert.Equal(1, categories["PIPE_FLANGE_SIDE"]); Assert.Equal(3, categories["STRUCTURAL_FLANGE"]); Assert.Equal(23, root.GetProperty("unassignedContourCount").GetInt32());
    }

}
