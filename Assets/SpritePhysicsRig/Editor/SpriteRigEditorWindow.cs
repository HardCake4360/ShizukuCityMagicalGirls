using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig.Editor
{
    public sealed class SpriteRigEditorWindow : EditorWindow
    {
        enum EditMode
        {
            Mesh,
            Bone,
            Weight,
            Physics,
            Preview
        }

        enum BoneDragMode
        {
            None,
            Position,
            Endpoint
        }

        const float HeaderHeight = 48f;
        const float SidebarWidth = 320f;
        const float SelectionRadius = 12f;

        [SerializeField] SpriteRigAsset rigAsset;
        [SerializeField] Sprite previewSprite;
        [SerializeField] DeformableSpriteRenderer previewComponent;
        [SerializeField] EditMode editMode;
        [SerializeField] float zoom = 0.9f;
        [SerializeField] int gridColumns = 2;
        [SerializeField] int gridRows = 2;
        [SerializeField] int selectedVertex = -1;
        [SerializeField] int selectedBone = -1;
        [SerializeField] int edgeVertexA;
        [SerializeField] int edgeVertexB = 1;
        [SerializeField] int triangleVertexA;
        [SerializeField] int triangleVertexB = 1;
        [SerializeField] int triangleVertexC = 2;
        [SerializeField] bool pixelSnap;
        [SerializeField, Range(8f, 120f)] float weightBrushRadius = 40f;
        [SerializeField, Range(0.01f, 1f)] float weightBrushStrength = 0.2f;

        bool draggingVertex;
        BoneDragMode boneDragMode;
        Vector2 boneDragPositionOffset;
        bool physicsPreviewPlaying;
        double previousPreviewTime;
        Vector2 scrollPosition;
        readonly List<RigValidationIssue> validationIssues = new List<RigValidationIssue>();

        [MenuItem("Window/2D Rig/Sprite Rig Editor")]
        public static void Open()
        {
            GetWindow<SpriteRigEditorWindow>("Sprite Rig Editor");
        }

        public static void Open(SpriteRigAsset asset)
        {
            SpriteRigEditorWindow window = GetWindow<SpriteRigEditorWindow>("Sprite Rig Editor");
            window.rigAsset = asset;
            window.selectedVertex = -1;
            window.selectedBone = -1;
            window.Show();
            window.Repaint();
        }

        void OnEnable()
        {
            minSize = new Vector2(850f, 520f);
            Undo.undoRedoPerformed += HandleUndoRedo;
            EditorApplication.update += HandleEditorUpdate;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.update -= HandleEditorUpdate;
            physicsPreviewPlaying = false;
        }

        void OnSelectionChange()
        {
            if (Selection.activeObject is SpriteRigAsset selectedAsset)
            {
                rigAsset = selectedAsset;
                Repaint();
            }
            else if (Selection.activeGameObject != null)
            {
                DeformableSpriteRenderer selectedRenderer =
                    Selection.activeGameObject.GetComponent<DeformableSpriteRenderer>();
                if (selectedRenderer != null)
                {
                    previewComponent = selectedRenderer;
                    rigAsset = selectedRenderer.RigAsset;
                    previewSprite = selectedRenderer.SourceRenderer != null
                        ? selectedRenderer.SourceRenderer.sprite
                        : previewSprite;
                    Repaint();
                }
            }
        }

        void OnGUI()
        {
            DrawHeader();
            Rect canvasRect = new Rect(
                8f,
                HeaderHeight,
                Mathf.Max(100f, position.width - SidebarWidth - 20f),
                Mathf.Max(100f, position.height - HeaderHeight - 8f));
            Rect sidebarRect = new Rect(
                position.width - SidebarWidth,
                HeaderHeight,
                SidebarWidth - 8f,
                position.height - HeaderHeight - 8f);

            DrawCanvas(canvasRect);
            DrawSidebar(sidebarRect);
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            rigAsset = (SpriteRigAsset)EditorGUILayout.ObjectField(
                new GUIContent("Rig Asset"),
                rigAsset,
                typeof(SpriteRigAsset),
                false,
                GUILayout.MinWidth(280f),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            previewSprite = (Sprite)EditorGUILayout.ObjectField(
                new GUIContent("Preview Sprite"),
                previewSprite,
                typeof(Sprite),
                false,
                GUILayout.MinWidth(260f),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            zoom = EditorGUILayout.Slider(
                new GUIContent("Zoom"),
                zoom,
                0.25f,
                1f,
                GUILayout.MinWidth(210f),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (EditorGUI.EndChangeCheck())
            {
                selectedVertex = -1;
                selectedBone = -1;
            }
            EditorGUILayout.EndHorizontal();

            editMode = (EditMode)GUILayout.Toolbar((int)editMode, Enum.GetNames(typeof(EditMode)));
        }

        void DrawCanvas(Rect canvasRect)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.11f, 0.11f, 0.11f));
            Rect contentRect = GetContentRect(canvasRect);
            DrawCheckerboard(contentRect);
            DrawSprite(contentRect);

            if (rigAsset == null)
            {
                GUI.Label(canvasRect, "Select or create a SpriteRigAsset.", CenteredLabelStyle());
                return;
            }

            DrawTriangles(contentRect);
            DrawEdges(contentRect);
            DrawBones(contentRect);
            DrawVertices(contentRect);
            HandleCanvasInput(contentRect);
        }

        void DrawSidebar(Rect sidebarRect)
        {
            GUILayout.BeginArea(sidebarRect, EditorStyles.helpBox);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (rigAsset == null)
            {
                EditorGUILayout.HelpBox("Assign a SpriteRigAsset to begin editing.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            DrawValidation();
            EditorGUILayout.Space();
            switch (editMode)
            {
                case EditMode.Mesh:
                    DrawMeshPanel();
                    break;
                case EditMode.Bone:
                    DrawBonePanel();
                    break;
                case EditMode.Weight:
                    DrawWeightPanel();
                    break;
                case EditMode.Physics:
                    DrawPhysicsPanel();
                    break;
                case EditMode.Preview:
                    DrawPreviewPanel();
                    break;
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawValidation()
        {
            bool valid = rigAsset.Validate(validationIssues);
            EditorGUILayout.LabelField(
                valid ? "Rig Valid" : $"Rig Errors: {validationIssues.Count}",
                valid ? EditorStyles.boldLabel : EditorStyles.whiteBoldLabel);

            int shown = Mathf.Min(4, validationIssues.Count);
            for (int i = 0; i < shown; i++)
            {
                EditorGUILayout.HelpBox(validationIssues[i].ToString(), MessageType.Error);
            }
        }

        void DrawMeshPanel()
        {
            EditorGUILayout.LabelField("Automatic Geometry", EditorStyles.boldLabel);
            gridColumns = EditorGUILayout.IntSlider("Grid Columns", gridColumns, 1, 16);
            gridRows = EditorGUILayout.IntSlider("Grid Rows", gridRows, 1, 16);
            if (GUILayout.Button("Generate Grid"))
            {
                SpriteRigEditorUtility.GenerateGrid(rigAsset, gridColumns, gridRows);
                selectedVertex = -1;
            }

            using (new EditorGUI.DisabledScope(previewSprite == null))
            {
                if (GUILayout.Button("Generate From Sprite Geometry"))
                {
                    if (!SpriteRigEditorUtility.GenerateFromSpriteGeometry(rigAsset, previewSprite, out string error))
                    {
                        EditorUtility.DisplayDialog("Sprite Geometry", error, "OK");
                    }
                    selectedVertex = -1;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Vertex", EditorStyles.boldLabel);
            pixelSnap = EditorGUILayout.Toggle("Pixel Snap", pixelSnap);
            if (GUILayout.Button("Add Vertex At Center"))
            {
                selectedVertex = SpriteRigEditorUtility.AddVertex(rigAsset, new Vector2(0.5f, 0.5f));
            }
            DrawSelectedVertexFields(includeWeights: false);
            using (new EditorGUI.DisabledScope(selectedVertex < 0 || rigAsset.Vertices.Count <= 3))
            {
                if (GUILayout.Button("Delete Selected Vertex"))
                {
                    if (SpriteRigEditorUtility.DeleteVertex(rigAsset, selectedVertex))
                    {
                        selectedVertex = -1;
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Edge", EditorStyles.boldLabel);
            edgeVertexA = EditorGUILayout.IntField("Vertex A", edgeVertexA);
            edgeVertexB = EditorGUILayout.IntField("Vertex B", edgeVertexB);
            if (GUILayout.Button("Add Edge"))
            {
                AddEdge(edgeVertexA, edgeVertexB);
            }

            if (GUILayout.Button("Remove Matching Edge"))
            {
                RemoveEdge(edgeVertexA, edgeVertexB);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Triangle (Counter-clockwise)", EditorStyles.boldLabel);
            triangleVertexA = EditorGUILayout.IntField("Vertex A", triangleVertexA);
            triangleVertexB = EditorGUILayout.IntField("Vertex B", triangleVertexB);
            triangleVertexC = EditorGUILayout.IntField("Vertex C", triangleVertexC);
            if (GUILayout.Button("Add Triangle"))
            {
                AddTriangle(triangleVertexA, triangleVertexB, triangleVertexC);
            }
        }

        void DrawBonePanel()
        {
            EditorGUILayout.LabelField("Bone Authoring", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag a bone body to move its start. Drag the round endpoint to rotate and resize it.",
                MessageType.None);
            if (GUILayout.Button("Add Root Bone"))
            {
                selectedBone = SpriteRigEditorUtility.AddBone(
                    rigAsset,
                    -1,
                    new Vector2(0.5f, 0.5f),
                    0f,
                    0.25f);
            }

            using (new EditorGUI.DisabledScope(selectedBone < 0 || selectedBone >= rigAsset.Bones.Count))
            {
                if (GUILayout.Button("Add Child Bone"))
                {
                    float parentLength = rigAsset.Bones[selectedBone].Length;
                    selectedBone = SpriteRigEditorUtility.AddBone(
                        rigAsset,
                        selectedBone,
                        new Vector2(parentLength, 0f),
                        0f,
                        parentLength);
                }
            }

            DrawSelectedBoneFields(includePhysics: false);

            using (new EditorGUI.DisabledScope(rigAsset.Bones.Count <= 1 || selectedBone < 0))
            {
                if (GUILayout.Button("Delete Selected Bone"))
                {
                    if (SpriteRigEditorUtility.DeleteBone(rigAsset, selectedBone))
                    {
                        selectedBone = Mathf.Clamp(selectedBone - 1, 0, rigAsset.Bones.Count - 1);
                    }
                }
            }
        }

        void DrawWeightPanel()
        {
            EditorGUILayout.LabelField("Weight Painting", EditorStyles.boldLabel);
            if (GUILayout.Button("Generate Distance Weights"))
            {
                SpriteRigEditorUtility.GenerateAutomaticWeights(rigAsset);
            }
            if (GUILayout.Button("Mirror Weights Horizontally"))
            {
                SpriteRigEditorUtility.MirrorWeightsHorizontally(rigAsset);
            }

            string[] boneNames = GetBoneNames();
            selectedBone = EditorGUILayout.Popup(
                "Paint Bone",
                Mathf.Clamp(selectedBone, 0, Mathf.Max(0, boneNames.Length - 1)),
                boneNames);
            weightBrushRadius = EditorGUILayout.Slider("Brush Radius", weightBrushRadius, 8f, 120f);
            weightBrushStrength = EditorGUILayout.Slider("Brush Strength", weightBrushStrength, 0.01f, 1f);
            EditorGUILayout.HelpBox("Hold Shift and drag over vertices to paint the selected bone weight.", MessageType.Info);

            DrawSelectedVertexFields(includeWeights: true);
            if (selectedVertex >= 0 && selectedVertex < rigAsset.Weights.Count)
            {
                if (GUILayout.Button("Normalize Selected Vertex"))
                {
                    Undo.RecordObject(rigAsset, "Normalize Vertex Weights");
                    rigAsset.NormalizeVertexWeights(selectedVertex);
                    EditorUtility.SetDirty(rigAsset);
                }
            }
        }

        void DrawPhysicsPanel()
        {
            EditorGUILayout.LabelField("Bone Physics", EditorStyles.boldLabel);
            DrawSelectedBoneFields(includePhysics: true);
            EditorGUILayout.HelpBox(
                "Collision Radius is consumed by endpoint or capsule collision when enabled on the preview component.",
                MessageType.Info);
        }

        void DrawPreviewPanel()
        {
            EditorGUILayout.LabelField("Scene Preview", EditorStyles.boldLabel);
            previewComponent = (DeformableSpriteRenderer)EditorGUILayout.ObjectField(
                "Rig Component",
                previewComponent,
                typeof(DeformableSpriteRenderer),
                true);

            using (new EditorGUI.DisabledScope(previewComponent == null || !previewComponent.IsBuilt))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(physicsPreviewPlaying ? "Pause" : "Play"))
                {
                    physicsPreviewPlaying = !physicsPreviewPlaying;
                    previousPreviewTime = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("Step"))
                {
                    StepPhysicsPreview(1f / 60f);
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Reset Simulation"))
                {
                    previewComponent.ResetSimulation();
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Rebuild Mesh"))
                {
                    previewComponent.Rebuild();
                    SceneView.RepaintAll();
                }
            }
        }

        void DrawSelectedVertexFields(bool includeWeights)
        {
            if (selectedVertex < 0 || selectedVertex >= rigAsset.Vertices.Count)
            {
                EditorGUILayout.HelpBox("Click a vertex in the canvas.", MessageType.Info);
                return;
            }

            SerializedObject serializedAsset = new SerializedObject(rigAsset);
            SerializedProperty vertex = serializedAsset.FindProperty("vertices").GetArrayElementAtIndex(selectedVertex);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField($"Vertex {selectedVertex}", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(vertex.FindPropertyRelative("position"));
            EditorGUILayout.PropertyField(vertex.FindPropertyRelative("hasCustomUv"));
            if (vertex.FindPropertyRelative("hasCustomUv").boolValue)
            {
                EditorGUILayout.PropertyField(vertex.FindPropertyRelative("uv"));
            }

            if (includeWeights && selectedVertex < rigAsset.Weights.Count)
            {
                SerializedProperty weight = serializedAsset.FindProperty("weights").GetArrayElementAtIndex(selectedVertex);
                string[] boneNames = GetBoneNames();
                for (int i = 0; i < SpriteRigAsset.MaximumInfluenceCount; i++)
                {
                    SerializedProperty boneIndex = weight.FindPropertyRelative($"boneIndex{i}");
                    SerializedProperty weightValue = weight.FindPropertyRelative($"weight{i}");
                    boneIndex.intValue = EditorGUILayout.Popup($"Bone {i}", Mathf.Clamp(boneIndex.intValue, 0, Mathf.Max(0, boneNames.Length - 1)), boneNames);
                    weightValue.floatValue = EditorGUILayout.Slider($"Weight {i}", weightValue.floatValue, 0f, 1f);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rigAsset, "Edit Rig Vertex");
                serializedAsset.ApplyModifiedProperties();
                EditorUtility.SetDirty(rigAsset);
            }
        }

        void DrawSelectedBoneFields(bool includePhysics)
        {
            if (selectedBone < 0 || selectedBone >= rigAsset.Bones.Count)
            {
                EditorGUILayout.HelpBox("Click a bone in the canvas.", MessageType.Info);
                return;
            }

            SerializedObject serializedAsset = new SerializedObject(rigAsset);
            SerializedProperty bone = serializedAsset.FindProperty("bones").GetArrayElementAtIndex(selectedBone);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField($"Bone {selectedBone}", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(bone.FindPropertyRelative("name"));
            EditorGUILayout.PropertyField(bone.FindPropertyRelative("parentIndex"));
            EditorGUILayout.PropertyField(bone.FindPropertyRelative("localPosition"));
            EditorGUILayout.PropertyField(bone.FindPropertyRelative("localRotation"));
            EditorGUILayout.PropertyField(bone.FindPropertyRelative("length"));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Stable ID", bone.FindPropertyRelative("stableId").stringValue);
            EditorGUI.EndDisabledGroup();
            if (includePhysics)
            {
                EditorGUILayout.PropertyField(bone.FindPropertyRelative("physics"), true);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rigAsset, "Edit Rig Bone");
                serializedAsset.ApplyModifiedProperties();
                rigAsset.EnsureStableBoneIds();
                EditorUtility.SetDirty(rigAsset);
            }
        }

        void DrawTriangles(Rect contentRect)
        {
            if (rigAsset.Triangles.Count < 3)
            {
                return;
            }

            Handles.BeginGUI();
            Color color = editMode == EditMode.Mesh
                ? new Color(0.2f, 0.65f, 1f, 0.12f)
                : new Color(0.4f, 0.4f, 0.4f, 0.06f);
            Handles.color = color;
            for (int i = 0; i + 2 < rigAsset.Triangles.Count; i += 3)
            {
                int a = rigAsset.Triangles[i];
                int b = rigAsset.Triangles[i + 1];
                int c = rigAsset.Triangles[i + 2];
                if (!ValidVertex(a) || !ValidVertex(b) || !ValidVertex(c))
                {
                    continue;
                }

                Handles.DrawAAConvexPolygon(
                    RigToCanvas(rigAsset.Vertices[a].Position, contentRect),
                    RigToCanvas(rigAsset.Vertices[b].Position, contentRect),
                    RigToCanvas(rigAsset.Vertices[c].Position, contentRect));
            }
            Handles.EndGUI();
        }

        void DrawEdges(Rect contentRect)
        {
            Handles.BeginGUI();
            for (int i = 0; i < rigAsset.Edges.Count; i++)
            {
                RigEdge edge = rigAsset.Edges[i];
                if (!ValidVertex(edge.VertexA) || !ValidVertex(edge.VertexB))
                {
                    continue;
                }

                Handles.color = edge.Boundary
                    ? new Color(0.2f, 0.85f, 1f, 0.95f)
                    : new Color(0.25f, 0.55f, 0.75f, 0.7f);
                Handles.DrawAAPolyLine(
                    edge.Boundary ? 2.5f : 1.25f,
                    RigToCanvas(rigAsset.Vertices[edge.VertexA].Position, contentRect),
                    RigToCanvas(rigAsset.Vertices[edge.VertexB].Position, contentRect));
            }
            Handles.EndGUI();
        }

        void DrawVertices(Rect contentRect)
        {
            for (int i = 0; i < rigAsset.Vertices.Count; i++)
            {
                Vector2 canvasPosition = RigToCanvas(rigAsset.Vertices[i].Position, contentRect);
                Color color = i == selectedVertex ? Color.yellow : GetVertexColor(i);
                EditorGUI.DrawRect(new Rect(canvasPosition.x - 4f, canvasPosition.y - 4f, 8f, 8f), color);
            }
        }

        void DrawBones(Rect contentRect)
        {
            if (!TryGetBindBoneMatrices(out Matrix4x4[] matrices))
            {
                return;
            }

            Handles.BeginGUI();
            for (int i = 0; i < rigAsset.Bones.Count; i++)
            {
                Matrix4x4 matrix = matrices[i];
                Vector2 start = new Vector2(matrix.m03, matrix.m13);
                Vector2 end = matrix.MultiplyPoint3x4(new Vector3(rigAsset.Bones[i].Length, 0f, 0f));
                Handles.color = i == selectedBone ? Color.yellow : new Color(1f, 0.45f, 0.2f, 0.95f);
                Handles.DrawAAPolyLine(
                    i == selectedBone ? 5f : 3f,
                    RigToCanvas(start, contentRect),
                    RigToCanvas(end, contentRect));
                Vector2 endCanvas = RigToCanvas(end, contentRect);
                Handles.DrawSolidDisc(endCanvas, Vector3.forward, i == selectedBone ? 5f : 3.5f);
            }
            Handles.EndGUI();
        }

        void HandleCanvasInput(Rect contentRect)
        {
            Event current = Event.current;
            if (!contentRect.Contains(current.mousePosition) || rigAsset == null)
            {
                if (current.type == EventType.MouseUp)
                {
                    draggingVertex = false;
                    boneDragMode = BoneDragMode.None;
                }
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (editMode == EditMode.Weight && current.shift)
                {
                    Undo.RecordObject(rigAsset, "Paint Rig Weights");
                    PaintWeights(current.mousePosition, contentRect);
                }
                else if (editMode == EditMode.Mesh || editMode == EditMode.Weight)
                {
                    selectedVertex = FindNearestVertex(current.mousePosition, contentRect);
                    draggingVertex = editMode == EditMode.Mesh && selectedVertex >= 0;
                    if (draggingVertex)
                    {
                        Undo.RecordObject(rigAsset, "Move Rig Vertex");
                    }
                }
                else if (editMode == EditMode.Bone || editMode == EditMode.Physics)
                {
                    selectedBone = FindNearestBone(current.mousePosition, contentRect);
                    if (editMode == EditMode.Bone && selectedBone >= 0
                        && TryGetBindBoneMatrices(out Matrix4x4[] matrices))
                    {
                        Vector2 start = new Vector2(
                            matrices[selectedBone].m03,
                            matrices[selectedBone].m13);
                        Vector2 endpoint = matrices[selectedBone].MultiplyPoint3x4(
                            new Vector3(rigAsset.Bones[selectedBone].Length, 0f, 0f));
                        float endpointDistance = Vector2.Distance(
                            current.mousePosition,
                            RigToCanvas(endpoint, contentRect));
                        boneDragMode = endpointDistance <= SelectionRadius
                            ? BoneDragMode.Endpoint
                            : BoneDragMode.Position;
                        boneDragPositionOffset =
                            start - CanvasToRig(current.mousePosition, contentRect);
                        Undo.RecordObject(
                            rigAsset,
                            boneDragMode == BoneDragMode.Endpoint
                                ? "Rotate and Resize Rig Bone"
                                : "Move Rig Bone");
                    }
                }

                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseDrag && editMode == EditMode.Weight && current.shift)
            {
                PaintWeights(current.mousePosition, contentRect);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseDrag && draggingVertex && selectedVertex >= 0)
            {
                SerializedObject serializedAsset = new SerializedObject(rigAsset);
                SerializedProperty positionProperty = serializedAsset
                    .FindProperty("vertices")
                    .GetArrayElementAtIndex(selectedVertex)
                    .FindPropertyRelative("position");
                Vector2 rigPosition = CanvasToRig(current.mousePosition, contentRect);
                if (pixelSnap && previewSprite != null)
                {
                    rigPosition.x = Mathf.Round(rigPosition.x * previewSprite.rect.width) / previewSprite.rect.width;
                    rigPosition.y = Mathf.Round(rigPosition.y * previewSprite.rect.height) / previewSprite.rect.height;
                }
                positionProperty.vector2Value = rigPosition;
                serializedAsset.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rigAsset);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseDrag
                && editMode == EditMode.Bone
                && boneDragMode != BoneDragMode.None
                && selectedBone >= 0)
            {
                if (boneDragMode == BoneDragMode.Endpoint)
                {
                    DragBoneEndpoint(current.mousePosition, contentRect);
                }
                else
                {
                    DragBonePosition(current.mousePosition, contentRect);
                }

                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                draggingVertex = false;
                boneDragMode = BoneDragMode.None;
            }
        }

        void DragBonePosition(Vector2 mousePosition, Rect contentRect)
        {
            if (!TryGetBindBoneMatrices(out Matrix4x4[] matrices))
            {
                return;
            }

            Vector2 desiredWorldPosition =
                CanvasToRig(mousePosition, contentRect) + boneDragPositionOffset;
            int parentIndex = rigAsset.Bones[selectedBone].ParentIndex;
            Vector2 localPosition = parentIndex >= 0
                ? matrices[parentIndex].inverse.MultiplyPoint3x4(desiredWorldPosition)
                : desiredWorldPosition;
            if (pixelSnap && previewSprite != null)
            {
                Vector2 snappedWorld = SnapRigPosition(desiredWorldPosition);
                localPosition = parentIndex >= 0
                    ? matrices[parentIndex].inverse.MultiplyPoint3x4(snappedWorld)
                    : snappedWorld;
            }

            SetSelectedBoneTransform(localPosition, null, null);
        }

        void DragBoneEndpoint(Vector2 mousePosition, Rect contentRect)
        {
            if (!TryGetBindBoneMatrices(out Matrix4x4[] matrices))
            {
                return;
            }

            Matrix4x4 boneMatrix = matrices[selectedBone];
            Vector2 start = new Vector2(boneMatrix.m03, boneMatrix.m13);
            Vector2 endpoint = CanvasToRig(mousePosition, contentRect);
            if (pixelSnap && previewSprite != null)
            {
                endpoint = SnapRigPosition(endpoint);
            }

            Vector2 direction = endpoint - start;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            int parentIndex = rigAsset.Bones[selectedBone].ParentIndex;
            float parentRotation = parentIndex >= 0
                ? Mathf.Atan2(matrices[parentIndex].m10, matrices[parentIndex].m00) * Mathf.Rad2Deg
                : 0f;
            float worldRotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float localRotation = Mathf.DeltaAngle(0f, worldRotation - parentRotation);
            SetSelectedBoneTransform(null, localRotation, direction.magnitude);
        }

        void SetSelectedBoneTransform(
            Vector2? localPosition,
            float? localRotation,
            float? length)
        {
            SerializedObject serializedAsset = new SerializedObject(rigAsset);
            SerializedProperty bone = serializedAsset
                .FindProperty("bones")
                .GetArrayElementAtIndex(selectedBone);
            if (localPosition.HasValue)
            {
                bone.FindPropertyRelative("localPosition").vector2Value = localPosition.Value;
            }
            if (localRotation.HasValue)
            {
                bone.FindPropertyRelative("localRotation").floatValue = localRotation.Value;
            }
            if (length.HasValue)
            {
                bone.FindPropertyRelative("length").floatValue = Mathf.Max(0.0001f, length.Value);
            }

            serializedAsset.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rigAsset);
        }

        Vector2 SnapRigPosition(Vector2 position)
        {
            position.x = Mathf.Round(position.x * previewSprite.rect.width) / previewSprite.rect.width;
            position.y = Mathf.Round(position.y * previewSprite.rect.height) / previewSprite.rect.height;
            return position;
        }

        int FindNearestVertex(Vector2 mousePosition, Rect contentRect)
        {
            int nearest = -1;
            float nearestDistance = SelectionRadius;
            for (int i = 0; i < rigAsset.Vertices.Count; i++)
            {
                float distance = Vector2.Distance(mousePosition, RigToCanvas(rigAsset.Vertices[i].Position, contentRect));
                if (distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        int FindNearestBone(Vector2 mousePosition, Rect contentRect)
        {
            if (!TryGetBindBoneMatrices(out Matrix4x4[] matrices))
            {
                return -1;
            }

            int nearest = -1;
            float nearestDistance = SelectionRadius;
            for (int i = 0; i < rigAsset.Bones.Count; i++)
            {
                Vector2 start = new Vector2(matrices[i].m03, matrices[i].m13);
                Vector2 end = matrices[i].MultiplyPoint3x4(new Vector3(rigAsset.Bones[i].Length, 0f, 0f));
                float distance = DistanceToSegment(
                    mousePosition,
                    RigToCanvas(start, contentRect),
                    RigToCanvas(end, contentRect));
                if (distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        void AddEdge(int vertexA, int vertexB)
        {
            if (!ValidVertex(vertexA) || !ValidVertex(vertexB) || vertexA == vertexB)
            {
                return;
            }

            Undo.RecordObject(rigAsset, "Add Rig Edge");
            SerializedObject serializedAsset = new SerializedObject(rigAsset);
            SerializedProperty edges = serializedAsset.FindProperty("edges");
            int index = edges.arraySize;
            edges.InsertArrayElementAtIndex(index);
            SerializedProperty edge = edges.GetArrayElementAtIndex(index);
            edge.FindPropertyRelative("vertexA").intValue = vertexA;
            edge.FindPropertyRelative("vertexB").intValue = vertexB;
            edge.FindPropertyRelative("boundary").boolValue = false;
            serializedAsset.ApplyModifiedProperties();
            EditorUtility.SetDirty(rigAsset);
        }

        void PaintWeights(Vector2 mousePosition, Rect contentRect)
        {
            if (selectedBone < 0 || selectedBone >= rigAsset.Bones.Count || rigAsset.Weights.Count != rigAsset.Vertices.Count)
            {
                return;
            }

            SerializedObject serializedAsset = new SerializedObject(rigAsset);
            SerializedProperty weights = serializedAsset.FindProperty("weights");
            bool changed = false;
            for (int vertexIndex = 0; vertexIndex < rigAsset.Vertices.Count; vertexIndex++)
            {
                float distance = Vector2.Distance(
                    mousePosition,
                    RigToCanvas(rigAsset.Vertices[vertexIndex].Position, contentRect));
                if (distance > weightBrushRadius)
                {
                    continue;
                }

                float falloff = 1f - distance / weightBrushRadius;
                SerializedProperty vertexWeight = weights.GetArrayElementAtIndex(vertexIndex);
                int selectedSlot = -1;
                int weakestSlot = 0;
                float weakestWeight = float.PositiveInfinity;
                float[] values = new float[SpriteRigAsset.MaximumInfluenceCount];
                int[] indices = new int[SpriteRigAsset.MaximumInfluenceCount];
                for (int influence = 0; influence < SpriteRigAsset.MaximumInfluenceCount; influence++)
                {
                    indices[influence] = vertexWeight.FindPropertyRelative($"boneIndex{influence}").intValue;
                    values[influence] = vertexWeight.FindPropertyRelative($"weight{influence}").floatValue;
                    if (indices[influence] == selectedBone)
                    {
                        selectedSlot = influence;
                    }
                    if (values[influence] < weakestWeight)
                    {
                        weakestWeight = values[influence];
                        weakestSlot = influence;
                    }
                }

                if (selectedSlot < 0)
                {
                    selectedSlot = weakestSlot;
                    indices[selectedSlot] = selectedBone;
                    values[selectedSlot] = 0f;
                }

                values[selectedSlot] = Mathf.Clamp01(values[selectedSlot] + weightBrushStrength * falloff);
                float sum = 0f;
                for (int influence = 0; influence < values.Length; influence++)
                {
                    sum += values[influence];
                }

                if (sum <= 0f)
                {
                    continue;
                }

                for (int influence = 0; influence < values.Length; influence++)
                {
                    vertexWeight.FindPropertyRelative($"boneIndex{influence}").intValue = indices[influence];
                    vertexWeight.FindPropertyRelative($"weight{influence}").floatValue = values[influence] / sum;
                }
                changed = true;
            }

            if (changed)
            {
                serializedAsset.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rigAsset);
            }
        }

        void RemoveEdge(int vertexA, int vertexB)
        {
            SerializedObject serializedAsset = new SerializedObject(rigAsset);
            SerializedProperty edges = serializedAsset.FindProperty("edges");
            for (int i = 0; i < edges.arraySize; i++)
            {
                SerializedProperty edge = edges.GetArrayElementAtIndex(i);
                int a = edge.FindPropertyRelative("vertexA").intValue;
                int b = edge.FindPropertyRelative("vertexB").intValue;
                if ((a == vertexA && b == vertexB) || (a == vertexB && b == vertexA))
                {
                    Undo.RecordObject(rigAsset, "Remove Rig Edge");
                    edges.DeleteArrayElementAtIndex(i);
                    serializedAsset.ApplyModifiedProperties();
                    EditorUtility.SetDirty(rigAsset);
                    return;
                }
            }
        }

        void AddTriangle(int vertexA, int vertexB, int vertexC)
        {
            if (!ValidVertex(vertexA) || !ValidVertex(vertexB) || !ValidVertex(vertexC)
                || vertexA == vertexB || vertexB == vertexC || vertexC == vertexA)
            {
                return;
            }

            Vector2 a = rigAsset.Vertices[vertexA].Position;
            Vector2 b = rigAsset.Vertices[vertexB].Position;
            Vector2 c = rigAsset.Vertices[vertexC].Position;
            if (Cross(b - a, c - a) < 0f)
            {
                int swap = vertexB;
                vertexB = vertexC;
                vertexC = swap;
            }

            Undo.RecordObject(rigAsset, "Add Rig Triangle");
            SerializedObject serializedAsset = new SerializedObject(rigAsset);
            SerializedProperty triangles = serializedAsset.FindProperty("triangles");
            int index = triangles.arraySize;
            triangles.arraySize += 3;
            triangles.GetArrayElementAtIndex(index).intValue = vertexA;
            triangles.GetArrayElementAtIndex(index + 1).intValue = vertexB;
            triangles.GetArrayElementAtIndex(index + 2).intValue = vertexC;
            serializedAsset.ApplyModifiedProperties();
            EditorUtility.SetDirty(rigAsset);
        }

        bool TryGetBindBoneMatrices(out Matrix4x4[] matrices)
        {
            matrices = null;
            if (rigAsset.Bones.Count == 0)
            {
                return false;
            }

            try
            {
                CpuSpriteSkinner skinner = new CpuSpriteSkinner(rigAsset);
                matrices = new Matrix4x4[rigAsset.Bones.Count];
                for (int i = 0; i < matrices.Length; i++)
                {
                    matrices[i] = skinner.GetPoseWorldMatrix(i);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        void HandleEditorUpdate()
        {
            if (!physicsPreviewPlaying || previewComponent == null || !previewComponent.IsBuilt)
            {
                previousPreviewTime = EditorApplication.timeSinceStartup;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Clamp((float)(now - previousPreviewTime), 0f, 1f / 30f);
            previousPreviewTime = now;
            StepPhysicsPreview(deltaTime);
            Repaint();
        }

        void StepPhysicsPreview(float deltaTime)
        {
            if (previewComponent == null || !previewComponent.IsBuilt)
            {
                return;
            }

            previewComponent.SimulatePhysics(deltaTime);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        void HandleUndoRedo()
        {
            Repaint();
            if (previewComponent != null)
            {
                previewComponent.Rebuild();
            }
        }

        Rect GetContentRect(Rect canvas)
        {
            float availableWidth = Mathf.Max(1f, canvas.width - 40f);
            float availableHeight = Mathf.Max(1f, canvas.height - 40f);
            float spriteAspect = previewSprite != null && previewSprite.rect.height > 0f
                ? previewSprite.rect.width / previewSprite.rect.height
                : 1f;
            float width = availableWidth;
            float height = width / spriteAspect;
            if (height > availableHeight)
            {
                height = availableHeight;
                width = height * spriteAspect;
            }

            width *= zoom;
            height *= zoom;
            return new Rect(
                canvas.center.x - width * 0.5f,
                canvas.center.y - height * 0.5f,
                width,
                height);
        }

        void DrawCheckerboard(Rect rect)
        {
            const float size = 16f;
            for (float y = rect.y; y < rect.yMax; y += size)
            {
                for (float x = rect.x; x < rect.xMax; x += size)
                {
                    int parity = Mathf.FloorToInt((x - rect.x) / size) + Mathf.FloorToInt((y - rect.y) / size);
                    Color color = parity % 2 == 0
                        ? new Color(0.22f, 0.22f, 0.22f)
                        : new Color(0.28f, 0.28f, 0.28f);
                    EditorGUI.DrawRect(new Rect(x, y, Mathf.Min(size, rect.xMax - x), Mathf.Min(size, rect.yMax - y)), color);
                }
            }
        }

        void DrawSprite(Rect rect)
        {
            if (previewSprite == null || previewSprite.texture == null)
            {
                return;
            }

            Rect textureRect;
            try
            {
                textureRect = previewSprite.textureRect;
            }
            catch (UnityException)
            {
                return;
            }

            Rect uv = new Rect(
                textureRect.x / previewSprite.texture.width,
                textureRect.y / previewSprite.texture.height,
                textureRect.width / previewSprite.texture.width,
                textureRect.height / previewSprite.texture.height);
            GUI.DrawTextureWithTexCoords(rect, previewSprite.texture, uv, true);
        }

        Color GetVertexColor(int vertexIndex)
        {
            if (editMode != EditMode.Weight || selectedBone < 0 || vertexIndex >= rigAsset.Weights.Count)
            {
                return Color.cyan;
            }

            VertexWeight4 weight = rigAsset.Weights[vertexIndex];
            float amount = 0f;
            for (int i = 0; i < SpriteRigAsset.MaximumInfluenceCount; i++)
            {
                if (weight.GetBoneIndex(i) == selectedBone)
                {
                    amount += weight.GetWeight(i);
                }
            }
            return Color.Lerp(Color.blue, Color.red, amount);
        }

        string[] GetBoneNames()
        {
            if (rigAsset.Bones.Count == 0)
            {
                return new[] { "None" };
            }

            string[] names = new string[rigAsset.Bones.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = $"{i}: {rigAsset.Bones[i].Name}";
            }
            return names;
        }

        bool ValidVertex(int index)
        {
            return index >= 0 && index < rigAsset.Vertices.Count;
        }

        static Vector2 RigToCanvas(Vector2 rigPosition, Rect contentRect)
        {
            return new Vector2(
                contentRect.x + rigPosition.x * contentRect.width,
                contentRect.yMax - rigPosition.y * contentRect.height);
        }

        static Vector2 CanvasToRig(Vector2 canvasPosition, Rect contentRect)
        {
            return new Vector2(
                (canvasPosition.x - contentRect.x) / contentRect.width,
                (contentRect.yMax - canvasPosition.y) / contentRect.height);
        }

        static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            if (segment.sqrMagnitude <= 0.0001f)
            {
                return Vector2.Distance(point, start);
            }
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude);
            return Vector2.Distance(point, start + segment * t);
        }

        static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        static GUIStyle CenteredLabelStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
            return style;
        }
    }
}
