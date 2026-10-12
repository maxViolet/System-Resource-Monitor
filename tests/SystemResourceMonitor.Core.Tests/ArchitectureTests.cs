using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

namespace SystemResourceMonitor.Core.Tests;

/// <summary>
/// Enforces the Core/Desktop boundary from ADR 0002 against the MSBuild evaluation of every solution project,
/// so settings inherited from imported props and targets are checked as well as the project files themselves.
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

    private static readonly string[] EvaluatedProperties =
        ["TargetFramework", "TargetFrameworks", "UseWPF", "UseWindowsForms", "WarningsAsErrors"];

    private static readonly string[] EvaluatedItems = ["ProjectReference", "PackageReference", "AdditionalFiles"];

    private static readonly Lazy<Task<SolutionModel>> Solution = new(SolutionModel.LoadAsync);

    [Fact]
    public async Task Solution_contains_exactly_the_planned_projects()
    {
        var solution = await Solution.Value;

        Assert.Equal(
            [
                CoreProject,
                "SystemResourceMonitor.Core.Tests",
                DesktopProject,
                "SystemResourceMonitor.Desktop.TestHost",
                "SystemResourceMonitor.Desktop.Tests",
            ],
            solution.Projects.Values.Select(project => project.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_project_reference_resolves_to_a_solution_project()
    {
        var solution = await Solution.Value;

        Assert.All(
            solution.Projects.Values.SelectMany(project => project.References),
            reference => Assert.Contains(reference, solution.Projects.Keys));
    }

    [Fact]
    public async Task Core_does_not_reference_Desktop_directly_or_transitively()
    {
        var solution = await Solution.Value;

        Assert.DoesNotContain(solution.PathOf(DesktopProject), solution.ReachableFrom(solution.PathOf(CoreProject)));
    }

    [Fact]
    public async Task Desktop_references_Core()
    {
        var solution = await Solution.Value;

        Assert.Contains(solution.PathOf(CoreProject), solution.Projects[solution.PathOf(DesktopProject)].References);
    }

    [Fact]
    public async Task Core_targets_plain_net10_without_Windows_UI_frameworks()
    {
        var core = (await Solution.Value).Project(CoreProject);

        Assert.Equal("net10.0", core.Property("TargetFramework"));
        Assert.Empty(core.Property("TargetFrameworks"));
        Assert.NotEqual("true", core.Property("UseWPF"), StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("true", core.Property("UseWindowsForms"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Built_Core_assembly_references_no_Desktop_or_Windows_UI_assembly()
    {
        string[] forbidden = [DesktopProject, "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms"];

        var referenced = Assembly.Load(CoreProject).GetReferencedAssemblies().Select(name => name.Name);

        Assert.Empty(referenced.Intersect(forbidden, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Core_enforces_banned_apis_as_errors()
    {
        var core = (await Solution.Value).Project(CoreProject);
        var bannedSymbolsPath = Path.Combine(Path.GetDirectoryName(core.Path)!, "BannedSymbols.txt");

        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", core.Items("PackageReference").Select(item => item.Identity));
        Assert.Contains(bannedSymbolsPath, core.Items("AdditionalFiles").Select(item => item.FullPath), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("RS0030", core.Property("WarningsAsErrors").Split(';', StringSplitOptions.TrimEntries));

        var declaredIds = File.ReadLines(bannedSymbolsPath)
            .Select(line => line.Split(';')[0].Trim())
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(BannedSymbolIds, id => Assert.Contains(id, declaredIds));
    }

    private sealed record EvaluatedItem(string Identity, string FullPath);

    private sealed record EvaluatedProject(
        string Path,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyDictionary<string, IReadOnlyList<EvaluatedItem>> ItemsByType)
    {
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        public IReadOnlyList<string> References =>
            Items("ProjectReference").Select(item => item.FullPath).ToList();

        public string Property(string name) => Properties.GetValueOrDefault(name, string.Empty);

        public IReadOnlyList<EvaluatedItem> Items(string itemType) => ItemsByType.GetValueOrDefault(itemType, []);
    }

    private sealed record SolutionModel(IReadOnlyDictionary<string, EvaluatedProject> Projects)
    {
        public string PathOf(string name) => Projects.Values.Single(project => project.Name == name).Path;

        public EvaluatedProject Project(string name) => Projects[PathOf(name)];

        public IReadOnlySet<string> ReachableFrom(string projectPath)
        {
            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>(Projects[projectPath].References);
            while (pending.TryPop(out var path))
            {
                if (reachable.Add(path) && Projects.TryGetValue(path, out var project))
                {
                    foreach (var reference in project.References)
                    {
                        pending.Push(reference);
                    }
                }
            }

            return reachable;
        }

        public static async Task<SolutionModel> LoadAsync()
        {
            var root = FindRepositoryRoot();
            var paths = XDocument.Load(Path.Combine(root, "SystemResourceMonitor.slnx"))
                .Descendants("Project")
                .Select(element => element.Attribute("Path")?.Value
                    ?? throw new InvalidDataException("Solution project without Path."))
                .Select(path => Path.GetFullPath(Path.Combine(root, path)));

            var projects = await Task.WhenAll(paths.Select(path => EvaluateAsync(root, path)));

            return new SolutionModel(projects.ToDictionary(project => project.Path, StringComparer.OrdinalIgnoreCase));
        }

        private static async Task<EvaluatedProject> EvaluateAsync(string root, string projectPath)
        {
            var startInfo = new ProcessStartInfo(FindDotnet())
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("-nologo");
            foreach (var property in EvaluatedProperties)
            {
                startInfo.ArgumentList.Add($"-getProperty:{property}");
            }

            foreach (var item in EvaluatedItems)
            {
                startInfo.ArgumentList.Add($"-getItem:{item}");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet msbuild did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"dotnet msbuild failed for {projectPath} with {process.ExitCode}: {await output}{await error}");
            }

            using var json = JsonDocument.Parse(await output);
            var properties = json.RootElement.GetProperty("Properties").EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty);
            var items = json.RootElement.GetProperty("Items").EnumerateObject()
                .ToDictionary(
                    itemType => itemType.Name,
                    itemType => (IReadOnlyList<EvaluatedItem>)itemType.Value.EnumerateArray()
                        .Select(item => new EvaluatedItem(
                            item.GetProperty("Identity").GetString()!,
                            item.GetProperty("FullPath").GetString()!))
                        .ToList());

            return new EvaluatedProject(projectPath, properties, items);
        }

        // The test process runs on the same dotnet installation that hosts the SDK: <root>/shared/Microsoft.NETCore.App/<version>/.
        private static string FindDotnet() =>
            Path.GetFullPath(Path.Combine(
                RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

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
}
