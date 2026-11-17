// Vendored from the EditorUI kit (EditorExtension.EditorUI); only namespace, visibility and asset loading changed.
using System.Collections.Generic;


namespace UnityGit.UI.Theme
{
    internal static class EditorUiClasses
    {
        public const string Root = "ed-root";

        public const string Toolbar = "ed-toolbar";
        public const string ToolbarSpacer = "ed-toolbar__spacer";

        public const string Section = "ed-section";
        public const string SectionHeader = "ed-section-header";
        public const string SectionTitle = "ed-section-header__title";
        public const string SectionSubtitle = "ed-section-header__subtitle";

        public const string Card = "ed-card";
        public const string CardHeader = "ed-card__header";
        public const string CardTitle = "ed-card__title";
        public const string CardContent = "ed-card__content";

        public const string Badge = "ed-badge";
        public const string BadgeNeutral = "ed-badge--neutral";
        public const string BadgeAccent = "ed-badge--accent";
        public const string BadgeSuccess = "ed-badge--success";
        public const string BadgeWarning = "ed-badge--warning";
        public const string BadgeError = "ed-badge--error";
        public const string BadgeInfo = "ed-badge--info";

        public const string Message = "ed-message";
        public const string MessageText = "ed-message__text";
        public const string MessageClose = "ed-message__close";
        public const string MessageNeutral = "ed-message--neutral";
        public const string MessageAccent = "ed-message--accent";
        public const string MessageSuccess = "ed-message--success";
        public const string MessageWarning = "ed-message--warning";
        public const string MessageError = "ed-message--error";
        public const string MessageInfo = "ed-message--info";

        public const string Chip = "ed-chip";
        public const string ChipRow = "ed-chip-row";
        public const string ChipNeutral = "ed-chip--neutral";
        public const string ChipAccent = "ed-chip--accent";
        public const string ChipSuccess = "ed-chip--success";
        public const string ChipWarning = "ed-chip--warning";
        public const string ChipError = "ed-chip--error";
        public const string ChipInfo = "ed-chip--info";

        public const string Table = "ed-table";
        public const string TableHeader = "ed-table__header";
        public const string TableHeaderCell = "ed-table__header-cell";
        public const string TableHeaderCellNumeric = "ed-table__header-cell--numeric";
        public const string TableRow = "ed-table__row";
        public const string TableRowAlt = "ed-table__row--alt";
        public const string TableCell = "ed-table__cell";
        public const string TableCellNumeric = "ed-table__cell--numeric";

        public const string Meter = "ed-meter";
        public const string MeterFill = "ed-meter__fill";
        public const string MeterNeutral = "ed-meter--neutral";
        public const string MeterAccent = "ed-meter--accent";
        public const string MeterSuccess = "ed-meter--success";
        public const string MeterWarning = "ed-meter--warning";
        public const string MeterError = "ed-meter--error";
        public const string MeterInfo = "ed-meter--info";

        public const string ListCell = "ed-list-cell";
        public const string ListCellNumeric = "ed-list-cell--numeric";

        public const string Divider = "ed-divider";

        public const string Muted = "ed-muted";
        public const string Faint = "ed-faint";
        public const string Strong = "ed-strong";
        public const string Mono = "ed-mono";
        public const string Small = "ed-small";

        public const string ButtonPrimary = "ed-button--primary";
        public const string ButtonDanger = "ed-button--danger";

        public const string Row = "ed-row";
        public const string Stack = "ed-stack";
        public const string Grow = "ed-grow";
        public const string Gap1 = "ed-gap-1";
        public const string Gap2 = "ed-gap-2";
        public const string Gap3 = "ed-gap-3";
        public const string Gap4 = "ed-gap-4";

        private static readonly string[] m_badgeTones =
            { BadgeNeutral, BadgeAccent, BadgeSuccess, BadgeWarning, BadgeError, BadgeInfo };

        private static readonly string[] m_messageTones =
            { MessageNeutral, MessageAccent, MessageSuccess, MessageWarning, MessageError, MessageInfo };

        private static readonly string[] m_chipTones =
            { ChipNeutral, ChipAccent, ChipSuccess, ChipWarning, ChipError, ChipInfo };

        private static readonly string[] m_meterTones =
            { MeterNeutral, MeterAccent, MeterSuccess, MeterWarning, MeterError, MeterInfo };

        internal static IReadOnlyList<string> BadgeTones => m_badgeTones;
        internal static IReadOnlyList<string> MessageTones => m_messageTones;
        internal static IReadOnlyList<string> ChipTones => m_chipTones;
        internal static IReadOnlyList<string> MeterTones => m_meterTones;

        public static string BadgeTone(EditorUiTone tone)
        {
            return m_badgeTones[(int)tone];
        }

        public static string MessageTone(EditorUiTone tone)
        {
            return m_messageTones[(int)tone];
        }

        public static string ChipTone(EditorUiTone tone)
        {
            return m_chipTones[(int)tone];
        }

        public static string MeterTone(EditorUiTone tone)
        {
            return m_meterTones[(int)tone];
        }
    }
}
