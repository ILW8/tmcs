# tmcs Milestone 3 (Details actions) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Task Manager's Details-tab actions to tmcs: the row right-click menu (End task, End process tree, Set priority, Set affinity, Open file location, Properties, Copy), the Delete key and the bottom-right End task button, and File ▸ Run new task. Each action copies Task Manager's dialogs and behavior 1:1, and every menu, dialog and action releases every handle and GDI object it takes.

**Architecture:**
- The Win32 that acts on a process lives behind one seam, `IProcessActions`, with a real implementation and a fake for tests. It opens, ends, re-prioritizes and re-affinitizes a process, reads whether a process is critical, and reports whether tmcs is elevated. Every call re-checks the process identity (PID + creation time) after it opens the handle, so a reused PID is never touched.
- The dialogs are their own classes, each a thin wrapper over the Win32 primitive Task Manager uses: `ConfirmDialog` (`TaskDialogIndirect`), `ErrorDialog` (`TaskDialog`), and `ProcessorAffinityDialog` (a WinForms `Form`, as `SelectColumnsDialog` already is). tmcs draws none of these itself; Windows draws them from the same arguments.
- `DetailsView` owns the row menu, the End task button and the Delete key, as it already owns the header menu. It builds the menu with `NativeMenu`, computes each item's enable state from the current selection, and routes the chosen command — exactly as `Sidebar` does with `PrepareMenu` / `OnCommand`. The dialogs and the shell calls are reached through `Func` seams that tests replace, so no window ever opens in a test except on a hidden desktop that is never shown.
- Run new task shows the shell's own Run dialog (`shell32` ordinal 61) on a thread of its own, so the UI thread keeps sampling. With the admin box unchecked the shell asks the owner window to start the task; `MainForm` answers that `WM_NOTIFY` and starts it non-elevated.
- Nothing new runs on a timer. Actions are driven by the user; the sample clock is untouched.

**Tech Stack:** C# 14 / .NET 10, WinForms, `LibraryImport` P/Invoke (kernel32, ntdll, advapi32, shell32, comctl32 `TaskDialog*`), from System32 only, plus `wdc.dll` resolved at runtime. xUnit 2.9.3, PowerShell 7 for tools.

**Spec:** `docs/superpowers/specs/2026-10-23-tmcs-design.md` (approved), sections 4.1 (Run new task) and 4.2 (Details rows, menu, button, Delete).

**Reference docs (read before the task that names them):**
- `docs/superpowers/reference/2026-10-26-taskmgr-actions-reference.md`: every menu id, dialog, string id, the end-task worker, the Run-dialog contract, the affinity dialog layout, and the runtime probes. This plan cites it by section (for example "reference 3.2").
- `docs/superpowers/reference/2026-10-24-taskmgr-performance-look.md`: section on the command-bar band (the empty 48-px band at the pane bottom) is the precedent for the Details tab's End task button band.
- A Details-tab capture of the owner's Task Manager, saved by `tools/capture-taskmgr.ps1` into `artifacts/reference/taskmgr/` in Task 5. That folder is git-ignored; the images are Microsoft's UI and are never committed.

**Milestone order and the early stop:** Tasks 1–5 build the probes and helpers, the actions service, the confirmation and error dialogs, the row menu with End task / End process tree / Delete / multi-select, and the End task button. **Task 6 stops execution** so the owner can try ending processes before the rest is built (owner's choice). Tasks 7–10 add Set priority, Set affinity, Open file location / Properties / Copy, and Run new task. Task 11 runs the acceptance tests once, for the whole milestone.

## Global Constraints

- Everything in milestone 1's and milestone 2's Global Constraints still holds (`docs/superpowers/plans/2026-10-23-tmcs-m1-details.md`, `docs/superpowers/plans/2026-10-24-tmcs-m2-performance.md`): framework and platform properties, names, version-resource identity, manifests, P/Invoke from System32 only, identity-keyed process state, off-screen test windows on comctl32 v6, GDI/USER/handle counts flat under churn.
- Every action acts on a `ProcessIdentity(Pid, CreateTime)`, never a bare PID. The handle is opened, then `GetProcessTimes` is checked against the row's creation time before anything is done (reference 7). This is M1's `ProcessInfoSource.QueryCore` rule, applied to every action.
- Menu text, access keys, dialog text and command ids copy Task Manager's, read from the reference. tmcs **recreates** the text and layout; it never loads, copies or embeds a Microsoft resource (spec 7). A resource id in this plan says where a string came from; it is never something tmcs loads.
- The trimmed row menu, in order (reference 1.2): End task, End process tree, separator, Set priority ▸ (Realtime, High, Above normal, Normal, Below normal, Low), Set affinity, separator, Open file location, Properties, then tmcs's Copy ▸ (PID, Image path). Items that don't apply are disabled (spec 4.2).
- The Release build runs elevated (`requireAdministrator`) and the Debug build runs `asInvoker`. Access-denied text and the Realtime-readback case differ between them (reference 3.3, 4); both paths are built and tested.
- Confirmation dialogs use one `TASKDIALOGCONFIG` shape (reference 3.1): title 32420 `Task Manager`, `TDCBF_CANCEL_BUTTON`, one custom button (id 1, the default), no icon. The result 1 confirms; anything else cancels.
- Dialogs, menus and the affinity list are created and destroyed per use and leak nothing: every `HMENU` from a right-click is destroyed, every dialog disposed, every opened handle closed. Each menu and dialog has a 1000×-open-and-cancel test on a hidden desktop with GDI, USER and handle counts flat.
- Live process-ending tests end only processes the test itself started, never the owner's. The soak and churn tooling is M2's, unchanged.
- **Owner rules:**
  - Never use computer use, screenshots or UI automation of the owner's desktop. Test windows use the off-screen helpers (`OffscreenForm`, `OffscreenMainForm`) or the hidden desktop of Task 1; they never appear on the owner's screen.
  - Before any UAC prompt or anything the owner must do on screen, tell them and wait for "ready", even if they approved it earlier. Task 5's capture and Task 11's elevated run are the only UAC points.
  - Never trigger a UAC prompt, change system settings, or `git push` without the owner's OK in chat at the time.
  - Never add a feature, menu item or setting the spec doesn't list. Copy is the one spec-listed addition (spec 4.2). Ask first about anything else.
  - Test and diagnostic knobs are `TMCS_*` environment variables, never command-line flags.
  - Use the Rider MCP inspections (`lint_files`, `get_file_problems`) when reviewing. Analysis only; a worktree needs its own `.slnx` open in Rider.
- Commits: author `ILW8 <ilw8@41f.dev>` on branch `feat/tmcs-m3`. End every commit message with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. `artifacts/` is never committed.

## Review Focus

The spec doesn't name these, but each is likely on the owner's machine. Each line names the task that owns its tests.

1. **A process exits, or its PID is reused, between the right-click and the action.** The owner runs heavy churn; a row's process can be gone by the time a menu item or the End task button is clicked.
   - The identity re-check after the open refuses the action rather than touching a different process.
   - An already-gone process gives Task Manager's "operation could not be completed" path, not a crash.
   - Tests: Task 2 (`End_task_refuses_a_reused_pid`, `End_task_on_a_gone_process_reports_not_the_wrong_one`).
2. **The selection changes, or selected processes exit, while a menu or dialog is open.** The menu is built from a snapshot of the selection; the dialog confirms against it.
   - The action uses the identities captured when the menu opened, not the live selection.
   - A process that exited while the dialog was up is skipped, and its disappearance from the list is not an error.
   - Tests: Task 4 (`Menu_acts_on_the_selection_captured_when_it_opened`, `A_selected_process_that_exits_before_confirm_is_skipped`).
3. **Ending many processes at once, some of which can't be opened.** Multi-select End task on a mix of the owner's own and protected processes.
   - One plural confirmation, then each is ended independently; one failure doesn't stop the rest.
   - At most one error dialog for the batch (my call), never one per process.
   - Tests: Task 4 (`Multi_end_confirms_once_and_ends_each`, `One_failure_shows_one_dialog_and_ends_the_others`).
4. **A critical (break-on-termination) process in the selection.** The owner can select System or another critical process.
   - The critical confirmation (32443/32444/32445) names the first critical one and gates on its verification checkbox.
   - Tests: Task 2 (`Critical_process_is_detected`) and Task 4 (`A_critical_process_in_the_selection_uses_the_critical_dialog`).
5. **Set priority to Realtime without the privilege.** The Debug (non-elevated) build can't grant Realtime; the class reads back as High (reference 4, string 32451).
   - The readback is detected and the "set to High instead" dialog shows, rather than silently claiming Realtime.
   - Tests: Task 7 (`Realtime_without_privilege_reads_back_as_high`, with a fake that models the readback).

## Calls for the owner to check

Decisions this plan makes that the spec, the owner and the reference don't settle. Each task marks them "my call". Task 6 and Task 11 put this list in front of the owner.

- **Ending:**
  - End task on a Details row goes straight to `TerminateProcess`, with no polite "close the windows first" pre-pass. Task Manager keeps that pre-pass for the Processes tab's app windows (reference 3.2 step 2, probe 7); Details rows are processes, not app windows (Task 2).
  - On `TerminateProcess` access-denied after a successful open, tmcs shows no dialog and closes the dying process's handles in the background, reading only that process's handle list through `NtQueryInformationProcess(ProcessHandleInformation)`, with Task Manager's File filter (local, disk or pipe) and `DUPLICATE_CLOSE_SOURCE` (owner's choice; reference 3.2 step 5) (Task 2).
  - A multi-select end shows one plural confirmation and, on failures, one error dialog for the first dialog-worthy failure, not one per process (Task 4).
  - End process tree ends descendants before their parent, and a descendant counts only when its creation time is after its parent's, so a reused PID is never pulled into the tree (Task 2).
- **Identity for the radio and the tree:** the current priority for the Set priority radio check comes from the snapshot's base priority (reference 4: 24/13/10/8/6/4), not a fresh handle query (Task 7).
- **Enable rules (reference 8 leaves these to the builder):**
  - With more than one row selected, Set priority, Set affinity, Open file location, Properties and Copy are grayed; End task, End process tree, Delete and the button act on all of them (owner's choice) (Task 4).
  - Open file location and Properties are grayed when the process has no image path (System, Registry, Memory Compression) (Task 4, Task 9).
  - The row menu does not open when no row is selected; the keyboard menu anchors at the focused row (Task 4).
  - The End task button is enabled only when at least one row is selected (Task 5).
- **Affinity dialog:** built as a WinForms `Form` matching dialog 34701's layout (reference 5.1), not an in-memory native template. OK is grayed while no CPU is checked; `<All Processors>` checks and clears every CPU (Task 8).
- **Copy:** `Copy ▸ PID` copies the decimal PID; `Copy ▸ Image path` copies the full image path, and is grayed when there is none. Clipboard writes go through WinForms `Clipboard.SetText`, catching `ExternalException` (Task 9).
- **Run new task:**
  - The dialog runs on its own STA thread (`SHCreateThread`, `CTF_COINIT_STA`), as Task Manager does (reference 2.1) (Task 10).
  - Ctrl+Run new task (Task Manager's "open cmd directly" shortcut) stays out (spec does not list it) (Task 10).
  - When `wdc.dll!WdcRunTaskAsInteractiveUser` resolves, the admin checkbox is shown and the unchecked path launches non-elevated through it; when it does not resolve, the checkbox is omitted and a non-elevated tmcs starts the process itself (it is already at the user's level). Which path Release takes is confirmed by Task 1's probe (Task 10).
- **Error dialogs:** one error dialog per command invocation, derived from the HRESULT and tmcs's elevation (reference 3.3), never a raw exception (Tasks 2, 4, 7, 8).

---

### Task 1: Runtime probes, the hidden-desktop test helper, and the launch-probe tool

The reference marks several facts **probe**: they must be confirmed at runtime before code depends on them (reference 9). This task confirms the ones later tasks rest on, builds the hidden desktop the dialog tests need, and records the results. **If a probe contradicts the reference or this plan, stop and tell the owner before writing the code that depends on it.**

**Files:**
- Create: `tests/Tmcs.Tests/Support/HiddenDesktop.cs`, `tests/Tmcs.Tests/Support/HiddenDesktopTests.cs`
- Create: `tools/launch-probe/LaunchProbe.csproj`, `tools/launch-probe/Program.cs`
- Modify: `tmcs.slnx` (add the tool project)
- Create: `docs/superpowers/reference/2026-10-26-m3-probe-results.md`

**Interfaces:**
- Produces: `internal static class HiddenDesktop` with `static void Run(Action body)`. On a fresh STA thread it creates a desktop with `CreateDesktopW` (access `GENERIC_ALL`), calls `SetThreadDesktop`, runs `body` with comctl32 v6 active (`CommonControls6.Activate`, as `Sta.Run` does), then restores the old desktop and closes the new one. Windows `body` creates belong to a desktop that is never switched to, so a modal `TaskDialog`, `RunFileDlg` or affinity dialog never appears on the owner's screen. `Run` rethrows what `body` throws.
- Produces: `tools/launch-probe`, a WinExe that, on start, writes one line to the file named by its first argument (or `%TMCS_PROBE_OUT%`): its token elevation (`GetTokenInformation(TokenElevation)`), its session id (`ProcessIdToSessionId`), its current directory, its full command line, and `STARTUPINFO.wShowWindow`. Then it exits 0. No window. It is in `tmcs.slnx`, built by `dotnet build`, and never published with tmcs.

- [ ] **Step 1: Write the failing test.**
  - `HiddenDesktopTests.A_task_dialog_on_the_hidden_desktop_opens_and_closes`: inside `HiddenDesktop.Run`, show a `TaskDialog` with only an OK button on a 1-second timer that posts `WM_CLOSE` to it, and assert it returned without throwing and that `GetThreadDesktop` was restored. (The point is that the helper runs modal UI without touching the owner's screen; later tasks reuse it.)
- [ ] **Step 2: Run it.** `dotnet test --filter HiddenDesktopTests`. Expect FAIL (no `HiddenDesktop`).
- [ ] **Step 3: Implement** `HiddenDesktop` and `launch-probe`, and add the tool to `tmcs.slnx`.
- [ ] **Step 4: Run the probes** on the **Debug (non-elevated)** build and record each in the results doc, with the observed values:
  - **P2/P3 (reference 9.2, 9.3):** from a throwaway Debug run, resolve `wdc.dll!WdcRunTaskAsInteractiveUser` (`LoadLibraryW("wdc.dll")` + `GetProcAddress`), and call it with `launch-probe`'s path as the command line, a known directory, and flag values 0, 1 and 9. Read `launch-probe`'s output file each time. Record: did it resolve; did the task run; was the launched process **non-elevated**; did session and directory match; what `wShowWindow` each flag produced. This decides whether Release's unchecked path uses wdc (reference 2.5) and what the flags argument means.
  - **P-payload (reference 9.1):** inside `HiddenDesktop.Run`, call `RunFileDlg` (shell32 ordinal 61) with flags `0x14 | 0x40` (the checkbox template — without 0x40 there is no admin box and the shell launches the typed command itself instead of sending −511) and an owner window whose `WndProc` logs, for `WM_NOTIFY` code −511, the bytes at `lParam + 0x18`, `+ 0x20` and `+ 0x2c`, then returns 1. Drive it by posting the command text and Enter to the combo, with the admin box left unchecked. Record whether offsets 0x18 (command), 0x20 (directory) and 0x2c (flags DWORD) hold. Note any autocomplete flakiness for Task 10.
  - **P6 (reference 9.6, string 32451):** on the Debug build, `SetPriorityClass(self, REALTIME_PRIORITY_CLASS)` then `GetPriorityClass`. Record whether it reads back as `HIGH_PRIORITY_CLASS` without the privilege (this is the Review Focus 5 case).
  - **P5 (reference 9.5):** record, from the reference alone, that tmcs will gate the critical-process button on the checkbox via the dialog callback; no owner screenshot is needed because tmcs defines its own callback. (No runtime probe.)
- [ ] **Step 5: Decide.** If any probe contradicts the reference or the plan's "Calls for the owner to check" (for example a non-elevated caller cannot run the Interactive task, or the payload offsets differ), **stop and tell the owner** with the observed values, and propose the adjustment, before continuing.
- [ ] **Step 6: Commit** with message `test: hidden-desktop helper, launch-probe tool, and M3 runtime probes`.

---

### Task 2: The process actions service — End task, End process tree, the handle closer

**Files:**
- Create: `src/Tmcs/Actions/IProcessActions.cs`, `src/Tmcs/Actions/ProcessActions.cs`, `src/Tmcs/Actions/ActionOutcome.cs`, `src/Tmcs/Actions/ProcessPriority.cs`, `src/Tmcs/Actions/ProcessTree.cs`
- Create: `tests/Tmcs.Tests/Support/FakeProcessActions.cs`
- Modify: `src/Tmcs/Native/Kernel32.cs` (`TerminateProcess`, `DuplicateHandle`, `GetFileType`, access constants), `src/Tmcs/Native/NtDll.cs` (`NtQueryInformationProcess` classes 51 and 29, `NtQueryInformationFile`, the handle-snapshot structs, `STATUS_PROCESS_IS_TERMINATING`), `src/Tmcs/Native/Advapi32.cs` (`TokenElevation` class)
- Test: `tests/Tmcs.Tests/Actions/ProcessActionsTests.cs`, `tests/Tmcs.Tests/Actions/ProcessTreeTests.cs`

**Interfaces:**
- Consumes: `ProcessIdentity`, `ProcessRow` (M1); `Kernel32.OpenProcess`, `GetProcessTimes` (M1).
- Produces:
  - `internal enum ProcessPriority { Low, BelowNormal, Normal, AboveNormal, High, Realtime }` (ascending). Maps to `IDLE/BELOW_NORMAL/NORMAL/ABOVE_NORMAL/HIGH/REALTIME_PRIORITY_CLASS` and to base priorities 4/6/8/10/13/24 (reference 4).
  - `internal static class ProcessTree` with `static IReadOnlyList<ProcessIdentity> OrderForEnding(ProcessIdentity root, IReadOnlyList<ProcessRow> rows)`: the root and all its descendants by the `ParentPid` chains in `rows`, ordered so every descendant comes before its ancestor (a post-order of the tree). A row is a child only when its parent's creation time is before its own, so a reused parent PID never pulls in an unrelated process. Pure and allocation-free of Win32; this is the unit `EndProcessTree` iterates, and the unit the tree tests exercise.
  - `internal readonly record struct ActionOutcome(int HResult, bool ShowDialog)`: `Ok => HResult >= 0`; `IsAccessDenied => HResult == unchecked((int)0x80070005)`; `IsInvalidArg => HResult == unchecked((int)0x80070057)`; `static Success => new(0, false)`. A failure the caller must show as a dialog has `ShowDialog == true`.
  - `internal interface IProcessActions`:
    - `bool IsElevated { get; }` — `GetTokenInformation(self, TokenElevation)`, read once.
    - `bool IsCritical(ProcessIdentity id)` — `NtQueryInformationProcess(ProcessBreakOnTermination)` with `PROCESS_QUERY_LIMITED_INFORMATION`; false when it can't be read.
    - `ActionOutcome EndTask(ProcessIdentity id)` — reference 3.2, the Details-row path (no window pre-pass).
    - `ActionOutcome EndProcessTree(ProcessIdentity id, IReadOnlyList<ProcessRow> snapshot)` — calls `EndTask` on each identity from `ProcessTree.OrderForEnding(id, snapshot)`, so descendants end before the parent. The outcome is the first dialog-worthy failure, else Success.
    - `PrioritySet SetPriority(ProcessIdentity id, ProcessPriority p)` (Task 7 implements the body; the signature is fixed here so the fake and the menu compile). `readonly record struct PrioritySet(ActionOutcome Outcome, bool ReadBackMatched)`.
    - `AffinityInfo? GetAffinity(ProcessIdentity id)` and `ActionOutcome SetAffinity(ProcessIdentity id, nuint mask)` (Task 8 implements the bodies). `readonly record struct AffinityInfo(nuint ProcessMask, nuint SystemMask)`.
  - `ProcessActions.EndTask`: refuse PIDs 0/1/4/5 with `E_INVALIDARG` and `ShowDialog == true` (reference 3.2 step 1); else `OpenProcess(0x1001)`; if the open fails, `ActionOutcome(HRESULT_FROM_WIN32(err), ShowDialog: true)`; `GetProcessTimes` and compare the creation time — a mismatch is `E_INVALIDARG`/dialog (the PID was reused); `TerminateProcess(h, 1)`; on success, Success; on failure with access-denied, queue the background handle-closer on `id` and return `ActionOutcome(hr, ShowDialog: false)`; on any other failure, `ActionOutcome(hr, ShowDialog: true)`.
  - The background closer (`ProcessActions` private, `QueueUserWorkItem(WT_EXECUTELONGFUNCTION)`): reopen with `0x1041` (`TERMINATE | DUP_HANDLE | QUERY_LIMITED_INFORMATION`) and the same identity check; `NtQueryInformationProcess(ProcessHandleInformation = 51)` on the target alone; for each handle, close it in the target with `DuplicateHandle(target, h, self, out dup, 0, false, DUPLICATE_CLOSE_SOURCE)` then `CloseHandle(dup)` — every type except File unconditionally, and a File handle only when `NtQueryInformationFile(FileIsRemoteDeviceInformation = 0x33)` says local **and** `GetFileType` is `FILE_TYPE_DISK (1)` or `FILE_TYPE_PIPE (3)` (reference 3.2 step 5). It finds the File type index by duplicating handles and reading each type name until one is `File`. A query that returns `STATUS_PROCESS_IS_TERMINATING` (the process finished exiting first) is treated as done, not an error. One pass over one process; it never touches another process.
- Produces: `internal sealed class FakeProcessActions : IProcessActions`. Tests set its elevation, which PIDs are critical, and a per-PID outcome for `EndTask`/`SetPriority`/`SetAffinity`, including a "reused PID" that fails the identity check and an "access-denied" that returns `ShowDialog == false`. It records the calls in order.

- [ ] **Step 1: Write the failing tests** in `ProcessActionsTests` (using the real `ProcessActions` against processes the test starts — `cmd /c pause` children it can terminate — and the fake elsewhere):
  - `Refuses_system_pids`: `EndTask` on 0, 1, 4 and 5 each returns `IsInvalidArg` and `ShowDialog`, with no open attempted.
  - `Ends_a_child_process`: start a child, `EndTask(child identity)` returns `Ok`, and the child exits.
  - `End_task_refuses_a_reused_pid` (Review Focus 1): `EndTask` with the child's PID but a creation time 1 tick off returns `IsInvalidArg`, and the child stays alive.
  - `End_task_on_a_gone_process_never_reports_ok`: end the child and wait for it to exit, then `EndTask` the same identity again returns `!Ok` (the exiting/gone process yields access-denied or an open failure, both non-`Ok`); a second, unrelated child the test started is still alive afterward.
  - (Tree ordering is tested in `ProcessTreeTests`, below, against the pure `ProcessTree.OrderForEnding`; `EndProcessTree`'s own test — `End_process_tree_ends_each_identity` — starts a parent and two children and asserts all three exit.)
  - `Critical_process_is_detected` (Review Focus 4): `IsCritical` is true for a process the test marks break-on-termination (use the fake; the real path is covered by not throwing on a normal process returning false).
  - `Handle_closer_touches_only_the_target`: start a child holding a pipe handle; run the closer directly on the child's identity; assert it returns without throwing and closes the child's non-File and local-disk/pipe File handles (observe the child's handle count drop), and that a second unrelated child's handle count is unchanged.

  And in `ProcessTreeTests`, against a hand-built `ProcessRow` snapshot (no Win32):
  - `Orders_descendants_before_their_parent`: a root with two children, one of which has a child → the deepest come first, the root last.
  - `Excludes_a_reused_parent_pid` (Review Focus 1): a row whose `ParentPid` matches the root's PID but whose creation time is before the root's is not included.
  - `A_leaf_returns_just_itself`.
- [ ] **Step 2: Run them.** `dotnet test --filter "ProcessActionsTests|ProcessTreeTests"`. Expect FAIL.
- [ ] **Step 3: Implement** the native additions, `ProcessTree.OrderForEnding`, `ProcessActions` (`EndTask`, `EndProcessTree`, `IsCritical`, `IsElevated`, the handle closer) and `FakeProcessActions`. Leave `SetPriority`/`GetAffinity`/`SetAffinity` as the minimal bodies Tasks 7–8 complete (enough to compile and to satisfy the fake).
- [ ] **Step 4: Run the whole suite.** `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: process actions service with end task, end process tree and the handle closer`.

---

### Task 3: The confirmation and error dialogs

**Files:**
- Create: `src/Tmcs/Actions/ConfirmDialog.cs`, `src/Tmcs/Actions/ErrorDialog.cs`, `src/Tmcs/Actions/ActionStrings.cs`
- Modify: `src/Tmcs/Native/ComCtl32.cs` (`TaskDialogIndirect`, `TaskDialog`, `TASKDIALOGCONFIG`, the button and flag constants)
- Test: `tests/Tmcs.Tests/Actions/ConfirmDialogTests.cs`, `tests/Tmcs.Tests/Actions/ErrorDialogTests.cs`

**Interfaces:**
- Consumes: `ActionOutcome` (Task 2), `HiddenDesktop` (Task 1).
- Produces:
  - `internal static class ActionStrings`: tmcs's own copies of the reference's strings, as constants with `{0}` where Task Manager has `%s`. Confirmations 32421/32422/32423, 32427/32428/32423, 32424/32425/32426, 32429/32430/32426, 32443/32444/32445, verification 32446, title 32420; priority confirmation 32447/32448/32449; error titles 37227/37228/37230/37231, bodies 37225/37226/37244/37245; the Realtime notice title 32450 and body 32451 (reference 3.1, 3.3, 4). A comment on each cites the id; none is loaded from a resource.
  - `internal enum ConfirmKind { EndTask, EndProcessTree, SetPriority }`.
  - `internal static class ConfirmDialog` with `static bool Show(IWin32Window owner, ConfirmKind kind, int count, string? singleName, bool critical, string? criticalName)`:
    - Builds the `TASKDIALOGCONFIG` of reference 3.1 (`cbSize` 0xA0, pack(1), `dwFlags` 0, `TDCBF_CANCEL_BUTTON`, one custom button id 1 as default, no icon, title 32420).
    - Main instruction and button text by `kind` and whether `count > 1` (the single form uses `singleName` in `{0}`; the plural forms take no name).
    - When `critical`, overrides to 32443/32444/32445 with `{0}` = `criticalName`, sets `pszVerificationText` = 32446 and `TDF_ENABLE_HYPERLINKS` off; a `PFTASKDIALOGCALLBACK` enables the custom button only while the verification box is checked (reference 3.1; tmcs defines its own callback).
    - Returns `true` only when the custom button (id 1) was pressed.
  - `internal enum ErrorKind { EndTask, EndProcessTree, SetPriority, Affinity }` → titles 37227/37228/37230/37231.
  - `internal static class ErrorDialog` with:
    - `static void Show(IWin32Window owner, ErrorKind kind, ActionOutcome outcome, bool isElevated)`: `TaskDialog(..., TDCBF_OK_BUTTON, TD_ERROR_ICON)`; content by reference 3.3 — `!isElevated && outcome.IsAccessDenied` → 37245/37244; `outcome.IsInvalidArg` → 37225/37226; else → 37225 + `FormatMessage(outcome.HResult)`.
    - `static void ShowNotice(IWin32Window owner, string title, string body)`: the same `TaskDialog` (OK, error icon) with an explicit title and body, for the Realtime "set to High instead" notice (Task 7, strings 32450/32451), which is not an `ActionOutcome` failure.

- [ ] **Step 1: Write the failing tests** (every dialog shown inside `HiddenDesktop.Run`, auto-dismissed by a posted button click or `WM_CLOSE`):
  - `ConfirmDialogTests`:
    - `Single_end_task_text`, `Plural_end_task_text`, `Single_tree_text`, `Plural_tree_text`, `Priority_text`: build each config and assert (through a seam that returns the built `TASKDIALOGCONFIG` without showing) the main instruction, content and button text equal `ActionStrings`' values with the name substituted.
    - `Confirm_returns_true_only_for_the_custom_button`: a config shown on the hidden desktop, dismissed by pressing id 1 → true; by Cancel → false.
    - `Critical_uses_the_shutdown_text_and_a_verification_box`: the critical config has 32443/32444/32445 and `pszVerificationText` 32446.
    - `Thousand_open_and_cancel_stay_flat`: 1000× show-and-cancel on the hidden desktop; GDI, USER and handle counts (via `GetGuiResources` and the process handle count) change by at most ±5 from after the first.
  - `ErrorDialogTests`:
    - `Access_denied_not_elevated_asks_for_admin`: `ErrorKind.EndTask`, access-denied, `isElevated: false` → 37245/37244.
    - `Invalid_arg_is_not_valid_for_this_process`: → 37225/37226.
    - `Other_error_uses_format_message`: a non-access, non-invalidarg HRESULT → 37225 + a non-empty system message.
    - `Elevated_access_denied_uses_format_message`: access-denied with `isElevated: true` → the third row, not 37244.
    - `Thousand_open_and_cancel_stay_flat`: 1000× show-and-dismiss (OK) on the hidden desktop; GDI, USER and handle counts flat (±5 from after the first).
- [ ] **Step 2: Run them.** `dotnet test --filter "ConfirmDialogTests|ErrorDialogTests"`. Expect FAIL.
- [ ] **Step 3: Implement** the native additions, `ActionStrings`, `ConfirmDialog` and `ErrorDialog`. Expose the built `TASKDIALOGCONFIG` through an `internal` seam so the text tests need not show a window.
- [ ] **Step 4: Run the whole suite.** `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: Task Manager confirmation and error dialogs`.

---

### Task 4: The row menu, enable rules, End task / End process tree, Delete, multi-select

**Files:**
- Modify: `src/Tmcs/Ui/CommandIds.cs` (the row-menu ids), `src/Tmcs/Details/DetailsView.cs`, `src/Tmcs/Details/ProcessListView.cs` (raise a row context-menu event, as it already raises the header one), `src/Tmcs/MainForm.cs` (pass `IProcessActions` into `DetailsView`), `src/Tmcs/AppServices.cs` (add `IProcessActions Actions`)
- Modify: every `new AppServices(...)` and `new DetailsView(...)` in the tests
- Test: `tests/Tmcs.Tests/Details/DetailsActionsTests.cs`

**Interfaces:**
- Consumes: `IProcessActions`, `ActionOutcome` (Task 2); `ConfirmDialog`, `ErrorDialog`, `ConfirmKind`, `ErrorKind` (Task 3); `SelectionKeeper`, `NativeMenu`, `ProcessRow` (M1).
- Produces:
  - `CommandIds` gains: `RunNewTask = 101`; and the row menu — `EndTask = 500`, `EndProcessTree = 501`; the **contiguous, Realtime-first** priority range `PriorityRealtime = 510 .. PriorityLow = 515` (so `CheckMenuRadioItem` takes it as one range, as the speed items already do); `SetAffinity = 520`, `OpenFileLocation = 521`, `Properties = 522`, `CopyPid = 530`, `CopyImagePath = 531`. A comment pins the priority range's order and contiguity.
  - `ProcessListView` raises `event Action<Point>? RowContextMenu` from `WM_CONTEXTMENU` when `wParam` is the list itself (not the header handle, which still routes to `HeaderContextMenu`). From the keyboard (`lParam == -1`) it anchors at the focused row's rectangle (`LVM_GETITEMRECT`), else at the cursor point — mirroring `RaiseHeaderContextMenu`.
  - `DetailsView`:
    - Constructor gains `IProcessActions actions`.
    - `internal IReadOnlyList<ProcessIdentity> SelectedIdentities()` — the selected rows' identities, read from the native selection like `SelectionKeeper.Capture`.
    - `internal NativeMenu PrepareRowMenu(IReadOnlyList<ProcessRow> selection)` — builds the trimmed menu (reference 1.2 order), sets each item's enable state from `selection` (the "Calls for the owner to check" enable rules), and radio-checks the current priority (Task 7 fills this). Tests read it, as they read `Sidebar.PrepareMenu`.
    - `internal void OnRowCommand(int id, IReadOnlyList<ProcessRow> selection)` — the chosen command. `EndTask`/`EndProcessTree`: critical check → `ConfirmDialog.Show` with the right `ConfirmKind`, `count`, single name or first critical name → on confirm, act on each captured identity through `actions`, collecting the first `ShowDialog` failure → at most one `ErrorDialog.Show`. A process that is gone when acted on is skipped silently (Review Focus 2).
    - A `Func` seam `internal Func<IReadOnlyList<ProcessRow>, Point, int> TrackRowMenu` (default `menu.Track(this, screen)`), and `Func`s for confirm and error (default the real dialogs) so tests drive the flow with no window.
    - The row context-menu handler: capture the selection (as `ProcessRow` copies) when the menu opens; do nothing if it is empty; show the menu; route the result through `OnRowCommand` with the **captured** selection.
    - `protected override bool ProcessCmdKey(ref Message, Keys)` handles `Delete`: same as End task on the current selection (spec 4.2). `ProcessCmdKey` sees the key even while the child `ProcessListView` has focus, as `MainForm`'s own override already does for F5; a `UserControl` has no `KeyPreview`, so a `KeyDown` handler would not fire.
  - `AppServices` gains a parameter `IProcessActions Actions`; `CreateReal` passes `new ProcessActions()`. `MainForm` passes `services.Actions` to `DetailsView`.

- [ ] **Step 1: Write the failing tests** in `DetailsActionsTests` (an `OffscreenForm`-hosted `DetailsView` with a `FakeProcessActions` and the dialog seams replaced by fakes that record and script the outcome):
  - `Menu_shape_matches_the_reference`: the prepared menu for a single selection has, in order, End task, End process tree, separator, Set priority ▸, Set affinity, separator, Open file location, Properties, Copy ▸, with the reference's access keys.
  - `Multi_select_grays_single_only_items`: with two rows selected, Set priority, Set affinity, Open file location, Properties and Copy are grayed; End task and End process tree are enabled.
  - `No_image_path_grays_open_and_properties`: a row with a null image path grays Open file location and Properties.
  - `No_selection_opens_no_menu`: the row context-menu handler with an empty selection shows nothing.
  - `Menu_acts_on_the_selection_captured_when_it_opened` (Review Focus 2): change the live selection after capture; the command still ends the captured identities.
  - `A_selected_process_that_exits_before_confirm_is_skipped` (Review Focus 2): the fake fails one identity's open after confirm; the others still end; no throw.
  - `Multi_end_confirms_once_and_ends_each` (Review Focus 3): two rows → one plural `ConfirmDialog` call, two `EndTask` calls.
  - `One_failure_shows_one_dialog_and_ends_the_others` (Review Focus 3): one identity returns a `ShowDialog` failure → exactly one `ErrorDialog` call, and the other is still ended.
  - `A_critical_process_in_the_selection_uses_the_critical_dialog` (Review Focus 4): the fake marks one selected process critical → the confirm call is the critical kind naming it.
  - `Delete_ends_the_selection`: pressing Delete (through `ProcessCmdKey`) runs the same End task flow.
  - `Thousand_menu_open_and_cancel_stay_flat`: 1000× `PrepareRowMenu` + dispose (the dismissed-menu path, no command chosen); GDI, USER and handle counts flat (±5). This pins that each right-click's `HMENU` is destroyed.
- [ ] **Step 2: Run them.** `dotnet test --filter DetailsActionsTests`. Expect FAIL.
- [ ] **Step 3: Implement** the `CommandIds`, the `ProcessListView` event, the `DetailsView` menu, enable rules, command routing and Delete, and the `AppServices` wiring. Update the test constructors.
- [ ] **Step 4: Run the whole suite.** `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: Details row menu with end task, end process tree, Delete and multi-select`.

---

### Task 5: The End task button and the Details capture

The End task button sits at the bottom right of the Details tab (spec 4.2). Its exact band height and the button's size and margins are measured from a capture of the owner's Task Manager, taken the same way the M2 panes were (owner's choice (4): extend `tools/capture-taskmgr.ps1` to the Details tab). The capture needs one UAC prompt, which must be announced first.

**Files:**
- Modify: `tools/capture-taskmgr.ps1` (a Details-tab mode), `src/Tmcs/Details/DetailsView.cs` (the button band)
- Create: `docs/superpowers/reference/2026-10-26-taskmgr-details-button.md` (the measured geometry)
- Test: `tests/Tmcs.Tests/Details/DetailsActionsTests.cs` (button tests added here)

**Interfaces:**
- Consumes: the capture; `DetailsView`'s End task flow (Task 4).
- Produces:
  - `tools/capture-taskmgr.ps1` gains a mode that, with Task Manager on the Details tab, saves `details.png` and `details.txt` (the element dump with bounds) into `artifacts/reference/taskmgr/`, by the same MSAA + `PrintWindow` path it uses for the panes. It never moves the mouse, activates or takes focus.
  - `DetailsView` lays out the list above a bottom band holding a right-aligned End task button, in 96-DPI units scaled by `DeviceDpi / 96`, to the band height and button metrics the capture gives (the look reference's command-bar band is the precedent). The button is enabled only when the selection is non-empty, and its `Click` runs the Task 4 End task flow on the current selection.

- [ ] **Step 1: Announce and capture.** Tell the owner a UAC prompt is coming to run `tools/capture-taskmgr.ps1` elevated against their Task Manager's Details tab, and **wait for "ready"**. Then run it, and record the band and button geometry in the reference doc. (If the owner prefers not to, fall back to the owner's screenshot or, failing that, the look reference's 48-px band with a standard button; note the fallback.)
- [ ] **Step 2: Write the failing tests** in `DetailsActionsTests`:
  - `End_task_button_disabled_without_a_selection` / `enabled_with_a_selection`.
  - `End_task_button_ends_the_selection`: clicking it runs the same flow as the menu's End task (assert via the fake).
  - `Button_band_layout_is_scaled`: at 192 DPI the band height and button bounds are the 96-DPI values doubled (±1).
- [ ] **Step 3: Run them.** `dotnet test --filter DetailsActionsTests`. Expect the new ones to FAIL.
- [ ] **Step 4: Implement** the capture mode and the button band.
- [ ] **Step 5: Run the whole suite.** `dotnet test`. Expect all PASS.
- [ ] **Step 6: Commit** with message `feat: Details End task button and the Details-tab capture`. `artifacts/` stays untracked.

---

### Task 6: STOP — the owner tries ending processes

**This task ends the turn. Do not start Task 7 until the owner says to go on.** The owner chose this stop so they can try the row menu, End task, End process tree, the End task button and Delete before the rest is built.

**Files:**
- Create: `docs/superpowers/results/2026-10-26-m3-actions.md` (the "Checkpoint" section; Task 11 completes the file)

- [ ] **Step 1: Check the build.** `dotnet test` (all PASS) and `dotnet build src/Tmcs -c Debug`.
- [ ] **Step 2: Inspect.** Run the Rider inspections (`lint_files`) on every file changed since the branch point, and fix what they report in those files. If the Rider MCP is unavailable, note it and skip.
- [ ] **Step 3: Write the checkpoint section**: what the owner should try, and the "Calls for the owner to check" entries that belong to Tasks 1–5, so they can check them while trying.
- [ ] **Step 4: Commit** with message `docs: M3 checkpoint notes`.
- [ ] **Step 5: Hand over, then stop.**
  - Tell the owner the Debug build is at `src/Tmcs/bin/Debug/net10.0-windows/win-x64/tmcs.exe`. It is `asInvoker`, so access-denied on a protected process is expected and shows the "need administrator" dialog; the Release build ends them.
  - Ask them to run it beside Task Manager and compare the row menu, the confirmation and error dialogs, End task, End process tree, Delete, multi-select and the End task button. **Warn them that these actually end processes**, so they should pick disposable ones.
  - **Then end the turn.**
- [ ] **Step 6: When the owner answers:** record their notes, fix what they ask for within the spec (each with its own test and commit), ask first about anything outside the spec, and go on to Task 7 only when they say so.

---

### Task 7: Set priority

**Files:**
- Modify: `src/Tmcs/Actions/ProcessActions.cs` (`SetPriority` body), `src/Tmcs/Actions/FakeProcessActions.cs` (readback modelling), `src/Tmcs/Details/DetailsView.cs` (the submenu, radio check and command), `src/Tmcs/Processes/ProcessRow.cs` and `src/Tmcs/Processes/ProcessTable.cs` (`BasePriority`), `src/Tmcs/Native/Kernel32.cs` (`GetPriorityClass`, `SetPriorityClass`)
- Test: `tests/Tmcs.Tests/Actions/ProcessActionsTests.cs`, `tests/Tmcs.Tests/Details/DetailsActionsTests.cs`

**Interfaces:**
- Consumes: `ProcessPriority`, `IProcessActions.SetPriority`, `PrioritySet` (Task 2); the confirmation (32447/32448/32449) and error (37230) of Task 3.
- Produces:
  - `ProcessRow.BasePriority { get; set; }` (int), copied in `ProcessTable.CopyLive` from `RawProcess.BasePriority`.
  - `ProcessActions.SetPriority`: `OpenProcess(0x1001)` with the identity check, `SetPriorityClass`, then `GetPriorityClass`; `ReadBackMatched` is false when the requested class differs from the read-back one (the Realtime-without-privilege case, reference 4). The outcome follows reference 3.3 for any failure.
  - `DetailsView`: `PrepareRowMenu` builds the Set priority submenu (Realtime, High, Above normal, Normal, Below normal, Low) and radio-checks the item matching the single selected row's base priority (mapping of reference 4) via `CheckMenuRadioItem` over the contiguous range. `OnRowCommand` for a priority id: `ConfirmDialog.Show(SetPriority, 1, name, critical?, …)` → on confirm, `actions.SetPriority(id, p)`; if `!Outcome.Ok && Outcome.ShowDialog`, `ErrorDialog.Show(SetPriority, …)`; else if `Outcome.Ok && !ReadBackMatched`, `ErrorDialog.ShowNotice(owner, 32450, 32451)` — the "set to High instead" notice. Set priority is grayed for a multi-selection (Task 4 rule).

- [ ] **Step 1: Write the failing tests.**
  - `ProcessActionsTests.Set_priority_changes_the_class`: on a child, `SetPriority(id, BelowNormal)` returns `Ok` and `GetPriorityClass` reads `BELOW_NORMAL_PRIORITY_CLASS`.
  - `ProcessActionsTests.Realtime_without_privilege_reads_back_as_high` (Review Focus 5): modelled with the fake (the real value depends on the privilege, confirmed by Task 1 P6) — `SetPriority(id, Realtime)` returns `Ok` with `ReadBackMatched == false`.
  - `DetailsActionsTests.Priority_radio_checks_the_current_class`: a row with base priority 13 → High is radio-checked.
  - `DetailsActionsTests.Priority_confirm_then_set`: choosing Above normal confirms (32447) then calls `SetPriority(id, AboveNormal)`.
  - `DetailsActionsTests.Realtime_readback_shows_the_high_notice`: the fake returns `Ok` + `ReadBackMatched == false` → the 32451 dialog is shown, not the error dialog.
- [ ] **Step 2: Run them.** `dotnet test --filter "ProcessActionsTests|DetailsActionsTests"`. Expect the new ones to FAIL.
- [ ] **Step 3: Implement** `BasePriority`, `SetPriority`, the submenu, the radio check and the command.
- [ ] **Step 4: Run the whole suite.** `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: set process priority with Task Manager's confirmation and Realtime notice`.

---

### Task 8: Set affinity

**Files:**
- Create: `src/Tmcs/Actions/ProcessorAffinityDialog.cs`
- Modify: `src/Tmcs/Actions/ProcessActions.cs` (`GetAffinity`, `SetAffinity` bodies), `src/Tmcs/Details/DetailsView.cs` (the command and its seam), `src/Tmcs/Native/Kernel32.cs` (`GetProcessAffinityMask`, `SetProcessAffinityMask`)
- Test: `tests/Tmcs.Tests/Actions/ProcessorAffinityDialogTests.cs`, `tests/Tmcs.Tests/Actions/ProcessActionsTests.cs`

**Interfaces:**
- Consumes: `IProcessActions.GetAffinity`/`SetAffinity`, `AffinityInfo` (Task 2); the error dialog (37231) of Task 3; `SelectColumnsDialog` as the WinForms-dialog precedent.
- Produces:
  - `ProcessActions.GetAffinity`: `OpenProcess(0x1001)` + identity check + `GetProcessAffinityMask(out process, out system)`; `null` on failure. `SetAffinity`: `OpenProcess(PROCESS_SET_INFORMATION | QUERY_LIMITED_INFORMATION)` + identity check + `SetProcessAffinityMask(mask)`; reference 3.3 on failure.
  - `internal sealed class ProcessorAffinityDialog : Form` matching dialog 34701 (reference 5.1): a fixed-size dialog titled `Processor affinity`, a static `Which processors are allowed to run "{0}"?`, a checkbox `ListView` (report, no header, `LVS_EX_CHECKBOXES`) whose first row is `<All Processors>` and then `CPU 0 … CPU {n-1}` for the system mask's set bits, and OK (default) / Cancel. `<All Processors>` checks and clears every CPU; checking all CPUs checks it. OK is grayed while no CPU is checked (reference 5.1 leaves the no-processor rule to the builder; this is my call). `DialogResult.OK` exposes the chosen mask as `nuint Mask`.
  - `DetailsView`: Set affinity (single selection only) reads `GetAffinity`; if null, `ErrorDialog.Show(Affinity, …)`; else shows `ProcessorAffinityDialog` (through a replaceable `Func` seam) seeded with the current mask; on OK, `SetAffinity(id, mask)` and, on failure, `ErrorDialog.Show(Affinity, …)`.

- [ ] **Step 1: Write the failing tests** (the dialog shown with `OffscreenForm`/hidden desktop; the list built in-process so its model is testable without showing):
  - `ProcessorAffinityDialogTests.Lists_all_processors_then_each_cpu`: for a system mask of 8 bits, 9 rows: `<All Processors>` then `CPU 0`…`CPU 7`.
  - `All_processors_toggles_every_cpu`: checking row 0 checks all CPUs; unchecking clears them; checking every CPU checks row 0.
  - `Ok_is_disabled_with_nothing_checked`.
  - `Seeded_mask_checks_the_current_cpus`: a seed mask of 0b1010 checks CPU 1 and CPU 3.
  - `Chosen_mask_reflects_the_checks`: checking CPU 0 and CPU 2 → `Mask == 0b101`.
  - `Thousand_open_and_cancel_stay_flat`: 1000× show-and-cancel on the hidden desktop; GDI/USER/handle counts flat (±5).
  - `ProcessActionsTests.Get_and_set_affinity_round_trip`: on a child, `SetAffinity` to a single CPU then `GetAffinity` reads that mask back.
- [ ] **Step 2: Run them.** `dotnet test --filter "ProcessorAffinityDialogTests|ProcessActionsTests"`. Expect the new ones to FAIL.
- [ ] **Step 3: Implement** the dialog, the affinity Win32 and the command.
- [ ] **Step 4: Run the whole suite.** `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: set processor affinity with Task Manager's dialog`.

---

### Task 9: Open file location, Properties, Copy

**Files:**
- Create: `src/Tmcs/Actions/ShellActions.cs`
- Modify: `src/Tmcs/Details/DetailsView.cs` (the three commands and their seams), `src/Tmcs/Native/Shell32.cs` (`SHOpenFolderAndSelectItems`, `ILCreateFromPathW`, `ILFree`, `SHObjectProperties`)
- Test: `tests/Tmcs.Tests/Actions/ShellActionsTests.cs`, `tests/Tmcs.Tests/Details/DetailsActionsTests.cs`

**Interfaces:**
- Consumes: the selected row's image path (`ProcessRow.Info.ImagePath`) and PID.
- Produces:
  - `internal static class ShellActions`:
    - `static void OpenFileLocation(string imagePath)`: `ILCreateFromPathW` then `SHOpenFolderAndSelectItems(pidl, 0, null, 0)`, `ILFree` in a `finally`. (reference 6)
    - `static void ShowProperties(IntPtr owner, string imagePath)`: `SHObjectProperties(owner, SHOP_FILEPATH, imagePath, null)`. (reference 6)
    - `static void CopyText(string text)`: WinForms `Clipboard.SetText`, catching `ExternalException` (the clipboard can be locked by another process) and ignoring it. (my call)
  - `DetailsView`: Open file location and Properties (single selection, grayed when no image path) call `ShellActions` through replaceable `Func`/`Action` seams. Copy ▸ PID copies the decimal PID; Copy ▸ Image path copies the image path and is grayed when there is none.

- [ ] **Step 1: Write the failing tests.**
  - `ShellActionsTests.Copy_text_sets_the_clipboard`: `CopyText("x")` on an STA thread leaves `Clipboard.GetText() == "x"`; a second call that simulates a locked clipboard (via a seam) does not throw.
  - `DetailsActionsTests.Copy_pid_copies_the_decimal_pid`: choosing Copy ▸ PID calls the copy seam with the PID as decimal text.
  - `DetailsActionsTests.Copy_image_path_copies_the_path` and `…_grayed_without_a_path`.
  - `DetailsActionsTests.Open_location_and_properties_use_the_image_path`: the two commands call their seams with the selected row's image path; both grayed when it is null (already covered by Task 4's enable test, re-asserted for the command path).
- [ ] **Step 2: Run them.** `dotnet test --filter "ShellActionsTests|DetailsActionsTests"`. Expect the new ones to FAIL.
- [ ] **Step 3: Implement** `ShellActions`, the native additions and the three commands.
- [ ] **Step 4: Run the whole suite.** `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: open file location, Properties and Copy`.

---

### Task 10: Run new task

**Files:**
- Create: `src/Tmcs/Actions/RunNewTask.cs`, `src/Tmcs/Actions/WdcLauncher.cs`
- Modify: `src/Tmcs/MainForm.cs` (File ▸ Run new task, the `WM_NOTIFY −511` handler), `src/Tmcs/Native/Shell32.cs` (ordinal 61 `RunFileDlg`, `SHCreateThread`), `src/Tmcs/Native/User32.cs` (`WM_NOTIFY`, `NMHDR`)
- Test: `tests/Tmcs.Tests/Actions/RunNewTaskTests.cs`, `tests/Tmcs.Tests/MainFormTests.cs`

**Interfaces:**
- Consumes: Task 1's probe results (P2/P3 and the −511 payload offsets); `IProcessActions.IsElevated` (Task 2).
- Produces:
  - `internal static class RunNewTask`:
    - `static void Show(IntPtr owner, IntPtr icon)`: computes the flags (reference 2.2) — base 0x14; +0x40 when not Safe Mode (`GetSystemMetrics(SM_CLEANBOOT) == 0`) **and** `WdcLauncher.IsAvailable` and not WOW64; +0x01 when `%SystemRoot%\System32\ShellStyle.dll` is missing; 0x80 omitted — and calls `RunFileDlg(owner, icon, GetCurrentDirectory(), title 37212, prompt 37213, flags)` on its own STA thread via `SHCreateThread(..., CTF_COINIT_STA = 8, ...)`. Title and prompt are tmcs's own copies of 37212/37213 (not loaded from a resource). tmcs passes its own icon.
    - A method `HandleRunNotify(IntPtr lParam) -> int` that reads the command (`+0x18`), directory (`+0x20`) and flags (`+0x2c`) at the probe-confirmed offsets and launches: `WdcLauncher.Run(cmd, dir, flags)` when available; otherwise, since a non-elevated tmcs is already at the user's level, start the process directly (`ShellExecuteEx`/`Process.Start` with the directory) — the Release path is settled by Task 1's P2. Returns 1 on success (dialog closes), 2 on failure (dialog stays, no error), per reference 2.4.
  - `internal static class WdcLauncher`: `static bool IsAvailable` (can resolve `wdc.dll!WdcRunTaskAsInteractiveUser`, loaded once and cached); `static int Run(string cmd, string dir, uint flags)` loads `wdc.dll`, resolves and calls the export, and frees the DLL (reference 2.5). The load uses `LoadLibraryExW(..., LOAD_LIBRARY_SEARCH_SYSTEM32)` (or the full `%SystemRoot%\System32\wdc.dll` path), never a bare name: M1's "System32 only" rule applies to runtime loads too, and the Release build is elevated.
  - `MainForm`: File menu gains `("Run &new task", CommandIds.RunNewTask)` before Exit (reference 1.1). `OnCommand(RunNewTask)` calls `RunNewTask.Show(Handle, Icon.Handle)`. `WndProc` answers `WM_NOTIFY` with code −511 by returning `RunNewTask.HandleRunNotify(m.LParam)` and setting `m.Result`; every other `WM_NOTIFY` is left to the base (reference 2.4).

- [ ] **Step 1: Write the failing tests.**
  - `RunNewTaskTests.Flags_add_the_checkbox_when_wdc_is_available`: with a fake `IsAvailable == true`, not Safe Mode, not WOW64 → 0x40 set; with `false` → not set. (Flag logic behind a seam so it needs no real dialog.)
  - `RunNewTaskTests.Flags_add_no_browse_when_shellstyle_is_missing`: a seam reporting ShellStyle.dll missing → 0x01 set.
  - `RunNewTaskTests.Notify_reads_the_payload_at_the_confirmed_offsets`: a crafted buffer with command/dir/flags at 0x18/0x20/0x2c → the launcher seam receives them; returns 1.
  - `RunNewTaskTests.Notify_returns_2_when_the_launch_fails`: the launcher seam fails → `HandleRunNotify` returns 2.
  - `MainFormTests.Run_new_task_menu_item_is_in_the_file_menu`: the File menu has Run new task before Exit.
  - `MainFormTests.Run_new_task_command_shows_the_dialog`: `OnCommand(RunNewTask)` calls the `RunNewTask.Show` seam once (no real dialog in the test).
- [ ] **Step 2: Run them.** `dotnet test --filter "RunNewTaskTests|MainFormTests"`. Expect the new ones to FAIL.
- [ ] **Step 3: Implement** `RunNewTask`, `WdcLauncher`, the native additions and the `MainForm` wiring, using Task 1's confirmed offsets and launch path. Put the flag computation and the launch behind seams so the tests above need no window.
- [ ] **Step 4: Run the whole suite.** `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: File menu Run new task with the shell Run dialog`.

---

### Task 11: Publish and milestone-3 acceptance

Run spec section 9's tests for the whole milestone. M1's and M2's results files show how each was run; follow them except where this task says otherwise. Milestone 3 adds no Performance panes, so the soak scenario is M2's, re-run to confirm the actions didn't regress the flatness. The actions' own leak tests are the 1000×-open-and-cancel tests in Tasks 3 and 8 and the live-end tests in Task 2, which `dotnet test` already runs.

**Files:**
- Modify: `docs/superpowers/results/2026-10-26-m3-actions.md` (begun in Task 6), `README.md` (the Run new task item and the Details actions)

- [ ] **Step 1: Build.** `dotnet test` (all PASS), `dotnet publish src/Tmcs -p:PublishProfile=win-x64`, `dotnet build -c Debug`, `dotnet build tools/launch-probe -c Release`.
- [ ] **Step 2: Ask first.** Tell the owner the churn soak will run for about 85 minutes with two tmcs windows behind their windows, as in M2. Wait for their OK.
- [ ] **Step 3: Churn + soak (spec tests 1–3).** Re-run M2's scenario and churn (`tools/churn.ps1`, the M2 scenario), non-elevated Debug then, after a UAC announcement and "ready", elevated published, writing `artifacts/soak/m3-debug.csv` and `artifacts/soak/m3-elevated.csv`. Score with `soak-report.ps1`; expect the same result as M2 (the GC heap-commit segment may still FAIL its private-bytes row). The Details tab now carries the row menu, the End task button and the Delete handler; the soak confirms they add no growth.
- [ ] **Step 4: Idle cost (spec test 4)** on the Details tab, as M1/M2: tmcs ≤ Task Manager. Announce each Task Manager change and wait for "ready".
- [ ] **Step 5: Icon dedup (spec test 5):** repeat M1's procedure; expect the same outcome.
- [ ] **Step 6: Fidelity (spec test 6).** Ask the owner to run `artifacts/publish/tmcs.exe` and compare, side by side with Task Manager: the row menu, each confirmation and error dialog, End task, End process tree, Delete, multi-select, the End task button, Set priority, Set affinity, Open file location, Properties, Copy, and Run new task (both admin-box states). Record their notes; fidelity fixes become follow-up tasks, never silent changes.
- [ ] **Step 7: Record the results.** Complete the results file: the soak tables, the idle-cost numbers, icon dedup, the probe outcomes (from Task 1), the fidelity notes, and the full "Calls for the owner to check" list.
  - On any FAIL, stop and use superpowers:systematic-debugging; fix it and re-run the failed check before going on.
- [ ] **Step 8: Update the README** with the Run new task item and the Details actions.
- [ ] **Step 9: Commit** with message `docs: milestone 3 acceptance results`. `artifacts/` stays untracked. Don't push without the owner's OK.
