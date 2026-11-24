using System;
using System.Linq;
using EasyLocalization;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace EasyNarrative.Editor
{
    public static class NarrativePreviewLanguage
    {
        private static LocalizedCatalog catalog;
        private static string locale = "";
        private static string[] locales;

        [InitializeOnLoadMethod]
        private static void ResetChoices() => EditorApplication.projectChanged += () => locales = null;

        public static void Configure(LocalizedCatalog textCatalog, string language)
        {
            catalog = textCatalog;
            locale = language ?? "";
        }

        public static void Draw()
        {
            locales ??= new[] { "Authored text" }.Concat(LocalizationEditorSettings.GetLocales().Select(l => l.Identifier.Code)).ToArray();
            int current = Math.Max(0, Array.IndexOf(locales, locale));
            int next = EditorGUILayout.Popup("Preview language", current, locales);
            if (next != current) locale = next == 0 ? "" : locales[next];
            if (next == 0) return;
            catalog = (LocalizedCatalog)EditorGUILayout.ObjectField("Player text catalog", catalog, typeof(LocalizedCatalog), false);
            if (!catalog) EditorGUILayout.HelpBox("Choose the same catalog as the dialogue presenter. Missing translations use authored text.", MessageType.Info);
        }

        public static string Text(string key, string fallback)
        {
            string authored = string.IsNullOrEmpty(fallback) ? key ?? "" : fallback;
            if (!catalog || string.IsNullOrEmpty(locale) || string.IsNullOrEmpty(key) ||
                !catalog.TryGet(key, out var reference) || reference == null || reference.IsEmpty) return authored;
            var collection = LocalizationEditorSettings.GetStringTableCollection(reference.TableReference);
            var table = collection ? collection.GetTable(locale) as StringTable : null;
            var shared = table ? table.SharedData.GetEntryFromReference(reference.TableEntryReference) : null;
            var entry = shared == null ? null : table.GetEntry(shared.Id);
            return string.IsNullOrEmpty(entry?.Value) ? authored : entry.Value;
        }
    }
}
