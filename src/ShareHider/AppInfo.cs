using System.Reflection;

namespace ShareHider;

internal static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/darthrater78/win_hide_app";
    public const string ReleaseNotesUrl = RepositoryUrl + "/releases/latest";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
