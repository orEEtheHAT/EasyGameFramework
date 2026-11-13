using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace EasyLocalization.Editor
{
    public enum LocalizationIssueSeverity { Info, Warning, Error }

    public readonly struct LocalizationIssue
    {
        public string Code { get; }
        public string Subject { get; }
        public string Message { get; }
        public LocalizationIssueSeverity Severity { get; }
        public LocalizationIssue(string code, string subject, string message, LocalizationIssueSeverity severity)
        { Code = code; Subject = subject; Message = message; Severity = severity; }
    }

    /// <summary>Read-only checks against the standard source tables. No runtime database, network, or automatic repair.</summary>
    public static class LocalizationDiagnostics
    {
        public static IReadOnlyList<LocalizationIssue> Inspect(LocalizedString text, string subject = "Text")
        {
            var issues = new List<LocalizationIssue>();
            InspectReference(text, subject, issues);
            return issues;
        }

        public static IReadOnlyList<LocalizationIssue> Inspect(LocalizedCatalog catalog)
        {
            var issues = new List<LocalizationIssue>();
            if (!catalog)
            {
                issues.Add(new LocalizationIssue("catalog.missing", "Catalog", "Assign a Localized Catalog asset.", LocalizationIssueSeverity.Error));
                return issues;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach(var entry in catalog.entries ?? Array.Empty<CatalogTranslation>())
            {
                string subject = entry?.id ?? "Entry " + index;
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                    issues.Add(new LocalizationIssue("catalog.id.empty", subject, "Enter a stable definition ID or message code.", LocalizationIssueSeverity.Error));
                else if (!ids.Add(entry.id))
                    issues.Add(new LocalizationIssue("catalog.id.duplicate", subject, "This ID is repeated. Keep one mapping for each stable ID.", LocalizationIssueSeverity.Error));
                if (entry != null) InspectReference(entry.text, subject, issues);
                index++;
            }
            return issues;
        }

        private static void InspectReference(LocalizedString text, string subject, List<LocalizationIssue> issues)
        {
            if (text == null || text.IsEmpty)
            {
                issues.Add(new LocalizationIssue("reference.empty", subject, "Choose a standard string table and entry. Until then the caller's fallback is used.", LocalizationIssueSeverity.Warning));
                return;
            }
            // Unity's GUID overload logs an Error for a deleted/mistyped SharedTableData asset.
            // Missing references are expected input to this inspector; diagnose them before that call.
            bool canResolve = true;
            var referenceTable = text.TableReference;
            if (referenceTable.ReferenceType == TableReference.Type.Guid)
            {
                string path = AssetDatabase.GUIDToAssetPath(referenceTable.TableCollectionNameGuid.ToString("N"));
                canResolve = !string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<SharedTableData>(path);
            }
            var collection = canResolve ? LocalizationEditorSettings.GetStringTableCollection(referenceTable) : null;
            if (!collection || !collection.SharedData)
            {
                issues.Add(new LocalizationIssue("table.missing", subject, "The source table collection is missing or its reference is invalid. Reassign an existing collection or create one in Tools > EasyGameFramework > Localization > Translations.", LocalizationIssueSeverity.Error));
                return;
            }
            var entry = collection.SharedData.GetEntryFromReference(text.TableEntryReference);
            if (entry == null)
            {
                issues.Add(new LocalizationIssue("key.missing", subject, "The referenced entry does not exist in '" + collection.name + "'. Choose a valid entry; renaming a key preserves references by ID.", LocalizationIssueSeverity.Error));
                return;
            }
            if (text.TableEntryReference.ReferenceType == TableEntryReference.Type.Name)
                issues.Add(new LocalizationIssue("key.by-name", subject, "This reference uses a key name. Select the entry by stable ID in the Inspector if keys may be renamed.", LocalizationIssueSeverity.Info));
            foreach(var tableReference in collection.Tables)
            {
                var table = tableReference.asset as StringTable;
                if (table && table.GetEntry(entry.Id) == null)
                    issues.Add(new LocalizationIssue("translation.missing", subject,
                        "Entry '" + entry.Key + "' has no translation for " + table.LocaleIdentifier.Code + ". Add it in String Tables; runtime uses the configured fallback.", LocalizationIssueSeverity.Warning));
            }
        }

        internal static void Draw(IReadOnlyList<LocalizationIssue> issues)
        {
            const int limit = 12;
            for(int i = 0; i < issues.Count && i < limit; i++)
            {
                var issue = issues[i];
                var type = issue.Severity == LocalizationIssueSeverity.Error ? MessageType.Error :
                    issue.Severity == LocalizationIssueSeverity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Subject + ": " + issue.Message, type);
            }
            if (issues.Count > limit) EditorGUILayout.HelpBox((issues.Count - limit) + " more issues. Fix these entries, then inspect again.", MessageType.Info);
        }
    }
}
