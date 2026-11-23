#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityGit.UI;

namespace UnityGit.Tests
{
    /// <summary>Package Manager for tests: what's installed, what was asked to install, and what it reports back.</summary>
    internal sealed class FakePackageHost : IPackageHost
    {
        public Dictionary<string, InstalledPackage> Installed { get; } = new Dictionary<string, InstalledPackage>();
        public List<IReadOnlyList<string>> Installs { get; } = new List<IReadOnlyList<string>>();
        /// <summary>What an install reports, in order. Registered alone by default.</summary>
        public List<(InstallReport report, string? message)> Reports { get; } = new List<(InstallReport, string?)> { (InstallReport.Registered, null) };
        public string EditorVersion { get; set; } = "6000.4.1f1";

        public InstalledPackage Find(string name)
            => Installed.TryGetValue(name, out var p) ? p : new InstalledPackage(name, PackageSource.Missing, null);

        public void Install(IReadOnlyList<string> identifiers, Action<InstallReport, string?> report)
        {
            Installs.Add(identifiers.ToList());
            foreach (var (r, message) in Reports) report(r, message);
        }
    }
}
