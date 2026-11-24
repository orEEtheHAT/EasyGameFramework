using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EasyLocalization;
using EasyLocalization.Editor;
using EasyQuests;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using Object = UnityEngine.Object;

namespace EasyNarrative.Editor
{
    public static class NarrativeTextCollector
    {
        public enum Resolution { KeepTable, UseGraph }
        public sealed class Row
        {
            public string key, source, primary, context, error;
            public long tableId;
            public bool update, conflict, mixed, selected = true, reviewed;
            public Resolution resolution;
            public readonly List<string> issues = new();
            internal readonly List<TranslationCell> translations = new();
            internal bool sourceIsSmart, showTranslations;
            internal string sourceLanguage;
            internal readonly List<NarrativeTextSource> sites = new();
            internal NarrativeSourceHistory.Record baseline;
            internal List<NarrativeSourceHistory.Location> locations;
        }
        public sealed class Plan
        {
            public StringTableCollection collection;
            public LocalizedCatalog catalog;
            public string locale;
            public readonly List<Row> rows = new();
            public readonly List<string> unusedKeys = new();
            internal ScriptableObject[] assets;
            internal string signature;
        }

        private const string Begin = "[EasyNarrative]";
        private const string End = "[/EasyNarrative]";
        private static readonly Regex Token = new(@"(?<!\{)\{([\p{L}_\d][\w.]*)(?:[,}:])", RegexOptions.CultureInvariant);

        private static void ReleaseGenerated(ref string key)
        {
            if (NarrativeTextSource.IsGenerated(key)) key = "";
        }

        /// <summary>Detach a copied step's private translations. Hand-authored shared keys keep their bindings.</summary>
        public static void PrepareCopy(QuestStep step)
        {
            if (step == null) return;
            ReleaseGenerated(ref step.nameKey);
            ReleaseGenerated(ref step.outcomeKey);
            foreach (var goal in step.goals ?? Array.Empty<QuestGoal>())
                if (goal != null) ReleaseGenerated(ref goal.textKey);
        }

        public static void PrepareCopy(QuestDefinition copy)
        {
            if (!copy) return;
            ReleaseGenerated(ref copy.titleKey);
            foreach (var step in copy.steps ?? Array.Empty<QuestStep>()) PrepareCopy(step);
        }

        public static void PrepareCopy(DialogueLine line)
        {
            if (line == null) return;
            ReleaseGenerated(ref line.textKey);
            ReleaseGenerated(ref line.speakerKey);
            foreach (var reply in line.replies ?? Array.Empty<DialogueReply>())
            {
                if (reply == null) continue;
                ReleaseGenerated(ref reply.textKey);
                ReleaseGenerated(ref reply.lockedReasonKey);
            }
        }

        public static void PrepareCopy(DialogueDefinition copy)
        {
            if (!copy) return;
            foreach (var line in copy.lines ?? Array.Empty<DialogueLine>()) PrepareCopy(line);
            copy.Invalidate();
        }

        public static ScriptableObject[] Assets(string folder = "Assets")
        {
            if (!AssetDatabase.IsValidFolder(folder)) throw new ArgumentException("Choose a project folder in Assets.");
            var paths = new HashSet<string>();
            foreach (string type in new[] { "QuestDefinition", "DialogueDefinition", "SpeakerProfile" })
                foreach (string guid in AssetDatabase.FindAssets("t:" + type, new[] { folder }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            return paths.OrderBy(p => p, StringComparer.Ordinal)
                .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p)).OfType<ScriptableObject>()
                .Where(a => a is QuestDefinition || a is DialogueDefinition || a is SpeakerProfile).ToArray();
        }

        public static Plan Preview(StringTableCollection collection, LocalizedCatalog catalog, string locale, IEnumerable<ScriptableObject> assets)
        {
            if (!collection || !catalog) throw new ArgumentException("Choose the string table collection and the Localized Catalog used by your presenters.");
            var primary = collection.GetTable(locale) as StringTable;
            if (!primary) throw new ArgumentException("Choose the source language in this collection.");
            var plan = new Plan { collection = collection, catalog = catalog, locale = locale, assets = assets.Where(a => a).Distinct().ToArray() };
            var history = collection.Extensions.OfType<NarrativeSourceHistory>().FirstOrDefault();
            var baselines = history?.records.Where(r => r.locale == locale).ToDictionary(r => r.entryId);
            var aliases = (catalog.entries ?? Array.Empty<CatalogTranslation>()).Where(e => e != null && !string.IsNullOrEmpty(e.id))
                .ToLookup(e => e.id, StringComparer.Ordinal);
            var stamp = new StringBuilder();
            var owners = new HashSet<string>(plan.assets.Select(NarrativeTextSource.OwnerId), StringComparer.Ordinal);
            var tables = collection.StringTables;
            void Hash(string s) { s ??= ""; stamp.Append(s.Length).Append(':').Append(s); }
            Hash(AssetDatabase.GetAssetPath(collection)); Hash(AssetDatabase.GetAssetPath(catalog)); Hash(locale);
            foreach (var group in NarrativeTextSource.Gather(plan.assets).GroupBy(s => s.key, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var row = new Row { key = group.Key };
                row.sites.AddRange(group);
                row.source = group.Select(s => s.text).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
                var shared = collection.SharedData.GetEntry(row.key);
                var links = aliases[row.key].ToArray();
                if (links.Length > 1) row.error = "Duplicate ID in Localized Catalog.";
                else if (links.Length == 1)
                {
                    var reference = links[0].text;
                    if (reference == null || reference.IsEmpty || LocalizationEditorSettings.GetStringTableCollection(reference.TableReference) != collection)
                        row.error = "Catalog key already belongs to a different collection or an incomplete reference.";
                    else
                    {
                        var linked = collection.SharedData.GetEntryFromReference(reference.TableEntryReference);
                        if (linked == null || (shared != null && linked.Id != shared.Id)) row.error = "Catalog reference and collection key disagree.";
                        else shared = linked;
                    }
                }
                row.tableId = shared?.Id ?? 0;
                var native = shared != null ? primary.GetEntry(shared.Id) : null;
                row.primary = native?.Value ?? "";
                if (baselines != null) baselines.TryGetValue(row.tableId, out row.baseline);
                row.locations = row.baseline?.locations?.Where(s => !owners.Contains(s.owner)).ToList() ?? new();
                foreach (var site in row.sites)
                    row.locations.Add(new NarrativeSourceHistory.Location { owner = site.ownerId, context = site.context, text = site.text });
                row.mixed = row.locations.Select(s => s.text).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Skip(1).Any();
                row.context = string.Join("\n", row.locations.Select(s => s.context).Distinct().OrderBy(s => s, StringComparer.Ordinal));
                if (row.source.Length == 0 && string.IsNullOrWhiteSpace(row.primary) && row.error == null)
                    row.error = "This key has no authored text or primary translation.";
                bool different = row.source != row.primary && row.source.Length != 0;
                // Existing hand-authored primary translations require review on first collection.
                row.conflict = different && native != null && (row.baseline == null ||
                    (row.source != row.baseline.graphText && row.primary != row.baseline.primaryText));
                if (row.mixed && native == null) row.error = "Several authored originals share this key. Assign separate keys or supply a canonical source in the table.";
                row.resolution = !row.mixed && !row.conflict && different &&
                    (native == null || row.baseline?.graphText != row.source) ? Resolution.UseGraph : Resolution.KeepTable;
                string comment = TranslationComments.Read(shared);
                row.update = shared == null || native == null || links.Length == 0 || row.sites.Any(s => s.key != s.originalKey) ||
                    row.baseline == null || row.baseline.graphText != row.source || row.baseline.primaryText != row.primary ||
                    comment != WithContext(comment, row.context);
                row.sourceLanguage = locale;
                row.sourceIsSmart = native?.IsSmart == true;
                foreach (var table in tables)
                {
                    string code = table.LocaleIdentifier.Code;
                    var entry = row.tableId == 0 ? null : table.GetEntry(row.tableId);
                    Hash(code); Hash(entry == null ? "missing" : "present"); Hash(entry?.Value); Hash(entry?.IsSmart.ToString());
                    if (code == locale) continue;
                    row.translations.Add(new TranslationCell { language = code, text = entry?.Value, present = entry != null, smart = entry?.IsSmart == true });
                    var review = row.baseline?.reviews.FirstOrDefault(r => r.locale == code);
                    if (review != null && review.previousTranslation != entry?.Value) row.update = true;
                }
                UpdateIssues(row);
                Hash(row.key); Hash(row.tableId.ToString()); Hash(row.primary); Hash(row.context); Hash(comment); Hash(row.error);
                Hash(row.baseline == null ? "" : JsonUtility.ToJson(row.baseline)); Hash(links.Length.ToString());
                foreach (var s in row.sites) { Hash(AssetDatabase.GetAssetPath(s.owner)); Hash(s.address); Hash(s.originalKey); Hash(s.text); }
                row.selected = row.error == null && row.update;
                plan.rows.Add(row);
            }
            // Two runtime aliases can intentionally point at the same translated string. Never let two writes race.
            foreach (var overlap in plan.rows.Where(r => r.tableId != 0).GroupBy(r => r.tableId).Where(g => g.Count() > 1))
                foreach (var r in overlap) { r.error = "Several catalog IDs point to this entry. Collect these aliases separately."; r.selected = false; }
            if (baselines != null)
            {
                var usedIds = new HashSet<long>(plan.rows.Select(r => r.tableId));
                var gone = new Dictionary<string, bool>(StringComparer.Ordinal);
                bool GoneOrScanned(string owner)
                {
                    if (owners.Contains(owner)) return true;
                    if (!gone.TryGetValue(owner, out bool removed))
                    {
                        int split = owner.IndexOf('/');
                        removed = split > 0 && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(owner.Substring(0, split)));
                        gone.Add(owner, removed);
                    }
                    return removed;
                }
                foreach (var record in baselines.Values)
                {
                    if (usedIds.Contains(record.entryId) || record.locations == null || record.locations.Count == 0 ||
                        !record.locations.All(s => GoneOrScanned(s.owner))) continue;
                    var unused = collection.SharedData.GetEntry(record.entryId);
                    if (unused != null) plan.unusedKeys.Add(unused.Key);
                }
                plan.unusedKeys.Sort(StringComparer.Ordinal);
            }
            plan.signature = Hash128.Compute(stamp.ToString()).ToString();
            return plan;
        }

        private static HashSet<string> Tokens(string text) => new(Token.Matches(text ?? "").Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);

        public static void UpdateIssues(Row row)
        {
            row.issues.Clear();
            if (row.mixed) row.issues.Add("Several originals use this key. Keep the table text or give each meaning a separate key.");
            string source = row.resolution == Resolution.UseGraph ? row.source : row.primary;
            if (string.IsNullOrWhiteSpace(source)) row.issues.Add(row.sourceLanguage + ": primary translation missing.");
            var tokens = Tokens(source);
            if (row.sourceIsSmart && !row.translations.Any(t => !string.IsNullOrWhiteSpace(t.text)))
            {
                var original = new TranslationRow { key = row.key };
                original.cells.Add(new TranslationCell { language = row.sourceLanguage, text = source, smart = true });
                SmartValidation.Validate(original, "Translation", row.issues);
            }
            foreach (var t in row.translations)
            {
                if (string.IsNullOrWhiteSpace(t.text)) { row.issues.Add(t.language + ": translation missing."); continue; }
                var review = row.baseline?.reviews.FirstOrDefault(r => r.locale == t.language && r.previousTranslation == t.text);
                if (review != null) row.issues.Add(t.language + ": source changed; review translation.");
                if (row.sourceIsSmart || t.smart)
                {
                    var pair = new TranslationRow { key = row.key };
                    pair.cells.Add(new TranslationCell { language = row.sourceLanguage, text = source, smart = true });
                    pair.cells.Add(new TranslationCell { language = t.language, text = t.text, smart = true });
                    SmartValidation.Validate(pair, "Translation", row.issues);
                }
                else
                {
                    var actual = Tokens(t.text);
                    if (!actual.SetEquals(tokens))
                        row.issues.Add(t.language + ": placeholder names differ. Expected [" + string.Join(", ", tokens) + "], found [" + string.Join(", ", actual) + "].");
                }
            }
        }

        private static string WithContext(string note, string context)
        {
            string managed = Begin + "\n" + context + "\n" + End;
            int start = note.IndexOf(Begin, StringComparison.Ordinal);
            int end = start < 0 ? -1 : note.IndexOf(End, start + Begin.Length, StringComparison.Ordinal);
            if (end >= 0) return note.Substring(0, start) + managed + note.Substring(end + End.Length);
            return string.IsNullOrEmpty(note) ? managed : note + "\n" + managed;
        }

        public static string ExportCsv(Plan preview)
        {
            var fresh = Preview(preview.collection, preview.catalog, preview.locale, preview.assets);
            if (fresh.signature != preview.signature) throw new InvalidOperationException("The graphs or translations changed. Preview again before exporting.");
            if (fresh.rows.Any(r => r.tableId == 0 || r.error != null))
                throw new InvalidOperationException("Apply new keys and resolve conflicting bindings before exporting stable IDs.");
            var languages = fresh.collection.StringTables.Select(t => t.LocaleIdentifier.Code)
                .OrderBy(c => c == fresh.locale ? 0 : 1).ThenBy(c => c, StringComparer.Ordinal).ToArray();
            var csv = new StringBuilder();
            void Write(IEnumerable<string> fields)
            {
                bool first = true;
                foreach (string value in fields)
                {
                    if (!first) csv.Append(',');
                    first = false;
                    csv.Append('"').Append((value ?? "").Replace("\"", "\"\"")).Append('"');
                }
                csv.Append("\r\n");
            }
            Write(new[] { "ID", "Key" }.Concat(languages).Concat(new[] { "Context", "Authored", "Proposed source", "Previous source", "Review" }));
            var choices = preview.rows.ToDictionary(row => row.key, StringComparer.Ordinal);
            foreach (var row in fresh.rows)
            {
                row.resolution = choices[row.key].resolution;
                UpdateIssues(row);
                var fields = new List<string> { row.tableId.ToString(System.Globalization.CultureInfo.InvariantCulture), row.key };
                foreach (string language in languages)
                    fields.Add(language == fresh.locale ? row.primary : row.translations.First(t => t.language == language).text);
                fields.Add(WithContext(TranslationComments.Read(fresh.collection.SharedData.GetEntry(row.tableId)), row.context));
                fields.Add(row.source);
                fields.Add(row.resolution == Resolution.UseGraph ? row.source : row.primary);
                fields.Add(string.Join("\n", row.baseline?.reviews.Where(r => !string.IsNullOrEmpty(r.previousSource))
                    .Select(r => r.locale + ": " + r.previousSource) ?? Enumerable.Empty<string>()));
                fields.Add(string.Join("\n", row.issues));
                Write(fields);
            }
            return csv.ToString();
        }

        public static int Apply(Plan preview)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Collect text in Edit Mode.");
            var fresh = Preview(preview.collection, preview.catalog, preview.locale, preview.assets);
            if (fresh.signature != preview.signature) throw new InvalidOperationException("The graphs, bindings or tables changed. Preview again.");
            var choices = preview.rows.Where(r => r.selected).ToDictionary(r => r.key);
            if (choices.Count == 0) return 0;
            var rows = fresh.rows.Where(r => choices.ContainsKey(r.key)).ToArray();
            if (rows.Any(r => r.error != null || (r.mixed && choices[r.key].resolution == Resolution.UseGraph)))
                throw new InvalidOperationException("Resolve conflicting source keys before applying.");
            var primary = (StringTable)fresh.collection.GetTable(fresh.locale);
            var tables = fresh.collection.StringTables;
            var touched = new HashSet<Object> { fresh.collection, fresh.collection.SharedData, primary, fresh.catalog };
            foreach (var row in rows) foreach (var site in row.sites) touched.Add(site.owner);
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Collect narrative text");
            Undo.RegisterCompleteObjectUndo(touched.ToArray(), "Collect narrative text");
            try
            {
                var ledger = fresh.collection.Extensions.OfType<NarrativeSourceHistory>().FirstOrDefault();
                if (ledger == null) { ledger = new NarrativeSourceHistory(); fresh.collection.AddExtension(ledger); }
                var aliases = new List<CatalogTranslation>(fresh.catalog.entries ?? Array.Empty<CatalogTranslation>());
                var known = new HashSet<string>(aliases.Where(e => e != null).Select(e => e.id));
                foreach (var row in rows)
                {
                    var choice = choices[row.key];
                    var shared = row.tableId == 0 ? fresh.collection.SharedData.AddKey(row.key) : fresh.collection.SharedData.GetEntry(row.tableId);
                    string value = choice.resolution == Resolution.UseGraph ? row.source : row.primary;
                    var native = primary.GetEntry(shared.Id);
                    if (native == null) primary.AddEntry(shared.Id, value);
                    else if (native.Value != value) native.Value = value;
                    if (known.Add(row.key)) aliases.Add(new CatalogTranslation { id = row.key, text = new LocalizedString(fresh.collection.SharedData.TableCollectionNameGuid, shared.Id) });
                    TranslationComments.Write(shared, WithContext(TranslationComments.Read(shared), row.context));
                    foreach (var site in row.sites)
                        if (site.originalKey != site.key)
                        {
                            site.bind(site.key);
                            if (site.owner is DialogueDefinition graph) graph.Invalidate();
                        }
                    var record = row.baseline;
                    if (record == null)
                    {
                        record = new NarrativeSourceHistory.Record { entryId = shared.Id, locale = fresh.locale };
                        ledger.records.Add(record);
                    }
                    bool changed = (row.baseline != null ? row.baseline.primaryText != value : row.primary.Length > 0 && row.primary != value);
                    foreach (var table in tables)
                    {
                        string code = table.LocaleIdentifier.Code;
                        if (code == fresh.locale) continue;
                        string translated = table.GetEntry(shared.Id)?.Value;
                        var prior = record.reviews.FirstOrDefault(r => r.locale == code);
                        if (prior != null && (choice.reviewed || prior.previousTranslation != translated || changed)) record.reviews.Remove(prior);
                        if (changed && !choice.reviewed && !string.IsNullOrWhiteSpace(translated))
                            record.reviews.Add(new NarrativeSourceHistory.Review { locale = code, previousTranslation = translated,
                                previousSource = prior?.previousTranslation == translated && prior.previousSource != null ? prior.previousSource : row.baseline?.primaryText ?? row.primary });
                    }
                    record.graphText = row.source; record.primaryText = value;
                    record.locations = row.locations;
                }
                fresh.catalog.SetEntries(aliases.ToArray());
                foreach (var asset in touched) EditorUtility.SetDirty(asset);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undo);
                foreach (var asset in touched) AssetDatabase.SaveAssetIfDirty(asset);
                return rows.Length;
            }
            catch { Undo.RevertAllDownToGroup(undo); throw; }
        }
    }
}
