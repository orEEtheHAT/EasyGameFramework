using System.Collections.Generic;
using EasyLocalization;
using EasyUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Profiling;

namespace EasyQuests.UI
{
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Marker HUD")]
    public sealed class QuestMarkerHud : MonoBehaviour
    {
        public QuestWorld world;
        public Camera view;
        public LocalizedCatalogCache translations;
        [Tooltip("Optional TMP label prefab for a target marker.")] public TextMeshProUGUI labelPrefab;
        [Min(0), Tooltip("Gap in Canvas units between the edge marker and the HUD boundary. An icon may override this.")] public float edgePadding = 45;
        private readonly List<QuestRun> owners = new();
        private readonly List<QuestDefinition> definitions = new();
        [Tooltip("Combine distant members of the same objective into counted badges.")]
        public bool clusterMarkers;
        [Min(1)] public float clusterDistance = 15;
        [Min(4)] public float clusterRadius = 36;
        private readonly List<int> clusters = new();
        private readonly List<bool> eligible = new();
        private readonly List<Vector2> positions = new();
        private static readonly ProfilerMarker markerCost = new("EasyNarrative.Markers");
        private readonly Dictionary<(QuestRun, QuestGoal,int,int), int> cellHeads = new();
        private readonly List<int> nextHead = new();
        private readonly List<Vector2> priorLabelPositions = new(), priorIconOffsets = new();
        private readonly List<float> lastAngles = new(), halfSizes = new();
        private readonly List<TMP_Text> badges = new();
        private readonly List<string> numberText = new() { "", "" };
        private int targetRevision;
        private readonly List<QuestTarget> anchors = new();
        private readonly List<QuestIcon> styles = new();
        private readonly List<Image> sprites = new();
        private readonly List<QuestGoal> goals = new();
        private readonly List<TextMeshProUGUI> labels = new();
        [Header("Wayfinder marker")]
        [Range(18, 64)] public float symbolSize = 34;
        public Color targetTint = new Color(.38f, .92f, .94f);
        public Color searchTint = new Color(1f, .76f, .39f);
        public bool pulse = true;
        [Tooltip("Optional material using the EasyGameFramework/UI/Wayfinder shader.")] public Material symbolMaterial;
        private readonly List<QuestMarkerGraphic> icons = new();
        [Header("Tracked quest")]
        [SerializeField] private bool showQuestTracker = true;
        [Tooltip("Distance from the upper-left corner, in Canvas units.")]
        public Vector2 trackerInset = new Vector2(20, 20);
        private QuestTrackerPanel tracker;
        public bool ShowQuestTracker
        {
            get => showQuestTracker;
            set { showQuestTracker = value; if (isActiveAndEnabled) RefreshTracker(); }
        }
        private WindowManager manager;
        private RectTransform area;
        private Canvas canvas;
#if UNITY_EDITOR
        private bool refreshInspector;
        private void OnValidate() => refreshInspector=true;
#endif
        private void OnEnable()
        {
            area = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            manager = GetComponentInParent<WindowManager>();
            if (manager) manager.TypographyChanged += RefreshAppearance;
            if (world) world.Session.Changed += Refresh;
            if (translations) translations.Changed += Refresh;
            Refresh();
        }
        private void OnDisable()
        {
            tracker?.Hide();
            if (manager) manager.TypographyChanged -= RefreshAppearance;
            if (world) world.Session.Changed -= Refresh;
            if (translations) translations.Changed -= Refresh;
            foreach (var label in labels) if (label) label.gameObject.SetActive(false);
        }
        private void Refresh()
        {
            goals.Clear();
            anchors.Clear();
            targetRevision = world ? world.TargetRevision : 0;
            definitions.Clear(); owners.Clear();
            if (world)
            {
                Gather(world.Session.Find(world.Session.Tracked));
                foreach (string id in world.Session.Pinned) Gather(world.Session.Find(id));
            }
            for (int i = 0; i < goals.Count; i++)
            {
                if (i == labels.Count)
                {
                    var label = labelPrefab ? Instantiate(labelPrefab, area) : CreateLabel();
                    label.raycastTarget = false;
                    label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
                    label.rectTransform.pivot = new Vector2(.5f, .5f);
                    label.alignment = TextAlignmentOptions.Center;
                    labels.Add(label);
                    var unset = new Vector2(float.NaN,float.NaN);
                    priorLabelPositions.Add(unset); priorIconOffsets.Add(unset);
                    lastAngles.Add(float.NaN); halfSizes.Add(0);
                    var iconObject = new GameObject("Wayfinder", typeof(RectTransform));
                    iconObject.layer = label.gameObject.layer; iconObject.transform.SetParent(label.transform, false);
                    var icon = iconObject.AddComponent<QuestMarkerGraphic>();
                    icon.raycastTarget = false;
                    icons.Add(icon);
                    var imageObject = new GameObject("Library icon", typeof(RectTransform));
                    imageObject.layer = iconObject.layer;
                    var image = imageObject.AddComponent<Image>();
                    image.rectTransform.SetParent(icon.rectTransform, false);
                    image.rectTransform.anchorMin = Vector2.zero;
                    image.rectTransform.anchorMax = Vector2.one;
                    image.rectTransform.sizeDelta = Vector2.zero;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    sprites.Add(image);
                    styles.Add(null);
                    var badgeGo = new GameObject("Group count", typeof(RectTransform));
                    badgeGo.layer = iconObject.layer;
                    badgeGo.transform.SetParent(label.transform, false);
                    var badge = UIText.Add(badgeGo, "", UITextRole.Caption, TextAlignmentOptions.Center);
                    badge.raycastTarget = false;
                    badge.rectTransform.sizeDelta = new Vector2(55, 26);
                    badgeGo.SetActive(false);
                    badges.Add(badge);
                    clusters.Add(0); eligible.Add(false); positions.Add(default); nextHead.Add(-1);
                }
                var g = goals[i];
                var title = translations && !string.IsNullOrEmpty(g.textKey) ? translations.Get(g.textKey, g.title) : g.title;
                labels[i].text = title;
                ResolveIcon(i);
                Style(i, icons[i].Shape);
            }
            for (int i = goals.Count; i < labels.Count; i++) labels[i].gameObject.SetActive(false);
            RefreshTracker();
        }
        private void RefreshTracker()
        {
            var run = world ? world.Session.Find(world.Session.Tracked) : null;
            if (run == null || run.Finished)
                foreach (string id in world ? world.Session.Pinned : System.Array.Empty<string>())
                { var candidate = world.Session.Find(id); if (candidate != null && !candidate.Finished) { run = candidate; break; } }
            if (!showQuestTracker || run == null || run.Finished || run.Definition.hidden ||
                run.Current.type != QuestStepType.Objectives || !System.Array.Exists(run.Current.goals, goal => !goal.hidden))
            {
                tracker?.Hide();
                return;
            }
            tracker ??= new QuestTrackerPanel((RectTransform)transform);
            tracker.Show(run, translations, trackerInset,world.Session);
        }
        private void OnRectTransformDimensionsChange() => tracker?.Position(trackerInset);

        private void Gather(QuestRun run)
        {
            if (run == null || run.Definition.hidden || run.Finished || run.Current.type != QuestStepType.Objectives) return;
            foreach (var g in run.Current.goals)
            {
                if (run.Id == world.Session.Tracked && world.Session.TrackedGoal != null && world.Session.TrackedGoal != g.id) continue;
                if (g.hidden || run.Satisfied(g) || g.marker == MarkerStyle.Hidden) continue;
                string key = run.ResolveTarget(g.target);
                if (g.targetGroup)
                {
                    var members = world.Group(key);
                    for (int j = 0; j < members.Count; j++) AddMarker(run,g,members[j]);
                }
                else if (world.Target(key) is QuestTarget single) AddMarker(run,g,single);
            }
        }
        private void AddMarker(QuestRun run, QuestGoal goal, QuestTarget target)
        {
            definitions.Add(run.Definition); owners.Add(run);
            goals.Add(goal);
            anchors.Add(target);
        }

        public void RefreshAppearance()
        {
            for (int i = 0; i < goals.Count; i++)
            {
                ResolveIcon(i);
                Style(i, icons[i].Shape);
            }
        }
        private void ResolveIcon(int i)
        {
            var trackedDefinition = definitions[i];
            var library = trackedDefinition ? trackedDefinition.iconLibrary : null;
            styles[i] = library ? (library.Find(goals[i].iconId) ?? library.Find(trackedDefinition.defaultIconId)) : null;
        }
        private void Style(int i, QuestGlyph shape)
        {
            var prefs = manager ? manager.ReadingPreferences : UIReadingPreferences.Default;
            var tint = goals[i].marker == MarkerStyle.SearchArea ? searchTint : targetTint;
            float size = Mathf.Clamp(symbolSize, 18, 64) * prefs.scale;
            halfSizes[i] = size * .5f;
            icons[i].rectTransform.sizeDelta = Vector2.one * size;
            icons[i].SetStyle(shape, tint, pulse && !prefs.reducedMotion, prefs.highContrast, symbolMaterial);
            var entry = styles[i];
            var sprite = shape == QuestGlyph.Direction && entry?.edgeSprite ? entry.edgeSprite : entry?.sprite;
            sprites[i].material = prefs.reducedMotion || prefs.highContrast ? null : entry?.spriteMaterial;
            sprites[i].sprite = sprite;
            sprites[i].enabled = sprite && !prefs.highContrast;
            icons[i].enabled = !sprites[i].enabled;

        }
        private TextMeshProUGUI CreateLabel()
        {
            var go = new GameObject("Quest marker", typeof(RectTransform));
            go.layer = area.gameObject.layer; go.transform.SetParent(area, false);
            var label = UIText.Add(go, "", UITextRole.Marker, TextAlignmentOptions.Center);
            label.rectTransform.sizeDelta = new Vector2(260, 64);
            return label;
        }
        private void LateUpdate()
        {
            using var sample = markerCost.Auto();
            cellHeads.Clear();
#if UNITY_EDITOR
            if(refreshInspector){refreshInspector=false;RefreshAppearance();RefreshTracker();}
#endif
            if (!view || !world || !canvas) { foreach (var label in labels) if (label) SetVisible(label, false); return; }
            if (targetRevision != world.TargetRevision) Refresh();
            if (goals.Count == 0) return;
            var canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var viewport = view.pixelRect;
            var viewPosition = view.transform.position;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, viewport.min, canvasCamera, out var lo) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(area, viewport.max, canvasCamera, out var hi)) return;
            var hud = area.rect;
            var min = Vector2.Max(hud.min, Vector2.Min(lo, hi));
            var max = Vector2.Min(hud.max, Vector2.Max(lo, hi));
            if (min.x >= max.x || min.y >= max.y)
            {
                foreach (var label in labels) if (label) SetVisible(label, false);
                return;
            }
            var centre = (min + max) * .5f;
            var half = (max - min) * .5f;
            for (int i = 0; i < goals.Count; i++)
            {
                eligible[i] = false; clusters[i] = 1;
                if (!clusterMarkers && badges[i].gameObject.activeSelf) badges[i].gameObject.SetActive(false);
                var goal = goals[i];
                var label = labels[i];
                var target = anchors[i];
                if (!target) { SetVisible(label, false); continue; }
                var worldPosition = target.Position;
                var screen = view.WorldToScreenPoint(worldPosition);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, canvasCamera, out var local))
                { SetVisible(label, false); continue; }
                var delta = local - centre;
                if (screen.z <= 0) delta = -delta;
                bool offscreen = screen.z <= 0 || Mathf.Abs(delta.x) > half.x || Mathf.Abs(delta.y) > half.y;
                var entry = styles[i];
                bool hideOutside = goal.edgeMode == MarkerEdgeMode.Hide || (goal.edgeMode == MarkerEdgeMode.IconDefault &&
                    (!goal.edgeArrow || entry?.edgeMode == MarkerEdgeMode.Hide));
                if (offscreen && hideOutside) { SetVisible(label, false); continue; }
                var icon = icons[i];
                var shape = offscreen ? QuestGlyph.Direction : QuestGlyph.Target;
                if (icon.Shape != shape) Style(i, shape);
                if (offscreen && delta.sqrMagnitude < .0001f) delta = Vector2.down;
                bool rotates = offscreen && (!sprites[i].enabled || entry.edgeSprite);
                var direction = delta;
                float radius = halfSizes[i];
                // A rotated square needs room for both projected half-axes.
                if (rotates) radius *= (Mathf.Abs(delta.x) + Mathf.Abs(delta.y)) / delta.magnitude;
                float padding = offscreen ? Mathf.Max(0, entry != null && entry.overrideEdgePadding ? entry.edgePadding : edgePadding) : 0;
                var bounds = Vector2.Max(Vector2.zero, half - Vector2.one * (radius + padding));
                if (offscreen)
                {
                    float tx = Mathf.Abs(delta.x) > .00001f ? bounds.x / Mathf.Abs(delta.x) : float.PositiveInfinity;
                    float ty = Mathf.Abs(delta.y) > .00001f ? bounds.y / Mathf.Abs(delta.y) : float.PositiveInfinity;
                    delta *= Mathf.Min(tx, ty);
                }
                else delta = ClampToBounds(delta, bounds);
                var iconPosition = centre - hud.center + delta;
                if (clusterMarkers)
                {
                    bool distant = goal.targetGroup && (worldPosition-viewPosition).sqrMagnitude >= clusterDistance*clusterDistance;
                    int leader = -1;
                    float radiusUnits = Mathf.Max(4,clusterRadius);
                    int x = Mathf.FloorToInt(iconPosition.x/radiusUnits);
                    int y = Mathf.FloorToInt(iconPosition.y/radiusUnits);
                    if (distant)
                        for (int dy=-1; dy<=1 && leader<0; dy++)
                            for (int dx=-1; dx<=1 && leader<0; dx++)
                                if (cellHeads.TryGetValue((owners[i],goal,x+dx,y+dy),out var candidate))
                                    for (int j=candidate; j>=0; j=nextHead[j])
                                        if ((positions[j]-iconPosition).sqrMagnitude<=radiusUnits*radiusUnits) { leader=j; break; }
                    SetVisible(label,leader<0);
                    if (leader>=0) { clusters[leader]++; continue; }
                    if (distant)
                    {
                        eligible[i]=true; positions[i]=iconPosition;
                        nextHead[i] = cellHeads.TryGetValue((owners[i],goal,x,y),out var prior) ? prior : -1;
                        cellHeads[(owners[i],goal,x,y)] = i;
                    }
                }
                else SetVisible(label,true);
                var mode = goal.labelMode == MarkerLabelMode.IconDefault ? entry?.labelMode ?? MarkerLabelMode.Hidden : goal.labelMode;
                bool showText = mode != MarkerLabelMode.IconDefault && mode != MarkerLabelMode.Hidden && !(offscreen && mode == MarkerLabelMode.OnScreenOnly)
                    && !(!offscreen && mode == MarkerLabelMode.OffScreenOnly);
                if (showText && goal.labelMaxDistance > 0)
                    showText = (worldPosition - viewPosition).sqrMagnitude <= goal.labelMaxDistance * goal.labelMaxDistance;
                if (label.enabled != showText) label.enabled = showText;
                float angle = rotates ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90 : 0;
                if (lastAngles[i] != angle)
                {
                    icon.rectTransform.localRotation = Quaternion.Euler(0,0,angle);
                    lastAngles[i] = angle;
                }
                var labelHalf = label.rectTransform.rect.size * .5f;
                float offset = radius + labelHalf.y;
                bool above = delta.y - offset - labelHalf.y < -half.y;
                var labelDelta = ClampToBounds(delta + Vector2.up * (above ? offset : -offset), Vector2.Max(Vector2.zero, half - labelHalf));
                var labelPosition = centre - hud.center + labelDelta;
                if (!priorLabelPositions[i].Equals(labelPosition))
                {
                    label.rectTransform.anchoredPosition = labelPosition;
                    priorLabelPositions[i] = labelPosition;
                }
                var offsetInLabel = iconPosition - labelPosition;
                if (!priorIconOffsets[i].Equals(offsetInLabel))
                {
                    icon.rectTransform.anchoredPosition = offsetInLabel;
                    priorIconOffsets[i] = offsetInLabel;
                }
            }
            if (clusterMarkers)
                for (int i=0;i<goals.Count;i++)
                {
                    int n=clusters[i];
                    if (!eligible[i] || n<2)
                    {
                        if (badges[i].gameObject.activeSelf) badges[i].gameObject.SetActive(false);
                        continue;
                    }
                    while(numberText.Count<=n) numberText.Add("×"+numberText.Count);
                    var badge=badges[i];
                    if (badge.text != numberText[n]) badge.text=numberText[n];
                    var badgePosition = priorIconOffsets[i] + Vector2.down*15;
                    if (!badge.rectTransform.anchoredPosition.Equals(badgePosition)) badge.rectTransform.anchoredPosition=badgePosition;
                    if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);
                }
        }
        private static Vector2 ClampToBounds(Vector2 point, Vector2 extent) =>
            new Vector2(Mathf.Clamp(point.x, -extent.x, extent.x), Mathf.Clamp(point.y, -extent.y, extent.y));
        private static void SetVisible(TMP_Text label, bool visible)
        { if (label.gameObject.activeSelf != visible) label.gameObject.SetActive(visible); }
    }
}
