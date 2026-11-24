using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyBuilding.UI
{
    /// <summary>Optional runtime UI. Creates its own canvas, recycles visible rows and uses Input System UI events.</summary>
    [DefaultExecutionOrder(-200), DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/UI/Building Palette")]
    public sealed class BuildingPaletteUI : MonoBehaviour
    {
        public BuildingCatalog Catalog;
        [Tooltip("Building Placer, Building Placer 2D, or another IBuildingPlacementInput component.")]
        public MonoBehaviour Controller;
        public BuildingMessages Messages;
        [Tooltip("Optional component implementing IBuildingMessageResolver, for example an application localization adapter.")]
        public MonoBehaviour MessageProvider;
        public IBuildingMessageResolver MessageResolver { get; set; }
        [Tooltip("Optional TextMeshPro font for the whole palette. Assign before initialization. Empty uses the TMP Settings default font and its fallbacks.")]
        public TMP_FontAsset Font;
        [Tooltip("Empty disables PlayerPrefs persistence.")] public string PreferencesKey = "EasyBuilding.Palette";
        [Min(180)] public float PanelWidth = 340;
        public bool MenuVisible = true;
        public BuildingRegistry Registry { get; private set; }
        public BuildingPalette Palette { get; private set; }
        public string ConfigurationError { get; private set; }
        public int PooledRowCount => rows.Count;
        public event Action<bool> VisibilityChanged;
        private IBuildingPlacementInput input;
        private BuildingRebinding rebinding;
        private GameObject canvasObject;
        private RectTransform canvasRect, panel, viewport, content, hotbar, settings;
        private ScrollRect scroll;
        private TMP_InputField search;
        private TMP_Dropdown categories;
        private GameObject lastFocusedCategory;
        private TextMeshProUGUI status, favoriteLabel;
        private TMP_FontAsset font;
        private string favoriteMark, unfavoriteMark;
        private readonly List<string> categoryNames = new List<string>();
        private readonly List<Row> rows = new List<Row>();
        private readonly List<TextMeshProUGUI> bindingLabels = new List<TextMeshProUGUI>();
        private readonly List<BuildingControl> bindingControls = new List<BuildingControl>();
        private readonly List<Button> hotbarButtons = new List<Button>();
        private readonly List<TextMeshProUGUI> hotbarLabels = new List<TextMeshProUGUI>();
        private readonly List<InputAction> numberActions = new List<InputAction>();
        private InputAction toggle;
        private bool initialized, started, viewDirty, favoritesOnly, gamepadMode, previousHelp;
        private BuildingDefinition lastSelected;
        private string selectionName, selectionMessage;
        private float previousWidth, previousHeight;
        private int firstRow = -1, suppressUntil;
        private const float RowHeight = 44;
        private static readonly Color Background = new Color(0.08f, 0.105f, 0.14f, 0.97f), Item = new Color(0.16f, 0.20f, 0.25f), Accent = new Color(0.12f, 0.42f, 0.43f);
        private sealed class Row { internal RectTransform Root; internal Button Button; internal TextMeshProUGUI Name, Star; internal Image Icon; internal BuildingDefinition Definition; }
        [Serializable] private sealed class Preferences { public BuildingPaletteState Palette; public BuildingInputPreferences Input; }
        private void Start() { started = true; if (!initialized) Initialize(out _); }
        private void OnEnable() { if (started && !initialized) Initialize(out _); }
        public bool Initialize(out string reason)
        {
            reason = null; if (initialized) return true;
            if (Controller == null) foreach (var behaviour in GetComponents<MonoBehaviour>()) if (behaviour is IBuildingPlacementInput) { Controller = behaviour; break; }
            input = Controller as IBuildingPlacementInput;
            if (Catalog == null || input?.Session == null) { reason = ConfigurationError = "Assign a Building Catalog and an enabled placement controller."; return false; }
            if (MessageProvider != null && !(MessageProvider is IBuildingMessageResolver)) { reason = ConfigurationError = "Message Provider must implement IBuildingMessageResolver."; return false; }
            if (TMP_Settings.LoadDefaultSettings() == null)
            {
                reason = ConfigurationError = "Import TMP Essential Resources (Window > TextMeshPro) before opening the building palette.";
                return false;
            }
            font = Font != null ? Font : TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                reason = ConfigurationError = "Assign a Font on Building Palette or a Default Font Asset in TMP Settings.";
                return false;
            }
            try
            {
                Registry = new BuildingRegistry(); Registry.Register("base", Catalog);
                Palette = new BuildingPalette(Registry, input.Session); Palette.Changed += Dirty;
                rebinding = new BuildingRebinding(input); rebinding.Finished += Rebound;
                toggle = new InputAction("Building menu", InputActionType.Button, "<Keyboard>/b"); toggle.AddBinding("<Gamepad>/buttonNorth"); toggle.Enable();
                for (int i = 0; i < Palette.SlotCount; i++) { var action = new InputAction("Building slot " + i, InputActionType.Button, "<Keyboard>/digit" + ((i + 1) % 10)); action.Enable(); numberActions.Add(action); }
                LoadPreferences(); BuildUI(); Registry.Changed += RefreshCategories; initialized = true; ConfigurationError = null;
                if (Controller is BuildingPlacer placer) { previousHelp = placer.ShowHelp; placer.ShowHelp = false; }
                SetMenuVisible(MenuVisible); Canvas.ForceUpdateCanvases(); Layout(); RefreshRows(); return true;
            }
            catch (Exception exception) { reason = ConfigurationError = exception.Message; Cleanup(false); return false; }
        }
        private void Update()
        {
            if (!initialized) { if (Time.frameCount % 30 == 0) Initialize(out _); return; }
            var current = input.Session;
            if (current != null && current.IsDisposed) current = null;
            if (!ReferenceEquals(Palette.Session, current)) { rebinding.Cancel(); Palette.SetSession(current); lastSelected = null; viewDirty = true; }
            if (current == null)
            {
                input.SetInputBlocked(this, false); status.text = "Connect an active building session.";
                if (viewDirty) RefreshRows(); return;
            }
            if (Mouse.current != null && (Mouse.current.delta.ReadValue().sqrMagnitude > 0 || Mouse.current.leftButton.wasPressedThisFrame)) gamepadMode = false;
            if (Gamepad.current != null && (Gamepad.current.dpad.ReadValue().sqrMagnitude > 0.1f || Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.25f || Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.buttonNorth.wasPressedThisFrame)) gamepadMode = true;
            bool editingFilters = search.isFocused || categories.IsExpanded;
            if (toggle.WasPressedThisFrame() && !editingFilters && !rebinding.IsRunning)
            {
                bool openedByKeyboard = toggle.activeControl?.device is Keyboard;
                if (openedByKeyboard) gamepadMode = false;
                SetMenuVisible(!MenuVisible);
                if (MenuVisible && openedByKeyboard) search.Select();
            }
            Layout();
            if (!editingFilters && !rebinding.IsRunning)
                for (int i = 0; i < numberActions.Count; i++) if (numberActions[i].WasPressedThisFrame())
                {
                    if (Keyboard.current != null && Keyboard.current.shiftKey.isPressed)
                    { if (current.Selected != null) { Palette.AssignSlot(i, current.Selected.Id); SavePreferences(); } }
                    else Palette.SelectSlot(i);
                }
            Vector2 pointer = Pointer.current != null ? Pointer.current.position.ReadValue() : new Vector2(-1, -1);
            bool overUI = Contains(panel, pointer) || Contains(hotbar, pointer) || Contains(settings, pointer);
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            bool focused = selected != null && selected.transform.IsChildOf(canvasObject.transform);
            RevealFocusedCategory(selected);
            input.SetInputBlocked(this, overUI || editingFilters || rebinding.IsRunning || gamepadMode && focused || Time.frameCount <= suppressUntil);
            string currentName = current.Selected != null ? current.Selected.Name : null;
            if (current.Selected != lastSelected || currentName != selectionName)
            {
                lastSelected = current.Selected; selectionName = currentName; viewDirty = true;
                selectionMessage = currentName != null ? currentName + " selected. Point at the world to place it." : null;
            }
            if (viewDirty || Mathf.FloorToInt(content.anchoredPosition.y / RowHeight) != firstRow) RefreshRows();
            string text;
            if (rebinding.IsRunning) text = "Press a control. Esc cancels rebinding.";
            else if (current.Selected == null)
                text = ResolveMessage(PlacementResult.Fail(PlacementFailure.NoSelection, "Select a building to begin."), input.Status);
            else if (overUI || input.PreviewResult.Reason == null) text = selectionMessage;
            else text = ResolveMessage(input.PreviewResult, input.Status);
            if (status.text != text) status.text = text ?? "Select a building.";
        }
        private string ResolveMessage(in PlacementResult result, string fallback)
        {
            var resolver = MessageResolver;
            if (resolver is Object unityObject && unityObject == null) resolver = null;
            resolver = resolver ?? MessageProvider as IBuildingMessageResolver ?? Messages;
            return resolver != null ? resolver.Resolve(result) : fallback;
        }
        public void SetMenuVisible(bool visible)
        {
            MenuVisible = visible; if (panel != null) panel.gameObject.SetActive(visible);
            if (!visible && settings != null) settings.gameObject.SetActive(false);
            if (EventSystem.current != null)
            {
                if (!visible && EventSystem.current.currentSelectedGameObject != null && canvasObject != null && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(canvasObject.transform)) EventSystem.current.SetSelectedGameObject(null);
                else if (visible && gamepadMode && rows.Count > 0) EventSystem.current.SetSelectedGameObject(rows[0].Button.gameObject);
            }
            previousWidth = -1; viewDirty = true;
            if (VisibilityChanged != null) foreach (Action<bool> handler in VisibilityChanged.GetInvocationList()) try { handler(visible); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
        private static bool Contains(RectTransform rect, Vector2 point) => rect != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, point);
        private void Dirty() { viewDirty = true; if (scroll != null) scroll.verticalNormalizedPosition = 1; }
        private void ApplyFilter()
        {
            if (Palette == null) return;
            int selected = categories.value;
            string category = selected > 0 && selected < categoryNames.Count ? categoryNames[selected] : "";
            Palette.Filter(search.text, category, favoritesOnly);
        }
        private void RevealFocusedCategory(GameObject selected)
        {
            if (!categories.IsExpanded || selected == null || !selected.transform.IsChildOf(categories.transform))
            {
                lastFocusedCategory = null;
                return;
            }
            if (lastFocusedCategory == selected) return;
            lastFocusedCategory = selected;
            if (!selected.TryGetComponent<Toggle>(out var option)) return;

            // TMP_Dropdown moves focus between toggles but does not scroll the focused toggle into view.
            var list = option.GetComponentInParent<ScrollRect>();
            if (list == null || list.content == null || list.viewport == null) return;
            var item = (RectTransform)option.transform;
            var view = list.viewport;
            var viewRect = view.rect;
            float bottom = view.InverseTransformPoint(item.TransformPoint(new Vector3(0, item.rect.yMin, 0))).y;
            float top = view.InverseTransformPoint(item.TransformPoint(new Vector3(0, item.rect.yMax, 0))).y;
            var position = list.content.anchoredPosition;
            if (bottom < viewRect.yMin) position.y += viewRect.yMin - bottom;
            else if (top > viewRect.yMax) position.y -= top - viewRect.yMax;
            else return;

            list.StopMovement();
            position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, list.content.rect.height - viewRect.height));
            list.content.anchoredPosition = position;
        }
        private void BuildUI()
        {
            bool hasStars = font.HasCharacter('★', searchFallbacks: true, tryAddCharacter: true) &&
                font.HasCharacter('☆', searchFallbacks: true, tryAddCharacter: true);
            favoriteMark = hasStars ? "★" : "-";
            unfavoriteMark = hasStars ? "☆" : "+";
            canvasObject = new GameObject("EasyBuilding Palette", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRect = (RectTransform)canvasObject.transform; canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; canvasObject.GetComponent<Canvas>().sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            if (EventSystem.current == null)
            {
                // One scene-lifetime event system may also serve other UI added later.
                var events = new GameObject("EasyBuilding UI Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            panel = Node("Catalog", canvasRect); panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.up; panel.pivot = Vector2.up; panel.sizeDelta = new Vector2(PanelWidth, 0); Image(panel, Background);
            Label("Build", panel, 20, FontStyles.Bold, 12, 12, 200, 32);
            search = CreateSearch(panel);
            categories = CreateCategories(panel); Position((RectTransform)categories.transform, 12, 96, 170, 32);
            search.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = categories };
            var favorites = Button("Favorites", panel, out favoriteLabel, () => { favoritesOnly = !favoritesOnly; favoriteLabel.text = favoritesOnly ? "Favorites only" : "All buildings"; ApplyFilter(); }); Position((RectTransform)favorites.transform, 190, 96, 138, 32);
            favoriteLabel.text = "All buildings";
            var list = Node("Building list", panel); Stretch(list, 12, 140, 12, 88); scroll = list.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
            viewport = Node("Viewport", list); Stretch(viewport, 0, 0, 12, 0); Image(viewport, Background); viewport.gameObject.AddComponent<RectMask2D>();
            content = Node("Content", viewport); content.anchorMin = Vector2.up; content.anchorMax = Vector2.one; content.pivot = Vector2.up;
            scroll.viewport = viewport; scroll.content = content;
            var barRoot = Node("Scrollbar", list); barRoot.anchorMin = Vector2.right; barRoot.anchorMax = Vector2.one; barRoot.pivot = Vector2.one; barRoot.sizeDelta = new Vector2(6, 0); Image(barRoot, Item);
            var bar = barRoot.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop;
            var handle = Node("Handle", barRoot); Stretch(handle); bar.handleRect = handle; bar.targetGraphic = Image(handle, new Color(0.35f, 0.43f, 0.49f));
            scroll.verticalScrollbar = bar;
            var controls = Button("Controls", panel, out _, () => { settings.gameObject.SetActive(!settings.gameObject.activeSelf); RefreshBindings(); }); Bottom((RectTransform)controls.transform, 12, 12, 110, 32);
            var hint = Label("1–0 select  ·  Shift+number assigns\nB / Y opens or closes this catalog", panel, 12, FontStyles.Normal, 0, 0, 300, 38); Bottom(hint.rectTransform, 12, 46, 300, 38);
            settings = Node("Input bindings", canvasRect); Position(settings, PanelWidth + 12, 12, 310, 280); Image(settings, Background);
            Label("Controls", settings, 20, FontStyles.Bold, 12, 10, 270, 28);
            foreach (BuildingControl control in Enum.GetValues(typeof(BuildingControl)))
            {
                var action = input.InputActionFor(control); if (action == null || action.bindings.Count == 0) continue;
                int index = bindingLabels.Count; var label = Label("", settings, 13, FontStyles.Normal, 12, 46 + index * 42, 204, 38); bindingLabels.Add(label); bindingControls.Add(control);
                var current = control; var button = Button("Change", settings, out _, () => { try { rebinding.Begin(current); } catch (Exception exception) { Debug.LogWarning(exception.Message, this); } }); Position((RectTransform)button.transform, 224, 48 + index * 42, 74, 32);
            }
            settings.gameObject.SetActive(false);
            hotbar = Node("Hotbar", canvasRect); var grid = hotbar.gameObject.AddComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(80, 56); grid.spacing = new Vector2(4, 4); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            hotbar.anchorMin = hotbar.anchorMax = new Vector2(1, 0); hotbar.pivot = new Vector2(1, 0); hotbar.anchoredPosition = new Vector2(-12, 12);
            for (int i = 0; i < Palette.SlotCount; i++)
            {
                int slot = i; var button = Button("", hotbar, out var label, () => Palette.SelectSlot(slot)); label.fontSize = 12; label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = true; label.fontSizeMin = 9; label.fontSizeMax = 12;
                hotbarButtons.Add(button); hotbarLabels.Add(label);
            }
            var statusRoot = Node("Placement status", canvasRect); Image(statusRoot, Background).raycastTarget = false;
            status = Label("Select a building", statusRoot, 14, FontStyles.Normal, 0, 0, 300, 44); Stretch(status.rectTransform, 10, 6, 10, 6);
            statusRoot.anchorMin = statusRoot.anchorMax = new Vector2(1, 0); statusRoot.pivot = new Vector2(1, 0);
        }
        private void Layout()
        {
            if (canvasRect == null) return;
            float width = canvasRect.rect.width, height = canvasRect.rect.height;
            if (Mathf.Approximately(width, previousWidth) && Mathf.Approximately(height, previousHeight)) return;
            previousWidth = width; previousHeight = height;
            float panelWidth = Mathf.Min(PanelWidth, width * 0.55f); panel.sizeDelta = new Vector2(panelWidth, 0);
            ((RectTransform)search.transform).sizeDelta = new Vector2(panelWidth - 24, 34);
            ((RectTransform)categories.transform).sizeDelta = new Vector2((panelWidth - 32) * 0.54f, 32);
            var favoriteRoot = (RectTransform)favoriteLabel.transform.parent; Position(favoriteRoot, 20 + (panelWidth - 32) * 0.54f, 96, (panelWidth - 32) * 0.46f, 32); favoriteLabel.fontSize = 12;
            settings.anchoredPosition = new Vector2(Mathf.Min(panelWidth + 12, Mathf.Max(0, width - settings.rect.width)), -12);
            float available = Mathf.Max(84, width - (MenuVisible ? panelWidth : 0) - 24); int columns = Mathf.Clamp(Mathf.FloorToInt(available / 84), 1, Palette.SlotCount);
            hotbar.GetComponent<GridLayoutGroup>().constraintCount = columns; float barWidth = columns * 84 - 4, barHeight = Mathf.Ceil(Palette.SlotCount / (float)columns) * 60 - 4;
            hotbar.sizeDelta = new Vector2(barWidth, barHeight);
            var statusRoot = (RectTransform)status.transform.parent; statusRoot.anchoredPosition = new Vector2(-12, barHeight + 22); statusRoot.sizeDelta = new Vector2(Mathf.Min(available, 720), 54);
            viewDirty = true;
        }
        private void RefreshRows()
        {
            if (Palette == null || viewport == null) return; viewDirty = false;
            content.sizeDelta = new Vector2(0, Palette.Visible.Count * RowHeight);
            int needed = Mathf.Min(Palette.Visible.Count, Mathf.Clamp(Mathf.CeilToInt(viewport.rect.height / RowHeight) + 2, 1, 64));
            while (rows.Count < needed) rows.Add(CreateRow());
            firstRow = Mathf.Clamp(Mathf.FloorToInt(content.anchoredPosition.y / RowHeight), 0, Mathf.Max(0, Palette.Visible.Count - 1));
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; int index = firstRow + i; row.Root.gameObject.SetActive(i < needed && index < Palette.Visible.Count);
                if (!row.Root.gameObject.activeSelf) continue;
                var definition = Palette.Visible[index]; row.Definition = definition; row.Root.anchoredPosition = new Vector2(0, -index * RowHeight);
                row.Name.text = definition.Name; row.Star.text = Palette.IsFavorite(definition.Id) ? favoriteMark : unfavoriteMark;
                row.Icon.sprite = definition.Icon; row.Icon.enabled = definition.Icon != null;
                row.Name.rectTransform.offsetMin = new Vector2(definition.Icon != null ? 46 : 10, 4);
                row.Button.image.color = Palette.Session?.Selected == definition ? Accent : Item;
            }
            for (int i = 0; i < hotbarButtons.Count; i++)
            {
                var definition = Palette.GetSlot(i); string name = definition != null ? definition.Name : string.IsNullOrEmpty(Palette.GetSlotId(i)) ? "Empty" : "Missing";
                hotbarLabels[i].text = ((i + 1) % 10) + "\n" + name; hotbarButtons[i].image.color = definition != null && definition == Palette.Session?.Selected ? Accent : Item;
            }
        }
        private Row CreateRow()
        {
            var row = new Row(); row.Button = Button("", content, out row.Name, () => { if (row.Definition != null) Palette.Select(row.Definition.Id); }); row.Root = (RectTransform)row.Button.transform;
            row.Root.anchorMin = Vector2.up; row.Root.anchorMax = Vector2.one; row.Root.pivot = Vector2.up; row.Root.sizeDelta = new Vector2(0, RowHeight - 4);
            Stretch(row.Name.rectTransform, 46, 4, 42, 4); row.Name.fontSize = 14;
            var icon = Node("Icon", row.Root); Position(icon, 8, 6, 28, 28); row.Icon = Image(icon, Color.white); row.Icon.preserveAspect = true; row.Icon.raycastTarget = false;
            var star = Button(unfavoriteMark, row.Root, out row.Star, () => { if (row.Definition != null) { Palette.ToggleFavorite(row.Definition.Id); SavePreferences(); } });
            var starRect = (RectTransform)star.transform; starRect.anchorMin = starRect.anchorMax = Vector2.one; starRect.pivot = Vector2.one; starRect.anchoredPosition = new Vector2(-4, -4); starRect.sizeDelta = new Vector2(32, 32); row.Star.alignment = TextAlignmentOptions.Center;
            return row;
        }
        private TMP_InputField CreateSearch(RectTransform parent)
        {
            var root = Node("Search", parent);
            Position(root, 12, 52, 316, 34);
            var background = Image(root, Item);
            var textArea = Node("Text Area", root);
            Stretch(textArea, 8, 2, 8, 2);
            textArea.gameObject.AddComponent<RectMask2D>();

            var placeholder = Label("Search buildings", textArea, 15, FontStyles.Italic, 0, 0, 0, 0);
            Stretch(placeholder.rectTransform);
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.overflowMode = TextOverflowModes.Ellipsis;
            placeholder.color = new Color(0.6f, 0.67f, 0.73f);
            var text = Label("", textArea, 15, FontStyles.Normal, 0, 0, 0, 0);
            Stretch(text.rectTransform);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.extraPadding = true;

            var field = root.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = background;
            field.textViewport = textArea;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.characterLimit = 80;
            field.shouldActivateOnSelect = false;
            field.onValueChanged.AddListener(_ => ApplyFilter());
            return field;
        }
        private TMP_Dropdown CreateCategories(RectTransform parent)
        {
            var root = Node("Category", parent);
            var dropdown = root.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = Image(root, Item);

            var caption = Label("All categories", root, 13, FontStyles.Normal, 0, 0, 0, 0);
            Stretch(caption.rectTransform, 8, 2, 28, 2);
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            caption.overflowMode = TextOverflowModes.Ellipsis;
            dropdown.captionText = caption;

            var arrow = Label("▼", root, 11, FontStyles.Normal, 0, 0, 16, 24);
            arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(1, 0.5f);
            arrow.rectTransform.pivot = new Vector2(1, 0.5f);
            arrow.rectTransform.anchoredPosition = new Vector2(-8, 0);
            arrow.alignment = TextAlignmentOptions.Center;

            var template = Node("Template", root);
            template.anchorMin = Vector2.zero;
            template.anchorMax = Vector2.right;
            template.pivot = new Vector2(0.5f, 1);
            template.anchoredPosition = new Vector2(0, -2);
            template.sizeDelta = new Vector2(0, 180);
            Image(template, Background);
            var list = template.gameObject.AddComponent<ScrollRect>();
            list.horizontal = false;
            list.movementType = ScrollRect.MovementType.Clamped;
            var view = Node("Viewport", template);
            Stretch(view);
            view.gameObject.AddComponent<RectMask2D>();

            var listContent = Node("Content", view);
            listContent.anchorMin = Vector2.up;
            listContent.anchorMax = Vector2.one;
            listContent.pivot = new Vector2(0.5f, 1);
            listContent.sizeDelta = new Vector2(0, 30);
            var item = Node("Item", listContent);
            item.anchorMin = new Vector2(0, 0.5f);
            item.anchorMax = new Vector2(1, 0.5f);
            item.sizeDelta = new Vector2(0, 30);
            var itemToggle = item.gameObject.AddComponent<Toggle>();
            itemToggle.targetGraphic = Image(item, Item);
            var check = Node("Selected", item);
            Position(check, 5, 9, 12, 12);
            var checkImage = Image(check, Accent);
            checkImage.raycastTarget = false;
            itemToggle.graphic = checkImage;
            var itemText = Label("Category", item, 14, FontStyles.Normal, 0, 0, 0, 0);
            Stretch(itemText.rectTransform, 22, 0, 4, 0);
            itemText.textWrappingMode = TextWrappingModes.NoWrap;
            itemText.overflowMode = TextOverflowModes.Ellipsis;

            list.viewport = view;
            list.content = listContent;
            dropdown.template = template;
            dropdown.itemText = itemText;
            template.gameObject.SetActive(false);
            PopulateCategories(dropdown, null);
            dropdown.onValueChanged.AddListener(_ => ApplyFilter());
            return dropdown;
        }
        private void RefreshCategories()
        {
            int selected = categories.value;
            string current = selected > 0 && selected < categoryNames.Count ? categoryNames[selected] : null;
            if (categories.IsExpanded) categories.Hide();
            PopulateCategories(categories, current);
            ApplyFilter();
        }
        private void PopulateCategories(TMP_Dropdown dropdown, string current)
        {
            categoryNames.Clear();
            categoryNames.Add("All categories");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in Registry.Definitions)
                if (!string.IsNullOrEmpty(definition.Category) && unique.Add(definition.Category)) categoryNames.Add(definition.Category);

            dropdown.ClearOptions();
            dropdown.AddOptions(categoryNames);
            dropdown.SetValueWithoutNotify(current == null ? 0 : Mathf.Max(0, categoryNames.IndexOf(current, 1)));
        }
        private void RefreshBindings()
        { for (int i = 0; i < bindingLabels.Count; i++) bindingLabels[i].text = bindingControls[i] + "\n" + input.InputActionFor(bindingControls[i]).GetBindingDisplayString(0); }
        private void Rebound(bool success) { suppressUntil = Time.frameCount + 1; RefreshBindings(); if (success) SavePreferences(); }
        public void SavePreferences()
        {
            if (Palette == null || input == null || string.IsNullOrWhiteSpace(PreferencesKey)) return;
            PlayerPrefs.SetString(PreferencesKey, JsonUtility.ToJson(new Preferences { Palette = Palette.CaptureState(), Input = BuildingInputPreferences.Capture(input) }));
        }
        private void LoadPreferences()
        {
            if (string.IsNullOrWhiteSpace(PreferencesKey) || !PlayerPrefs.HasKey(PreferencesKey)) return;
            var saved = JsonUtility.FromJson<Preferences>(PlayerPrefs.GetString(PreferencesKey)); if (saved?.Palette != null) Palette.RestoreState(saved.Palette); saved?.Input?.Apply(input);
        }
        private void OnDisable() => Cleanup(true);
        private void Cleanup(bool save)
        {
            if (rebinding != null) { rebinding.Finished -= Rebound; rebinding.Dispose(); rebinding = null; }
            if (save && initialized) SavePreferences();
            input?.SetInputBlocked(this, false);
            if (initialized && Controller is BuildingPlacer placer && !placer.ShowHelp) placer.ShowHelp = previousHelp;
            if (Palette != null) { Palette.Changed -= Dirty; Palette.Dispose(); Palette = null; }
            if (Registry != null) Registry.Changed -= RefreshCategories;
            toggle?.Dispose(); toggle = null; foreach (var action in numberActions) action.Dispose(); numberActions.Clear();
            if (canvasObject != null) { canvasObject.SetActive(false); if (Application.isPlaying) Destroy(canvasObject); else DestroyImmediate(canvasObject); }
            canvasObject = null; rows.Clear(); hotbarButtons.Clear(); hotbarLabels.Clear(); categoryNames.Clear(); bindingLabels.Clear(); bindingControls.Clear();
            initialized = false; Registry = null; previousWidth = previousHeight = -1;
        }
        private RectTransform Node(string name, Transform parent) { var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform; rect.SetParent(parent, false); return rect; }
        private static Image Image(RectTransform rect, Color color) { var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image; }
        private TextMeshProUGUI Label(string text, RectTransform parent, int size, FontStyles style, float x, float y, float width, float height)
        {
            var rect = Node("Label", parent);
            Position(rect, x, y, width, height);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = new Color(0.94f, 0.96f, 0.98f);
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.richText = true;
            label.parseCtrlCharacters = false;
            return label;
        }
        private Button Button(string text, RectTransform parent, out TextMeshProUGUI label, UnityEngine.Events.UnityAction click)
        {
            var rect = Node(text.Length == 0 ? "Building" : text, parent);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = Image(rect, Item);
            label = Label(text, rect, 14, FontStyles.Normal, 0, 0, 100, 32);
            Stretch(label.rectTransform, 8, 2, 8, 2);
            button.onClick.AddListener(click);
            return button;
        }
        private static void Position(RectTransform rect, float x, float y, float width, float height) { rect.anchorMin = rect.anchorMax = Vector2.up; rect.pivot = Vector2.up; rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
        private static void Bottom(RectTransform rect, float x, float y, float width, float height) { rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height); }
        private static void Stretch(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top); }
    }
}
