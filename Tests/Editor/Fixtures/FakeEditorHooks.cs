#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityGit.Tests
{
    internal sealed class FakeEditorHooks : IEditorHooks
    {
        public List<string> Calls { get; } = new List<string>();
        public bool ConfirmScenesResult { get; set; } = true;
        public bool ConfirmResult { get; set; } = true;
        public string? LastConfirmMessage { get; private set; }
        public Action? OnSaveAssets { get; set; }
        public Action? OnBeginGitOperation { get; set; }
        public Action? OnEndGitOperation { get; set; }

        public int Count(string call) => Calls.Count(c => c == call);

        public void SaveAssets() { Calls.Add("SaveAssets"); OnSaveAssets?.Invoke(); }
        public bool ConfirmSaveModifiedScenes() { Calls.Add("ConfirmScenes"); return ConfirmScenesResult; }
        public void BeginGitOperation() { Calls.Add("Begin"); OnBeginGitOperation?.Invoke(); }
        public void EndGitOperation() { Calls.Add("End"); OnEndGitOperation?.Invoke(); }
        public bool Confirm(string title, string message, string ok) { Calls.Add("Confirm:" + title); LastConfirmMessage = message; return ConfirmResult; }
        public void RefreshAssets() => Calls.Add("RefreshAssets");
    }
}
