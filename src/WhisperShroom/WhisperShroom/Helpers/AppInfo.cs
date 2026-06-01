using Windows.ApplicationModel;

namespace WhisperShroom.Helpers;

/// <summary>
/// Exposes the running app's package version (from the MSIX manifest Identity),
/// i.e. the version actually installed on the machine.
/// </summary>
public static class AppInfo
{
    /// <summary>Installed package version as "Major.Minor.Build.Revision".</summary>
    public static string Version
    {
        get
        {
            try
            {
                var v = Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch
            {
                // Unpackaged / no app identity — should not happen for the MSIX build.
                return "—";
            }
        }
    }
}
