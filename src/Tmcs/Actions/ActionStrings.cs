namespace Tmcs.Actions;

/// <summary>
/// tmcs's own copies of the text Task Manager shows for Run new task and the Details actions (reference 2.1, 3.1, 3.3 and 4). Each
/// comment gives the MUI string id the text was read from; tmcs never loads it. <c>{0}</c> stands where Task Manager has <c>%s</c>:
/// the process name as the Name column shows it.
/// </summary>
internal static class ActionStrings
{
    // 32420
    public const string TaskManager = "Task Manager";

    // 37212, the Run dialog's caption.
    public const string CreateNewTask = "Create new task";

    // 37213, the Run dialog's prompt.
    public const string CreateNewTaskPrompt = "Type the name of a program, folder, document, or Internet resource, and Windows will open it for you.";

    // 32421
    public const string EndTaskOne = "Do you want to end {0}?";

    // 32422
    public const string EndTaskOneContent = "If an open program is associated with this process, it will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?";

    // 32423
    public const string EndProcess = "End process";

    // 32427
    public const string EndTaskSeveral = "Do you want to end the selected processes?";

    // 32428
    public const string EndTaskSeveralContent = "If open programs are associated with the selected processes, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?";

    // 32424
    public const string EndTreeOne = "Do you want to end the process tree of {0}?";

    // 32425
    public const string EndTreeOneContent = "If open programs or processes are associated with this process tree, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?";

    // 32426
    public const string EndProcessTree = "End process tree";

    // 32429
    public const string EndTreeSeveral = "Do you want to end the selected process trees?";

    // 32430
    public const string EndTreeSeveralContent = "If open programs or processes are associated with the selected process trees, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?";

    // 32447
    public const string ChangePriorityOne = "Do you want to change the priority of '{0}'?";

    // 32448
    public const string ChangePriorityContent = "Changing the priority of certain processes could cause system instability.";

    // 32449
    public const string ChangePriority = "Change priority";

    // 32450
    public const string RealtimeNoticeTitle = "Unable to change priority";

    // 32451
    public const string RealtimeNotice = "Unable to set priority to Realtime. The priority was set to High instead.";

    // 37227
    public const string EndTaskFailedTitle = "Unable to terminate process";

    // 37228
    public const string EndTreeFailedTitle = "Unable to terminate process tree";

    // 37230
    public const string PriorityFailedTitle = "Unable to change priority";

    // 37231
    public const string AffinityFailedTitle = "Unable to access or set process affinity";

    // 34701, the dialog's caption.
    public const string AffinityTitle = "Processor affinity";

    // 34702
    public const string AffinityPrompt = "Which processors are allowed to run \"{0}\"?";

    // 34707
    public const string AllProcessors = "<All Processors>";

    // 34705, with the logical processor's number where Task Manager has %1!u!.
    public const string CpuRow = "CPU {0}";

    // 37225
    public const string OperationFailed = "The operation could not be completed.";

    // 37226
    public const string NotValidForProcess = "The operation is not valid for this process.";

    // 37245
    public const string AccessDenied = "Access denied.";

    // 37244
    public const string NeedAdministrator = "You need administrator privileges to complete this task. Close Task Manager, then run it as an administrator and try again.";
}
