using System.Reflection;
using System.Xml.Linq;

namespace SystemResourceMonitor.Core.Tests;

/// <summary>
/// Enforces the Core/Desktop boundary from ADR 0002 by reading the solution and project files.
/// </summary>
public sealed class ArchitectureTests
{
    private const string CoreProject = "SystemResourceMonitor.Core";
    private const string DesktopProject = "SystemResourceMonitor.Desktop";

    // Guards against bans being dropped from BannedSymbols.txt; the analyzer itself enforces the file.
    private static readonly string[] BannedSymbolIds =
    [
        "T:System.IO.File",
        "T:System.IO.Directory",
        "T:System.Diagnostics.Process",
        "T:Microsoft.Win32.Registry",
        "T:System.Runtime.InteropServices.DllImportAttribute",
        "T:System.Runtime.InteropServices.LibraryImportAttribute",
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, ProjectFile>> Projects = new(LoadProjects);

    [Fact]
    public void Solution_contains_exactly_the_planned_projects()
    {
        Assert.Equal(
            [
                CoreProject,
                "SystemResourceMonitor.Core.Tests",
                DesktopProject,
                "SystemResourceMonitor.Desktop.TestHost",
                "SystemResourceMonitor.Desktop.Tests",
            ],
            Projects.Value.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_project_reference_points_to_a_solution_project()
    {
        Assert.All(
            Projects.Value.Values.SelectMany(project => project.References),
            reference => Assert.Contains(reference, Projects.Value.Keys));
    }

    [Fact]
    public void Core_does_not_reference_Desktop_directly_or_transitively()
    {
        Assert.DoesNotContain(DesktopProject, ReachableFrom(CoreProject));
    }

    [Fact]
    public void Desktop_references_Core()
    {
        Assert.Contains(CoreProject, Projects.Value[DesktopProject].References);
    }

    [Fact]
    public void Core_targets_plain_net10_without_Windows_UI_frameworks()
    {
        var core = Projects.Value[CoreProject];

        Assert.Equal("net10.0", core.Property("TargetFramework"));
        Assert.Null(core.Property("TargetFrameworks"));
        Assert.NotEqual("true", core.Property("UseWPF")?.ToLowerInvariant());
        Assert.NotEqual("true", core.Property("UseWindowsForms")?.ToLowerInvariant());
    }

    [Fact]
    public void Built_Core_assembly_references_no_Desktop_or_Windows_UI_assembly()
    {
        // Checks the evaluated build output, so settings inherited from imported props are covered too.
        string[] forbidden = [DesktopProject, "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms"];

        var referenced = Assembly.Load(CoreProject).GetReferencedAssemblies().Select(name => name.Name);

        Assert.Empty(referenced.Intersect(forbidden, StringComparer.Ordinal));
    }

    [Fact]
    public void Core_enforces_banned_apis_as_errors()
    {
        var core = Projects.Value[CoreProject];

        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", core.Items("PackageReference"));
        Assert.Contains("BannedSymbols.txt", core.Items("AdditionalFiles"));
        Assert.Contains("RS0030", (core.Property("WarningsAsErrors") ?? string.Empty).Split(';', StringSplitOptions.TrimEntries));

        var declaredIds = File.ReadLines(Path.Combine(core.Directory, "BannedSymbols.txt"))
            .Select(line => line.Split(';')[0].Trim())
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(BannedSymbolIds, id => Assert.Contains(id, declaredIds));
    }

    private sealed record ProjectFile(string Directory, XDocument Document, IReadOnlyList<string> References)
    {
        public string? Property(string name) =>
            Document.Descendants("PropertyGroup").Elements(name).LastOrDefault()?.Value.Trim();

        public IReadOnlyList<string> Items(string itemType) =>
            Document.Descendants(itemType).Select(item => item.Attribute("Include")?.Value).OfType<string>().ToList();
    }

    private static IReadOnlySet<string> ReachableFrom(string projectName)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(Projects.Value[projectName].References);
        while (pending.TryPop(out var name))
        {
            if (reachable.Add(name) && Projects.Value.TryGetValue(name, out var project))
            {
                foreach (var reference in project.References)
                {
                    pending.Push(reference);
                }
            }
        }

        return reachable;
    }

    private static IReadOnlyDictionary<string, ProjectFile> LoadProjects()
    {
        var root = FindRepositoryRoot();

        return XDocument.Load(Path.Combine(root, "SystemResourceMonitor.slnx"))
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value
                ?? throw new InvalidDataException("Solution project without Path."))
            .Select(path => Path.GetFullPath(Path.Combine(root, path)))
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), LoadProject, StringComparer.Ordinal);
    }

    private static ProjectFile LoadProject(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var references = document.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => Path.GetFileNameWithoutExtension(include))
            .ToList();

        return new ProjectFile(Path.GetDirectoryName(projectPath)!, document, references);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SystemResourceMonitor.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("SystemResourceMonitor.slnx was not found above the test output directory.");
    }
}
