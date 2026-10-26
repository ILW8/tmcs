# Task Manager Details actions: reference

Found on 2026-10-26 by reading Task Manager statically on this machine. It holds what milestone 3 (spec 4.1 and 4.2) needs to recreate Run new task and the Details row actions 1:1.

- **Sources:**
  - `C:\Windows\System32\Taskmgr.exe` file version 10.0.19041.7663 (sha256 `fe9a1c2b…b33075f3`), decompiled with Ghidra.
  - `C:\Windows\System32\wdc.dll`, decompiled with Ghidra.
  - `en-US\Taskmgr.exe.mui` and `en-US\shell32.dll.mui`: string tables, MENUEX templates and DIALOGEX templates. They were read as data files with `LoadLibraryEx(LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE)`.
  - The scheduled task `\Microsoft\Windows\Task Manager\Interactive`, read with `schtasks /query /xml`.
- **Addresses** are Taskmgr.exe virtual addresses (image base 0x140000000) unless named otherwise. Functions are named by Ghidra (`FUN_…`).
- **Strings** are quoted exactly and carry their MUI string id, for example 32421. `%s` is the process name as the Name column shows it.
- **Scope:** this is reference only. tmcs recreates the text, layout and behavior. It never loads, copies or embeds Microsoft resources (spec 7). A resource id below says where a value came from. It is never something tmcs loads.
- **Confidence:** each fact is static unless marked **probe**, which means it must be confirmed at runtime before code depends on it. Section 9 lists the probes.

## 1. Command ids and menus

### 1.1 Main menu (menu 30205): File

| Item | Id |
|---|---|
| Run &new task | 30467 |
| E&xit | 30435 |

Run new task comes first, then Exit, with no separator between them.

### 1.2 Details row menu (menu 30200, "CPU Context")

The full template, in order. Items marked **out** are not in tmcs (spec 4.2). The spec adds Copy.

| Item | Id | tmcs |
|---|---|---|
| &End task | 30400 | in |
| End process &tree | 30401 | in |
| Provide feed&back | 44008 | out |
| — separator — | | in |
| Set &priority ▸ | 30494 | in |
| &nbsp;&nbsp;&Realtime | 30488 | in |
| &nbsp;&nbsp;&High | 30489 | in |
| &nbsp;&nbsp;&Above normal | 30490 | in |
| &nbsp;&nbsp;&Normal | 30491 | in |
| &nbsp;&nbsp;&Below normal | 30492 | in |
| &nbsp;&nbsp;&Low | 30493 | in |
| Set a&ffinity | 30410 | in |
| — separator — | | in |
| &Analyze wait chain | 30411 | out |
| &Debug | 30452 | out |
| UAC &virtualization | 30454 | out |
| &Create dump file | 30453 | out |
| — separator — | | out (it would follow an empty group) |
| &Open file location | 30455 | in |
| Search o&nline | 30422 | out |
| P&roperties | 30457 | in |
| Go to &service(s) | 30433 | out |

- With the out items removed, the shape is: End task, End process tree, separator, Set priority ▸, Set affinity, separator, Open file location, Properties.
- Task Manager has no Copy item. The spec adds Copy ▸ PID / Image path (spec 4.2). Its position and access keys are tmcs's call.
- The priority ids run from Realtime 30488 to Low 30493. They are contiguous, so `CheckMenuRadioItem` can take them as one range.

### 1.3 Header menu (menu 30201)

Hi&de column 30413 and &Select columns 30414. M1 already has these.

### 1.4 Other strings

| Id | Text | Use |
|---|---|---|
| 38001 | `&End task` | The Details tab's bottom-right button |
| 32255–32260 | `Low`, `Below normal`, `Normal`, `Above normal`, `High`, `Realtime` | Priority names (also the Base priority column) |

## 2. Run new task

### 2.1 The call

Task Manager shows the shell's Run dialog: `shell32` ordinal 61, `RunFileDlg`.

```
void RunFileDlg(HWND hwndOwner, HICON hIcon, LPCWSTR directory, LPCWSTR title, LPCWSTR prompt, UINT flags);
```

| Argument | Task Manager passes |
|---|---|
| hwndOwner | The main window |
| hIcon | Its own icon, resource 0x77bb. tmcs passes its own icon. |
| directory | `GetCurrentDirectory()` |
| title | 37212 `Create new task` |
| prompt | 37213 `Type the name of a program, folder, document, or Internet resource, and Windows will open it for you.` |
| flags | See 2.2 |

- **Thread:** the dialog runs on a thread of its own: `SHCreateThread(proc, data, 8, syncCallback)`. 8 is `CTF_COINIT_STA`. The UI thread keeps running while the dialog is open.
- **Ctrl:** with Ctrl held, Task Manager skips the dialog. It runs `%systemroot%\system32\cmd.exe` with `ShellExecuteEx` (fMask 0x8000600). The spec does not list this. It is not in tmcs unless the owner adds it.

### 2.2 Flags

The base value is 0x14. Task Manager adds:

| Flag | When | Effect |
|---|---|---|
| 0x01 | `FUN_14007c828()` is false. That function checks that `%SystemRoot%\System32\ShellStyle.dll` exists. | No Browse button. On a normal desktop the file exists, so Browse shows. |
| 0x40 | Not in Safe Mode (`GetSystemMetrics(SM_CLEANBOOT) == 0`, `FUN_140008d54`) **and** `wdc.dll!WdcRunTaskAsInteractiveUser` can be resolved (`FUN_140075d80` with its probe argument set). The flag is cleared under WOW64. | The dialog uses template 1011, which has the checkbox. |
| 0x80 | The global byte `DAT_14013a720` is set. `FUN_14007ca0c` toggles it, and that function then restarts Task Manager with ` /1`. | Unknown. tmcs leaves it out. |

### 2.3 The three shell32 templates (shell32.dll.mui, Segoe UI 9)

| Template | When | Size (DLU) | Difference |
|---|---|---|---|
| 1003 | No 0x40 | 227×93 | Plain dialog |
| 1005 | (not chosen by Task Manager's flags) | 235×105 | Static 12327 `This task will be created with administrative privileges.` and a 16×16 icon static at 12326 |
| 1011 | 0x40 | 235×105 | Checkbox 12307 `Create this task with administrative privileges.` at 40,50 180×10 |

All three share these controls:
- Icon 12297 at 7,11.
- Prompt 12289 at 36,11 182×22.
- `&Open:` 12305.
- Combo 12298 at 36,37.
- A hidden `Run in separate &memory space` 12306.
- OK, Cancel and `&Browse...` (12288).

The shell draws all of this. tmcs recreates nothing here. It passes the same arguments.

### 2.4 What happens on OK

- **Box checked:** the shell starts the task itself, in the dialog's process with that process's token. The Release tmcs runs elevated, so the task runs elevated.
- **Box unchecked:** the shell does not start the task. It sends `WM_NOTIFY` to `hwndOwner` with `code = -511` (0xfffffe01). Task Manager's main window procedure is `FUN_1400020c0`, and this branch is at 0x14004fdec. It does this:
  ```
  hr = FUN_140075d80(*(LPCWSTR*)(lParam + 0x18),   // the command line typed
                     *(LPCWSTR*)(lParam + 0x20),   // the working directory
                     *(DWORD*)  (lParam + 0x2c),   // passed on as the task's flags
                     0);                            // 0 = run, not just probe
  return hr < 0 ? 2 : 1;
  ```
  - `NMHDR` is 0x18 bytes on x64, so the payload starts right after it. The DWORD at 0x28 is not read.
  - Return 1 tells the shell the task was handled, and the dialog closes.
  - Return 2 keeps the dialog open. Task Manager shows no error then; it only writes a trace.
  - Every other `WM_NOTIFY` code returns 0 from this window procedure.
  - **Probe:** the payload layout (offsets 0x18, 0x20, 0x2c) and what the DWORD at 0x2c holds.
- `FUN_140075d80` loads `wdc.dll` for each call, resolves `WdcRunTaskAsInteractiveUser`, calls it, and frees the DLL.

### 2.5 `wdc.dll!WdcRunTaskAsInteractiveUser`

```
HRESULT WdcRunTaskAsInteractiveUser(LPCWSTR commandLine, LPCWSTR directory, DWORD flags);
```

Steps, from wdc.dll:
1. `CoCreateInstance(CLSID_TaskScheduler)` → `ITaskService::Connect()`.
2. `ProcessIdToSessionId(GetCurrentProcessId())`, then `WTSQuerySessionInformation` for `WTSUserName` (5) and `WTSDomainName` (7).
3. Builds the argument `"%08x|%s\%s|%s|%s"`: flags | domain\user | commandLine | directory.
4. `GetFolder("\Microsoft\Windows\Task Manager")` → `GetTask("Interactive")` → `RunEx(argument, TASK_RUN_USE_SESSION_ID (4), sessionId, NULL)`.

Task Manager also uses this export to restart Explorer: `(L"%windir%\\explorer.exe", NULL, 9)`. In that call the flags look like an `nShow` value (9 = `SW_RESTORE`). **Probe.**

The task exists on this machine:

| Field | Value |
|---|---|
| Principal | The Interactive group, `S-1-5-4` (GroupId) |
| Action | COM handler `{855FEC53-D2E4-4999-9E87-3414E9CF0FF4}`, Data `$(Arg0)` |
| Settings | Hidden; MultipleInstancesPolicy Parallel |
| Security descriptor | `O:BAG:BAD:P(A;;FA;;;BA)(A;;FA;;;SY)(A;;FR;;;IU)` |

- The task runs as the signed-in user, without elevation. It needs no Explorer.
- Administrators and SYSTEM have full access. Interactive users have read only. **Probe:** can a non-elevated caller (the Debug build) run it? If not, a non-elevated tmcs is already at the user's level and can start the task itself.

## 3. Ending a process

### 3.1 Confirmation (`FUN_14008adc0` → `FUN_14008b534` → `TaskDialogIndirect`)

Every confirmation in Task Manager uses the same `TASKDIALOGCONFIG`:
- `dwFlags` 0.
- `dwCommonButtons` = `TDCBF_CANCEL_BUTTON`.
- `pszWindowTitle` 32420 `Task Manager`.
- No main icon.
- One custom button with id 1, which is also `nDefaultButton`.
- The result is the id of the pressed button. 1 confirms; anything else cancels.

| Case | Main instruction | Content | Button |
|---|---|---|---|
| End task, one row | 32421 `Do you want to end %s?` | 32422 `If an open program is associated with this process, it will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?` | 32423 `End process` |
| End task, several rows | 32427 `Do you want to end the selected processes?` | 32428 `If open programs are associated with the selected processes, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?` | 32423 `End process` |
| End process tree, one row | 32424 `Do you want to end the process tree of %s?` | 32425 `If open programs or processes are associated with this process tree, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?` | 32426 `End process tree` |
| End process tree, several rows | 32429 `Do you want to end the selected process trees?` | 32430 `If open programs or processes are associated with the selected process trees, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?` | 32426 `End process tree` |
| Either command, a critical system process | 32443 `Do you want to end the system process '%s'?` | 32444 `Ending this process will cause Windows to become unusable or shut down, causing you to lose any data hasn't been saved. Are you sure you want to continue?` | 32445 `Shut down` |

- The critical-process dialog also sets `pszVerificationText` 32446 `Abandon unsaved data and shut down.` with callback `FUN_14008b620`. Most likely the callback enables the button only while the box is checked. **Probe** (by eye, in Task Manager).
- 32444 has a grammar slip ("any data hasn't been saved"). It is Task Manager's text, kept as is.
- **Critical process:** this is the process's break-on-termination flag, `NtQueryInformationProcess(ProcessBreakOnTermination)`. Its use in this dialog was not traced statically. It is the builder's call.

### 3.2 The end-task worker (`FUN_1400d8fe8`, endtask.cpp)

1. `OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION (0x1001), FALSE, pid)`.
2. Check that the creation time matches the row's. A PID that was reused is never ended.
3. `TerminateProcess(h, 1)`. The exit code is **1**.
4. If the open fails, show the error dialog (3.3) with the title for this command.
5. If `TerminateProcess` itself fails with access denied, Task Manager shows no dialog. Instead it lists `SystemHandleInformation` and closes the target's File handles. **tmcs does not copy this.** It shows the error dialog instead (the builder's call).
6. It restarts Explorer only for the Processes tab's Restart command, which is not in tmcs.

### 3.3 The error dialog (`FUN_14008aa5c` → `FUN_14008b93c` → `TaskDialog`)

```
TaskDialog(hwndOwner, hInstance, title, mainInstruction, content, TDCBF_OK_BUTTON, TD_ERROR_ICON, NULL);
```

| Title (by command) | Id |
|---|---|
| `Unable to terminate process` | 37227 |
| `Unable to terminate process tree` | 37228 |
| `Unable to change priority` | 37230 |
| `Unable to access or set process affinity` | 37231 |

| Condition | Main instruction | Content |
|---|---|---|
| Not elevated, and the error is `E_ACCESSDENIED` | 37245 `Access denied.` | 37244 `You need administrator privileges to complete this task. Close Task Manager, then run it as an administrator and try again.` |
| The error is `E_INVALIDARG` | 37225 `The operation could not be completed.` | 37226 `The operation is not valid for this process.` |
| Any other error, or elevated | 37225 `The operation could not be completed.` | `FormatMessage(hr)`, for example `Access is denied.` |

The Release tmcs always runs elevated, so its access-denied case is the third row. The Debug build is not elevated and shows the first row.

## 4. Set priority

- **Confirmation:** the dialog of 3.1 with main instruction 32447 `Do you want to change the priority of '%s'?`, content 32448 `Changing the priority of certain processes could cause system instability.`, and button 32449 `Change priority`.
- **Error:** the dialog of 3.3 with title 37230.
- **Realtime:** strings 32450 `Unable to change priority` and 32451 `Unable to set priority to Realtime. The priority was set to High instead.` exist. The second most likely shows when the class reads back as High after a Realtime request, which happens without `SeIncreaseBasePriorityPrivilege`. The elevated Release build holds that privilege. **Probe** (the Debug build).
- **Priority classes:** Realtime `REALTIME_PRIORITY_CLASS`, High `HIGH_PRIORITY_CLASS`, Above normal `ABOVE_NORMAL_PRIORITY_CLASS`, Normal `NORMAL_PRIORITY_CLASS`, Below normal `BELOW_NORMAL_PRIORITY_CLASS`, Low `IDLE_PRIORITY_CLASS`.
- **Base priority:** the snapshot's base priorities for these classes are 24, 13, 10, 8, 6 and 4.

## 5. Set affinity

There is no confirmation. The error dialog of 3.3 uses title 37231.

### 5.1 Dialog 34701 "Processor affinity" (one processor group)

- Size 202×178 DLU at 0,0.
- Style `0x80c800c8`: `WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME | DS_SETFONT | DS_FIXEDSYS`, that is `DS_SHELLFONT`.
- Font: 8 pt `MS Shell Dlg`.

| Control | Id | Rect (DLU) | Style | Text |
|---|---|---|---|---|
| STATIC | 34702 | 7,4 188×19 | `0x50020000` (`SS_LEFT \| WS_GROUP`) | `Which processors are allowed to run "%s"?` |
| SysListView32 | 34703 | 7,28 188×120 | `0x5081c08d`: `WS_BORDER \| WS_TABSTOP`, `LVS_REPORT \| LVS_SINGLESEL \| LVS_SHOWSELALWAYS \| LVS_NOLABELWRAP \| LVS_NOCOLUMNHEADER \| LVS_NOSORTHEADER` | `Processors` |
| BUTTON OK | 1 | 90,157 50×14 | `BS_DEFPUSHBUTTON` | `OK` |
| BUTTON Cancel | 2 | 145,157 50×14 | push button | `Cancel` |

- **List rows:** the first row is 34707 `<All Processors>`, then one row per logical processor: 34705 `CPU %1!u!`, that is `CPU 0` … `CPU 31` on this machine.
- **Checkboxes:** set at runtime, most likely with `LVS_EX_CHECKBOXES`.
- Strings 34706 `CPU %2!u! (Node %1!d!)` and 34708 `Group %1!u!` belong to the NUMA and multi-group variants.
- **Variants:**
  - Dialog 34700 is the multi-group variant. It adds `Processor group:` and a combo, and the list moves to 7,49 188×99.
  - Dialog 30130 "Select processors" has the same layout, for another use.
  - This machine has one group, so tmcs needs only 34701.
- **No-processor rule:** the MUI has no string for "at least one processor". What OK does with nothing checked was not traced. It is the builder's call.

## 6. Open file location, Properties, Copy

The handlers were not traced. These are the standard shell calls, and their use is the builder's call:
- **Open file location:** `SHOpenFolderAndSelectItems` on the image path.
- **Properties:** `SHObjectProperties(hwnd, SHOP_FILEPATH, path, NULL)`, or `ShellExecuteEx` with the `properties` verb.
- **Copy:** a tmcs addition with no Task Manager counterpart.

## 7. Identity at action time

Every action acts on a `ProcessIdentity(Pid, CreateTime)`, never on a bare PID. Task Manager re-checks the creation time after it opens the process (3.2). M1's `ProcessInfoSource.QueryCore` does the same check.

## 8. What is not here

- Pixel captures of the dialogs and of the End task button. The TaskDialogs and the Run dialog are drawn by Windows from the arguments above, so they need no capture.
- The End task button's geometry and look, and the affinity dialog as rendered, need an owner screenshot, an approved capture, or the builder's call.
- The rule Task Manager uses to enable or gray each row-menu item when several rows are selected, or when a process cannot be opened.

## 9. Probes (runtime, before code depends on them)

1. The `WM_NOTIFY -511` payload: offsets 0x18, 0x20 and 0x2c, and the meaning of the DWORD at 0x2c.
2. Whether a non-elevated caller can run `\Microsoft\Windows\Task Manager\Interactive` through `WdcRunTaskAsInteractiveUser`.
3. What the flags argument of `WdcRunTaskAsInteractiveUser` does (it looks like `nShow`).
4. What run-dialog flag 0x80 does.
5. The critical-process verification checkbox (3.1).
6. When 32451 shows (4).
