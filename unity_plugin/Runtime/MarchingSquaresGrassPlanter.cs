// MarchingSquaresGrassPlanter.cs
// Unity C# port of marching_squares_grass_planter.gd (Godot 4)
// All mathematical / computational logic is preserved exactly – no differences.

using System.Collections.Generic;
using UnityEngine;

namespace MarchingSquaresTerrain
{
    /// <summary>
    /// Places grass instances on the terrain surface using barycentric-coordinate
    /// point-in-triangle testing.
    ///
    /// Direct port of marching_squares_grass_planter.gd – every constant,
    /// formula, and branch is mathematically identical to the Godot source.
    /// </summary>
    [ExecuteAlways]
    public class MarchingSquaresGrassPlanter : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Constants (identical to GDScript source)
        // -----------------------------------------------------------------------

        /// <summary>Alpha encoding per texture ID (1–6).</summary>
        public static readonly float[] GRASS_ALPHA_VALUES = { 0.0f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };

        // -----------------------------------------------------------------------
        // Runtime references
        // -----------------------------------------------------------------------
        public MarchingSquaresTerrainChunk  chunk          { get; private set; }
        public MarchingSquaresTerrain       terrain_system { get; private set; }

        // -----------------------------------------------------------------------
        // MultiMesh output
        // -----------------------------------------------------------------------
        /// <summary>
        /// Unity equivalent of Godot's MultiMesh.
        /// Populated by <see cref="RegenerateAllCells"/>.
        /// </summary>
        public MarchingSquaresMultiMesh multimesh { get; private set; }

        // -----------------------------------------------------------------------
        // Setup – mirrors setup() in GDScript exactly
        // -----------------------------------------------------------------------
        /// <summary>
        /// Initialises this planter for the given chunk.
        /// Mirrors setup() in GDScript.
        /// </summary>
        public void Setup(MarchingSquaresTerrainChunk chunk_, bool redo = true)
        {
            chunk          = chunk_;
            terrain_system = chunk_.terrain_system;

            if (chunk == null || terrain_system == null)
            {
                Debug.LogError("SETUP FAILED - no chunk or terrain system found for GrassPlanter");
                return;
            }

            if (redo || multimesh == null)
                multimesh = new MarchingSquaresMultiMesh();

            int cellsX = chunk.dimensions.x - 1;
            int cellsZ = chunk.dimensions.z - 1;
            int subdivisions = terrain_system.grass_subdivisions;

            multimesh.instance_count = cellsX * cellsZ * subdivisions * subdivisions;
            // GDScript: multimesh.mesh.size = terrain_system.grass_size * scalar (Vector2 * float)
            float scalar = (terrain_system.cell_size.x + terrain_system.cell_size.y) / 4.0f;
            multimesh.mesh_size = terrain_system.grass_size * scalar;
        }

        // -----------------------------------------------------------------------
        // RegenerateAllCells – mirrors regenerate_all_cells() exactly
        // -----------------------------------------------------------------------
        public void RegenerateAllCells()
        {
            if (chunk == null)         { Debug.LogError("_chunk not set while regenerating cells");         return; }
            if (terrain_system == null){ Debug.LogError("terrain_system not set while regenerating cells"); return; }
            if (multimesh == null)     { Setup(chunk); }
            if (chunk.cell_geometry == null || chunk.cell_geometry.Count == 0)
            {
                chunk.RegenerateMesh();
                return;
            }

            for (int z = 0; z < terrain_system.dimensions.z - 1; z++)
                for (int x = 0; x < terrain_system.dimensions.x - 1; x++)
                    GenerateGrassOnCell(new Vector2Int(x, z));

            // Stops floating grass bug on startup (mirrors multimesh.mesh.center_offset.y = size.y/2)
            multimesh.mesh_center_offset_y = multimesh.mesh_size.y / 2f;

            ApplyToGameObject();
        }

        // -----------------------------------------------------------------------
        // GenerateGrassOnCell – mirrors generate_grass_on_cell() exactly
        // -----------------------------------------------------------------------
        public void GenerateGrassOnCell(Vector2Int cell_coords)
        {
            if (chunk == null)         { Debug.LogError("Couldn't find a reference to _chunk");         return; }
            if (terrain_system == null){ Debug.LogError("Couldn't find a reference to terrain_system"); return; }
            if (chunk.cell_geometry == null)                                { Debug.LogError("Couldn't find a reference to cell_geometry"); return; }
            if (!chunk.cell_geometry.TryGetValue(cell_coords, out var cg)) { Debug.LogError("Couldn't find a reference to cell_coords"); return; }

            var verts           = cg.verts;
            var uvs             = cg.uvs;
            var color_0s        = cg.color_0s;
            var color_1s        = cg.color_1s;
            var custom_1_values = cg.custom_1_values;
            var is_floor        = cg.is_floor;

            int subdivisions = terrain_system.grass_subdivisions;
            int count        = subdivisions * subdivisions;

            // Generate random sample points within the cell
            var points = new List<Vector2>(count);
            for (int sz = 0; sz < subdivisions; sz++)
            {
                for (int sx = 0; sx < subdivisions; sx++)
                {
                    points.Add(new Vector2(
                        (cell_coords.x + (sx + Random.Range(0f, 1f)) / (float)subdivisions) * terrain_system.cell_size.x,
                        (cell_coords.y + (sz + Random.Range(0f, 1f)) / (float)subdivisions) * terrain_system.cell_size.y
                    ));
                }
            }

            int index     = (cell_coords.y * (chunk.dimensions.x - 1) + cell_coords.x) * count;
            int end_index = index + count;

            // Iterate over every triangle in the cell
            for (int i = 0; i + 2 < verts.Count; i += 3)
            {
                // Only place grass on floor triangles
                if (!is_floor[i]) continue;

                var a = verts[i];
                var b = verts[i + 1];
                var c = verts[i + 2];

                // Precompute barycentric denominator (identical to GDScript)
                var v0 = new Vector2(c.x - a.x, c.z - a.z);
                var v1 = new Vector2(b.x - a.x, b.z - a.z);

                float dot00 = Vector2.Dot(v0, v0);
                float dot01 = Vector2.Dot(v0, v1);
                float dot11 = Vector2.Dot(v1, v1);
                float invDenom = 1.0f / (dot00 * dot11 - dot01 * dot01);

                int point_index = 0;
                while (point_index < points.Count)
                {
                    var v2 = new Vector2(points[point_index].x - a.x, points[point_index].y - a.z);

                    float dot02 = Vector2.Dot(v0, v2);
                    float dot12 = Vector2.Dot(v1, v2);

                    float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
                    if (u < 0f) { point_index++; continue; }

                    float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
                    if (v < 0f) { point_index++; continue; }

                    if (u + v <= 1f)
                    {
                        // Point is inside the triangle
                        points.RemoveAt(point_index);

                        Vector3 p = a * (1f - u - v) + b * u + c * v;

                        // Ledge / ridge check (mirrors GDScript)
                        var uv     = uvs[i] * u + uvs[i + 1] * v + uvs[i + 2] * (1f - u - v);
                        bool on_ledge_or_ridge = uv.y > 0f || uv.x > 0.5f;

                        // Dominant colour interpolation (mirrors GDScript)
                        Color c0_interp = color_0s[i] * u + color_0s[i + 1] * v + color_0s[i + 2] * (1f - u - v);
                        Color c1_interp = color_1s[i] * u + color_1s[i + 1] * v + color_1s[i + 2] * (1f - u - v);
                        Color col_0 = MarchingSquaresTerrainVertexColorHelper.GetDominantColor(c0_interp);
                        Color col_1 = MarchingSquaresTerrainVertexColorHelper.GetDominantColor(c1_interp);

                        // Grass mask check (mirrors GDScript)
                        Color mask        = custom_1_values[i] * u + custom_1_values[i + 1] * v + custom_1_values[i + 2] * (1f - u - v);
                        bool is_masked    = mask.r < 0.9999f;
                        bool force_grass  = mask.g >= 0.9999f;

                        int  texture_id    = GetTextureId(col_0, col_1);
                        bool on_grass_tex  = HasGrassForTexture(texture_id, force_grass);

                        if (on_grass_tex && !on_ledge_or_ridge && !is_masked)
                            CreateGrassInstance(index, p, a, b, c, texture_id);
                        else
                            HideGrassInstance(index);

                        index++;
                    }
                    else
                    {
                        point_index++;
                    }
                }
            }

            // Fill remaining indices with hidden instances
            while (index < end_index)
            {
                if (index >= multimesh.instance_count) return;
                HideGrassInstance(index);
                index++;
            }
        }

        // -----------------------------------------------------------------------
        // Grass property getters (mirrors GDScript _get_* helpers)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns texture ID (1–16) from two dominant vertex colours.
        /// Mirrors _get_texture_id() in GDScript exactly.
        /// </summary>
        private int GetTextureId(Color vc_col_0, Color vc_col_1)
        {
            int id = 1;
            if (vc_col_0.r > 0.9999f)
            {
                if      (vc_col_1.r > 0.9999f) id = 1;
                else if (vc_col_1.g > 0.9999f) id = 2;
                else if (vc_col_1.b > 0.9999f) id = 3;
                else if (vc_col_1.a > 0.9999f) id = 4;
            }
            else if (vc_col_0.g > 0.9999f)
            {
                if      (vc_col_1.r > 0.9999f) id = 5;
                else if (vc_col_1.g > 0.9999f) id = 6;
                else if (vc_col_1.b > 0.9999f) id = 7;
                else if (vc_col_1.a > 0.9999f) id = 8;
            }
            else if (vc_col_0.b > 0.9999f)
            {
                if      (vc_col_1.r > 0.9999f) id = 9;
                else if (vc_col_1.g > 0.9999f) id = 10;
                else if (vc_col_1.b > 0.9999f) id = 11;
                else if (vc_col_1.a > 0.9999f) id = 12;
            }
            else if (vc_col_0.a > 0.9999f)
            {
                if      (vc_col_1.r > 0.9999f) id = 13;
                else if (vc_col_1.g > 0.9999f) id = 14;
                else if (vc_col_1.b > 0.9999f) id = 15;
                else if (vc_col_1.a > 0.9999f) id = 16;
            }
            return id;
        }

        /// <summary>
        /// Checks whether the given texture ID should have grass.
        /// Mirrors _has_grass_for_texture() in GDScript exactly.
        /// </summary>
        private bool HasGrassForTexture(int texture_id, bool force_grass_on)
        {
            if (force_grass_on)  return true;
            if (texture_id == 1) return true;  // Base grass always has grass
            if (texture_id < 2 || texture_id > 6) return false;

            bool[] has_grass_flags =
            {
                terrain_system.tex2_has_grass,
                terrain_system.tex3_has_grass,
                terrain_system.tex4_has_grass,
                terrain_system.tex5_has_grass,
                terrain_system.tex6_has_grass,
            };
            return has_grass_flags[texture_id - 2];
        }

        /// <summary>
        /// Returns the texture UV scale for the given texture ID.
        /// Mirrors _get_texture_scale() in GDScript exactly.
        /// </summary>
        private float GetTextureScale(int texture_id)
        {
            float[] scales =
            {
                terrain_system.texture_scale_1,
                terrain_system.texture_scale_2,
                terrain_system.texture_scale_3,
                terrain_system.texture_scale_4,
                terrain_system.texture_scale_5,
                terrain_system.texture_scale_6,
            };
            int idx = Mathf.Clamp(texture_id - 1, 0, 5);
            return scales[idx];
        }

        /// <summary>
        /// Returns the grass sprite alpha for the given texture ID.
        /// Mirrors _get_grass_alpha() in GDScript exactly.
        /// </summary>
        private float GetGrassAlpha(int texture_id)
        {
            int idx = Mathf.Clamp(texture_id - 1, 0, 5);
            return GRASS_ALPHA_VALUES[idx];
        }

        /// <summary>
        /// Samples the terrain texture colour at the given world position.
        /// Mirrors _sample_terrain_texture_color() in GDScript exactly.
        /// </summary>
        private Color SampleTerrainTextureColor(Vector3 world_pos, int texture_id, float tex_scale)
        {
            Texture2D tex = GetTerrainTexture(texture_id);
            if (tex == null) return Color.white;

            float uv_x = Mathf.Clamp01(world_pos.x / ((terrain_system.dimensions.x - 1) * terrain_system.cell_size.x));
            float uv_y = Mathf.Clamp01(world_pos.z / ((terrain_system.dimensions.z - 1) * terrain_system.cell_size.y));

            uv_x = Mathf.Abs(uv_x * tex_scale % 1.0f);
            uv_y = Mathf.Abs(uv_y * tex_scale % 1.0f);

            int px = (int)(uv_x * (tex.width  - 1));
            int py = (int)(uv_y * (tex.height - 1));

            Color color = tex.GetPixel(px, py);
            // sRGB → linear (mirrors _format_needs_conversion in GDScript)
            return color.linear;
        }

        /// <summary>Returns the Unity Texture2D assigned to the given texture slot.</summary>
        private Texture2D GetTerrainTexture(int texture_id)
        {
            return texture_id switch
            {
                2  => terrain_system.texture_2,
                3  => terrain_system.texture_3,
                4  => terrain_system.texture_4,
                5  => terrain_system.texture_5,
                6  => terrain_system.texture_6,
                _  => terrain_system.texture_1,
            };
        }

        // -----------------------------------------------------------------------
        // Grass placement helpers (mirrors GDScript _create_grass_instance / _hide)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates a grass instance at world_pos with correct orientation and colour.
        /// Mirrors _create_grass_instance() in GDScript exactly.
        /// </summary>
        private void CreateGrassInstance(int index, Vector3 world_pos,
            Vector3 a, Vector3 b, Vector3 c, int texture_id)
        {
            Vector3 edge1  = b - a;
            Vector3 edge2  = c - a;
            Vector3 normal = Vector3.Cross(edge1, edge2).normalized;

            Vector3 right   = Vector3.Cross(Vector3.forward, normal).normalized;
            Vector3 forward = Vector3.Cross(normal, Vector3.right).normalized;

            var instance_basis = new GrassInstanceBasis(right, forward, -normal);
            multimesh.SetInstanceTransform(index, world_pos, instance_basis);

            float tex_scale    = GetTextureScale(texture_id);
            Color instanceColor = SampleTerrainTextureColor(world_pos, texture_id, tex_scale);
            instanceColor.a    = GetGrassAlpha(texture_id);

            multimesh.SetInstanceCustomData(index, instanceColor);
        }

        /// <summary>
        /// Hides a grass instance by zero-scaling it.
        /// Mirrors _hide_grass_instance() in GDScript.
        /// </summary>
        private void HideGrassInstance(int index)
        {
            multimesh.SetInstanceTransform(index, Vector3.zero, GrassInstanceBasis.Zero);
        }

        // -----------------------------------------------------------------------
        // Round mode setter (called by chunk.merge_mode setter)
        // -----------------------------------------------------------------------
        /// <summary>Propagates the is_merge_round flag to the grass material.</summary>
        public void SetRoundMode(bool isRound)
        {
            if (multimesh?.material is Material mat)
                mat.SetInt("_IsMergeRound", isRound ? 1 : 0);
        }

        // -----------------------------------------------------------------------
        // Apply accumulated multimesh data to Unity GameObjects
        // -----------------------------------------------------------------------
        private void ApplyToGameObject()
        {
            if (multimesh == null) return;

            var mr = GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
            var mf = GetComponent<MeshFilter>()   ?? gameObject.AddComponent<MeshFilter>();

            if (terrain_system != null && terrain_system.grass_mesh_template != null)
            {
                mf.sharedMesh = terrain_system.grass_mesh_template;
                mr.sharedMaterial = terrain_system.grass_material;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper structs
    // -----------------------------------------------------------------------

    /// <summary>
    /// Lightweight equivalent of Godot Basis for grass instance orientation.
    /// </summary>
    public struct GrassInstanceBasis
    {
        public Vector3 right, up, forward;

        public static readonly GrassInstanceBasis Zero = new GrassInstanceBasis(Vector3.zero, Vector3.zero, Vector3.zero);

        public GrassInstanceBasis(Vector3 right_, Vector3 up_, Vector3 forward_)
        {
            right   = right_;
            up      = up_;
            forward = forward_;
        }
    }

    /// <summary>
    /// Stores per-instance transform + custom data.
    /// Equivalent to Godot MultiMesh.
    /// </summary>
    public class MarchingSquaresMultiMesh
    {
        public int       instance_count;
        public Vector2   mesh_size;
        public float     mesh_center_offset_y;
        public Material  material;

        private Vector3[]          _positions;
        private GrassInstanceBasis[] _bases;
        private Color[]            _customData;

        public void SetInstanceTransform(int index, Vector3 position, GrassInstanceBasis basis)
        {
            EnsureArrays();
            if (index >= instance_count) return;
            _positions[index] = position;
            _bases[index]     = basis;
        }

        public void SetInstanceCustomData(int index, Color data)
        {
            EnsureArrays();
            if (index >= instance_count) return;
            _customData[index] = data;
        }

        public (Vector3 position, GrassInstanceBasis basis) GetInstanceTransform(int index)
        {
            EnsureArrays();
            return (_positions[index], _bases[index]);
        }

        public Color GetInstanceCustomData(int index)
        {
            EnsureArrays();
            return _customData[index];
        }

        private void EnsureArrays()
        {
            if (_positions == null || _positions.Length != instance_count)
            {
                _positions  = new Vector3[instance_count];
                _bases      = new GrassInstanceBasis[instance_count];
                _customData = new Color[instance_count];
            }
        }
    }
}
