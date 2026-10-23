# tmcs

tmcs is a small, original Windows 10 Task Manager replacement written in C# (WinForms, .NET 10). It exists because the stock Task Manager leaks icons and handles when left running for days; tmcs lists processes in a virtual-mode Details view and releases per-process state, including icons, on the same tick a process exits.

```
dotnet build
dotnet test
```

Debug builds run as the invoking user (no UAC prompt); Release builds request administrator rights.

The design is in [docs/superpowers/specs/2026-10-23-tmcs-design.md](docs/superpowers/specs/2026-10-23-tmcs-design.md).
