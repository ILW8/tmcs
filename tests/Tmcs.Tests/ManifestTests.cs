using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Tmcs.Tests;

public class ManifestTests
{
    private static readonly XNamespace Asm1 = "urn:schemas-microsoft-com:asm.v1";
    private static readonly XNamespace Asm3 = "urn:schemas-microsoft-com:asm.v3";

    private static XDocument Load(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tmcs.slnx"))) dir = dir.Parent;
        if (dir is null)
            Assert.Fail($"No tmcs.slnx in {AppContext.BaseDirectory} or any folder above it. The manifests are found from the test " +
                        "output, so a --artifacts-path must stay inside the repository, for example artifacts/verify.");
        return XDocument.Load(Path.Combine(dir.FullName, "src", "Tmcs", fileName));
    }

    [Theory]
    [InlineData("app.manifest", "requireAdministrator")]
    [InlineData("app.debug.manifest", "asInvoker")]
    public void Manifest_keeps_its_execution_level(string fileName, string level) =>
        Assert.Equal(level, Load(fileName).Descendants(Asm3 + "requestedExecutionLevel").Single().Attribute("level")?.Value);

    // The native image lists (ImageList_Create) must come from the comctl32 that WinForms ListViews use, which is v6 only when the manifest asks for it.
    [Theory]
    [InlineData("app.manifest")]
    [InlineData("app.debug.manifest")]
    public void Manifest_depends_on_common_controls_6(string fileName)
    {
        var dependency = Load(fileName).Root!.Elements(Asm1 + "dependency")
            .SelectMany(d => d.Elements(Asm1 + "dependentAssembly"))
            .SelectMany(a => a.Elements(Asm1 + "assemblyIdentity"))
            .Single(i => (string?)i.Attribute("name") == "Microsoft.Windows.Common-Controls");

        Assert.Equal("6.0.0.0", (string?)dependency.Attribute("version"));
        Assert.Equal("win32", (string?)dependency.Attribute("type"));
        Assert.Equal("6595b64144ccf1df", (string?)dependency.Attribute("publicKeyToken"));
    }

    // tmcs runs elevated: its P/Invokes must not load a DLL planted in the exe's folder.
    [Fact]
    public void Native_libraries_load_only_from_System32() =>
        Assert.Equal(DllImportSearchPath.System32, typeof(MainForm).Assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths);

    // tmcs runs elevated with the user's environment: DOTNET_STARTUP_HOOKS must not load code into it.
    [Fact]
    public void Startup_hooks_are_disabled() =>
        Assert.Equal("false", Load("Tmcs.csproj").Descendants("StartupHookSupport").Single().Value);
}
