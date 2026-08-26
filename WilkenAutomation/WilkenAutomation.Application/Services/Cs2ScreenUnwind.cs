namespace WilkenAutomation.Application.Services;

/// <summary>
/// Decides the next action when returning to Prozesse verwalten after an export.
/// Never opens Prozesse verwalten from navigation while a child window is still
/// open (that is CAD18 / Funktion gesperrt). Nav_Home in the tree is not home.
/// </summary>
public static class Cs2ScreenUnwind
{
    public enum Step
    {
        Done,
        CloseInternalWindow,
        OpenProcessManagerFromHome,
        WaitForUi
    }

    public static Step Next(
        bool processManagerVisible,
        bool homeWorkspaceVisible,
        bool childWindowOpen,
        bool internalCloseAvailable)
    {
        if (processManagerVisible)
            return Step.Done;

        if (childWindowOpen || internalCloseAvailable)
            return internalCloseAvailable ? Step.CloseInternalWindow : Step.WaitForUi;

        if (homeWorkspaceVisible)
            return Step.OpenProcessManagerFromHome;

        return Step.WaitForUi;
    }

    /// <summary>
    /// Expected screen after one InternalWindow_Close. Unwind waits for this
    /// instead of "any change" so close is correct and fast.
    /// </summary>
    public static string? ExpectedAfterClose(string current) => current switch
    {
        "GITTERBOX_EXPORT" => "SPOOL_LIST",
        "SPOOL_LIST" => "REPORT",
        "REPORT" => "PROCESS_MANAGER",
        _ => "PROCESS_MANAGER"
    };
}
