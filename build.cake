#tool "nuget:?package=GitVersion.CommandLine"
#tool "nuget:?package=gitlink"

var sln = new FilePath("DownloadManager.sln");
var outputDir = new DirectoryPath("artifacts");
var nuspecDir = new DirectoryPath("nuspec");
var target = Argument("target", "Default");

var local = BuildSystem.IsLocalBuild;
var isDevelopBranch = StringComparer.OrdinalIgnoreCase.Equals("develop", AppVeyor.Environment.Repository.Branch);
var isReleaseBranch = StringComparer.OrdinalIgnoreCase.Equals("master", AppVeyor.Environment.Repository.Branch);
var isTagged = AppVeyor.Environment.Repository.Tag.IsTag;

var isRunningOnAppVeyor = AppVeyor.IsRunningOnAppVeyor;
var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;
var isRepository = StringComparer.OrdinalIgnoreCase.Equals("SimonSimCity/Xamarin-CrossDownloadManager", AppVeyor.Environment.Repository.Name);

Task("Clean").Does(() =>
{
    CleanDirectories("./**/bin");
    CleanDirectories("./**/obj");
	CleanDirectories(outputDir.FullPath);
});

GitVersion versionInfo = null;
Task("Version").Does(() => {
	GitVersion(new GitVersionSettings {
		UpdateAssemblyInfo = true,
		OutputType = GitVersionOutput.BuildServer
	});

	versionInfo = GitVersion(new GitVersionSettings{ OutputType = GitVersionOutput.Json });
	Information("VI:\t{0}", versionInfo.FullSemVer);
});

Task("UpdateAppVeyorBuildNumber")
	.IsDependentOn("Version")
    .WithCriteria(() => isRunningOnAppVeyor)
    .Does(() =>
{
    AppVeyor.UpdateBuildVersion(versionInfo.FullBuildMetaData);
});

Task("Restore").Does(() => {
	NuGetRestore(sln);
});

Task("Build")
	.IsDependentOn("Clean")
	.IsDependentOn("UpdateAppVeyorBuildNumber")
	.IsDependentOn("Restore")
	.Does(() =>  {
	
	DotNetBuild(sln, 
		settings => settings.SetConfiguration("Release")
							.WithProperty("DebugSymbols", "true")
            				.WithProperty("DebugType", "Full")
							.WithTarget("Build"));
});

Task("GitLink")
	.IsDependentOn("Build")
	//pdbstr.exe and costura are not xplat currently
	.WithCriteria(() => IsRunningOnWindows())
	.Does(() => 
{
	GitLink(sln.GetDirectory(), 
		new GitLinkSettings {
			RepositoryUrl = "https://github.com/SimonSimCity/Xamarin-CrossDownloadManager",
			ArgumentCustomization = args => args.Append("-ignore DownloadExample,DownloadExample.iOS,DownloadExample.Droid")
		});
});

Task("Package")
	.IsDependentOn("GitLink")
	.Does(() => 
{
	var nugetSettings = new NuGetPackSettings {
		Authors = new [] { "SimonSimCity" },
		Owners = new [] { "SimonSimCity" },
		IconUrl = new Uri(""),
		ProjectUrl = new Uri("https://github.com/SimonSimCity/Xamarin-CrossDownloadManager"),
		LicenseUrl = new Uri("https://github.com/SimonSimCity/Xamarin-CrossDownloadManager/blob/master/LICENSE.md"),
		Copyright = "Copyright (c) SimonSimCity",
		RequireLicenseAcceptance = false,
		Version = versionInfo.NuGetVersion,
		Symbols = false,
		NoPackageAnalysis = true,
		OutputDirectory = outputDir,
		Verbosity = NuGetVerbosity.Detailed,
		BasePath = "./nuspec"
	};

	EnsureDirectoryExists(outputDir);

	var nuspecs = new List<string> {
		"Xam.Plugins.DownloadManager.nuspec"
	};

	foreach(var nuspec in nuspecs)
	{
		NuGetPack(nuspecDir + "/" + nuspec, nugetSettings);
	}
});

Task("PublishPackages")
    .IsDependentOn("Package")
    .WithCriteria(() => !local)
    .WithCriteria(() => !isPullRequest)
    .WithCriteria(() => isRepository)
    .WithCriteria(() => isDevelopBranch || isReleaseBranch)
    .Does (() =>
{
	if (isReleaseBranch && !isTagged)
    {
        Information("Packages will not be published as this release has not been tagged.");
        return;
    }

	// Resolve the API key.
    var apiKey = EnvironmentVariable("NUGET_APIKEY");
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new Exception("The NUGET_APIKEY environment variable is not defined.");
    }

    var source = EnvironmentVariable("NUGET_SOURCE");
    if (string.IsNullOrEmpty(source))
    {
        throw new Exception("The NUGET_SOURCE environment variable is not defined.");
    }

	var nugetFiles = GetFiles(outputDir + "/*.nupkg");

	foreach(var nugetFile in nugetFiles)
	{
    	NuGetPush(nugetFile, new NuGetPushSettings {
            Source = source,
            ApiKey = apiKey
        });
	}
});


Task("Default")
	.IsDependentOn("PublishPackages")
	.Does(() => {
	
	});

RunTarget(target);