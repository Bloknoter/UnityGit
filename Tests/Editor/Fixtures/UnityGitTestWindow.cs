#nullable enable
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace UnityGit.Tests
{
    /// <summary>
    /// A real editor window for tests that need a panel. UI Toolkit dispatches events (ChangeEvent, KeyDownEvent) only to
    /// elements in a panel, and resolves styles only there. Dispose it to close the window.
    /// </summary>
    internal sealed class UnityGitTestWindow : EditorWindow, IDisposable
    {
        public static UnityGitTestWindow Open(VisualElement content)
        {
            var window = CreateInstance<UnityGitTestWindow>();
            window.position = new Rect(20, 20, 900, 600);
            WithoutGraphicsDeviceErrors(window.Show);
            window.rootVisualElement.Add(content);
            Assert.That(content.panel, Is.Not.Null, "setup: the content is in a panel");
            return window;
        }

        /// <summary>
        /// Resolves styles and layout now, as a repaint would; batch mode never repaints. This calls the panel's
        /// ValidateLayout, which is not public API, so the test is ignored on a Unity version without it.
        /// </summary>
        public void ResolveStylesAndLayout()
        {
            var panel = rootVisualElement.panel;
            var validate = panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (validate == null) Assert.Ignore("This Unity version's panel has no ValidateLayout() to resolve styles with.");
            validate!.Invoke(panel, null);
        }

        public void Dispose() => WithoutGraphicsDeviceErrors(Close);

        // With -nographics, Unity logs "No graphic device is available to initialize the view / show the window" while
        // it builds and tears down the window's views; the window still gets a working panel. Only those calls ignore
        // error logs, and only when there is no graphics device.
        static void WithoutGraphicsDeviceErrors(Action action)
        {
            var before = LogAssert.ignoreFailingMessages;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) LogAssert.ignoreFailingMessages = true;
            try
            {
                action();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = before;
            }
        }
    }
}
