namespace OmniSharp.MSBuild.Logging
{
    internal class ErrorMessages
    {
        internal const string ReferenceAssembliesNotFoundUnix = "This project targets a .NET version whose reference assemblies are not installed. Install the matching .NET SDK or targeting pack.";

        internal const string ReferenceAssembliesNotFoundNet50Unix = "This project targets .NET 5.0 but the currently used MSBuild is not compatible with it - MSBuild 16.8+ is required. Run the net8.0 build of OmniSharp on the .NET SDK.";

        internal const string ReferenceAssembliesNotFoundNet60Unix = "This project targets .NET 6.0 but the currently used MSBuild is not compatible with it - MSBuild 17.0+ is required. Run the net8.0 build of OmniSharp on the .NET SDK.";

        internal const string ReferenceAssembliesNotFoundNet50Windows = "This project targets .NET 5.0 but the currently used MSBuild is not compatible with it - MSBuild 16.8+ is required. To solve this, if you have Visual Studio 2019 installed on your machine, make sure it is updated to version 16.8.";

        internal const string ReferenceAssembliesNotFoundNet60Windows = "This project targets .NET 6.0 but the currently used MSBuild is not compatible with it - MSBuild 17.0+ is required. To solve this, if you have Visual Studio 2022 installed on your machine, make sure it is updated to version 17.0.";
    }
}
