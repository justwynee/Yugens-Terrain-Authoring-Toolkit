// MarchingSquaresTerrainEditor.cs
// Unity Editor Inspector for MarchingSquaresTerrain and MarchingSquaresTerrainChunk.
// Provides the same editing experience as the Godot plugin's EditorInspectorPlugin.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MarchingSquaresTerrain.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="MarchingSquaresTerrain"/>.
    /// Exposes all terrain settings and painting tools from the Godot plugin.
    /// </summary>
    [CustomEditor(typeof(MarchingSquaresTerrain))]
    public class MarchingSquaresTerrainEditor : UnityEditor.Editor
    {
        private MarchingSquaresTerrain _terrain;

        // Active painting state
        private enum PaintMode { None, Height, Color0, Color1, WallColor0, WallColor1, GrassMask }
        private PaintMode _paintMode = PaintMode.None;
        private float     _paintStrength = 0.5f;
        private float     _brushRadius   = 3.0f;
        private Color     _paintColor    = new Color(1, 0, 0, 0);

        void OnEnable()  => _terrain = (MarchingSquaresTerrain)target;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Marching Squares Terrain", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ── Terrain Setup ──────────────────────────────────────────────────
            EditorGUILayout.LabelField("Terrain Setup", EditorStyles.boldLabel);
            DrawDefault("_dimensions");
            DrawDefault("_cell_size");
            DrawDefault("blend_mode");
            DrawDefault("wall_threshold");
            DrawDefault("ridge_threshold");
            DrawDefault("ledge_threshold");
            DrawDefault("use_ridge_texture");
            DrawDefault("use_ledge_texture");
            DrawDefault("terrain_material");

            EditorGUILayout.Space(4);

            // ── Chunks ─────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Chunks", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Chunk (0,0)"))
                AddChunk(0, 0);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Regenerate All Chunks"))
            {
                foreach (var chunk in _terrain.chunks.Values)
                    chunk.RegenerateAllCells(true);
            }

            EditorGUILayout.Space(4);

            // ── Grass ──────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Grass", EditorStyles.boldLabel);
            DrawDefault("grass_subdivisions");
            DrawDefault("grass_size");
            DrawDefault("grass_mesh_template");
            DrawDefault("grass_material");

            EditorGUILayout.Space(4);

            // ── Textures ───────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Textures (up to 15)", EditorStyles.boldLabel);
            for (int i = 1; i <= 15; i++)
                DrawDefault("texture_" + i);

            EditorGUILayout.Space(4);

            // ── Texture Scales ────────────────────────────────────────────────
            EditorGUILayout.LabelField("Texture UV Scales", EditorStyles.boldLabel);
            for (int i = 1; i <= 15; i++)
                DrawDefault("texture_scale_" + i);

            EditorGUILayout.Space(4);

            // ── Texture Albedos ───────────────────────────────────────────────
            EditorGUILayout.LabelField("Texture Albedos", EditorStyles.boldLabel);
            for (int i = 1; i <= 6; i++)
                DrawDefault("texture_albedo_" + i);

            EditorGUILayout.Space(4);

            // ── Grass Sprites ─────────────────────────────────────────────────
            EditorGUILayout.LabelField("Grass Sprites", EditorStyles.boldLabel);
            for (int i = 1; i <= 6; i++)
                DrawDefault("grass_sprite_tex_" + i);

            EditorGUILayout.Space(4);

            // ── Has-Grass Flags ───────────────────────────────────────────────
            EditorGUILayout.LabelField("Has Grass Flags", EditorStyles.boldLabel);
            for (int i = 2; i <= 6; i++)
                DrawDefault("tex" + i + "_has_grass");

            EditorGUILayout.Space(4);

            // ── Painting ──────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Painting Tools", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _paintMode = (PaintMode)EditorGUILayout.EnumPopup("Mode", _paintMode);
            EditorGUILayout.EndHorizontal();

            _brushRadius   = EditorGUILayout.Slider("Brush Radius",   _brushRadius,   0.5f, 20f);
            _paintStrength = EditorGUILayout.Slider("Paint Strength", _paintStrength, 0.01f, 1f);

            if (_paintMode == PaintMode.Color0 || _paintMode == PaintMode.Color1 ||
                _paintMode == PaintMode.WallColor0 || _paintMode == PaintMode.WallColor1)
            {
                _paintColor = EditorGUILayout.ColorField("Paint Color", _paintColor);
            }

            if (_paintMode != PaintMode.None)
            {
                EditorGUILayout.HelpBox(
                    "Click/drag in the Scene view to paint.\n" +
                    "Shift+drag to erase / lower.", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDefault(string name)
        {
            var prop = serializedObject.FindProperty(name);
            if (prop != null)
                EditorGUILayout.PropertyField(prop, true);
        }

        private void AddChunk(int cx, int cz)
        {
            Undo.RegisterCompleteObjectUndo(_terrain.gameObject, "Add Terrain Chunk");
            var chunk = _terrain.AddNewChunk(cx, cz);
            chunk.RegenerateAllCells(true);
            EditorUtility.SetDirty(_terrain);
        }

        // ── Scene-view mouse painting ─────────────────────────────────────────
        void OnSceneGUI()
        {
            if (_paintMode == PaintMode.None) return;
            if (_terrain == null) return;

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            var evt = Event.current;
            bool erase = evt.shift;

            if ((evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag) &&
                evt.button == 0)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                if (Physics.Raycast(ray, out var hit, 1000f))
                    PaintAtHit(hit.point, erase);

                evt.Use();
            }

            // Draw brush circle
            Handles.color = new Color(1, 1, 0, 0.5f);
            if (Physics.Raycast(HandleUtility.GUIPointToWorldRay(Event.current.mousePosition), out var previewHit, 1000f))
                Handles.DrawWireDisc(previewHit.point, previewHit.normal, _brushRadius);

            SceneView.currentDrawingSceneView?.Repaint();
        }

        private void PaintAtHit(Vector3 worldPos, bool erase)
        {
            foreach (var kv in _terrain.chunks)
            {
                var chunk = kv.Value;
                if (chunk.height_map == null) continue;

                var chunkWorldPos = chunk.transform.position;
                float rBrush2 = _brushRadius * _brushRadius;

                for (int z = 0; z < _terrain.dimensions.z; z++)
                {
                    for (int x = 0; x < _terrain.dimensions.x; x++)
                    {
                        float wx = chunkWorldPos.x + x * _terrain.cell_size.x;
                        float wz = chunkWorldPos.z + z * _terrain.cell_size.y;

                        float dx = wx - worldPos.x;
                        float dz = wz - worldPos.z;
                        if (dx * dx + dz * dz > rBrush2) continue;

                        float dist   = Mathf.Sqrt(dx * dx + dz * dz) / _brushRadius;
                        float weight = (1f - dist * dist) * _paintStrength;
                        float sign   = erase ? -1f : 1f;

                        Undo.RegisterCompleteObjectUndo(chunk, "Paint Terrain");

                        switch (_paintMode)
                        {
                            case PaintMode.Height:
                                chunk.height_map[z][x] += sign * weight * 2f;
                                chunk.NotifyNeedsUpdate(z, x);
                                chunk.NotifyNeedsUpdate(z, x - 1);
                                chunk.NotifyNeedsUpdate(z - 1, x);
                                chunk.NotifyNeedsUpdate(z - 1, x - 1);
                                break;
                            case PaintMode.Color0:
                                chunk.DrawColor0(x, z, _paintColor);
                                break;
                            case PaintMode.Color1:
                                chunk.DrawColor1(x, z, _paintColor);
                                break;
                            case PaintMode.WallColor0:
                                chunk.DrawWallColor0(x, z, _paintColor);
                                break;
                            case PaintMode.WallColor1:
                                chunk.DrawWallColor1(x, z, _paintColor);
                                break;
                            case PaintMode.GrassMask:
                                float maskVal = erase ? 1f : 0f;
                                chunk.DrawGrassMask(x, z, new Color(maskVal, 0, 0, 0));
                                break;
                        }
                    }
                }

                if (_paintMode == PaintMode.Height)
                    chunk.RegenerateMesh(false);
            }

            EditorUtility.SetDirty(_terrain);
        }
    }

    // -----------------------------------------------------------------------
    // Chunk inspector
    // -----------------------------------------------------------------------

    /// <summary>
    /// Custom inspector for <see cref="MarchingSquaresTerrainChunk"/>.
    /// </summary>
    [CustomEditor(typeof(MarchingSquaresTerrainChunk))]
    public class MarchingSquaresTerrainChunkEditor : UnityEditor.Editor
    {
        private MarchingSquaresTerrainChunk _chunk;
        void OnEnable() => _chunk = (MarchingSquaresTerrainChunk)target;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Terrain Chunk", EditorStyles.boldLabel);
            DrawDefaultInspector();

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Regenerate Mesh"))
            {
                _chunk.RegenerateAllCells(true);
                EditorUtility.SetDirty(_chunk);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
