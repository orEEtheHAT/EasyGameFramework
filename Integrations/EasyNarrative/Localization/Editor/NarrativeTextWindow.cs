using System;
using System.Collections.Generic;
using System.Linq;
using EasyLocalization;
using EasyQuests;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace EasyNarrative.Editor
{
    public sealed class NarrativeTextWindow : EditorWindow
    {
        [SerializeField] private StringTableCollection collection;
        [SerializeField] private LocalizedCatalog catalog;
        [SerializeField] private DefaultAsset folder;
        [SerializeField] private string locale = "en";
        private NarrativeTextCollector.Plan plan;
        private string filter = "", message;
        private bool changesOnly = true;
        private bool showUnused;
        private Vector2 scroll;
        private int page;
        private const int PageSize = 35;

        [MenuItem("Tools/EasyGameFramework/Narrative/Collect text for translation")]
        public static void Open() => GetWindow<NarrativeTextWindow>("Narrative text");

        public static void OpenFor(StringTableCollection collection, LocalizedCatalog catalog, string sourceLanguage, string root)
        {
            var window = GetWindow<NarrativeTextWindow>("Narrative text");
            window.collection = collection;
            window.catalog = catalog;
            window.locale = sourceLanguage;
            window.folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(root);
            window.changesOnly = false;
            window.Scan();
        }

        private void OnEnable()
        {
            minSize = new Vector2(560, 430);
            if (!collection)
            {
                var choices = LocalizationEditorSettings.GetStringTableCollections();
                if (choices.Count == 1) collection = choices[0];
            }
            if (!catalog)
            {
                var paths = AssetDatabase.FindAssets("t:LocalizedCatalog", new[] { "Assets" });
                if (paths.Length == 1) catalog = AssetDatabase.LoadAssetAtPath<LocalizedCatalog>(AssetDatabase.GUIDToAssetPath(paths[0]));
            }
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            collection = (StringTableCollection)EditorGUILayout.ObjectField("Unity string collection", collection, typeof(StringTableCollection), false);
            catalog = (LocalizedCatalog)EditorGUILayout.ObjectField("Player text catalog", catalog, typeof(LocalizedCatalog), false);
            folder = (DefaultAsset)EditorGUILayout.ObjectField(new GUIContent("Source folder", "Empty scans Assets. Use a game-specific folder to limit scope."),
                folder, typeof(DefaultAsset), false);
            string[] codes = collection ? collection.StringTables.Select(t => t.LocaleIdentifier.Code).OrderBy(c => c).ToArray() : Array.Empty<string>();
            if (codes.Length > 0) locale = codes[EditorGUILayout.Popup("Language of authored text", Math.Max(0, Array.IndexOf(codes, locale)), codes)];
            if (EditorGUI.EndChangeCheck()) { plan = null; message = null; }
            using (new EditorGUI.DisabledScope(!collection || !catalog || codes.Length == 0 || Application.isPlaying))
                if (GUILayout.Button("Preview text and bindings")) Scan();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            if (plan == null) return;
            using (new EditorGUI.DisabledScope(plan.rows.Count == 0))
                if (GUILayout.Button("Export all collected text to CSV…"))
                {
                    try
                    {
                        string csv = NarrativeTextCollector.ExportCsv(plan);
                        string path = EditorUtility.SaveFilePanel("Export narrative translations", "", collection.name + " - narrative", "csv");
                        if (!string.IsNullOrEmpty(path))
                        {
                            System.IO.File.WriteAllText(path, csv, new System.Text.UTF8Encoding(true));
                            message = "Exported " + plan.rows.Count + " keys. Import the ID column as text to keep its full precision.";
                        }
                    }
                    catch (Exception error) { message = error.Message; }
                }
            if (plan.unusedKeys.Count > 0)
            {
                showUnused = EditorGUILayout.Foldout(showUnused, $"Previously collected keys without a scanned source ({plan.unusedKeys.Count})", true);
                if (showUnused) EditorGUILayout.HelpBox(string.Join("\n", plan.unusedKeys.Take(35)) +
                    (plan.unusedKeys.Count > 35 ? "\n…" : "") + "\nThe table keeps these entries for other consumers.", MessageType.Info);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                changesOnly = GUILayout.Toggle(changesOnly, "Changes or issues", GUILayout.Width(132));
                filter = EditorGUILayout.TextField(filter, EditorStyles.toolbarSearchField);
            }
            IEnumerable<NarrativeTextCollector.Row> shown = plan.rows;
            if (changesOnly) shown = shown.Where(r => r.update || r.error != null || r.conflict || r.issues.Count > 0);
            if (filter.Length > 0) shown = shown.Where(r => Contains(r.key, filter) || Contains(r.context, filter) || Contains(r.source, filter));
            var rows = shown.ToArray();
            int pages = Math.Max(1, (rows.Length + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{rows.Length} rows · page {page + 1}/{pages}");
                using (new EditorGUI.DisabledScope(page == 0)) if (GUILayout.Button("Previous")) { page--; scroll = default; }
                using (new EditorGUI.DisabledScope(page == pages - 1)) if (GUILayout.Button("Next")) { page++; scroll = default; }
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var row in rows.Skip(page * PageSize).Take(PageSize))
            {
                using var panel = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
                using (new EditorGUI.DisabledScope(row.error != null))
                    row.selected = EditorGUILayout.ToggleLeft(row.key, row.selected, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(row.context, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Authored text", row.source, EditorStyles.wordWrappedLabel);
                if (row.error != null) EditorGUILayout.HelpBox(row.error, MessageType.Error);
                else if (!row.mixed && row.tableId != 0 && row.source != row.primary)
                {
                    EditorGUILayout.LabelField("Current table: " + row.primary, EditorStyles.wordWrappedLabel);
                    var selected = (NarrativeTextCollector.Resolution)EditorGUILayout.EnumPopup("Primary text", row.resolution);
                    if (selected != row.resolution)
                    {
                        row.resolution = selected;
                        row.selected = true;
                        NarrativeTextCollector.UpdateIssues(row);
                    }
                }
                row.showTranslations = EditorGUILayout.Foldout(row.showTranslations, "Compare original and translations", true);
                if (row.showTranslations)
                {
                    if (row.baseline != null && row.baseline.graphText != row.source)
                        EditorGUILayout.LabelField("Previous authored text", row.baseline.graphText, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Primary result · " + plan.locale,
                        row.resolution == NarrativeTextCollector.Resolution.UseGraph ? row.source : row.primary, EditorStyles.wordWrappedLabel);
                    foreach (var translation in row.translations)
                    {
                        var review = row.baseline?.reviews.FirstOrDefault(r => r.locale == translation.language && r.previousTranslation == translation.text);
                        if (!string.IsNullOrEmpty(review?.previousSource))
                            EditorGUILayout.LabelField(translation.language + " · previously translated from", review.previousSource, EditorStyles.wordWrappedLabel);
                        EditorGUILayout.LabelField(translation.language, string.IsNullOrEmpty(translation.text) ? "Missing translation" : translation.text,
                            EditorStyles.wordWrappedLabel);
                    }
                }
                foreach (string issue in row.issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
                if (row.issues.Any(i => i.Contains("review translation")))
                {
                    row.reviewed = EditorGUILayout.ToggleLeft("Acknowledge reviewed translations on this row", row.reviewed);
                    if (row.reviewed) row.selected = true;
                }
                if (GUILayout.Button("Select graph or speaker", EditorStyles.miniButton)) Selection.activeObject = row.sites[0].owner;
            }
            EditorGUILayout.EndScrollView();
            int count = plan.rows.Count(r => r.selected);
            using (new EditorGUI.DisabledScope(count == 0 || Application.isPlaying))
                if (GUILayout.Button($"Apply {count} selected rows"))
                {
                    try
                    {
                        int changed = NarrativeTextCollector.Apply(plan);
                        NarrativePreviewLanguage.Configure(catalog, locale);
                        Scan();
                        message = changed + " rows applied. In Translations, map a free Context column for Google Sheets.";
                    }
                    catch (Exception error) { message = error.Message; }
                }
        }

        private static bool Contains(string text, string term) => text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        private void Scan()
        {
            try
            {
                string root = folder ? AssetDatabase.GetAssetPath(folder) : "Assets";
                plan = NarrativeTextCollector.Preview(collection, catalog, locale, NarrativeTextCollector.Assets(root));
                page = 0; scroll = default; message = plan.rows.Count + " distinct text keys in scope.";
            }
            catch (Exception error) { plan = null; message = error.Message; }
        }
    }
}
