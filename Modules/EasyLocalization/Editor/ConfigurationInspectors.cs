using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace EasyLocalization.Editor
{
    [CustomEditor(typeof(SheetMapping))]
    public sealed class SheetMappingInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            try { ((SheetMapping)target).Validate(); }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }
        }
    }

    [CustomEditor(typeof(LocalizedCatalog))]
    public sealed class LocalizedCatalogInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            bool changed = DrawDefaultInspector();
            var catalog = (LocalizedCatalog)target;
            LocalizationDiagnostics.Draw(LocalizationDiagnostics.Inspect(catalog));
            if (changed && Application.isPlaying) catalog.NotifyChanged();
        }
    }

    [CustomEditor(typeof(LocalizedCatalogCache))]
    public sealed class LocalizedCatalogCacheInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            bool changed = DrawDefaultInspector();
            var cache = (LocalizedCatalogCache)target;
            if (!cache.catalog) EditorGUILayout.HelpBox("Create a Localized Catalog and assign it here. Missing translations use the caller's fallback.", MessageType.Info);
            if (changed && Application.isPlaying) cache.Refresh();
        }
    }

    [CustomEditor(typeof(LocalizedText))]
    public sealed class LocalizedTextInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            bool changed = DrawDefaultInspector();
            var text=(LocalizedText)target;
            LocalizationDiagnostics.Draw(LocalizationDiagnostics.Inspect(text.text));
            if (changed && Application.isPlaying) text.Refresh();
        }
    }
}
