// MarchingSquaresTerrainCell.cs
// Unity C# port of marching_squares_terrain_cell.gd (Godot 4)
// All mathematical / computational logic is preserved exactly – no differences.

using System.Collections.Generic;
using UnityEngine;

namespace MarchingSquaresTerrain
{
    /// <summary>
    /// Generates the geometry for a single 2×2 terrain cell using the Marching Squares
    /// algorithm.  The cell has four corner heights (A = top-left, B = top-right,
    /// C = bottom-left, D = bottom-right) and produces up to 19 mesh cases that are
    /// composed from helper primitives (full floor, outer corner, edge, inner corner,
    /// diagonal floor).
    ///
    /// Direct port of marching_squares_terrain_cell.gd – every constant, formula, and
    /// control-flow branch is identical to the Godot source.
    /// </summary>
    public class MarchingSquaresTerrainCell
    {
        // -----------------------------------------------------------------------
        // Constants (identical to GDScript source)
        // -----------------------------------------------------------------------

        /// <summary>
        /// &lt; 1.0 = more aggressive wall detection.
        /// &gt; 1.0 = less aggressive / more slope blend.
        /// </summary>
        public const float BLEND_EDGE_SENSITIVITY = 1.25f;

        /// <summary>When true the floor uses 4 triangles (fan) instead of 2.</summary>
        public const bool HIGHER_POLY_FLOORS = true;

        // -----------------------------------------------------------------------
        // Corner rotation enum  (mirrors CellRotation in GDScript)
        // -----------------------------------------------------------------------
        public enum CellRotation { DEG0 = 0, DEG90 = 1, DEG180 = 2, DEG270 = 3 }

        // -----------------------------------------------------------------------
        // Per-call geometry accumulators
        // -----------------------------------------------------------------------
        public List<Vector3>  pts            = new List<Vector3>();
        public List<Vector2>  uvs            = new List<Vector2>();
        public List<Vector2>  uv2s           = new List<Vector2>();
        public List<Color>    color_0s       = new List<Color>();
        public List<Color>    color_1s       = new List<Color>();
        public List<Color>    custom_1_values = new List<Color>();
        public List<Color>    mat_blends     = new List<Color>();
        public List<bool>     floors         = new List<bool>();

        public Vector2Int cell_coords;

        /// <summary>True while adding floor vertices, false while adding wall vertices.</summary>
        public bool floor_mode;

        // Active (rotated) corner heights
        public float ay, by, dy, cy;

        // Original (pre-rotation) corner heights
        private float _ay, _by, _cy, _dy;

        // Edge-connection flags (recomputed on every rotation)
        public bool ab, bd, cd, ac;

        // -----------------------------------------------------------------------
        // Rotation property  (mirrors the GDScript set(x): block exactly)
        // -----------------------------------------------------------------------
        private CellRotation _rotation;
        public CellRotation rotation
        {
            get => _rotation;
            set
            {
                switch (value)
                {
                    case CellRotation.DEG90:  ay = _by; break;
                    case CellRotation.DEG180: ay = _dy; break;
                    case CellRotation.DEG270: ay = _cy; break;
                    default:                  ay = _ay; break;
                }
                switch (value)
                {
                    case CellRotation.DEG90:  by = _dy; break;
                    case CellRotation.DEG180: by = _cy; break;
                    case CellRotation.DEG270: by = _ay; break;
                    default:                  by = _by; break;
                }
                switch (value)
                {
                    case CellRotation.DEG90:  dy = _cy; break;
                    case CellRotation.DEG180: dy = _ay; break;
                    case CellRotation.DEG270: dy = _by; break;
                    default:                  dy = _dy; break;
                }
                switch (value)
                {
                    case CellRotation.DEG90:  cy = _ay; break;
                    case CellRotation.DEG180: cy = _by; break;
                    case CellRotation.DEG270: cy = _dy; break;
                    default:                  cy = _cy; break;
                }

                ab = Mathf.Abs(ay - by) < merge_threshold; // top edge
                bd = Mathf.Abs(by - dy) < merge_threshold; // right edge
                cd = Mathf.Abs(cy - dy) < merge_threshold; // bottom edge
                ac = Mathf.Abs(ay - cy) < merge_threshold; // left edge

                _rotation = value;
            }
        }

        // -----------------------------------------------------------------------
        // Merge threshold (computed in constructor – same formula as GDScript)
        // -----------------------------------------------------------------------
        public float merge_threshold;

        // -----------------------------------------------------------------------
        // References
        // -----------------------------------------------------------------------
        public MarchingSquaresTerrainChunk              chunk;
        public MarchingSquaresTerrainVertexColorHelper  color_helper;

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------
        /// <param name="chunk_">Owner chunk.</param>
        /// <param name="colorHelper_">Pre-constructed vertex color helper.</param>
        /// <param name="yTopLeft">Height of corner A (top-left).</param>
        /// <param name="yTopRight">Height of corner B (top-right).</param>
        /// <param name="yBottomLeft">Height of corner C (bottom-left).</param>
        /// <param name="yBottomRight">Height of corner D (bottom-right).</param>
        /// <param name="mergeThreshold_">Base merge threshold from the chunk's merge mode.</param>
        public MarchingSquaresTerrainCell(
            MarchingSquaresTerrainChunk             chunk_,
            MarchingSquaresTerrainVertexColorHelper colorHelper_,
            float yTopLeft, float yTopRight, float yBottomLeft, float yBottomRight,
            float mergeThreshold_)
        {
            chunk        = chunk_;
            color_helper = colorHelper_;
            _ay = yTopLeft;
            _by = yTopRight;
            _cy = yBottomLeft;
            _dy = yBottomRight;

            // --- Identical scale computation from GDScript ---
            var cellSz = chunk_.terrain_system.cell_size;
            var dims   = chunk_.terrain_system.dimensions;

            float cell_scale_factor       = Mathf.Clamp((cellSz.x + cellSz.y) / 4.0f, 0.3f, 1.0f);
            float dimensions_scale_factor = Mathf.Clamp(
                ((dims.x / 33.0f) + (dims.z / 33.0f)) / 2.0f, 0.5f, 2.0f);

            merge_threshold = mergeThreshold_ * dimensions_scale_factor * cell_scale_factor;

            // Set rotation to 0 (this also initialises ay/by/cy/dy and the flags)
            rotation = CellRotation.DEG0;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------
        private void ResetGeometryCache()
        {
            pts.Clear(); uvs.Clear(); uv2s.Clear();
            color_0s.Clear(); color_1s.Clear();
            custom_1_values.Clear(); mat_blends.Clear();
            floors.Clear();
        }

        /// <summary>Rotate by <paramref name="r"/> steps counter-clockwise (mirrors GDScript Rotate).</summary>
        public void Rotate(int r) =>
            rotation = (CellRotation)(((4 + r + (int)rotation) % 4));

        public bool AllEdgesAreConnected() => ab && ac && bd && cd;

        /// <summary>True if A is higher than B and outside merge distance.</summary>
        public bool IsHigher(float a, float b) => a - b > merge_threshold;

        /// <summary>True if A is lower than B and outside merge distance.</summary>
        public bool IsLower(float a, float b) => a - b < -merge_threshold;

        public bool IsMerged(float a, float b) => Mathf.Abs(a - b) < merge_threshold;

        // -----------------------------------------------------------------------
        // Main entry – exactly mirrors generate_geometry() in GDScript
        // -----------------------------------------------------------------------
        public void GenerateGeometry(Vector2Int cellCoords_)
        {
            cell_coords = cellCoords_;
            ResetGeometryCache();
            color_helper.CalculateCornerColors();

            // Case 0 – all edges connected → full floor
            if (AllEdgesAreConnected())
            {
                AddC0();
                chunk.AddPolygons(cell_coords, pts, uvs, uv2s,
                    color_0s, color_1s, custom_1_values, mat_blends, floors);
                return;
            }

            bool caseFound = false;
            for (int rot = 0; rot < 4; rot++)
            {
                rotation  = (CellRotation)rot;
                caseFound = true;

                if (IsHigher(ay, by) && IsHigher(ay, cy) && bd && cd)
                    AddC1();
                else if (IsHigher(ay, cy) && IsHigher(by, dy) && ab && cd)
                    AddC2();
                else if (IsHigher(ay, by) && IsHigher(ay, cy) && IsHigher(by, dy) && cd)
                    AddC3();
                else if (IsHigher(by, ay) && IsHigher(ay, cy) && IsHigher(by, dy) && cd)
                    AddC4();
                else if (IsLower(ay, by) && IsLower(ay, cy) && IsLower(dy, by) && IsLower(dy, cy) && IsMerged(by, cy))
                    AddC5();
                else if (IsLower(ay, by) && IsLower(ay, cy) && IsLower(dy, by) && IsLower(dy, cy) && IsHigher(by, cy))
                    AddC6();
                else if (IsLower(ay, by) && IsLower(ay, cy) && bd && cd)
                    AddC7();
                else if (IsLower(ay, by) && IsLower(ay, cy) && IsHigher(dy, by) && IsHigher(dy, cy) && IsMerged(by, cy))
                    AddC8();
                else if (IsLower(ay, by) && IsLower(ay, cy) && IsLower(dy, cy) && bd)
                    AddC9();
                else if (IsLower(ay, by) && IsLower(ay, cy) && IsLower(dy, by) && cd)
                    AddC10();
                else if (IsLower(ay, by) && IsLower(ay, cy) && IsHigher(dy, cy) && bd)
                    AddC11();
                else if (IsLower(ay, by) && IsLower(ay, cy) && IsHigher(dy, by) && cd)
                    AddC12();
                else if (IsLower(ay, by) && IsLower(by, dy) && IsLower(dy, cy) && IsHigher(cy, ay))
                    AddC13();
                else if (IsLower(ay, cy) && IsLower(cy, dy) && IsLower(dy, by) && IsHigher(by, ay))
                    AddC14();
                else if (IsLower(ay, by) && IsLower(by, cy) && IsLower(cy, dy))
                    AddC15();
                else if (IsLower(ay, cy) && IsLower(cy, by) && IsLower(by, dy))
                    AddC16();
                else if (ab && bd && cd && IsHigher(ay, cy))
                    AddC17();
                else if (ab && ac && cd && IsHigher(by, dy))
                    AddC18();
                else
                    caseFound = false;

                if (caseFound) break;
            }

            if (!caseFound)
                AddC0(); // Invalid / unknown – fall back to full floor

            chunk.AddPolygons(cell_coords, pts, uvs, uv2s,
                color_0s, color_1s, custom_1_values, mat_blends, floors);
        }

        // -----------------------------------------------------------------------
        // Floor / wall mode toggles
        // -----------------------------------------------------------------------
        public void StartFloor() => floor_mode = true;
        public void StartWall()  => floor_mode = false;

        // -----------------------------------------------------------------------
        // AddPoint – identical coordinate math from GDScript add_point()
        // -----------------------------------------------------------------------
        /// <summary>
        /// Adds a vertex to the current geometry lists.
        /// <c>x</c>/<c>z</c> are local cell fractions [0,1]; <c>y</c> is world height.
        /// <c>u</c>/<c>v</c> are ledge/ridge UV hints used by the shader.
        /// </summary>
        public void AddPoint(float x, float y, float z, float u, float v)
        {
            // Apply counter-clockwise rotation
            for (int i = 0; i < (int)rotation; i++)
            {
                float temp = x;
                x = 1f - z;
                z = temp;
            }

            // UV1 – walls always get (1,1)
            Vector2 uv = floor_mode ? new Vector2(u, v) : Vector2.one;

            // World-space vertex position (same formula as GDScript)
            var vert = new Vector3(
                (cell_coords.x + x) * chunk.cell_size.x,
                y,
                (cell_coords.y + z) * chunk.cell_size.y
            );

            // UV2 – floor uses local planar projection; wall uses global xz+y projection
            Vector2 uv2;
            if (floor_mode)
            {
                // GDScript: uv2 = Vector2(vert.x, vert.z) / chunk.cell_size  (element-wise)
                uv2 = new Vector2(vert.x / chunk.cell_size.x, vert.z / chunk.cell_size.y);
            }
            else
            {
                Vector3 chunkPos  = chunk.globalPositionCached;
                Vector3 globalPos = vert + chunkPos;
                // Matches: (Vector2(global_pos.x, global_pos.y) + Vector2(global_pos.z, global_pos.y))
                uv2 = new Vector2(globalPos.x + globalPos.z, globalPos.y + globalPos.y);
            }

            pts.Add(vert);
            uvs.Add(uv);
            uv2s.Add(uv2);

            var colors = color_helper.BlendColors(new Vector3(x, y, z), uv);
            custom_1_values.Add(colors.custom_1_value);
            color_0s.Add(colors.color_0);
            color_1s.Add(colors.color_1);
            mat_blends.Add(colors.mat_blend);
            floors.Add(floor_mode);
        }

        // -----------------------------------------------------------------------
        // Case dispatchers (mirror add_cN() in GDScript)
        // -----------------------------------------------------------------------
        void AddC0() => AddFullFloor();
        void AddC1() => AddOuterCorner(true, true);
        void AddC2() => AddEdge(true, true);

        void AddC3()
        {
            AddEdge(true, true, 0.5f, 1f);
            AddOuterCorner(false, true, true, by);
        }

        void AddC4()
        {
            AddEdge(true, true, 0f, 0.5f);
            Rotate(1);
            AddOuterCorner(false, true, true, cy);
        }

        void AddC5()
        {
            AddInnerCorner(true, false);
            AddDiagonalFloor(by, cy, true, true);
            Rotate(2);
            AddInnerCorner(true, false);
        }

        void AddC6()
        {
            AddInnerCorner(true, false, true);
            AddDiagonalFloor(cy, cy, true, true);
            Rotate(2);
            AddInnerCorner(true, false, true);
            Rotate(-1);
            AddOuterCorner(false, true);
        }

        void AddC7() => AddInnerCorner(true, true);

        void AddC8()
        {
            AddInnerCorner(true, false);
            AddDiagonalFloor(by, cy, true, false);
            Rotate(2);
            AddOuterCorner(false, true);
        }

        void AddC9()
        {
            AddInnerCorner(true, false, true);
            StartFloor();

            // D corner (B edge connected → use midpoint between B and D)
            AddPoint(1f,  dy,          1f,  0f, 0f);
            AddPoint(0.5f, dy,          1f,  1f, 0f);
            AddPoint(1f,  (by+dy)/2f,  0.5f, 0f, 0f);

            // B corner
            AddPoint(1f,  by,          0f,  0f, 0f);
            AddPoint(1f,  (by+dy)/2f,  0.5f, 0f, 0f);
            AddPoint(0.5f, by,          0f,  0f, 1f);

            // Centre floors
            AddPoint(0.5f, by,          0f,  0f, 1f);
            AddPoint(1f,  (by+dy)/2f,  0.5f, 0f, 0f);
            AddPoint(0f,  by,          0.5f, 1f, 1f);

            AddPoint(0.5f, dy,          1f,  1f, 0f);
            AddPoint(0f,  by,          0.5f, 1f, 1f);
            AddPoint(1f,  (by+dy)/2f,  0.5f, 0f, 0f);

            // Walls to upper corner
            StartWall();
            AddPoint(0f,  by, 0.5f, 0f, 0f);
            AddPoint(0.5f, dy, 1f,  0f, 0f);
            AddPoint(0f,  cy, 0.5f, 0f, 0f);

            AddPoint(0.5f, cy, 1f,  0f, 0f);
            AddPoint(0f,  cy, 0.5f, 0f, 0f);
            AddPoint(0.5f, dy, 1f,  0f, 0f);

            // C upper floor
            StartFloor();
            AddPoint(0f,  cy, 1f,  0f, 0f);
            AddPoint(0f,  cy, 0.5f, 0f, 1f);
            AddPoint(0.5f, cy, 1f,  0f, 1f);
        }

        void AddC10()
        {
            AddInnerCorner(true, false, true);

            // D corner (C edge connected → use midpoint between C and D)
            StartFloor();
            AddPoint(1f,  dy,              1f,  0f, 0f);
            AddPoint(0.5f, (dy+cy)/2f,      1f,  0f, 0f);
            AddPoint(1f,  dy,              0.5f, 0f, 0f);

            // C corner
            AddPoint(0f,  cy,              1f,  0f, 0f);
            AddPoint(0f,  cy,              0.5f, 0f, 0f);
            AddPoint(0.5f, (dy+cy)/2f,      1f,  0f, 0f);

            // Centre floors
            AddPoint(0f,  cy,              0.5f, 0f, 0f);
            AddPoint(0.5f, cy,              0f,  0f, 0f);
            AddPoint(0.5f, (dy+cy)/2f,      1f,  0f, 0f);

            AddPoint(1f,  dy,              0.5f, 0f, 0f);
            AddPoint(0.5f, (dy+cy)/2f,      1f,  0f, 0f);
            AddPoint(0.5f, cy,              0f,  0f, 0f);

            // Walls to upper corner
            StartWall();
            AddPoint(0.5f, cy, 0f,  0f, 0f);
            AddPoint(0.5f, by, 0f,  0f, 0f);
            AddPoint(1f,  dy, 0.5f, 0f, 0f);

            AddPoint(1f,  by, 0.5f, 0f, 0f);
            AddPoint(1f,  dy, 0.5f, 0f, 0f);
            AddPoint(0.5f, by, 0f,  0f, 0f);

            // B upper floor
            StartFloor();
            AddPoint(1f,  by, 0f,  0f, 0f);
            AddPoint(1f,  by, 0.5f, 0f, 0f);
            AddPoint(0.5f, by, 0f,  0f, 0f);
        }

        void AddC11()
        {
            AddInnerCorner(true, false, true, true, false);
            Rotate(1);
            AddEdge(false, true);
        }

        void AddC12()
        {
            AddInnerCorner(true, false, true, false, true);
            Rotate(2);
            AddEdge(false, true);
        }

        void AddC13()
        {
            AddInnerCorner(true, false, true, false, true);
            Rotate(2);
            AddEdge(false, true, 0f, 0.5f);
            Rotate(1);
            AddOuterCorner(false, true, true, cy);
        }

        void AddC14()
        {
            AddInnerCorner(true, false, true, true, false);
            Rotate(1);
            AddEdge(false, true, 0.5f, 1f);
            AddOuterCorner(false, true, true, by);
        }

        void AddC15()
        {
            AddInnerCorner(true, false, true, false, true);
            Rotate(2);
            AddEdge(false, true, 0.5f, 1f);
            AddOuterCorner(false, true, true, by);
        }

        void AddC16()
        {
            AddInnerCorner(true, false, true, true, false);
            Rotate(1);
            AddEdge(false, true, 0f, 0.5f);
            Rotate(1);
            AddOuterCorner(false, true, true, cy);
        }

        void AddC17()
        {
            float edge_by = (by + dy) / 2f;
            float edge_dy = (by + dy) / 2f;

            // Upper floor
            StartFloor();
            AddPoint(0f,  ay,     0f,   0f, 0f);
            AddPoint(1f,  by,     0f,   0f, 0f);
            AddPoint(1f,  edge_by, 0.5f, 0f, 0f);

            AddPoint(1f,  edge_by, 0.5f, 0f, 1f);
            AddPoint(0f,  ay,     0.5f, 0f, 1f);
            AddPoint(0f,  ay,     0f,   0f, 0f);

            // Wall
            StartWall();
            AddPoint(0f,  cy,     0.5f, 0f, 0f);
            AddPoint(0f,  ay,     0.5f, 0f, 1f);
            AddPoint(1f,  edge_dy, 0.5f, 1f, 0f);

            // Lower floor
            StartFloor();
            AddPoint(0f,  cy,     0.5f, 1f, 0f);
            AddPoint(1f,  edge_dy, 0.5f, 1f, 0f);
            AddPoint(0f,  cy,     1f,  0f, 0f);

            AddPoint(1f,  dy,     1f,  0f, 0f);
            AddPoint(0f,  cy,     1f,  0f, 0f);
            AddPoint(1f,  edge_dy, 0.5f, 0f, 0f);
        }

        void AddC18()
        {
            float edge_ay = (ay + cy) / 2f;
            float edge_cy = (ay + cy) / 2f;

            // Upper floor
            StartFloor();
            AddPoint(0f, ay,     0f,  0f, 0f);
            AddPoint(1f, by,     0f,  0f, 0f);
            AddPoint(0f, edge_ay, 0.5f, 0f, 0f);

            AddPoint(1f, by,     0.5f, 0f, 1f);
            AddPoint(0f, edge_ay, 0.5f, 0f, 1f);
            AddPoint(1f, by,     0f,  0f, 0f);

            // Wall
            StartWall();
            AddPoint(1f, by,     0.5f, 1f, 1f);
            AddPoint(1f, dy,     0.5f, 1f, 0f);
            AddPoint(0f, edge_ay, 0.5f, 0f, 0f);

            // Lower floor
            StartFloor();
            AddPoint(0f, edge_cy, 0.5f, 1f, 0f);
            AddPoint(1f, dy,     0.5f, 1f, 0f);
            AddPoint(1f, dy,     1f,  0f, 0f);

            AddPoint(0f, cy,     1f,  0f, 0f);
            AddPoint(0f, edge_cy, 0.5f, 0f, 0f);
            AddPoint(1f, dy,     1f,  0f, 0f);
        }

        // -----------------------------------------------------------------------
        // Primitive builders – identical to GDScript helpers
        // -----------------------------------------------------------------------

        /// <summary>Full 4-fan or 2-tri floor (mirrors add_full_floor).</summary>
        void AddFullFloor()
        {
            StartFloor();
            if (HIGHER_POLY_FLOORS)
            {
                float ey = (ay + by + cy + dy) / 4f;

                AddPoint(0f,  ay, 0f,  0f, 0f);
                AddPoint(1f,  by, 0f,  0f, 0f);
                AddPoint(0.5f, ey, 0.5f, 0f, 0f);

                AddPoint(1f,  by, 0f,  0f, 0f);
                AddPoint(1f,  dy, 1f,  0f, 0f);
                AddPoint(0.5f, ey, 0.5f, 0f, 0f);

                AddPoint(1f,  dy, 1f,  0f, 0f);
                AddPoint(0f,  cy, 1f,  0f, 0f);
                AddPoint(0.5f, ey, 0.5f, 0f, 0f);

                AddPoint(0f,  cy, 1f,  0f, 0f);
                AddPoint(0f,  ay, 0f,  0f, 0f);
                AddPoint(0.5f, ey, 0.5f, 0f, 0f);
            }
            else
            {
                AddPoint(0f, ay, 0f, 0f, 0f);
                AddPoint(1f, by, 0f, 0f, 0f);
                AddPoint(0f, cy, 1f, 0f, 0f);

                AddPoint(1f, dy, 1f, 0f, 0f);
                AddPoint(0f, cy, 1f, 0f, 0f);
                AddPoint(1f, by, 0f, 0f, 0f);
            }
        }

        /// <summary>
        /// Outer corner where A is the raised corner
        /// (mirrors add_outer_corner in GDScript).
        /// </summary>
        void AddOuterCorner(
            bool floorBelow = true, bool floorAbove = true,
            bool flattenBottom = false, float bottomHeight = -1f)
        {
            float edge_by = flattenBottom ? bottomHeight : by;
            float edge_cy = flattenBottom ? bottomHeight : cy;

            if (floorAbove)
            {
                StartFloor();
                AddPoint(0f,   ay, 0f,   0f, 0f);
                AddPoint(0.5f, ay, 0f,   0f, 1f);
                AddPoint(0f,   ay, 0.5f, 0f, 1f);
            }

            StartWall();
            AddPoint(0f,   edge_cy, 0.5f, 0f, 0f);
            AddPoint(0f,   ay,      0.5f, 0f, 1f);
            AddPoint(0.5f, edge_by, 0f,   1f, 0f);

            AddPoint(0.5f, ay,      0f,   1f, 1f);
            AddPoint(0.5f, edge_by, 0f,   1f, 0f);
            AddPoint(0f,   ay,      0.5f, 0f, 1f);

            if (floorBelow)
            {
                StartFloor();
                AddPoint(1f,   dy,      1f,  0f, 0f);
                AddPoint(0f,   cy,      1f,  0f, 0f);
                AddPoint(1f,   by,      0f,  0f, 0f);

                AddPoint(0f,   cy,      1f,  0f, 0f);
                AddPoint(0f,   cy,      0.5f, 1f, 0f);
                AddPoint(0.5f, by,      0f,   1f, 0f);

                AddPoint(1f,   by,      0f,  0f, 0f);
                AddPoint(0f,   cy,      1f,  0f, 0f);
                AddPoint(0.5f, by,      0f,   1f, 0f);
            }
        }

        /// <summary>
        /// Edge where AB is the raised edge (mirrors add_edge in GDScript).
        /// </summary>
        void AddEdge(bool floorBelow, bool floorAbove, float a_x = 0f, float b_x = 1f)
        {
            float edge_ay = ab ? ay : Mathf.Min(ay, by);
            float edge_by = ab ? by : Mathf.Min(ay, by);
            float edge_cy = cd ? cy : Mathf.Max(cy, dy);
            float edge_dy = cd ? dy : Mathf.Max(cy, dy);

            if (floorAbove)
            {
                StartFloor();
                AddPoint(a_x, edge_ay, 0f,  (a_x > 0f ? 1f : 0f), 0f);
                AddPoint(b_x, edge_by, 0f,  (b_x < 1f ? 1f : 0f), 0f);
                AddPoint(0f,  edge_ay, 0.5f, (b_x < 1f ? -1f : (a_x > 0f ? 1f : 0f)), 1f);

                AddPoint(1f,  edge_by, 0.5f, (a_x > 0f ? -1f : (b_x < 1f ? 1f : 0f)), 1f);
                AddPoint(0f,  edge_ay, 0.5f, (b_x < 1f ? -1f : (a_x > 0f ? 1f : 0f)), 1f);
                AddPoint(b_x, edge_by, 0f,  (b_x < 1f ? 1f : 0f), 0f);
            }

            StartWall();
            AddPoint(0f, edge_cy, 0.5f, 0f, 0f);
            AddPoint(0f, edge_ay, 0.5f, 0f, 1f);
            AddPoint(1f, edge_dy, 0.5f, 1f, 0f);

            AddPoint(1f, edge_by, 0.5f, 1f, 1f);
            AddPoint(1f, edge_dy, 0.5f, 1f, 0f);
            AddPoint(0f, edge_ay, 0.5f, 0f, 1f);

            if (floorBelow)
            {
                StartFloor();
                AddPoint(0f, cy,     0.5f, 1f, 0f);
                AddPoint(1f, dy,     0.5f, 1f, 0f);
                AddPoint(0f, cy,     1f,  0f, 0f);

                AddPoint(1f, dy,     1f,  0f, 0f);
                AddPoint(0f, cy,     1f,  0f, 0f);
                AddPoint(1f, dy,     0.5f, 1f, 0f);
            }
        }

        /// <summary>
        /// Inner corner where A is the lowered corner (mirrors add_inner_corner in GDScript).
        /// </summary>
        void AddInnerCorner(
            bool lowerFloor = true, bool fullUpperFloor = true,
            bool flatten = false, bool bdFloor = false, bool cdFloor = false)
        {
            float corner_by = flatten ? Mathf.Min(by, cy) : by;
            float corner_cy = flatten ? Mathf.Min(by, cy) : cy;

            if (lowerFloor)
            {
                StartFloor();
                AddPoint(0f,   ay, 0f,  0f, 0f);
                AddPoint(0.5f, ay, 0f,  1f, 0f);
                AddPoint(0f,   ay, 0.5f, 1f, 0f);
            }

            StartWall();
            AddPoint(0f,   ay,       0.5f, 1f, 0f);
            AddPoint(0.5f, ay,       0f,   0f, 0f);
            AddPoint(0f,   corner_cy, 0.5f, 1f, 1f);

            AddPoint(0.5f, corner_by, 0f,   0f, 1f);
            AddPoint(0f,   corner_cy, 0.5f, 1f, 1f);
            AddPoint(0.5f, ay,       0f,   0f, 0f);

            StartFloor();
            if (fullUpperFloor)
            {
                AddPoint(1f,   dy,       1f,  0f, 0f);
                AddPoint(0f,   corner_cy, 1f,  0f, 0f);
                AddPoint(1f,   corner_by, 0f,  0f, 0f);

                AddPoint(0f,   corner_cy, 1f,  0f, 0f);
                AddPoint(0f,   corner_cy, 0.5f, 0f, 1f);
                AddPoint(0.5f, corner_by, 0f,   0f, 1f);

                AddPoint(1f,   corner_by, 0f,  0f, 0f);
                AddPoint(0f,   corner_cy, 1f,  0f, 0f);
                AddPoint(0.5f, corner_by, 0f,   0f, 1f);
            }

            // BD edge: B and D both higher than C
            if (cdFloor)
            {
                AddPoint(1f,  by, 0f,   0f,  0f);
                AddPoint(0f,  by, 0.5f, 1f,  1f);
                AddPoint(0.5f, by, 0f,   0f,  1f);

                AddPoint(1f,  by, 0f,   0f,  0f);
                AddPoint(1f,  by, 0.5f, 1f, -1f);
                AddPoint(0f,  by, 0.5f, 1f,  1f);
            }

            // CD edge: C and D both higher than B
            if (bdFloor)
            {
                AddPoint(0f,  cy, 0.5f, 0f,  1f);
                AddPoint(0.5f, cy, 0f,   1f,  1f);
                AddPoint(0f,  cy, 1f,   0f,  0f);

                AddPoint(0.5f, cy, 1f,   1f, -1f);
                AddPoint(0f,  cy, 1f,   0f,  0f);
                AddPoint(0.5f, cy, 0f,   1f,  1f);
            }
        }

        /// <summary>
        /// Diagonal floor connecting B and C corners
        /// (mirrors add_diagonal_floor in GDScript).
        /// </summary>
        void AddDiagonalFloor(float b_y, float c_y, bool aCliff, bool dCliff)
        {
            StartFloor();

            AddPoint(1f,  b_y, 0f,  0f, 0f);
            AddPoint(0f,  c_y, 1f,  0f, 0f);
            AddPoint(0.5f, b_y, 0f,  aCliff ? 0f : 1f, aCliff ? 1f : 0f);

            AddPoint(0f,  c_y, 1f,  0f, 0f);
            AddPoint(0f,  c_y, 0.5f, aCliff ? 0f : 1f, aCliff ? 1f : 0f);
            AddPoint(0.5f, b_y, 0f,  aCliff ? 0f : 1f, aCliff ? 1f : 0f);

            AddPoint(1f,  b_y, 0f,  0f, 0f);
            AddPoint(1f,  b_y, 0.5f, dCliff ? 0f : 1f, dCliff ? 1f : 0f);
            AddPoint(0f,  c_y, 1f,  0f, 0f);

            AddPoint(0f,  c_y, 1f,  0f, 0f);
            AddPoint(1f,  b_y, 0.5f, dCliff ? 0f : 1f, dCliff ? 1f : 0f);
            AddPoint(0.5f, c_y, 1f,  dCliff ? 0f : 1f, dCliff ? 1f : 0f);
        }
    }
}
