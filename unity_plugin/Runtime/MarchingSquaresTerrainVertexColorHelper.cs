// MarchingSquaresTerrainVertexColorHelper.cs
// Unity C# port of marching_squares_terrain_vertex_color_helper.gd (Godot 4)
// All mathematical / computational logic is preserved exactly – no differences.

using System.Collections.Generic;
using UnityEngine;

namespace MarchingSquaresTerrain
{
    /// <summary>
    /// Holds the four Color channels calculated by <see cref="BlendColors"/>.
    /// Corresponds to the dictionary returned by blend_colors() in GDScript.
    /// </summary>
    public struct VertexColors
    {
        public Color color_0;
        public Color color_1;
        public Color custom_1_value;
        public Color mat_blend;
    }

    /// <summary>
    /// Calculates per-vertex color data for terrain geometry.
    ///
    /// Direct port of marching_squares_terrain_vertex_color_helper.gd – every
    /// constant, formula, branch, and helper is mathematically identical to the
    /// Godot source.
    /// </summary>
    public class MarchingSquaresTerrainVertexColorHelper
    {
        // -----------------------------------------------------------------------
        // Constants (identical to GDScript source)
        // -----------------------------------------------------------------------

        /// <summary>
        /// &lt; 1.0 = more aggressive wall detection.
        /// &gt; 1.0 = less aggressive / more slope blend.
        /// </summary>
        public const float BLEND_EDGE_SENSITIVITY = 1.25f;

        // -----------------------------------------------------------------------
        // Per-cell cached boundary data
        // -----------------------------------------------------------------------
        public float cell_min_height;
        public float cell_max_height;

        // Floor boundary colors
        public Color cell_floor_lower_color_0;
        public Color cell_floor_upper_color_0;
        public Color cell_floor_lower_color_1;
        public Color cell_floor_upper_color_1;

        // Wall boundary colors
        public Color cell_wall_lower_color_0;
        public Color cell_wall_upper_color_0;
        public Color cell_wall_lower_color_1;
        public Color cell_wall_upper_color_1;

        public bool cell_is_boundary;

        // Per-cell dominant material indices
        public int cell_mat_a;
        public int cell_mat_b;
        public int cell_mat_c;

        // -----------------------------------------------------------------------
        // References
        // -----------------------------------------------------------------------
        public MarchingSquaresTerrainChunk chunk;
        public MarchingSquaresTerrainCell  cell;

        // -----------------------------------------------------------------------
        // blend_colors() – identical to GDScript
        // -----------------------------------------------------------------------
        /// <summary>
        /// Computes the four vertex colour channels for a single vertex.
        /// Mirrors blend_colors() in GDScript exactly.
        /// </summary>
        public VertexColors BlendColors(Vector3 vertex, Vector2 uv, bool diagMidpoint = false)
        {
            float blend_threshold = cell.merge_threshold * BLEND_EDGE_SENSITIVITY;
            bool blend_ab = Mathf.Abs(cell.ay - cell.by) < blend_threshold;
            bool blend_ac = Mathf.Abs(cell.ay - cell.cy) < blend_threshold;
            bool blend_bd = Mathf.Abs(cell.by - cell.dy) < blend_threshold;
            bool blend_cd = Mathf.Abs(cell.cy - cell.dy) < blend_threshold;
            bool cell_has_walls_for_blend = !(blend_ab && blend_ac && blend_bd && blend_cd);

            bool is_ridge = cell.floor_mode && (uv.y > 0f);
            bool is_ledge = cell.floor_mode && (uv.x > 0f);

            // Get source color maps based on floor/wall state
            GetColorSources(cell.floor_mode,
                out var source_map_0, out var source_map_1,
                out var rl_source_map_0, out var rl_source_map_1);
            bool use_wall_colors = (source_map_0 == chunk.wall_color_map_0);

            // Calculate vertex colors
            Color lower_0 = use_wall_colors ? cell_wall_lower_color_0 : cell_floor_lower_color_0;
            Color upper_0 = use_wall_colors ? cell_wall_upper_color_0 : cell_floor_upper_color_0;
            Color color_0 = InterpolateVertexColor(vertex.x, vertex.y, vertex.z,
                source_map_0, diagMidpoint, lower_0, upper_0);

            Color lower_1 = use_wall_colors ? cell_wall_lower_color_1 : cell_floor_lower_color_1;
            Color upper_1 = use_wall_colors ? cell_wall_upper_color_1 : cell_floor_upper_color_1;
            Color color_1 = InterpolateVertexColor(vertex.x, vertex.y, vertex.z,
                source_map_1, diagMidpoint, lower_1, upper_1);

            // Custom1 value: grass mask + ridge/ledge flags + rl texture index
            int cellIdx   = cell.cell_coords.y * chunk.dimensions.x + cell.cell_coords.x;
            Color c_1_val = chunk.grass_mask_map[cellIdx]; // Grass mask (r channel)
            c_1_val.g = is_ridge ? 1f : 0f;
            c_1_val.b = is_ledge ? 1f : 0f;

            Color rl_lower_0 = cell_wall_lower_color_0;
            Color rl_upper_0 = cell_wall_upper_color_0;
            Color rl_color_0 = InterpolateVertexColor(vertex.x, vertex.y, vertex.z,
                rl_source_map_0, diagMidpoint, rl_lower_0, rl_upper_0);

            Color rl_lower_1 = cell_wall_lower_color_1;
            Color rl_upper_1 = cell_wall_upper_color_1;
            Color rl_color_1 = InterpolateVertexColor(vertex.x, vertex.y, vertex.z,
                rl_source_map_1, diagMidpoint, rl_lower_1, rl_upper_1);

            c_1_val.a = GetTextureIndexFromColors(rl_color_0, rl_color_1);

            // Material blend data
            Color mat_blend = CalculateMaterialBlendData(vertex.x, vertex.z, source_map_0, source_map_1);
            if (cell_has_walls_for_blend && cell.floor_mode)
                mat_blend.a = 2f;

            return new VertexColors
            {
                color_0        = color_0,
                color_1        = color_1,
                custom_1_value = c_1_val,
                mat_blend      = mat_blend,
            };
        }

        // -----------------------------------------------------------------------
        // calculate_corner_colors() – identical to GDScript
        // -----------------------------------------------------------------------
        /// <summary>
        /// Pre-computes per-cell height boundaries and dominant color pairs.
        /// Mirrors calculate_corner_colors() in GDScript exactly.
        /// </summary>
        public void CalculateCornerColors()
        {
            cell_min_height = Mathf.Min(Mathf.Min(cell.ay, cell.by), Mathf.Min(cell.cy, cell.dy));
            cell_max_height = Mathf.Max(Mathf.Max(cell.ay, cell.by), Mathf.Max(cell.cy, cell.dy));

            int x = cell.cell_coords.x;
            int z = cell.cell_coords.y;

            cell_is_boundary = (cell_max_height - cell_min_height) > cell.merge_threshold;

            CalculateCellMaterialPair(chunk.color_map_0, chunk.color_map_1);

            if (cell_is_boundary)
            {
                // Floor colors from color_map
                Color[] floor_cc0 = {
                    chunk.color_map_0[z * chunk.dimensions.x + x],
                    chunk.color_map_0[z * chunk.dimensions.x + x + 1],
                    chunk.color_map_0[(z + 1) * chunk.dimensions.x + x],
                    chunk.color_map_0[(z + 1) * chunk.dimensions.x + x + 1],
                };
                Color[] floor_cc1 = {
                    chunk.color_map_1[z * chunk.dimensions.x + x],
                    chunk.color_map_1[z * chunk.dimensions.x + x + 1],
                    chunk.color_map_1[(z + 1) * chunk.dimensions.x + x],
                    chunk.color_map_1[(z + 1) * chunk.dimensions.x + x + 1],
                };

                // Wall colors from wall_color_map
                Color[] wall_cc0 = {
                    chunk.wall_color_map_0[z * chunk.dimensions.x + x],
                    chunk.wall_color_map_0[z * chunk.dimensions.x + x + 1],
                    chunk.wall_color_map_0[(z + 1) * chunk.dimensions.x + x],
                    chunk.wall_color_map_0[(z + 1) * chunk.dimensions.x + x + 1],
                };
                Color[] wall_cc1 = {
                    chunk.wall_color_map_1[z * chunk.dimensions.x + x],
                    chunk.wall_color_map_1[z * chunk.dimensions.x + x + 1],
                    chunk.wall_color_map_1[(z + 1) * chunk.dimensions.x + x],
                    chunk.wall_color_map_1[(z + 1) * chunk.dimensions.x + x + 1],
                };
                float[] heights = { cell.ay, cell.by, cell.cy, cell.dy };

                int minIdx = 0, maxIdx = 0;
                for (int i = 1; i < 4; i++)
                {
                    if (heights[i] < heights[minIdx]) minIdx = i;
                    if (heights[i] > heights[maxIdx]) maxIdx = i;
                }

                cell_floor_lower_color_0 = floor_cc0[minIdx];
                cell_floor_upper_color_0 = floor_cc0[maxIdx];
                cell_floor_lower_color_1 = floor_cc1[minIdx];
                cell_floor_upper_color_1 = floor_cc1[maxIdx];

                cell_wall_lower_color_0  = wall_cc0[minIdx];
                cell_wall_upper_color_0  = wall_cc0[maxIdx];
                cell_wall_lower_color_1  = wall_cc1[minIdx];
                cell_wall_upper_color_1  = wall_cc1[maxIdx];
            }
        }

        // -----------------------------------------------------------------------
        // Private helpers – mirror every private function in GDScript exactly
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns four source maps [src0, src1, rl_src0, rl_src1] depending on
        /// whether the vertex is a floor or a wall vertex.
        /// Mirrors _get_color_sources().
        /// </summary>
        private void GetColorSources(bool isFloor,
            out Color[] src0, out Color[] src1,
            out Color[] rl_src0, out Color[] rl_src1)
        {
            bool useWall = !isFloor;
            src0    = useWall ? chunk.wall_color_map_0 : chunk.color_map_0;
            src1    = useWall ? chunk.wall_color_map_1 : chunk.color_map_1;
            rl_src0 = chunk.wall_color_map_0;
            rl_src1 = chunk.wall_color_map_1;
        }

        /// <summary>Mirrors _calc_diagonal_color().</summary>
        private Color CalcDiagonalColor(Color[] sourceMap)
        {
            if (chunk.terrain_system.blend_mode == 1)
                return sourceMap[cell.cell_coords.y * chunk.dimensions.x + cell.cell_coords.x];

            int idx      = cell.cell_coords.y * chunk.dimensions.x + cell.cell_coords.x;
            Color adColor = Color.Lerp(sourceMap[idx], sourceMap[idx + chunk.dimensions.x + 1], 0.5f);
            Color bcColor = Color.Lerp(sourceMap[idx + 1], sourceMap[idx + chunk.dimensions.x], 0.5f);

            var result = new Color(
                Mathf.Min(adColor.r, bcColor.r),
                Mathf.Min(adColor.g, bcColor.g),
                Mathf.Min(adColor.b, bcColor.b),
                Mathf.Min(adColor.a, bcColor.a));

            if (adColor.r > 0.99f || bcColor.r > 0.99f) result.r = 1f;
            if (adColor.g > 0.99f || bcColor.g > 0.99f) result.g = 1f;
            if (adColor.b > 0.99f || bcColor.b > 0.99f) result.b = 1f;
            if (adColor.a > 0.99f || bcColor.a > 0.99f) result.a = 1f;
            return result;
        }

        /// <summary>Mirrors _calc_boundary_color().</summary>
        private Color CalcBoundaryColor(float y, Color[] sourceMap, Color lowerColor, Color upperColor)
        {
            if (chunk.terrain_system.blend_mode == 1)
                return sourceMap[cell.cell_coords.y * chunk.dimensions.x + cell.cell_coords.x];

            float height_range  = cell_max_height - cell_min_height;
            float height_factor = Mathf.Clamp((y - cell_min_height) / height_range, 0f, 1f);

            Color color;
            if (height_factor < chunk.lower_thresh)
                color = lowerColor;
            else if (height_factor > chunk.upper_thresh)
                color = upperColor;
            else
            {
                float blend_factor = (height_factor - chunk.lower_thresh) / chunk.blend_zone;
                color = Color.Lerp(lowerColor, upperColor, blend_factor);
            }

            return GetDominantColor(color);
        }

        /// <summary>Mirrors _calc_bilinear_color().</summary>
        private Color CalcBilinearColor(float x, float z, Color[] sourceMap)
        {
            int idx      = cell.cell_coords.y * chunk.dimensions.x + cell.cell_coords.x;
            Color abColor = Color.Lerp(sourceMap[idx], sourceMap[idx + 1], x);
            Color cdColor = Color.Lerp(sourceMap[idx + chunk.dimensions.x],
                                       sourceMap[idx + chunk.dimensions.x + 1], x);

            if (chunk.terrain_system.blend_mode != 1)
                return GetDominantColor(Color.Lerp(abColor, cdColor, z));

            return sourceMap[idx]; // hard squares / hard triangles
        }

        /// <summary>Mirrors _interpolate_vertex_color().</summary>
        private Color InterpolateVertexColor(
            float x, float y, float z,
            Color[] sourceMap,
            bool diagMidpoint,
            Color lowerColor,
            Color upperColor)
        {
            if (diagMidpoint)           return CalcDiagonalColor(sourceMap);
            if (cell_is_boundary)       return CalcBoundaryColor(y, sourceMap, lowerColor, upperColor);
            return CalcBilinearColor(x, z, sourceMap);
        }

        // -----------------------------------------------------------------------
        // Static helpers (public to allow Grass Planter to call them)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns a Color where only the dominant channel is 1.0.
        /// Mirrors get_dominant_color() in GDScript.
        /// </summary>
        public static Color GetDominantColor(Color c)
        {
            float maxVal = c.r;
            int   idx    = 0;
            if (c.g > maxVal) { maxVal = c.g; idx = 1; }
            if (c.b > maxVal) { maxVal = c.b; idx = 2; }
            if (c.a > maxVal) {                idx = 3; }

            return idx switch
            {
                0 => new Color(1f, 0f, 0f, 0f),
                1 => new Color(0f, 1f, 0f, 0f),
                2 => new Color(0f, 0f, 1f, 0f),
                _ => new Color(0f, 0f, 0f, 1f),
            };
        }

        /// <summary>
        /// Converts a vertex Color pair to a texture index 0–15.
        /// Mirrors get_texture_index_from_colors() in GDScript.
        /// </summary>
        public static int GetTextureIndexFromColors(Color c0, Color c1)
        {
            int c0Idx = 0; float c0Max = c0.r;
            if (c0.g > c0Max) { c0Max = c0.g; c0Idx = 1; }
            if (c0.b > c0Max) { c0Max = c0.b; c0Idx = 2; }
            if (c0.a > c0Max) {                c0Idx = 3; }

            int c1Idx = 0; float c1Max = c1.r;
            if (c1.g > c1Max) { c1Max = c1.g; c1Idx = 1; }
            if (c1.b > c1Max) { c1Max = c1.b; c1Idx = 2; }
            if (c1.a > c1Max) {                c1Idx = 3; }

            return c0Idx * 4 + c1Idx;
        }

        /// <summary>
        /// Converts texture index (0–15) back to a Color pair.
        /// Mirrors texture_index_to_colors() in GDScript.
        /// </summary>
        public static (Color c0, Color c1) TextureIndexToColors(int idx)
        {
            int c0Ch = idx / 4;
            int c1Ch = idx % 4;
            Color c0 = c0Ch switch { 0 => new Color(1,0,0,0), 1 => new Color(0,1,0,0), 2 => new Color(0,0,1,0), _ => new Color(0,0,0,1) };
            Color c1 = c1Ch switch { 0 => new Color(1,0,0,0), 1 => new Color(0,1,0,0), 2 => new Color(0,0,1,0), _ => new Color(0,0,0,1) };
            return (c0, c1);
        }

        // -----------------------------------------------------------------------
        // Material pair / blend data – identical to GDScript
        // -----------------------------------------------------------------------

        /// <summary>
        /// Computes the two dominant textures for the current cell.
        /// Mirrors calculate_cell_material_pair() in GDScript.
        /// </summary>
        public void CalculateCellMaterialPair(Color[] sourceMap0, Color[] sourceMap1)
        {
            var cc     = cell.cell_coords;
            int stride = chunk.dimensions.x;

            int texA = GetTextureIndexFromColors(sourceMap0[cc.y * stride + cc.x],
                                                 sourceMap1[cc.y * stride + cc.x]);
            int texB = GetTextureIndexFromColors(sourceMap0[cc.y * stride + cc.x + 1],
                                                 sourceMap1[cc.y * stride + cc.x + 1]);
            int texC = GetTextureIndexFromColors(sourceMap0[(cc.y + 1) * stride + cc.x],
                                                 sourceMap1[(cc.y + 1) * stride + cc.x]);
            int texD = GetTextureIndexFromColors(sourceMap0[(cc.y + 1) * stride + cc.x + 1],
                                                 sourceMap1[(cc.y + 1) * stride + cc.x + 1]);

            var counts = new Dictionary<int, int>();
            counts[texA] = counts.GetValueOrDefault(texA, 0) + 1;
            counts[texB] = counts.GetValueOrDefault(texB, 0) + 1;
            counts[texC] = counts.GetValueOrDefault(texC, 0) + 1;
            counts[texD] = counts.GetValueOrDefault(texD, 0) + 1;

            var sorted = new List<int>(counts.Keys);
            sorted.Sort((a, b) => counts[b].CompareTo(counts[a]));

            cell_mat_a = sorted[0];
            cell_mat_b = sorted.Count > 1 ? sorted[1] : sorted[0];
            cell_mat_c = sorted.Count > 2 ? sorted[2] : cell_mat_b;
        }

        /// <summary>
        /// Computes CUSTOM2 blend data.
        /// Encoding: Color(packed_mats, mat_c/15, weight_a, weight_b)
        ///   R = (mat_a + mat_b * 16) / 255.0
        ///   G = mat_c / 15.0
        ///   B = weight_a (0.0–1.0)
        ///   A = weight_b (0.0–1.0), or 2.0 to signal use_vertex_colors
        /// Mirrors calculate_material_blend_data() in GDScript exactly.
        /// </summary>
        public Color CalculateMaterialBlendData(float vertX, float vertZ,
            Color[] sourceMap0, Color[] sourceMap1)
        {
            var cc     = cell.cell_coords;
            int stride = chunk.dimensions.x;

            int texA = GetTextureIndexFromColors(sourceMap0[cc.y * stride + cc.x],
                                                 sourceMap1[cc.y * stride + cc.x]);
            int texB = GetTextureIndexFromColors(sourceMap0[cc.y * stride + cc.x + 1],
                                                 sourceMap1[cc.y * stride + cc.x + 1]);
            int texC = GetTextureIndexFromColors(sourceMap0[(cc.y + 1) * stride + cc.x],
                                                 sourceMap1[(cc.y + 1) * stride + cc.x]);
            int texD = GetTextureIndexFromColors(sourceMap0[(cc.y + 1) * stride + cc.x + 1],
                                                 sourceMap1[(cc.y + 1) * stride + cc.x + 1]);

            float wA = (1f - vertX) * (1f - vertZ);
            float wB = vertX        * (1f - vertZ);
            float wC = (1f - vertX) * vertZ;
            float wD = vertX        * vertZ;

            float wMatA = 0f, wMatB = 0f, wMatC = 0f;

            if (texA == cell_mat_a) wMatA += wA; else if (texA == cell_mat_b) wMatB += wA; else if (texA == cell_mat_c) wMatC += wA;
            if (texB == cell_mat_a) wMatA += wB; else if (texB == cell_mat_b) wMatB += wB; else if (texB == cell_mat_c) wMatC += wB;
            if (texC == cell_mat_a) wMatA += wC; else if (texC == cell_mat_b) wMatB += wC; else if (texC == cell_mat_c) wMatC += wC;
            if (texD == cell_mat_a) wMatA += wD; else if (texD == cell_mat_b) wMatB += wD; else if (texD == cell_mat_c) wMatC += wD;

            float total = wMatA + wMatB + wMatC;
            if (total > 0.001f) { wMatA /= total; wMatB /= total; }

            float packedMats = (cell_mat_a + cell_mat_b * 16f) / 255f;
            return new Color(packedMats, cell_mat_c / 15f, wMatA, wMatB);
        }
    }
}
