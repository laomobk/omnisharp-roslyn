using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MicrosoftBuildLocator = Microsoft.Build.Locator.MSBuildLocator;

namespace OmniSharp.MSBuild.Discovery.Providers
{
    internal class SdkInstanceProvider : MSBuildInstanceProvider
    {
        internal const int MinimumSdkMajorVersion = 8;
        private readonly SdkOptions _options;

        public SdkInstanceProvider(ILoggerFactory loggerFactory, IConfiguration sdkConfiguration)
            : base(loggerFactory)
        {
            _options = sdkConfiguration?.Get<SdkOptions>();
        }

        public override ImmutableArray<MSBuildInstance> GetInstances()
        {
            var includePrerelease = _options?.IncludePrereleases == true;

            SemanticVersion optionsVersion = null;
            if (!string.IsNullOrEmpty(_options?.Version) &&
                !TryParseVersion(_options.Version, out optionsVersion, out var errorMessage))
            {
                Logger.LogError(errorMessage);
                return NoInstances;
            }

            ImmutableArray<MSBuildInstance> instances;
            try
            {
                instances = MicrosoftBuildLocator.QueryVisualStudioInstances()
                    .Where(instance => IncludeSdkInstance(instance.VisualStudioRootPath, optionsVersion, includePrerelease))
                    .OrderByDescending(instance => instance.Version)
                    .Select(CreateInstance)
                    .ToImmutableArray();
            }
            catch (Exception ex) when (IsNativeSdkDiscoveryFailure(ex))
            {
                Logger.LogWarning($"Microsoft.Build.Locator could not enumerate .NET SDKs on this platform ({ex.GetType().Name}). Falling back to managed SDK directory discovery.");
                instances = GetInstancesFromSdkDirectories(optionsVersion, includePrerelease);
            }

            // Some platform-specific Locator builds return no instances instead of throwing.
            if (instances.Length == 0)
            {
                var directoryInstances = GetInstancesFromSdkDirectories(optionsVersion, includePrerelease);
                if (directoryInstances.Length > 0)
                {
                    instances = directoryInstances;
                }
            }

            if (instances.Length == 0)
            {
                if (optionsVersion is null)
                {
                    Logger.LogError($"OmniSharp requires the .NET 8 SDK or higher be installed. Please visit https://dotnet.microsoft.com/download/dotnet/8.0 to download the .NET SDK.");
                }
                else
                {
                    Logger.LogError($"The Sdk version specified in the OmniSharp settings could not be found. Configured version is '{optionsVersion}'. Please update your settings and restart OmniSharp.");
                }

                return NoInstances;
            }

            return instances;
        }

        private MSBuildInstance CreateInstance(VisualStudioInstance instance)
        {
            var microsoftBuildPath = Path.Combine(instance.MSBuildPath, "Microsoft.Build.dll");
            var version = GetMSBuildVersion(microsoftBuildPath);

            return new MSBuildInstance(
                $"{instance.Name} {instance.Version}",
                instance.MSBuildPath,
                version,
                DiscoveryType.DotNetSdk,
                _options?.PropertyOverrides?.ToImmutableDictionary());
        }

        private ImmutableArray<MSBuildInstance> GetInstancesFromSdkDirectories(SemanticVersion targetVersion, bool includePrerelease)
        {
            var instances = new List<(SemanticVersion SdkVersion, MSBuildInstance Instance)>();
            foreach (var sdkPath in GetSdkDirectories())
            {
                if (!IncludeSdkInstance(sdkPath, targetVersion, includePrerelease))
                {
                    continue;
                }

                var microsoftBuildPath = Path.Combine(sdkPath, "MSBuild.dll");
                if (!File.Exists(microsoftBuildPath))
                {
                    Logger.LogDebug($"Skipping .NET SDK at '{sdkPath}' because MSBuild.dll was not found.");
                    continue;
                }

                try
                {
                    TryGetSdkVersion(sdkPath, out var sdkVersion);
                    var instance = new MSBuildInstance(
                        $".NET SDK {sdkVersion}",
                        sdkPath,
                        GetMSBuildVersion(microsoftBuildPath),
                        DiscoveryType.DotNetSdk,
                        _options?.PropertyOverrides?.ToImmutableDictionary());
                    instances.Add((sdkVersion, instance));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    Logger.LogDebug($"Skipping .NET SDK at '{sdkPath}' because its MSBuild version could not be read: {ex.Message}");
                }
            }

            return instances
                .OrderByDescending(item => item.SdkVersion)
                .Select(item => item.Instance)
                .ToImmutableArray();
        }

        internal static ImmutableArray<string> GetSdkDirectories()
        {
            var dotNetRoots = new List<string>
            {
                Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"),
                Environment.GetEnvironmentVariable("DOTNET_ROOT_ARM64"),
                Environment.GetEnvironmentVariable("DOTNET_ROOT_X64")
            };

            var prefix = Environment.GetEnvironmentVariable("PREFIX");
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                dotNetRoots.Add(Path.Combine(prefix, "lib", "dotnet"));
            }

            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                dotNetRoots.Add(processPath);
            }

            return GetSdkDirectories(dotNetRoots);
        }

        internal static ImmutableArray<string> GetSdkDirectories(IEnumerable<string> dotNetRoots)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dotNetRoot in dotNetRoots)
            {
                AddSdkRoot(dotNetRoot);
            }

            var result = new List<string>();
            foreach (var root in roots)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        result.AddRange(Directory.EnumerateDirectories(root));
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();

            void AddSdkRoot(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                var path = value.Trim();
                if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    path = Path.GetDirectoryName(path);
                }

                if (!string.IsNullOrEmpty(path) && !Path.GetFileName(path).Equals("sdk", StringComparison.OrdinalIgnoreCase))
                {
                    path = Path.Combine(path, "sdk");
                }

                if (!string.IsNullOrEmpty(path))
                {
                    roots.Add(Path.GetFullPath(path));
                }
            }
        }

        private static bool IsNativeSdkDiscoveryFailure(Exception exception)
            => exception is MarshalDirectiveException
                || exception is DllNotFoundException
                || exception is EntryPointNotFoundException
                || exception is BadImageFormatException
                || exception is TypeInitializationException && exception.InnerException is not null && IsNativeSdkDiscoveryFailure(exception.InnerException);

        public static bool TryParseVersion(string versionString, out SemanticVersion version, out string errorMessage)
        {
            if (!SemanticVersion.TryParse(versionString, out version))
            {
                errorMessage = $"The Sdk version specified in the OmniSharp settings was not a valid semantic version. Configured version is '{versionString}'. Please update your settings and restart OmniSharp.";
                return false;
            }
            else if (version.Major < MinimumSdkMajorVersion)
            {
                errorMessage = $"The Sdk version specified in the OmniSharp settings is not .NET 8 or higher. Configured version is '{versionString}'. Please update your settings and restart OmniSharp.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public static bool IncludeSdkInstance(string sdkPath, SemanticVersion targetVersion, bool includePrerelease)
        {
            // If the path does not have a `.version` file, then do not consider it a valid option.
            if (!TryGetSdkVersion(sdkPath, out var version))
            {
                return false;
            }

            // The server targets net8.0, so older SDKs cannot host its MSBuild workspace.
            if (version.Major < MinimumSdkMajorVersion)
            {
                return false;
            }

            // If a target version was specified, then only a matching version is a valid option.
            if (targetVersion is not null)
            {
                return version.Equals(targetVersion);
            }

            // If we are including prereleases then everything else is valid, otherwise check that it is not a prerelease sdk.
            return includePrerelease ||
                string.IsNullOrEmpty(version.PreReleaseLabel);
        }

        public static bool TryGetSdkVersion(string sdkPath, out SemanticVersion version)
        {
            version = null;

            var versionPath = Path.Combine(sdkPath, ".version");
            if (!File.Exists(versionPath))
            {
                return false;
            }

            var lines = File.ReadAllLines(versionPath);
            foreach (var line in lines)
            {
                if (SemanticVersion.TryParse(line, out version))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
