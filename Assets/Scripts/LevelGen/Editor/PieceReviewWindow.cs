using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Editor window (TeaLeaf > Level Gen > Review Pieces) for stage 2: going
    /// through the catalogue piece by piece, confirming by eye what the
    /// scanner suggested, and saving it to annotations.json.
    ///
    /// The left side lists the catalogue, filtered by folder, suggested
    /// orientation, flag, style, status and name. Choosing a piece puts a copy of
    /// it in the open scene, high above the level, and frames it in the
    /// Scene view, where Handles draw its pivot, render box (white), collider
    /// box (green), back plane (cyan), walking surface (pale green), openings
    /// (orange), ramps (white outline), front(s) (yellow arrows) and
    /// connection points (magenta, named, with their way out). The right
    /// side shows what was measured and the annotation's fields, prefilled
    /// from the catalogue; the chosen connection point can also be dragged
    /// in the Scene view. Enter accepts the piece and moves to the next,
    /// the up and down arrows move through the list.
    ///
    /// The preview copy is a plain Instantiate (not a prefab instance) marked
    /// DontSave and NotEditable, with its lights off, and is destroyed when
    /// another piece is chosen, the window closes, scripts reload or play
    /// starts: it is never saved with the scene. Handles are used rather than
    /// IDebugDrawable because this is an editor window, not a scene component.
    /// </summary>
    public class PieceReviewWindow : EditorWindow
    {
        private const string PreviewName = "[Piece Review Preview]";
        private const float ListWidth = 300f;
        private const float PointSize = 0.12f;

        // Where the preview copy stands: well above the level, against the sky.
        private static readonly Vector3 _previewPosition = new(0f, 300f, 0f);

        private static readonly string[] _statusNames = { "All", "Not looked at or not verified", "Verified", "Excluded" };
        private static readonly Vector3[] _levelAxes = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        private static readonly string[] _levelAxisNames = { "+X", "-X", "+Z", "-Z" };
        private static readonly Vector3[] _axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly string[] _axisNames = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };

        // Filters and the chosen piece, serialized so they survive a script reload.
        [SerializeField] private string selectedGuid;
        [SerializeField] private string folderFilter = "";
        [SerializeField] private string orientationFilter = "";
        [SerializeField] private string flagFilter = "";
        [SerializeField] private string styleFilter = "";
        [SerializeField] private int statusFilter = 1;
        [SerializeField] private string search = "";

        private PrefabCatalogue _catalogue;
        private PieceAnnotations _annotations;
        private readonly Dictionary<string, PieceAnnotation> _byGuid = new();
        private readonly List<CatalogueEntry> _filtered = new();
        private string[] _folderOptions;
        private string[] _orientationOptions;
        private string[] _flagOptions;
        private string[] _styleOptions;
        // Every style any annotation has, offered as a toggle on each piece.
        private readonly SortedSet<string> _knownStyles = new(StringComparer.Ordinal);
        private string[] _categoryLabels;
        private string _loadError;
        private int _orphans;

        private CatalogueEntry _entry;
        // The chosen piece's suggested annotation, until it is first changed
        // (then it is stored and this is null).
        private PieceAnnotation _draft;
        private GameObject _preview;
        private int _selectedConnection = -1;
        private bool _unsaved;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private static GUIStyle _sceneLabelStyle;

        /// <summary>
        /// The annotation being shown: the stored one, or the suggestion.
        /// </summary>
        private PieceAnnotation Current => _byGuid.TryGetValue(_entry.guid, out PieceAnnotation stored) ? stored : _draft;

        [MenuItem("TeaLeaf/Level Gen/Review Pieces")]
        private static void Open()
        {
            GetWindow<PieceReviewWindow>("Review Pieces");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Load();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SaveIfChanged();
            DestroyPreview();
        }

        /// <summary>
        /// Takes the preview copy out before play starts, and puts it back
        /// after.
        /// </summary>
        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) {
                SaveIfChanged();
                DestroyPreview();
            } else if (state == PlayModeStateChange.EnteredEditMode) {
                ShowPreview(false);
            }
        }

        /// <summary>
        /// Reads the catalogue and the annotations, builds the filter lists
        /// and chooses the piece chosen before (if it is still there).
        /// </summary>
        private void Load()
        {
            _loadError = null;
            _catalogue = null;
            _annotations = null;
            _byGuid.Clear();

            if (!File.Exists(PrefabCatalogueScanner.CataloguePath)) {
                _loadError = "There is no catalogue yet: run TeaLeaf > Level Gen > Scan Prefab Catalogue.";
                return;
            }

            _catalogue = JsonUtility.FromJson<PrefabCatalogue>(File.ReadAllText(PrefabCatalogueScanner.CataloguePath));

            if (_catalogue == null || _catalogue.format != PrefabCatalogue.CurrentFormat) {
                _loadError = "The catalogue is in an old format: scan it again.";
                _catalogue = null;
                return;
            }

            _annotations = PieceAnnotations.Load();

            if (_annotations == null) {
                _loadError = $"'{PieceAnnotations.AnnotationsPath}' is in another format; it is left as it is.";
                return;
            }

            foreach (PieceAnnotation piece in _annotations.pieces) {
                _byGuid[piece.guid] = piece;
            }

            // Keep the readable name and path in step with the catalogue (saved
            // with the next change), and count annotations whose prefab is gone.
            SortedSet<string> folders = new(StringComparer.Ordinal);
            SortedSet<string> orientations = new(StringComparer.Ordinal);
            SortedSet<string> flags = new(StringComparer.Ordinal);
            int matched = 0;

            foreach (CatalogueEntry entry in _catalogue.prefabs) {
                folders.Add(entry.folder);
                orientations.Add(entry.orientation);
                flags.UnionWith(entry.flags);

                if (_byGuid.TryGetValue(entry.guid, out PieceAnnotation piece)) {
                    piece.name = entry.name;
                    piece.path = entry.path;
                    matched++;
                }
            }

            _orphans = _annotations.pieces.Count - matched;
            _folderOptions = WithAll(folders);
            _orientationOptions = WithAll(orientations);
            _flagOptions = WithAll(flags);
            RebuildStyles();
            _categoryLabels = new string[PieceAnnotation.Categories.Length];

            for (int i = 0; i < _categoryLabels.Length; i++) {
                _categoryLabels[i] = i == 0 ? "(choose)" : PieceAnnotation.Categories[i];
            }

            RebuildFilter();
            Select(_catalogue.prefabs.Find(entry => entry.guid == selectedGuid), false);
        }

        /// <summary>
        /// A popup's options: "All" (no filter) and then the values.
        /// </summary>
        private static string[] WithAll(SortedSet<string> values)
        {
            List<string> options = new() { "All" };
            options.AddRange(values);
            return options.ToArray();
        }

        /// <summary>
        /// Gathers every style the annotations use, for the style toggles and
        /// the style filter.
        /// </summary>
        private void RebuildStyles()
        {
            _knownStyles.Clear();
            _knownStyles.UnionWith(StyleRules.All);

            foreach (PieceAnnotation piece in _annotations.pieces) {
                _knownStyles.UnionWith(piece.styles);
            }

            _styleOptions = WithAll(_knownStyles);
        }

        /// <summary>
        /// Rebuilds the list of pieces the filters let through, in catalogue
        /// (path) order.
        /// </summary>
        private void RebuildFilter()
        {
            _filtered.Clear();

            foreach (CatalogueEntry entry in _catalogue.prefabs) {
                if (folderFilter != "" && entry.folder != folderFilter) {
                    continue;
                }

                if (orientationFilter != "" && entry.orientation != orientationFilter) {
                    continue;
                }

                if (flagFilter != "" && !entry.flags.Contains(flagFilter)) {
                    continue;
                }

                if (search != "" && entry.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) {
                    continue;
                }

                _byGuid.TryGetValue(entry.guid, out PieceAnnotation piece);

                // A piece not looked at yet is filtered by its suggested styles.
                if (styleFilter != "" && !(piece != null ? piece.styles : StyleRules.Suggest(entry.name)).Contains(styleFilter)) {
                    continue;
                }

                bool excluded = piece != null && piece.excluded;
                bool verified = piece != null && piece.verified && !excluded;

                bool shown = statusFilter switch {
                    1 => !verified && !excluded,
                    2 => verified,
                    3 => excluded,
                    _ => true
                };

                if (shown) {
                    _filtered.Add(entry);
                }
            }
        }

        /// <summary>
        /// Chooses a piece: its suggestion is made ready if it has no
        /// annotation yet, and its preview copy is put in the scene.
        /// </summary>
        private void Select(CatalogueEntry entry, bool frame)
        {
            SaveIfChanged();
            _entry = entry;
            selectedGuid = entry?.guid;
            _selectedConnection = -1;
            _draft = entry != null && !_byGuid.ContainsKey(entry.guid) ? PieceAnnotation.Suggest(entry) : null;
            GUIUtility.keyboardControl = 0;
            ShowPreview(frame);
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Moves the choice up or down the filtered list.
        /// </summary>
        private void Step(int delta)
        {
            if (_filtered.Count == 0) {
                return;
            }

            int index = Mathf.Clamp(_filtered.IndexOf(_entry) + delta, 0, _filtered.Count - 1);
            Select(_filtered[index], true);
        }

        /// <summary>
        /// Marks the chosen piece verified (and not excluded), saves, and
        /// moves to the next piece in the list.
        /// </summary>
        private void AcceptAndNext()
        {
            if (_entry == null) {
                return;
            }

            PieceAnnotation piece = Current;

            if (string.IsNullOrEmpty(piece.category)) {
                ShowNotification(new GUIContent("Choose a category first"));
                return;
            }

            piece.verified = true;
            piece.excluded = false;
            Store();
            MoveOn();
        }

        /// <summary>
        /// Marks the chosen piece excluded (never placed), saves, and moves
        /// to the next piece in the list.
        /// </summary>
        private void ExcludeAndNext()
        {
            if (_entry == null) {
                return;
            }

            Current.excluded = true;
            Store();
            MoveOn();
        }

        /// <summary>
        /// After a piece is accepted or excluded: saves, refilters (the piece
        /// may have left the list) and chooses the one after it.
        /// </summary>
        private void MoveOn()
        {
            int oldIndex = _filtered.IndexOf(_entry);
            SaveIfChanged();
            RebuildFilter();

            if (_filtered.Count == 0) {
                Select(null, false);
                return;
            }

            int index = _filtered.IndexOf(_entry);
            index = index >= 0 ? index + 1 : Mathf.Max(oldIndex, 0);

            if (index >= _filtered.Count) {
                ShowNotification(new GUIContent("End of the list"));
                index = _filtered.Count - 1;
            }

            Select(_filtered[index], true);
        }

        /// <summary>
        /// Stores the chosen piece's annotation (the suggestion becomes a
        /// stored annotation the first time it changes) and marks the file
        /// as needing a save.
        /// </summary>
        private void Store()
        {
            if (!_byGuid.ContainsKey(_entry.guid)) {
                _annotations.pieces.Add(_draft);
                _byGuid[_entry.guid] = _draft;
                _draft = null;
            }

            _unsaved = true;
        }

        /// <summary>
        /// Writes annotations.json if anything changed, unless a Scene view
        /// handle is still being dragged (saved when it is let go).
        /// </summary>
        private void SaveIfChanged()
        {
            if (!_unsaved || _annotations == null || GUIUtility.hotControl != 0) {
                return;
            }

            _annotations.Save();
            _unsaved = false;
        }

        private void OnGUI()
        {
            if (_loadError != null) {
                EditorGUILayout.HelpBox(_loadError, MessageType.Warning);

                if (GUILayout.Button("Reload")) {
                    Load();
                    GUIUtility.ExitGUI();
                }

                return;
            }

            SaveIfChanged();
            HandleKeys();

            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawDetail();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Enter accepts and moves on; the up and down arrows move through the
        /// list. Ignored while a text or number field is being typed in.
        /// </summary>
        private void HandleKeys()
        {
            Event current = Event.current;

            if (current.type != EventType.KeyDown || EditorGUIUtility.editingTextField) {
                return;
            }

            switch (current.keyCode) {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    AcceptAndNext();
                    break;
                case KeyCode.DownArrow:
                    Step(1);
                    break;
                case KeyCode.UpArrow:
                    Step(-1);
                    break;
                default:
                    return;
            }

            // What is drawn has changed since this event's layout was worked
            // out, so this pass ends here and the next one draws it afresh.
            current.Use();
            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// The filters, the counts and the list of pieces.
        /// </summary>
        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            EditorGUI.BeginChangeCheck();
            folderFilter = FilterPopup("Folder", folderFilter, _folderOptions);
            orientationFilter = FilterPopup("Suggested", orientationFilter, _orientationOptions);
            flagFilter = FilterPopup("Flag", flagFilter, _flagOptions);
            styleFilter = FilterPopup("Style", styleFilter, _styleOptions);
            statusFilter = EditorGUILayout.Popup("Show", statusFilter, _statusNames);
            search = EditorGUILayout.TextField("Name", search);

            if (EditorGUI.EndChangeCheck()) {
                RebuildFilter();
                GUIUtility.ExitGUI();
            }

            int verified = 0;
            int excluded = 0;

            foreach (PieceAnnotation piece in _annotations.pieces) {
                if (piece.excluded) {
                    excluded++;
                } else if (piece.verified) {
                    verified++;
                }
            }

            EditorGUILayout.LabelField($"{_filtered.Count} shown. {verified} verified, {excluded} excluded of {_catalogue.prefabs.Count}.", EditorStyles.miniLabel);

            if (_orphans > 0) {
                EditorGUILayout.HelpBox($"{_orphans} annotation(s) are for prefabs no longer in the catalogue.", MessageType.Info);
            }

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            foreach (CatalogueEntry entry in _filtered) {
                _byGuid.TryGetValue(entry.guid, out PieceAnnotation piece);
                string mark = piece == null ? "  " : piece.excluded ? "✗" : piece.verified ? "✓" : "·";
                GUIStyle style = entry == _entry ? EditorStyles.boldLabel : EditorStyles.label;

                if (GUILayout.Button($"{mark}  {entry.name}", style)) {
                    Select(entry, true);
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// A filter popup whose first option, "All", means no filter (an
        /// empty value).
        /// </summary>
        private static string FilterPopup(string label, string value, string[] options)
        {
            int index = Mathf.Max(0, Array.IndexOf(options, value));
            index = EditorGUILayout.Popup(label, index, options);
            return index == 0 ? "" : options[index];
        }

        /// <summary>
        /// The chosen piece: what was measured, its annotation and the
        /// buttons that move through the list.
        /// </summary>
        private void DrawDetail()
        {
            EditorGUILayout.BeginVertical();

            if (_entry == null) {
                EditorGUILayout.LabelField("Choose a piece from the list.");
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            EditorGUILayout.LabelField(_entry.name, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.SelectableLabel(_entry.path, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            if (GUILayout.Button("Ping", GUILayout.Width(50f))) {
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(_entry.path));
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            DrawMeasured();
            EditorGUILayout.Space();
            DrawAnnotation();
            EditorGUILayout.EndScrollView();
            DrawButtons();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// What the catalogue measured, read-only.
        /// </summary>
        private void DrawMeasured()
        {
            EditorGUILayout.LabelField("Measured", EditorStyles.boldLabel);
            string suggestion = _entry.orientation;

            if (_entry.suggestedFronts.Count > 0) {
                suggestion += _entry.frontAssumed
                    ? $", front {AxisNames(_entry.suggestedFronts)} (assumed)"
                    : $", front {AxisNames(_entry.suggestedFronts)} (confidence {_entry.frontConfidence:0.00})";
            }

            if (!string.IsNullOrEmpty(_entry.suggestedCategory)) {
                suggestion += $", {_entry.suggestedCategory}";
            }

            EditorGUILayout.LabelField("Suggested", suggestion);

            if (_entry.hasWalkSurface) {
                EditorGUILayout.LabelField("Walk surface", $"at {_entry.walkHeight:0.###} m, {_entry.walkMax.x - _entry.walkMin.x:0.###} x {_entry.walkMax.z - _entry.walkMin.z:0.###} m ({_entry.walkShare:P0} of collider tops)");
            }

            foreach (Opening opening in _entry.openings) {
                Vector3 openingSize = opening.max - opening.min;
                EditorGUILayout.LabelField("Opening", $"{opening.kind}, {Mathf.Max(openingSize.x, openingSize.z):0.##} x {openingSize.y:0.##} m");
            }

            foreach (RampInfo ramp in _entry.ramps) {
                Vector3 run = ramp.top - ramp.foot;
                EditorGUILayout.LabelField("Ramp", $"{ramp.width:0.##} m wide, rises {run.y:0.##} m over {new Vector2(run.x, run.z).magnitude:0.##} m");
            }
            EditorGUILayout.LabelField("Render box", _entry.hasRenderBounds ? $"{Metres(_entry.renderSize)}, pivot at {_entry.pivotInBounds.ToString("0.00")} of it" : "none");
            EditorGUILayout.LabelField("Collider box", _entry.hasColliderBounds ? Metres(_entry.colliderSize) : "none");

            if (_entry.hasBackPlane) {
                EditorGUILayout.LabelField("Back plane", Metres(_entry.backPlaneSize));
            }

            EditorGUILayout.LabelField("LOD0", $"{_entry.trianglesLod0} triangles, {_entry.renderersLod0} renderers, {_entry.materialsLod0} materials");

            if (_entry.lights.Count > 0) {
                EditorGUILayout.LabelField("Lights", $"{_entry.lights.Count} (turned off where placed)");
            }

            if (_entry.flags.Count > 0) {
                EditorGUILayout.LabelField("Flags", string.Join(", ", _entry.flags), EditorStyles.wordWrappedLabel);
            }
        }

        /// <summary>
        /// The annotation's fields. Any change stores the annotation and is
        /// saved straight away.
        /// </summary>
        private void DrawAnnotation()
        {
            PieceAnnotation piece = Current;
            bool stored = _byGuid.ContainsKey(_entry.guid);
            EditorGUILayout.LabelField(stored ? "Annotation" : "Annotation (suggested, not saved yet)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            int category = Mathf.Max(0, Array.IndexOf(PieceAnnotation.Categories, piece.category));
            category = EditorGUILayout.Popup("Category", category, _categoryLabels);
            piece.category = PieceAnnotation.Categories[category];
            DrawStyles(piece);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Front");

            for (int i = 0; i < _levelAxes.Length; i++) {
                int index = IndexOfAxis(piece.fronts, _levelAxes[i]);
                bool on = GUILayout.Toggle(index >= 0, _levelAxisNames[i], EditorStyles.miniButton, GUILayout.Width(40f));

                if (on && index < 0) {
                    piece.fronts.Add(_levelAxes[i]);
                } else if (!on && index >= 0) {
                    piece.fronts.RemoveAt(index);
                }
            }

            EditorGUILayout.EndHorizontal();
            piece.width = PieceAnnotation.Round(EditorGUILayout.DelayedFloatField("Width", piece.width));
            piece.height = PieceAnnotation.Round(EditorGUILayout.DelayedFloatField("Height", piece.height));
            piece.depth = PieceAnnotation.Round(EditorGUILayout.DelayedFloatField("Depth", piece.depth));

            EditorGUILayout.LabelField("Connection points (choose one to drag it in the Scene view)");
            int removeAt = -1;

            for (int i = 0; i < piece.connections.Count; i++) {
                ConnectionPoint point = piece.connections[i];
                EditorGUILayout.BeginHorizontal();
                bool chosen = GUILayout.Toggle(_selectedConnection == i, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(16f));

                if (chosen != (_selectedConnection == i)) {
                    _selectedConnection = chosen ? i : -1;
                    SceneView.RepaintAll();
                }

                point.name = EditorGUILayout.DelayedTextField(point.name, GUILayout.Width(70f));
                point.position = PieceAnnotation.Round(EditorGUILayout.Vector3Field(GUIContent.none, point.position));
                int direction = EditorGUILayout.Popup(IndexOfAxis(_axes, point.direction), _axisNames, GUILayout.Width(45f));

                if (direction >= 0) {
                    point.direction = _axes[direction];
                }

                if (GUILayout.Button("x", GUILayout.Width(20f))) {
                    removeAt = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0) {
                piece.connections.RemoveAt(removeAt);
                _selectedConnection = -1;
            }

            if (GUILayout.Button("Add point", GUILayout.Width(100f))) {
                piece.connections.Add(new ConnectionPoint { name = "point", position = Vector3.zero, direction = Vector3.up });
                _selectedConnection = piece.connections.Count - 1;
            }

            if (piece.category.StartsWith("pipe-", StringComparison.Ordinal)) {
                DrawPipe(piece);
            }

            piece.note = EditorGUILayout.DelayedTextField("Note", piece.note);
            piece.groundStoreyOnly = EditorGUILayout.Toggle(new GUIContent("Ground Storey Only", "The generator places this wall only on a building's ground storey."),
                piece.groundStoreyOnly);
            piece.showsOpening = EditorGUILayout.Toggle(new GUIContent("Shows Opening", "A plain wall showing a window or door (a blind window, half a door): never used as a side wall."),
                piece.showsOpening);
            piece.fillsStorey = EditorGUILayout.Toggle(new GUIContent("Fills Storey", "A plain wall that, on a storey whose style offers it, stands at every module but the spec's door: no other walls, random doors or window walls."),
                piece.fillsStorey);
            piece.onePerStorey = EditorGUILayout.Toggle(new GUIContent("One Per Storey", "A plain wall that never mixes on a storey with another so marked: each storey draws one of them and uses it for every plain wall (modules, corner fillers, side walls)."),
                piece.onePerStorey);
            piece.excluded = EditorGUILayout.Toggle("Excluded", piece.excluded);
            piece.verified = EditorGUILayout.Toggle("Verified", piece.verified);

            if (EditorGUI.EndChangeCheck()) {
                Store();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(piece.fronts.Count != 1)) {
                if (GUILayout.Button(new GUIContent("Re-measure for this front", "Size and left/right/top/bottom points from the back plane (or render box), for the one front chosen."))) {
                    piece.SuggestSize(_entry);
                    piece.SuggestConnections(_entry);
                    _selectedConnection = -1;
                    Store();
                    SceneView.RepaintAll();
                }
            }

            if (GUILayout.Button(new GUIContent("Reset to suggestion", "Forget this piece's annotation and start again from the catalogue."))) {
                if (stored) {
                    _annotations.pieces.Remove(piece);
                    _byGuid.Remove(_entry.guid);
                    _unsaved = true;
                }

                Select(_entry, false);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// A pipe piece's ends: how they are marked, a button that finds them
        /// from the mesh (PipeEnds), and what the generator will make of them
        /// - a straight's length from a to b, an elbow's two legs from its
        /// bend - in world metres (the root's scale applied). Called inside
        /// DrawAnnotation's change check.
        /// </summary>
        private void DrawPipe(PieceAnnotation piece)
        {
            EditorGUILayout.HelpBox("Pipe ends: one point per open end, named a, b, c..., at the middle of the flange's outer face, "
                + "pointing out of the piece along the pipe. A straight is placed by a and b; an elbow by a and b, whose ways out "
                + "must be at right angles (its bend is where their legs' lines cross, drawn yellow); a T (pipe-tee) by a and b along "
                + "the pipe and c the branch, at right angles; a valve (pipe-valve) by a, pointing into the T's branch; a capped end "
                + "(pipe-end) by b, the flange the pipe meets, its cap face a laid on the wall; a vent (pipe-vent) by b, pointing "
                + "into the wall.", MessageType.None);

            if (GUILayout.Button(new GUIContent("Find pipe ends", "Replace the points with the open ends found on the mesh: flanges where the pipe meets a face of its render box."),
                    GUILayout.Width(120f))) {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(_entry.path);

                if (asset != null) {
                    piece.connections = PipeEnds.Find(asset);
                    _selectedConnection = -1;
                    Store();
                }
            }

            float scale = _entry.rootScale.x != 0f ? Mathf.Abs(_entry.rootScale.x) : 1f;
            ConnectionPoint a = PipeEnds.End(piece, "a");
            ConnectionPoint b = PipeEnds.End(piece, "b");

            if (piece.category == "pipe-straight") {
                EditorGUILayout.LabelField("Generator", a != null && b != null
                    ? $"straight, {Vector3.Distance(a.position, b.position) * scale:0.###} m from a to b"
                    : "needs points a and b");
            } else if (piece.category == "pipe-corner") {
                EditorGUILayout.LabelField("Generator", PipeEnds.Bend(a, b, out Vector3 node)
                    ? $"elbow, leg a {Vector3.Distance(node, a.position) * scale:0.###} m, leg b {Vector3.Distance(node, b.position) * scale:0.###} m"
                    : "needs points a and b pointing at right angles");
            }
        }

        /// <summary>
        /// The piece's styles: a toggle for every style already in use (one
        /// click to put a piece in a style) and a field that adds a new one
        /// when Enter is pressed in it. Called inside DrawAnnotation's change
        /// check, so a change is stored with the rest (the delayed field
        /// counts as a change when it hands its text over).
        /// </summary>
        private void DrawStyles(PieceAnnotation piece)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Styles");
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            int inRow = 0;

            foreach (string style in _knownStyles) {
                // A few toggles to a row, so a long list wraps.
                if (inRow == 4) {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    inRow = 0;
                }

                bool had = piece.styles.Contains(style);
                bool on = GUILayout.Toggle(had, style, EditorStyles.miniButton, GUILayout.MinWidth(70f));

                if (on && !had) {
                    piece.styles.Add(style);
                    piece.styles.Sort(StringComparer.Ordinal);
                } else if (!on && had) {
                    piece.styles.Remove(style);
                }

                inRow++;
            }

            EditorGUILayout.EndHorizontal();
            // A delayed field hands its text over only when Enter is pressed in
            // it (or it loses focus), which is when the new style is added.
            // The button is only a cue: clicking it takes the focus away.
            EditorGUILayout.BeginHorizontal();
            string typed = EditorGUILayout.DelayedTextField("");
            GUILayout.Button("Add style", GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            string added = PieceAnnotation.NormaliseStyle(typed);

            if (added == "") {
                return;
            }

            if (!piece.styles.Contains(added)) {
                piece.styles.Add(added);
                piece.styles.Sort(StringComparer.Ordinal);
            }

            if (_knownStyles.Add(added)) {
                _styleOptions = WithAll(_knownStyles);
            }
        }

        /// <summary>
        /// Moving through the list, accepting and excluding, and framing the
        /// preview. Kept below the scroll view so they are always in reach.
        /// </summary>
        private void DrawButtons()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("◀ Previous")) {
                Step(-1);
                GUIUtility.ExitGUI();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(Current.category))) {
                if (GUILayout.Button("Accept and next (Enter)")) {
                    AcceptAndNext();
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("Exclude and next")) {
                ExcludeAndNext();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Next ▶")) {
                Step(1);
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Frame", GUILayout.Width(60f))) {
                FramePreview();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Puts a copy of the chosen piece in the open scene, at the preview
        /// position, never saved and with its lights off.
        /// </summary>
        private void ShowPreview(bool frame)
        {
            DestroyPreview();

            if (_entry == null || EditorApplication.isPlayingOrWillChangePlaymode) {
                return;
            }

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(_entry.path);

            if (asset == null) {
                return;
            }

            _preview = Instantiate(asset, _previewPosition, Quaternion.identity);
            _preview.name = PreviewName;

            foreach (Transform child in _preview.GetComponentsInChildren<Transform>(true)) {
                child.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            }

            foreach (Light light in _preview.GetComponentsInChildren<Light>(true)) {
                light.enabled = false;
            }

            if (frame) {
                FramePreview();
            }
        }

        /// <summary>
        /// Removes the preview copy, if there is one.
        /// </summary>
        private void DestroyPreview()
        {
            if (_preview != null) {
                DestroyImmediate(_preview);
            }

            _preview = null;
        }

        /// <summary>
        /// Points the Scene view at the preview copy.
        /// </summary>
        private void FramePreview()
        {
            SceneView view = SceneView.lastActiveSceneView;

            if (view == null || _preview == null || !_entry.hasRenderBounds) {
                return;
            }

            Transform root = _preview.transform;
            Vector3 centre = root.TransformPoint((_entry.renderMin + _entry.renderMax) * 0.5f);
            Vector3 size = Vector3.Scale(_entry.renderSize, root.lossyScale);
            view.Frame(new Bounds(centre, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) + Vector3.one), false);
        }

        /// <summary>
        /// Draws the chosen piece's pivot, boxes, front(s) and connection
        /// points on the preview copy, and a move handle on the chosen
        /// connection point.
        /// </summary>
        private void OnSceneGUI(SceneView view)
        {
            if (_preview == null || _entry == null) {
                return;
            }

            SaveIfChanged();
            PieceAnnotation piece = Current;
            _sceneLabelStyle ??= new GUIStyle(EditorStyles.whiteBoldLabel);

            using (new Handles.DrawingScope(_preview.transform.localToWorldMatrix)) {
                if (Event.current.type == EventType.Repaint) {
                    DrawPieceHandles(piece);
                }

                if (_selectedConnection >= 0 && _selectedConnection < piece.connections.Count) {
                    ConnectionPoint point = piece.connections[_selectedConnection];
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.PositionHandle(point.position, Quaternion.identity);

                    if (EditorGUI.EndChangeCheck()) {
                        point.position = PieceAnnotation.Round(moved);
                        Store();
                        Repaint();
                    }
                }
            }
        }

        /// <summary>
        /// The Handles drawing itself, in the preview root's space.
        /// </summary>
        private void DrawPieceHandles(PieceAnnotation piece)
        {
            // The pivot: the root's three axes, half a metre long.
            Handles.color = Color.red;
            Handles.DrawLine(Vector3.zero, Vector3.right * 0.5f, 2f);
            Handles.color = Color.green;
            Handles.DrawLine(Vector3.zero, Vector3.up * 0.5f, 2f);
            Handles.color = Color.blue;
            Handles.DrawLine(Vector3.zero, Vector3.forward * 0.5f, 2f);
            Handles.Label(Vector3.zero, "pivot", _sceneLabelStyle);

            Vector3 centre = (_entry.renderMin + _entry.renderMax) * 0.5f;
            Vector3 extents = _entry.renderSize * 0.5f;

            if (_entry.hasRenderBounds) {
                Handles.color = Color.white;
                Handles.DrawWireCube(centre, _entry.renderSize);
            }

            if (_entry.hasColliderBounds) {
                Handles.color = Color.green;
                Handles.DrawWireCube((_entry.colliderMin + _entry.colliderMax) * 0.5f, _entry.colliderSize);
            }

            if (_entry.hasBackPlane) {
                Handles.color = Color.cyan;
                Handles.DrawWireCube((_entry.backPlaneMin + _entry.backPlaneMax) * 0.5f, _entry.backPlaneSize);
            }

            // The walking surface (collider tops), each opening, and each ramp's slope.
            if (_entry.hasWalkSurface) {
                Handles.color = new Color(0.4f, 1f, 0.4f);
                Vector3 min = _entry.walkMin;
                Vector3 max = _entry.walkMax;
                Handles.DrawSolidRectangleWithOutline(new[] {
                    new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z)
                }, new Color(0.4f, 1f, 0.4f, 0.1f), Handles.color);
            }

            Handles.color = new Color(1f, 0.6f, 0.1f);

            foreach (Opening opening in _entry.openings) {
                Handles.DrawWireCube((opening.min + opening.max) * 0.5f, opening.max - opening.min);
                Handles.Label(new Vector3(opening.min.x, opening.max.y, opening.min.z), opening.kind, _sceneLabelStyle);
            }

            foreach (RampInfo ramp in _entry.ramps) {
                Vector3 across = Vector3.Cross(Vector3.up, ramp.direction) * (ramp.width * 0.5f);
                Handles.DrawLine(ramp.foot - across, ramp.top - across, 2f);
                Handles.DrawLine(ramp.foot + across, ramp.top + across, 2f);
                Handles.DrawLine(ramp.foot - across, ramp.foot + across, 2f);
                Handles.DrawLine(ramp.top - across, ramp.top + across, 2f);
            }

            // Each front: an arrow out of the render box's face on that side.
            Handles.color = Color.yellow;

            foreach (Vector3 front in piece.fronts) {
                Vector3 start = centre + front * (Mathf.Abs(extents.x * front.x) + Mathf.Abs(extents.z * front.z));
                Vector3 end = start + front;
                Handles.DrawLine(start, end, 3f);
                Handles.ConeHandleCap(0, end, Quaternion.LookRotation(front), 0.25f, EventType.Repaint);
                Handles.Label(end, "front", _sceneLabelStyle);
            }

            for (int i = 0; i < piece.connections.Count; i++) {
                ConnectionPoint point = piece.connections[i];
                Handles.color = i == _selectedConnection ? new Color(1f, 0.5f, 0f) : Color.magenta;
                Handles.SphereHandleCap(0, point.position, Quaternion.identity, PointSize, EventType.Repaint);
                Handles.DrawLine(point.position, point.position + point.direction * 0.4f, 2f);
                Handles.Label(point.position + point.direction * 0.5f, point.name, _sceneLabelStyle);
            }

            // A pipe as the generator reads it: a straight's axis from a to
            // b, an elbow's bend and its two legs.
            ConnectionPoint a = PipeEnds.End(piece, "a");
            ConnectionPoint b = PipeEnds.End(piece, "b");
            Handles.color = Color.yellow;

            if (piece.category == "pipe-straight" && a != null && b != null) {
                Handles.DrawDottedLine(a.position, b.position, 4f);
            } else if (piece.category == "pipe-corner" && PipeEnds.Bend(a, b, out Vector3 node)) {
                Handles.DrawLine(node, a.position, 2f);
                Handles.DrawLine(node, b.position, 2f);
                Handles.SphereHandleCap(0, node, Quaternion.identity, PointSize, EventType.Repaint);
                Handles.Label(node, "bend", _sceneLabelStyle);
            }
        }

        /// <summary>
        /// The index of the axis in a list nearest to a direction, or -1 if
        /// none is within about 25 degrees of it.
        /// </summary>
        private static int IndexOfAxis(IList<Vector3> axes, Vector3 direction)
        {
            for (int i = 0; i < axes.Count; i++) {
                if (Vector3.Dot(axes[i], direction) > 0.9f) {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Front directions as axis names, e.g. "+X +Z".
        /// </summary>
        private static string AxisNames(List<Vector3> directions)
        {
            List<string> names = new();

            foreach (Vector3 direction in directions) {
                int index = IndexOfAxis(_axes, direction);
                names.Add(index >= 0 ? _axisNames[index] : direction.ToString());
            }

            return string.Join(" ", names);
        }

        /// <summary>
        /// A size as "w x h x d m".
        /// </summary>
        private static string Metres(Vector3 size)
        {
            return $"{size.x:0.###} x {size.y:0.###} x {size.z:0.###} m";
        }
    }
}
