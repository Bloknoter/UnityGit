#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// The Add rule window's content (spec "Add rule"): an optional comment, the extension, folder, file or custom rule,
    /// and for all but Custom the Negate and "Also ignore .meta" toggles. Add stays disabled until the rule is valid; the
    /// reason shows once the value has been edited. Enter adds (Ctrl/Cmd+Enter in a custom rule), Escape cancels.
    /// </summary>
    internal sealed class GitignoreRuleForm : VisualElement
    {
        readonly GitignoreRuleKind _kind;
        readonly string _folder;
        readonly string _project;
        readonly Action<IReadOnlyList<string>> _onAdd;
        readonly Action _onCancel;
        bool _edited;

        public GitignoreRuleForm(GitignoreRuleKind kind, string gitignoreFolder, string projectPath,
            Action<IReadOnlyList<string>> onAdd, Action onCancel)
        {
            _kind = kind;
            _folder = gitignoreFolder;
            _project = projectPath;
            _onAdd = onAdd;
            _onCancel = onCancel;
            AddToClassList("unitygit-gitignore-rule-form");

            Comment = new TextField(GitignoreText.Comment);
            Comment.AddToClassList("unitygit-gitignore-rule-field");
            Add(Comment);

            Value = new TextField(ValueLabel(kind)) { multiline = kind == GitignoreRuleKind.Custom };
            Value.AddToClassList("unitygit-gitignore-rule-field");
            Value.AddToClassList(EditorUiClasses.Grow);
            if (kind == GitignoreRuleKind.Custom) Value.AddToClassList("unitygit-gitignore-rule-custom");
            var valueRow = new VisualElement();
            valueRow.AddToClassList("unitygit-gitignore-rule-value");
            valueRow.Add(Value);
            if (kind == GitignoreRuleKind.Folder || kind == GitignoreRuleKind.File)
                valueRow.Add(new Button(Browse) { text = GitignoreText.Browse });
            Add(valueRow);

            if (kind != GitignoreRuleKind.Custom)
            {
                Negate = new Toggle(GitignoreText.Negate);
                IncludeMeta = new Toggle(GitignoreText.AlsoMeta) { value = true };
                Add(Negate);
                Add(IncludeMeta);
                Negate.RegisterValueChangedCallback(_ => Refresh());
                IncludeMeta.RegisterValueChangedCallback(_ => Refresh());
            }
            NegateHint = PanelParts.Text(GitignoreText.NegateHint, EditorUiClasses.Muted, EditorUiClasses.Small, "unitygit-gitignore-rule-hint");
            Add(NegateHint);
            Error = PanelParts.Text(string.Empty, EditorUiClasses.Small, "unitygit-gitignore-rule-error");
            Add(Error);

            var buttons = new VisualElement();
            buttons.AddToClassList("unitygit-gitignore-rule-buttons");
            buttons.Add(new Button(() => _onCancel()) { text = GitignoreText.Cancel });
            AddButton = new Button(Submit) { text = GitignoreText.Add };
            AddButton.AddToClassList(EditorUiClasses.ButtonPrimary);
            buttons.Add(AddButton);
            Add(buttons);

            Comment.RegisterValueChangedCallback(_ => Refresh());
            Value.RegisterValueChangedCallback(_ =>
            {
                _edited = true;
                Refresh();
            });
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown); // before the text box takes the key
            Refresh();
        }

        internal TextField Comment { get; }

        /// <summary>The extension, folder, file or rule; multiline for a custom rule.</summary>
        internal TextField Value { get; }

        /// <summary>Null for a custom rule.</summary>
        internal Toggle? Negate { get; }

        /// <summary>Null for a custom rule; displayed only while the .meta rule applies.</summary>
        internal Toggle? IncludeMeta { get; }

        internal Button AddButton { get; }

        internal Label Error { get; }

        internal Label NegateHint { get; }

        internal GitignoreRuleResult Result => GitignoreRules.Build(_kind, Comment.value, Value.value, Negate?.value ?? false,
            IncludeMeta?.value ?? false, _folder, _project);

        /// <summary>Adds the rule when it's valid.</summary>
        internal void Submit()
        {
            var result = Result;
            if (result.Error == null) _onAdd(result.Lines);
        }

        void Refresh()
        {
            var result = Result;
            AddButton.SetEnabled(result.Error == null);
            Error.text = result.Error ?? string.Empty;
            PanelParts.SetDisplayed(Error, _edited && result.Error != null);
            PanelParts.SetDisplayed(NegateHint, Negate?.value == true);
            if (IncludeMeta != null) PanelParts.SetDisplayed(IncludeMeta, GitignoreRules.MetaApplies(_kind, Value.value, _folder, _project));
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                e.StopPropagation();
                _onCancel();
                return;
            }
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            var inCustomRule = Value.multiline && e.target is VisualElement target && (target == Value || Value.Contains(target));
            if (inCustomRule && !e.actionKey) return; // a new line in the rule
            e.StopPropagation();
            Submit();
        }

        void Browse()
        {
            var picked = _kind == GitignoreRuleKind.Folder
                ? EditorUtility.OpenFolderPanel("Folder to ignore", _folder, string.Empty)
                : EditorUtility.OpenFilePanel("File to ignore", _folder, string.Empty);
            if (!string.IsNullOrEmpty(picked)) Value.value = picked;
        }

        static string ValueLabel(GitignoreRuleKind kind)
        {
            switch (kind)
            {
                case GitignoreRuleKind.Extension: return GitignoreText.Extension;
                case GitignoreRuleKind.Folder: return GitignoreText.Folder;
                case GitignoreRuleKind.File: return GitignoreText.File;
                default: return GitignoreText.Rule;
            }
        }
    }
}
