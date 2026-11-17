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
        public Action? OnSaveAssets { get; set; }
        public Action? OnBeginGitOperation { get; set; }

        public int Count(string call) => Calls.Count(c => c == call);

        public void SaveAssets() { Calls.Add("SaveAssets"); OnSaveAssets?.Invoke(); }
        public bool ConfirmSaveModifiedScenes() { Calls.Add("ConfirmScenes"); return ConfirmScenesResult; }
        public void BeginGitOperation() { Calls.Add("Begin"); OnBeginGitOperation?.Invoke(); }
        public void EndGitOperation() => Calls.Add("End");
        public void RefreshAssets() => Calls.Add("RefreshAssets");
    }
}
