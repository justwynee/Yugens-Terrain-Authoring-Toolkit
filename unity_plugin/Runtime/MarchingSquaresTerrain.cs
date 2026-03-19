// MarchingSquaresTerrain.cs
// Unity C# port of marching_squares_terrain.gd (Godot 4)
// All mathematical / computational logic is preserved exactly – no differences.

using System.Collections.Generic;
using UnityEngine;

namespace MarchingSquaresTerrain
{
    /// <summary>
    /// Root terrain node. Owns one or more <see cref="MarchingSquaresTerrainChunk"/>
    /// children and holds all global settings.
    ///
    /// Direct port of marching_squares_terrain.gd – every constant, formula,
    /// and control-flow branch is identical to the Godot source.
    /// </summary>
    [ExecuteAlways]
    public class MarchingSquaresTerrain : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Storage mode enum (mirrors Godot enum)
        // -----------------------------------------------------------------------
        public enum StorageMode { BAKED, RUNTIME }

        // -----------------------------------------------------------------------
        // Serialised settings (mirrors @export vars in GDScript)
        // -----------------------------------------------------------------------

        [Header("Storage")]
        public StorageMode storage_mode = StorageMode.BAKED;
        public bool bake_grass      = true;
        public bool bake_collision  = true;

        [Header("Runtime Baking")]
        public bool enable_runtime_texture_baking = true;
        public int  polygon_texture_resolution    = 32;
        public Material bake_material_override;

        // -----------------------------------------------------------------------
        // Global terrain settings (mirrors @export_custom vars)
        // -----------------------------------------------------------------------

        [Header("Terrain")]
        [SerializeField] private Vector3Int _dimensions = new Vector3Int(33, 32, 33);
        /// <summary>
        /// Total number of height values in X and Z, and the Y scale.
        /// Mirrors dimensions in GDScript (33, 32, 33 default).
        /// </summary>
        public Vector3Int dimensions
        {
            get => _dimensions;
            set
            {
                _dimensions = value;
                if (terrain_material != null)
                    terrain_material.SetVector("_ChunkSize", new Vector4(value.x, value.y, value.z, 0f));
            }
        }

        [SerializeField] private Vector2 _cell_size = new Vector2(2f, 2f);
        /// <summary>Unit XZ size of a single cell.</summary>
        public Vector2 cell_size
        {
            get => _cell_size;
            set
            {
                _cell_size = value;
                if (terrain_material != null)
                    terrain_material.SetVector("_CellSize", new Vector4(value.x, value.y, 0f, 0f));
            }
        }

        public int   blend_mode          = 0;
        public int   extra_collision_layer = 9;
        public float wall_threshold      = 0.0f;
        public float ridge_threshold     = 1.0f;
        public float ledge_threshold     = 1.0f;
        public bool  use_ridge_texture   = true;
        public bool  use_ledge_texture   = true;

        // -----------------------------------------------------------------------
        // Grass settings
        // -----------------------------------------------------------------------
        [Header("Grass")]
        public int     grass_subdivisions = 3;
        public Vector2 grass_size         = new Vector2(1f, 1f);
        public Mesh    grass_mesh_template;
        public Material grass_material;

        // -----------------------------------------------------------------------
        // Terrain material (assigned by user in Unity Inspector)
        // -----------------------------------------------------------------------
        [Header("Material")]
        public Material terrain_material;

        // -----------------------------------------------------------------------
        // Terrain textures (16 slots, mirrors GDScript)
        // -----------------------------------------------------------------------
        [Header("Textures")]
        public Texture2D texture_1;
        public Texture2D texture_2;
        public Texture2D texture_3;
        public Texture2D texture_4;
        public Texture2D texture_5;
        public Texture2D texture_6;
        public Texture2D texture_7;
        public Texture2D texture_8;
        public Texture2D texture_9;
        public Texture2D texture_10;
        public Texture2D texture_11;
        public Texture2D texture_12;
        public Texture2D texture_13;
        public Texture2D texture_14;
        public Texture2D texture_15;

        // -----------------------------------------------------------------------
        // Grass sprite textures
        // -----------------------------------------------------------------------
        [Header("Grass Sprites")]
        public Texture2D grass_sprite_tex_1;
        public Texture2D grass_sprite_tex_2;
        public Texture2D grass_sprite_tex_3;
        public Texture2D grass_sprite_tex_4;
        public Texture2D grass_sprite_tex_5;
        public Texture2D grass_sprite_tex_6;

        // -----------------------------------------------------------------------
        // Has-grass flags
        // -----------------------------------------------------------------------
        [Header("Grass Flags")]
        public bool tex2_has_grass = true;
        public bool tex3_has_grass = true;
        public bool tex4_has_grass = true;
        public bool tex5_has_grass = true;
        public bool tex6_has_grass = true;

        // -----------------------------------------------------------------------
        // Texture albedos
        // -----------------------------------------------------------------------
        [Header("Texture Albedos")]
        public Color texture_albedo_1 = new Color(0.392f, 0.471f, 0.318f);
        public Color texture_albedo_2 = new Color(0.322f, 0.482f, 0.384f);
        public Color texture_albedo_3 = new Color(0.373f, 0.424f, 0.294f);
        public Color texture_albedo_4 = new Color(0.392f, 0.475f, 0.255f);
        public Color texture_albedo_5 = new Color(0.290f, 0.494f, 0.365f);
        public Color texture_albedo_6 = new Color(0.443f, 0.447f, 0.365f);

        // -----------------------------------------------------------------------
        // Texture UV scales
        // -----------------------------------------------------------------------
        [Header("Texture Scales")]
        public float texture_scale_1  = 1f;
        public float texture_scale_2  = 1f;
        public float texture_scale_3  = 1f;
        public float texture_scale_4  = 1f;
        public float texture_scale_5  = 1f;
        public float texture_scale_6  = 1f;
        public float texture_scale_7  = 1f;
        public float texture_scale_8  = 1f;
        public float texture_scale_9  = 1f;
        public float texture_scale_10 = 1f;
        public float texture_scale_11 = 1f;
        public float texture_scale_12 = 1f;
        public float texture_scale_13 = 1f;
        public float texture_scale_14 = 1f;
        public float texture_scale_15 = 1f;

        // -----------------------------------------------------------------------
        // Chunk registry
        // -----------------------------------------------------------------------
        [HideInInspector]
        public Dictionary<Vector2Int, MarchingSquaresTerrainChunk> chunks =
            new Dictionary<Vector2Int, MarchingSquaresTerrainChunk>();

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------
        void Awake()
        {
            RegisterChunks();
        }

        void Start()
        {
            InitializeAllChunks(true);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (terrain_material != null)
                ApplyMaterialParameters();
        }
#endif

        // -----------------------------------------------------------------------
        // Initialization – mirrors _deferred_enter_tree() in GDScript
        // -----------------------------------------------------------------------

        /// <summary>
        /// Scans child objects for <see cref="MarchingSquaresTerrainChunk"/> components
        /// and registers them in the chunk dictionary.
        /// </summary>
        public void RegisterChunks()
        {
            chunks.Clear();
            foreach (var chunk in GetComponentsInChildren<MarchingSquaresTerrainChunk>())
            {
                chunk.terrain_system = this;
                chunks[chunk.chunk_coords] = chunk;
            }
        }

        /// <summary>
        /// Initialises all chunks (generates or loads mesh, grass, collision).
        /// Mirrors the per-chunk loop in _deferred_enter_tree().
        /// </summary>
        public void InitializeAllChunks(bool regenerate = true)
        {
            ApplyMaterialParameters();
            foreach (var chunk in chunks.Values)
                chunk.InitializeTerrain(regenerate);
        }

        // -----------------------------------------------------------------------
        // Chunk creation – mirrors add_new_chunk() in GDScript exactly
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates a new chunk at <c>(chunkX, chunkZ)</c> and stitches its edges
        /// to existing adjacent chunks.  Mirrors add_new_chunk() exactly.
        /// </summary>
        public MarchingSquaresTerrainChunk AddNewChunk(int chunkX, int chunkZ)
        {
            var chunk_coords = new Vector2Int(chunkX, chunkZ);

            var go = new GameObject("Chunk " + chunk_coords);
            go.transform.SetParent(transform, false);
            var chunk = go.AddComponent<MarchingSquaresTerrainChunk>();
            chunk.terrain_system = this;
            chunk.chunk_coords   = chunk_coords;

            // Position chunk in world space
            go.transform.localPosition = new Vector3(
                chunkX * (dimensions.x - 1) * cell_size.x,
                0f,
                chunkZ * (dimensions.z - 1) * cell_size.y
            );

            chunk.InitializeTerrain(false);
            chunks[chunk_coords] = chunk;

            // --- Stitch edges (identical logic to GDScript) ---
            if (chunks.TryGetValue(new Vector2Int(chunkX - 1, chunkZ), out var chunk_left))
            {
                for (int z = 0; z < dimensions.z; z++)
                    chunk.height_map[z][0] = chunk_left.height_map[z][dimensions.x - 1];
            }

            if (chunks.TryGetValue(new Vector2Int(chunkX + 1, chunkZ), out var chunk_right))
            {
                for (int z = 0; z < dimensions.z; z++)
                    chunk_right.height_map[z][dimensions.x - 1] = chunk_right.height_map[z][0];
            }

            if (chunks.TryGetValue(new Vector2Int(chunkX, chunkZ - 1), out var chunk_up))
            {
                for (int x = 0; x < dimensions.x; x++)
                    chunk.height_map[0][x] = chunk_up.height_map[dimensions.z - 1][x];
            }

            if (chunks.TryGetValue(new Vector2Int(chunkX, chunkZ + 1), out var chunk_down))
            {
                for (int x = 0; x < dimensions.x; x++)
                    chunk_down.height_map[dimensions.z - 1][x] = chunk_down.height_map[0][x];
            }

            return chunk;
        }

        /// <summary>Returns true if a chunk exists at <c>(x, z)</c>.</summary>
        public bool HasChunk(int x, int z) => chunks.ContainsKey(new Vector2Int(x, z));

        // -----------------------------------------------------------------------
        // Terrain painting API  (mirrors draw_*() helpers in GDScript)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Sets the height of a single vertex at terrain-local <c>(x, z)</c>
        /// and triggers affected cell updates in the correct chunk.
        /// </summary>
        public void DrawHeight(Vector2Int chunkCoords, int x, int z, float y)
        {
            if (chunks.TryGetValue(chunkCoords, out var chunk))
                chunk.DrawHeight(x, z, y);
        }

        public void DrawColor0(Vector2Int chunkCoords, int x, int z, Color color)
        {
            if (chunks.TryGetValue(chunkCoords, out var chunk))
                chunk.DrawColor0(x, z, color);
        }

        public void DrawColor1(Vector2Int chunkCoords, int x, int z, Color color)
        {
            if (chunks.TryGetValue(chunkCoords, out var chunk))
                chunk.DrawColor1(x, z, color);
        }

        public void DrawWallColor0(Vector2Int chunkCoords, int x, int z, Color color)
        {
            if (chunks.TryGetValue(chunkCoords, out var chunk))
                chunk.DrawWallColor0(x, z, color);
        }

        public void DrawWallColor1(Vector2Int chunkCoords, int x, int z, Color color)
        {
            if (chunks.TryGetValue(chunkCoords, out var chunk))
                chunk.DrawWallColor1(x, z, color);
        }

        public void DrawGrassMask(Vector2Int chunkCoords, int x, int z, Color masked)
        {
            if (chunks.TryGetValue(chunkCoords, out var chunk))
                chunk.DrawGrassMask(x, z, masked);
        }

        // -----------------------------------------------------------------------
        // Material parameter sync – mirrors shader_parameter setters in GDScript
        // -----------------------------------------------------------------------
        private void ApplyMaterialParameters()
        {
            if (terrain_material == null) return;

            terrain_material.SetVector("_ChunkSize", new Vector4(_dimensions.x, _dimensions.y, _dimensions.z, 0));
            terrain_material.SetVector("_CellSize",  new Vector4(_cell_size.x,  _cell_size.y,  0, 0));
            terrain_material.SetFloat("_WallThreshold",  wall_threshold);
            terrain_material.SetFloat("_RidgeThreshold", ridge_threshold);
            terrain_material.SetFloat("_LedgeThreshold", ledge_threshold);
            terrain_material.SetInt("_UseRidgeTexture", use_ridge_texture ? 1 : 0);
            terrain_material.SetInt("_UseLedgeTexture", use_ledge_texture ? 1 : 0);
            terrain_material.SetInt("_BlendMode",       blend_mode);

            SetTerrainTexture(terrain_material, "_VcTexRR", texture_1);
            SetTerrainTexture(terrain_material, "_VcTexRG", texture_2);
            SetTerrainTexture(terrain_material, "_VcTexRB", texture_3);
            SetTerrainTexture(terrain_material, "_VcTexRA", texture_4);
            SetTerrainTexture(terrain_material, "_VcTexGR", texture_5);
            SetTerrainTexture(terrain_material, "_VcTexGG", texture_6);
            SetTerrainTexture(terrain_material, "_VcTexGB", texture_7);
            SetTerrainTexture(terrain_material, "_VcTexGA", texture_8);
            SetTerrainTexture(terrain_material, "_VcTexBR", texture_9);
            SetTerrainTexture(terrain_material, "_VcTexBG", texture_10);
            SetTerrainTexture(terrain_material, "_VcTexBB", texture_11);
            SetTerrainTexture(terrain_material, "_VcTexBA", texture_12);
            SetTerrainTexture(terrain_material, "_VcTexAR", texture_13);
            SetTerrainTexture(terrain_material, "_VcTexAG", texture_14);
            SetTerrainTexture(terrain_material, "_VcTexAB", texture_15);

            terrain_material.SetColor("_TexAlbedo1", texture_albedo_1);
            terrain_material.SetColor("_TexAlbedo2", texture_albedo_2);
            terrain_material.SetColor("_TexAlbedo3", texture_albedo_3);
            terrain_material.SetColor("_TexAlbedo4", texture_albedo_4);
            terrain_material.SetColor("_TexAlbedo5", texture_albedo_5);
            terrain_material.SetColor("_TexAlbedo6", texture_albedo_6);

            terrain_material.SetFloat("_TexScale1",  texture_scale_1);
            terrain_material.SetFloat("_TexScale2",  texture_scale_2);
            terrain_material.SetFloat("_TexScale3",  texture_scale_3);
            terrain_material.SetFloat("_TexScale4",  texture_scale_4);
            terrain_material.SetFloat("_TexScale5",  texture_scale_5);
            terrain_material.SetFloat("_TexScale6",  texture_scale_6);
            terrain_material.SetFloat("_TexScale7",  texture_scale_7);
            terrain_material.SetFloat("_TexScale8",  texture_scale_8);
            terrain_material.SetFloat("_TexScale9",  texture_scale_9);
            terrain_material.SetFloat("_TexScale10", texture_scale_10);
            terrain_material.SetFloat("_TexScale11", texture_scale_11);
            terrain_material.SetFloat("_TexScale12", texture_scale_12);
            terrain_material.SetFloat("_TexScale13", texture_scale_13);
            terrain_material.SetFloat("_TexScale14", texture_scale_14);
            terrain_material.SetFloat("_TexScale15", texture_scale_15);
        }

        private static void SetTerrainTexture(Material mat, string property, Texture2D tex)
        {
            if (tex != null) mat.SetTexture(property, tex);
        }
    }
}
