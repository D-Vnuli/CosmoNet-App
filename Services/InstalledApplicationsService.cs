using System.Diagnostics;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class InstalledApplicationsService
{
    private static readonly string[] NoisyProcessNames =
    [
        "conhost.exe",
        "dllhost.exe",
        "svchost.exe",
        "unins000.exe",
        "uninstall.exe",
        "updater.exe"
    ];

    public Task<IReadOnlyList<InstalledApplication>> LoadApplicationsAsync(
        IEnumerable<string> selectedProcessNames,
        CancellationToken cancellationToken = default)
    {
        var selected = selectedProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Task.Run(() => LoadApplications(selected, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<InstalledApplication> LoadApplications(
        ISet<string> selectedProcessNames,
        CancellationToken cancellationToken)
    {
        var byProcess = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in GetRunningApplications(cancellationToken).Concat(GetProgramFilesApplications(cancellationToken)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(app.ProcessName) || NoisyProcessNames.Contains(app.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!byProcess.ContainsKey(app.ProcessName))
            {
                byProcess[app.ProcessName] = app;
            }
        }

        foreach (var processName in selectedProcessNames)
        {
            if (!byProcess.ContainsKey(processName))
            {
                byProcess[processName] = new InstalledApplication
                {
                    DisplayName = Path.GetFileNameWithoutExtension(processName),
                    ProcessName = processName,
                    IsSelected = true
                };
            }
        }

        return byProcess.Values
            .Select(app => new InstalledApplication
            {
                DisplayName = app.DisplayName,
                ProcessName = app.ProcessName,
                Path = app.Path,
                IsSelected = selectedProcessNames.Contains(app.ProcessName)
            })
            .OrderByDescending(app => app.IsSelected)
            .ThenBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(180)
            .ToList();
    }

    private static IEnumerable<InstalledApplication> GetRunningApplications(CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (process)
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // Some system processes deny module access; they are not useful for user routing.
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var processName = Path.GetFileName(path);
                yield return new InstalledApplication
                {
                    DisplayName = Path.GetFileNameWithoutExtension(path),
                    ProcessName = processName,
                    Path = path
                };
            }
        }
    }

    private static IEnumerable<InstalledApplication> GetProgramFilesApplications(CancellationToken cancellationToken)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 4
            };

            foreach (var path in Directory.EnumerateFiles(root, "*.exe", options).Take(120))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var processName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                yield return new InstalledApplication
                {
                    DisplayName = Path.GetFileNameWithoutExtension(path),
                    ProcessName = processName,
                    Path = path
                };
            }
        }
    }
}
