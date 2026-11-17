// Vendored from the EditorUI kit (EditorExtension.EditorUI); only namespace, visibility and asset loading changed.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;


namespace UnityGit.UI.Theme
{
    internal static class EditorUiElements
    {
        private const float c_percent = 100f;
        private const int c_zebraPeriod = 2;
        private const string c_messageCloseMark = "✕";
        private const string c_messageCloseTooltip = "Close";

        public static VisualElement SectionHeader(string title, string subtitle = null)
        {
            var header = new VisualElement();
            header.AddToClassList(EditorUiClasses.SectionHeader);

            var titleLabel = new Label(title);
            titleLabel.AddToClassList(EditorUiClasses.SectionTitle);
            header.Add(titleLabel);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.AddToClassList(EditorUiClasses.SectionSubtitle);
                header.Add(subtitleLabel);
            }

            return header;
        }

        public static VisualElement Toolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList(EditorUiClasses.Toolbar);
            return toolbar;
        }

        public static VisualElement ToolbarSpacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList(EditorUiClasses.ToolbarSpacer);
            return spacer;
        }

        public static EditorUiCard Card(string title = null)
        {
            return new EditorUiCard(title);
        }

        public static Label Badge(string text, EditorUiTone tone)
        {
            var badge = new Label();
            badge.AddToClassList(EditorUiClasses.Badge);
            SetBadge(badge, text, tone);
            return badge;
        }

        public static void SetBadge(Label badge, string text, EditorUiTone tone)
        {
            badge.text = text;
            SetTone(badge, EditorUiClasses.BadgeTones, tone);
        }

        public static VisualElement Message(string text, EditorUiTone tone)
        {
            var message = new VisualElement();
            message.AddToClassList(EditorUiClasses.Message);

            var label = new Label();
            label.AddToClassList(EditorUiClasses.MessageText);
            message.Add(label);

            SetMessage(message, text, tone);
            return message;
        }

        public static VisualElement ClosableMessage(string text, EditorUiTone tone, Action onClosed = null)
        {
            VisualElement message = Message(text, tone);

            var closeButton = new Button(() => CloseMessage(message, onClosed));
            closeButton.text = c_messageCloseMark;
            closeButton.tooltip = c_messageCloseTooltip;
            closeButton.AddToClassList(EditorUiClasses.MessageClose);
            message.Add(closeButton);

            return message;
        }

        public static void SetMessage(VisualElement message, string text, EditorUiTone tone)
        {
            var label = message.Q<Label>(className: EditorUiClasses.MessageText);

            if (label == null)
            {
                Debug.LogError($"[{nameof(EditorUiElements)}] '{message.name}' is not a {nameof(Message)}: no '{EditorUiClasses.MessageText}' label.");
                return;
            }

            label.text = text;
            SetTone(message, EditorUiClasses.MessageTones, tone);
        }

        public static Label Chip(string text, EditorUiTone tone = EditorUiTone.Neutral)
        {
            var chip = new Label(text);
            chip.AddToClassList(EditorUiClasses.Chip);
            SetTone(chip, EditorUiClasses.ChipTones, tone);
            return chip;
        }

        public static VisualElement Meter(float fraction, EditorUiTone tone)
        {
            var meter = new VisualElement();
            meter.AddToClassList(EditorUiClasses.Meter);

            var fill = new VisualElement();
            fill.AddToClassList(EditorUiClasses.MeterFill);
            meter.Add(fill);

            SetMeter(meter, fraction, tone);
            return meter;
        }

        public static void SetMeter(VisualElement meter, float fraction)
        {
            var fill = meter.Q(className: EditorUiClasses.MeterFill);

            if (fill == null)
            {
                Debug.LogError($"[{nameof(EditorUiElements)}] '{meter.name}' is not a {nameof(Meter)}: no '{EditorUiClasses.MeterFill}' element.");
                return;
            }

            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * c_percent);
        }

        public static void SetMeter(VisualElement meter, float fraction, EditorUiTone tone)
        {
            SetTone(meter, EditorUiClasses.MeterTones, tone);
            SetMeter(meter, fraction);
        }

        public static VisualElement Table(IReadOnlyList<string> headers, IReadOnlyList<bool> numericColumns)
        {
            var table = new VisualElement();
            table.AddToClassList(EditorUiClasses.Table);

            var header = new VisualElement();
            header.AddToClassList(EditorUiClasses.TableRow);
            header.AddToClassList(EditorUiClasses.TableHeader);

            for (int i = 0; i < headers.Count; ++i)
            {
                var cell = new Label(headers[i]);
                cell.AddToClassList(EditorUiClasses.TableCell);
                cell.AddToClassList(EditorUiClasses.TableHeaderCell);
                cell.EnableInClassList(EditorUiClasses.TableHeaderCellNumeric, IsNumeric(numericColumns, i));
                header.Add(cell);
            }

            table.Add(header);
            return table;
        }

        public static VisualElement AddTableRow(VisualElement table, IReadOnlyList<string> cells)
        {
            var header = table.Q(className: EditorUiClasses.TableHeader);
            int dataRowIndex = header != null ? table.childCount - 1 : table.childCount;

            var row = new VisualElement();
            row.AddToClassList(EditorUiClasses.TableRow);
            row.EnableInClassList(EditorUiClasses.TableRowAlt, dataRowIndex % c_zebraPeriod == 1);

            for (int i = 0; i < cells.Count; ++i)
            {
                var cell = new Label(cells[i]);
                cell.AddToClassList(EditorUiClasses.TableCell);
                cell.EnableInClassList(EditorUiClasses.TableCellNumeric, IsNumericHeader(header, i));
                row.Add(cell);
            }

            table.Add(row);
            return row;
        }

        private static void CloseMessage(VisualElement message, Action onClosed)
        {
            message.RemoveFromHierarchy();
            onClosed?.Invoke();
        }

        private static bool IsNumeric(IReadOnlyList<bool> numericColumns, int column)
        {
            return numericColumns != null && column < numericColumns.Count && numericColumns[column];
        }

        private static bool IsNumericHeader(VisualElement header, int column)
        {
            if (header == null || column >= header.childCount)
                return false;

            var headerCell = header[column];
            return headerCell.ClassListContains(EditorUiClasses.TableHeaderCellNumeric);
        }

        private static void SetTone(VisualElement element, IReadOnlyList<string> toneClasses, EditorUiTone tone)
        {
            for (int i = 0; i < toneClasses.Count; ++i)
                element.RemoveFromClassList(toneClasses[i]);

            element.AddToClassList(toneClasses[(int)tone]);
        }
    }
}
