#addin "nuget:?package=Cake.SemVer&version=1.0.6"
#tool "nuget:?package=ILRepack&version=2.0.12"
#tool "nuget:?package=NUnit.Runners&version=2.6.4"

using System.Text.RegularExpressions;
using Semver;

var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Debug");
var solution = Argument<string>("solution", "CKAN.sln");

var rootDirectory = Context.Environment.WorkingDirectory;
var buildDirectory = rootDirectory.Combine(".build");
var outDirectory = buildDirectory.Combine("out");
var repackDirectory = buildDirectory.Combine("repack");
var ckanFile = repackDirectory.Combine(configuration).CombineWithFilePath("ckan.exe");
var netkanFile = repackDirectory.Combine(configuration).CombineWithFilePath("netkan.exe");

Task("Default")
    .IsDependentOn("Ckan")
    .IsDependentOn("Netkan");

Task("Ckan")
    .IsDependentOn("Repack-Ckan");

Task("Netkan")
    .IsDependentOn("Repack-Netkan");

Task("Restore-Nuget")
    .Does(() =>
{
    NuGetRestore(solution, new NuGetRestoreSettings
    {
        ConfigFile = "nuget.config"
    });
});

Task("Build-DotNet")
    .IsDependentOn("Restore-Nuget")
    .IsDependentOn("Generate-GlobalAssemblyVersionInfo")
    .Does(() =>
{
    DotNetBuild(solution, settings =>
    {
        settings.Configuration = configuration;
    });
});

Task("Generate-GlobalAssemblyVersionInfo")
    .Does(() =>
{
    var version = GetVersion();
    var versionStr2 = string.Format("{0}.{1}", version.Major, version.Minor);
    var versionStr3 = string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Patch);

    var metaDirectory = buildDirectory.Combine("meta");

    CreateDirectory(metaDirectory);
    
    CreateAssemblyInfo(metaDirectory.CombineWithFilePath("GlobalAssemblyVersionInfo.cs"), new AssemblyInfoSettings
    {
        Version = versionStr2,
        FileVersion = versionStr3,
        InformationalVersion = version.ToString()
    });
});

Task("Repack-Ckan")
    .IsDependentOn("Build-DotNet")
    .Does(() =>
{
    var cmdLineBinDirectory = outDirectory.Combine("CmdLine").Combine(configuration).Combine("bin");
    var assemblyPaths = GetFiles(string.Format("{0}/*.dll", cmdLineBinDirectory));
    assemblyPaths.Add(cmdLineBinDirectory.CombineWithFilePath("CKAN-GUI.exe"));

    ILRepack(ckanFile, cmdLineBinDirectory.CombineWithFilePath("CmdLine.exe"), assemblyPaths,
        new ILRepackSettings
        {
            Libs = new List<FilePath> { cmdLineBinDirectory.ToString() },
            TargetPlatform = TargetPlatformVersion.v4
        }
    );
});

Task("Repack-Netkan")
    .IsDependentOn("Build-DotNet")
    .Does(() =>
{
    var netkanBinDirectory = outDirectory.Combine("NetKAN").Combine(configuration).Combine("bin");
    var assemblyPaths = GetFiles(string.Format("{0}/*.dll", netkanBinDirectory));

    ILRepack(netkanFile, netkanBinDirectory.CombineWithFilePath("NetKAN.exe"), assemblyPaths,
        new ILRepackSettings
        {
            Libs = new List<FilePath> { netkanBinDirectory.ToString() },
        }
    );
});

Task("Test")
    .IsDependentOn("Default")
    .IsDependentOn("Test+Only");

Task("Test+Only")
    .IsDependentOn("Test-UnitTests+Only")
    .IsDependentOn("Test-Executables+Only");

Task("Test-UnitTests+Only")
    .Does(() =>
{
    var exclude = Argument<string>("exclude", null);

    var testFile = outDirectory
        .Combine("CKAN.Tests")
        .Combine(configuration)
        .Combine("bin")
        .CombineWithFilePath("CKAN.Tests.dll");

    if (!FileExists(testFile))
        throw new Exception("Test assembly not found: " + testFile);

    var nunitOutputDirectory = buildDirectory.Combine("test/nunit");

    CreateDirectory(nunitOutputDirectory);

    NUnit(testFile.FullPath, new NUnitSettings {
        Exclude = exclude,
        ResultsFile = nunitOutputDirectory.CombineWithFilePath("TestResult.xml")
    });
});

Task("Test-Executables+Only")
    .IsDependentOn("Test-CkanExecutable+Only")
    .IsDependentOn("Test-NetkanExecutable+Only");

Task("Test-CkanExecutable+Only")
    .Does(() =>
{
    if (RunExecutable(ckanFile, "version").FirstOrDefault() != string.Format("v{0}", GetVersion()))
        throw new Exception("ckan.exe smoke test failed.");
});

Task("Test-NetkanExecutable+Only")
    .Does(() =>
{
    if (RunExecutable(netkanFile, "--version").FirstOrDefault() != string.Format("v{0}", GetVersion()))
        throw new Exception("netkan.exe smoke test failed.");
});

Task("Version")
    .Does(() =>
{
    Information(GetVersion().ToString());
});

RunTarget(target);

private SemVersion GetVersion()
{
    var pattern = new Regex(@"^\s*##\s+v(?<version>\S+)\s*$");
    var rootDirectory = Context.Environment.WorkingDirectory;

    var versionMatch = System.IO.File
        .ReadAllLines(rootDirectory.CombineWithFilePath("CHANGELOG.md").FullPath)
        .Select(i => pattern.Match(i))
        .FirstOrDefault(i => i.Success);

    var version = ParseSemVer(versionMatch.Groups["version"].Value);

    if (DirectoryExists(rootDirectory.Combine(".git")))
    {
        var hash = GetGitCommitHash();

        version = CreateSemVer(
            version.Major,
            version.Minor,
            version.Patch,
            version.Prerelease,
            hash == null ? null : hash.Substring(0, 12)
        );
    }

    return version;
}

private string GetGitCommitHash()
{
    IEnumerable<string> output;
    try
    {
        var exitCode = StartProcess(
            "git",
            new ProcessSettings { Arguments = "rev-parse HEAD", RedirectStandardOutput = true },
            out output
        );

        return exitCode == 0 ? output.FirstOrDefault() : null;
    }
    catch(Exception)
    {
        return null;
    }
}

private IEnumerable<string> RunExecutable(FilePath executable, string arguments)
{
    IEnumerable<string> output;
    var exitCode = StartProcess(
        executable,
        new ProcessSettings { Arguments = arguments, RedirectStandardOutput = true },
        out output
    );

    if (exitCode == 0)
        return output;
    else
        throw new Exception("Process failed with exit code: " + exitCode);
}
