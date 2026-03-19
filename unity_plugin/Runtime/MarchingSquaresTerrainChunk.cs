// MarchingSquaresTerrainChunk.cs
// Unity C# port of marching_squares_terrain_chunk.gd (Godot 4)
// All mathematical / computational logic is preserved exactly – no differences.

using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace MarchingSquaresTerrain
{
    /// <summary>
    /// Represents a single terrain chunk.  Each chunk owns a height map,
    /// colour maps, and generates its own <see cref="Mesh"/> at runtime.
    ///
    /// Direct port of marching_squares_terrain_chunk.gd – every constant,
    /// formula, and control-flow branch is identical to the Godot source.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class MarchingSquaresTerrainChunk : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Merge mode enum + lookup table (identical to Godot source)
        // -----------------------------------------------------------------------
        public enum Mode { CUBIC, POLYHEDRON, ROUNDED_POLYHEDRON, SEMI_ROUND, SPHERICAL }

        public static readonly Dictionary<Mode, float> MERGE_MODE = new Dictionary<Mode, float>
        {
            { Mode.CUBIC,              0.6f  },
            { Mode.POLYHEDRON,         1.3f  },
            { Mode.ROUNDED_POLYHEDRON, 2.1f  },
            { Mode.SEMI_ROUND,         5.0f  },
            { Mode.SPHERICAL,          20.0f },
        };

        // -----------------------------------------------------------------------
        // Public fields (serialised by Unity)
        // -----------------------------------------------------------------------
        [HideInInspector] public MarchingSquaresTerrain terrain_system;
        [HideInInspector] public Vector2Int chunk_coords = Vector2Int.zero;

        [SerializeField] private Mode _merge_mode = Mode.POLYHEDRON;
        public Mode merge_mode
        {
            get => _merge_mode;
            set
            {
                _merge_mode = value;
                merge_threshold = MERGE_MODE[value];
                if (grass_planter != null && grass_planter.multimesh != null)
                {
                    bool isRound = value == Mode.SEMI_ROUND || value == Mode.SPHERICAL;
                    grass_planter.SetRoundMode(isRound);
                }
                RegenerateAllCells(true);
            }
        }

        // -----------------------------------------------------------------------
        // Runtime data (not serialised – populated at runtime)
        // -----------------------------------------------------------------------
        /// <summary>height_map[z][x]</summary>
        public float[][] height_map;

        /// <summary>Flat array, indexed as [z * dims.x + x].</summary>
        public Color[] color_map_0;
        public Color[] color_map_1;
        public Color[] wall_color_map_0;
        public Color[] wall_color_map_1;

        /// <summary>
        /// Grass mask per cell-vertex.
        /// r &gt;= 1 = masked OFF, g &gt;= 1 = force ON.
        /// </summary>
        public Color[] grass_mask_map;

        public float merge_threshold = MERGE_MODE[Mode.POLYHEDRON];

        // -----------------------------------------------------------------------
        // Blend option vars (identical to GDScript)
        // -----------------------------------------------------------------------
        /// <summary>Below this normalised height → lower boundary colour.</summary>
        public float lower_thresh = 0.3f;
        /// <summary>Above this normalised height → upper boundary colour.</summary>
        public float upper_thresh = 0.7f;
        public float blend_zone;

        // -----------------------------------------------------------------------
        // Cell geometry cache
        // -----------------------------------------------------------------------
        public class CellGeometry
        {
            public List<Vector3> verts           = new List<Vector3>();
            public List<Vector2> uvs             = new List<Vector2>();
            public List<Vector2> uv2s            = new List<Vector2>();
            public List<Color>   color_0s        = new List<Color>();
            public List<Color>   color_1s        = new List<Color>();
            public List<Color>   custom_1_values = new List<Color>();
            public List<Color>   mat_blends      = new List<Color>();
            public List<bool>    is_floor        = new List<bool>();
        }

        public Dictionary<Vector2Int, CellGeometry> cell_geometry = new Dictionary<Vector2Int, CellGeometry>();
        private bool[][] needs_update;

        // -----------------------------------------------------------------------
        // Grass planter reference
        // -----------------------------------------------------------------------
        public MarchingSquaresGrassPlanter grass_planter;

        // -----------------------------------------------------------------------
        // Cached world position (avoids Transform calls from threads)
        // -----------------------------------------------------------------------
        public Vector3 globalPositionCached;

        // -----------------------------------------------------------------------
        // Convenience properties – delegate to terrain_system (mirrors GDScript)
        // -----------------------------------------------------------------------
        public Vector3Int dimensions => terrain_system != null ? terrain_system.dimensions : Vector3Int.zero;
        public Vector2   cell_size   => terrain_system != null ? terrain_system.cell_size   : Vector2.one;

        // -----------------------------------------------------------------------
        // Thread synchronisation
        // -----------------------------------------------------------------------
        private readonly object _meshLock = new object();

        // -----------------------------------------------------------------------
        // Mesh building accumulators (flushed per-RegenerateMesh call)
        // -----------------------------------------------------------------------
        private readonly List<Vector3>  _verts       = new List<Vector3>();
        private readonly List<int>      _tris        = new List<int>();
        private readonly List<Vector2>  _uvs         = new List<Vector2>();
        private readonly List<Vector2>  _uv2s        = new List<Vector2>();
        private readonly List<Color>    _colors      = new List<Color>();       // color_0
        private readonly List<Vector4>  _color1s     = new List<Vector4>();     // custom0 (color_1)
        private readonly List<Vector4>  _custom1s    = new List<Vector4>();     // custom1
        private readonly List<Vector4>  _matBlends   = new List<Vector4>();     // custom2
        private readonly List<bool>     _floors      = new List<bool>();

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------
        void Awake()
        {
            blend_zone = upper_thresh - lower_thresh;
            if (grass_planter == null)
                grass_planter = GetComponentInChildren<MarchingSquaresGrassPlanter>();
        }

        // -----------------------------------------------------------------------
        // initialize_terrain – mirrors GDScript exactly
        // -----------------------------------------------------------------------
        /// <summary>
        /// Called by <see cref="MarchingSquaresTerrain"/> after all chunk data is loaded.
        /// </summary>
        public void InitializeTerrain(bool shouldRegenerateMesh = true)
        {
            blend_zone = upper_thresh - lower_thresh;

            // Initialise needs_update – all cells dirty on first load
            int cx = dimensions.x - 1;
            int cz = dimensions.z - 1;
            needs_update = new bool[cz][];
            for (int z = 0; z < cz; z++)
            {
                needs_update[z] = new bool[cx];
                for (int x = 0; x < cx; x++)
                    needs_update[z][x] = true;
            }

            if (height_map == null)        GenerateHeightMap();
            if (color_map_0 == null)       GenerateColorMaps();
            if (wall_color_map_0 == null)  GenerateWallColorMaps();
            if (grass_mask_map == null)    GenerateGrassMaskMap();

            if (shouldRegenerateMesh)
                RegenerateMesh(true);
        }

        // -----------------------------------------------------------------------
        // RegenerateMesh – mirrors regenerate_mesh() in GDScript
        // -----------------------------------------------------------------------
        public void RegenerateMesh(bool useThreads = false)
        {
            _verts.Clear(); _tris.Clear(); _uvs.Clear(); _uv2s.Clear();
            _colors.Clear(); _color1s.Clear(); _custom1s.Clear(); _matBlends.Clear(); _floors.Clear();

            globalPositionCached = transform.position;

            // Ensure grass planter exists
            if (grass_planter == null)
            {
                grass_planter = GetComponentInChildren<MarchingSquaresGrassPlanter>();
                if (grass_planter == null)
                {
                    var go = new GameObject("GrassPlanter");
                    go.transform.SetParent(transform, false);
                    grass_planter = go.AddComponent<MarchingSquaresGrassPlanter>();
                }
            }
            grass_planter.Setup(this);

            GenerateTerrainCells(useThreads);

            // Build Unity Mesh
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(_verts);

            // Build triangle list.
            // Reverse winding (swap i+1 / i+2) so normals point upward in Unity's
            // left-hand coordinate system.  The Godot source used a right-hand
            // coordinate system where the same vertex order produced outward normals,
            // but in Unity the winding must be flipped to achieve the same result.
            for (int i = 0; i < _verts.Count; i += 3)
            {
                _tris.Add(i);
                _tris.Add(i + 2);
                _tris.Add(i + 1);
            }
            mesh.SetTriangles(_tris, 0);

            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _uv2s);
            mesh.SetColors(_colors);
            mesh.SetUVs(2, _color1s);
            mesh.SetUVs(3, _custom1s);
            mesh.SetUVs(4, _matBlends);

            mesh.RecalculateNormals();

            var mf = GetComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            // Update the MeshCollider so Physics.Raycast hits the terrain in the
            // editor (required for the paint / height tools in OnSceneGUI).
            var mc = GetComponent<MeshCollider>();
            if (mc != null)
                mc.sharedMesh = mesh;

            if (terrain_system != null)
            {
                var mr = GetComponent<MeshRenderer>();
                mr.sharedMaterial = terrain_system.terrain_material;
            }

            grass_planter.RegenerateAllCells();
        }

        // -----------------------------------------------------------------------
        // GenerateTerrainCells – mirrors generate_terrain_cells() exactly
        // -----------------------------------------------------------------------
        private void GenerateTerrainCells(bool useThreads)
        {
            int cx = dimensions.x - 1;
            int cz = dimensions.z - 1;

            if (useThreads)
            {
                var jobs = new List<System.Action>();

                for (int z = 0; z < cz; z++)
                {
                    for (int x = 0; x < cx; x++)
                    {
                        var cellCoords = new Vector2Int(x, z);
                        int lz = z, lx = x;

                        if (!needs_update[lz][lx])
                        {
                            jobs.Add(() => AddExistingCellGeometry(cellCoords));
                        }
                        else
                        {
                            needs_update[lz][lx] = false;
                            EnsureCellGeometry(cellCoords);
                            var colorHelper = new MarchingSquaresTerrainVertexColorHelper();
                            var cell = new MarchingSquaresTerrainCell(
                                this, colorHelper,
                                height_map[lz][lx], height_map[lz][lx + 1],
                                height_map[lz + 1][lx], height_map[lz + 1][lx + 1],
                                merge_threshold);
                            colorHelper.chunk = this;
                            colorHelper.cell  = cell;
                            jobs.Add(() => cell.GenerateGeometry(cellCoords));
                        }
                    }
                }

                // Parallel execution using ThreadPool
                var pool = new MarchingSquaresThreadPool(jobs);
                pool.Execute();
            }
            else
            {
                for (int z = 0; z < cz; z++)
                {
                    for (int x = 0; x < cx; x++)
                    {
                        var cellCoords = new Vector2Int(x, z);

                        if (!needs_update[z][x])
                        {
                            AddExistingCellGeometry(cellCoords);
                        }
                        else
                        {
                            needs_update[z][x] = false;
                            EnsureCellGeometry(cellCoords);
                            var colorHelper = new MarchingSquaresTerrainVertexColorHelper();
                            var cell = new MarchingSquaresTerrainCell(
                                this, colorHelper,
                                height_map[z][x], height_map[z][x + 1],
                                height_map[z + 1][x], height_map[z + 1][x + 1],
                                merge_threshold);
                            colorHelper.chunk = this;
                            colorHelper.cell  = cell;
                            cell.GenerateGeometry(cellCoords);
                        }
                    }
                }
            }
        }

        // -----------------------------------------------------------------------
        // AddPolygons / _add_point – mirrors add_polygons() / _add_point() in GDScript
        // -----------------------------------------------------------------------
        /// <summary>
        /// Appends geometry from a single cell into the chunk mesh accumulators.
        /// Mirrors add_polygons() in GDScript (including smooth-group tracking).
        /// </summary>
        public void AddPolygons(
            Vector2Int cellCoords,
            List<Vector3> pts,
            List<Vector2> uvs,
            List<Vector2> uv2s,
            List<Color>   color_0s,
            List<Color>   color_1s,
            List<Color>   custom_1_values,
            List<Color>   mat_blends,
            List<bool>    floorsIn)
        {
            lock (_meshLock)
            {
                var cg = cell_geometry[cellCoords];
                for (int i = 0; i < pts.Count; i++)
                {
                    _AddPoint(cellCoords, pts[i], uvs[i], uv2s[i],
                              color_0s[i], color_1s[i], custom_1_values[i], mat_blends[i], floorsIn[i],
                              cg);
                }
            }
        }

        private void _AddPoint(
            Vector2Int cellCoords,
            Vector3 vert, Vector2 uv, Vector2 uv2,
            Color color_0, Color color_1,
            Color custom_1_value, Color mat_blend,
            bool isFloor,
            CellGeometry cg)
        {
            _verts.Add(vert);
            _uvs.Add(uv);
            _uv2s.Add(uv2);
            _colors.Add(color_0);
            _color1s.Add(new Vector4(color_1.r, color_1.g, color_1.b, color_1.a));
            _custom1s.Add(new Vector4(custom_1_value.r, custom_1_value.g, custom_1_value.b, custom_1_value.a));
            _matBlends.Add(new Vector4(mat_blend.r, mat_blend.g, mat_blend.b, mat_blend.a));
            _floors.Add(isFloor);

            cg.verts.Add(vert);
            cg.uvs.Add(uv);
            cg.uv2s.Add(uv2);
            cg.color_0s.Add(color_0);
            cg.color_1s.Add(color_1);
            cg.custom_1_values.Add(custom_1_value);
            cg.mat_blends.Add(mat_blend);
            cg.is_floor.Add(isFloor);
        }

        // Replay an already-generated cell's geometry into the mesh buffers
        private void AddExistingCellGeometry(Vector2Int cellCoords)
        {
            if (!cell_geometry.TryGetValue(cellCoords, out var cg)) return;
            lock (_meshLock)
            {
                for (int i = 0; i < cg.verts.Count; i++)
                {
                    _verts.Add(cg.verts[i]);
                    _uvs.Add(cg.uvs[i]);
                    _uv2s.Add(cg.uv2s[i]);
                    _colors.Add(cg.color_0s[i]);
                    _color1s.Add(new Vector4(cg.color_1s[i].r, cg.color_1s[i].g, cg.color_1s[i].b, cg.color_1s[i].a));
                    _custom1s.Add(new Vector4(cg.custom_1_values[i].r, cg.custom_1_values[i].g, cg.custom_1_values[i].b, cg.custom_1_values[i].a));
                    _matBlends.Add(new Vector4(cg.mat_blends[i].r, cg.mat_blends[i].g, cg.mat_blends[i].b, cg.mat_blends[i].a));
                    _floors.Add(cg.is_floor[i]);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Map generators (mirrors GDScript generate_*() functions exactly)
        // -----------------------------------------------------------------------

        /// <summary>Initialises height_map to all zeros (or noise if terrain_system has a noise object).</summary>
        public void GenerateHeightMap()
        {
            height_map = new float[dimensions.z][];
            for (int z = 0; z < dimensions.z; z++)
            {
                height_map[z] = new float[dimensions.x];
                for (int x = 0; x < dimensions.x; x++)
                    height_map[z][x] = 0f;
            }
        }

        /// <summary>Initialises color_map_0 / color_map_1 to transparent black.</summary>
        public void GenerateColorMaps()
        {
            int count = dimensions.z * dimensions.x;
            color_map_0 = new Color[count];
            color_map_1 = new Color[count];
            for (int i = 0; i < count; i++)
            {
                color_map_0[i] = new Color(0, 0, 0, 0);
                color_map_1[i] = new Color(0, 0, 0, 0);
            }
        }

        /// <summary>Initialises wall colour maps to default texture slot 0 (R=1).</summary>
        public void GenerateWallColorMaps()
        {
            int count = dimensions.z * dimensions.x;
            wall_color_map_0 = new Color[count];
            wall_color_map_1 = new Color[count];
            for (int i = 0; i < count; i++)
            {
                wall_color_map_0[i] = new Color(1, 0, 0, 0);
                wall_color_map_1[i] = new Color(1, 0, 0, 0);
            }
        }

        /// <summary>Initialises grass_mask_map to all white (grass ON everywhere).</summary>
        public void GenerateGrassMaskMap()
        {
            int count = dimensions.z * dimensions.x;
            grass_mask_map = new Color[count];
            for (int i = 0; i < count; i++)
                grass_mask_map[i] = new Color(1, 1, 1, 1);
        }

        // -----------------------------------------------------------------------
        // Cell geometry cache helpers
        // -----------------------------------------------------------------------
        private void EnsureCellGeometry(Vector2Int cellCoords)
        {
            lock (_meshLock)
            {
                cell_geometry[cellCoords] = new CellGeometry();
            }
        }

        // -----------------------------------------------------------------------
        // Draw helpers – mirror draw_height() / draw_color_*() / draw_grass_mask()
        // -----------------------------------------------------------------------

        /// <summary>Sets a height value and marks affected cells for update.</summary>
        public void DrawHeight(int x, int z, float y)
        {
            height_map[z][x] = y;
            MarkDirty();
            NotifyNeedsUpdate(z, x);
            NotifyNeedsUpdate(z, x - 1);
            NotifyNeedsUpdate(z - 1, x);
            NotifyNeedsUpdate(z - 1, x - 1);
        }

        public void DrawColor0(int x, int z, Color color)
        {
            color_map_0[z * dimensions.x + x] = color;
            MarkDirty();
            NotifyNeedsUpdate(z, x); NotifyNeedsUpdate(z, x - 1);
            NotifyNeedsUpdate(z - 1, x); NotifyNeedsUpdate(z - 1, x - 1);
        }

        public void DrawColor1(int x, int z, Color color)
        {
            color_map_1[z * dimensions.x + x] = color;
            MarkDirty();
            NotifyNeedsUpdate(z, x); NotifyNeedsUpdate(z, x - 1);
            NotifyNeedsUpdate(z - 1, x); NotifyNeedsUpdate(z - 1, x - 1);
        }

        public void DrawWallColor0(int x, int z, Color color)
        {
            wall_color_map_0[z * dimensions.x + x] = color;
            MarkDirty();
            NotifyNeedsUpdate(z, x); NotifyNeedsUpdate(z, x - 1);
            NotifyNeedsUpdate(z - 1, x); NotifyNeedsUpdate(z - 1, x - 1);
        }

        public void DrawWallColor1(int x, int z, Color color)
        {
            wall_color_map_1[z * dimensions.x + x] = color;
            MarkDirty();
            NotifyNeedsUpdate(z, x); NotifyNeedsUpdate(z, x - 1);
            NotifyNeedsUpdate(z - 1, x); NotifyNeedsUpdate(z - 1, x - 1);
        }

        public void DrawGrassMask(int x, int z, Color masked)
        {
            grass_mask_map[z * dimensions.x + x] = masked;
            MarkDirty();
            NotifyNeedsUpdate(z, x); NotifyNeedsUpdate(z, x - 1);
            NotifyNeedsUpdate(z - 1, x); NotifyNeedsUpdate(z - 1, x - 1);
        }

        // -----------------------------------------------------------------------
        // Data accessors – mirror GDScript get_* functions
        // -----------------------------------------------------------------------
        public float  GetHeight(Vector2Int cc)      => height_map[cc.y][cc.x];
        public Color  GetColor0(Vector2Int cc)       => color_map_0[cc.y * dimensions.x + cc.x];
        public Color  GetColor1(Vector2Int cc)       => color_map_1[cc.y * dimensions.x + cc.x];
        public Color  GetWallColor0(Vector2Int cc)   => wall_color_map_0[cc.y * dimensions.x + cc.x];
        public Color  GetWallColor1(Vector2Int cc)   => wall_color_map_1[cc.y * dimensions.x + cc.x];
        public Color  GetGrassMask(Vector2Int cc)    => grass_mask_map[cc.y * dimensions.x + cc.x];

        // -----------------------------------------------------------------------
        // Update tracking
        // -----------------------------------------------------------------------
        public void NotifyNeedsUpdate(int z, int x)
        {
            if (z < 0 || z >= dimensions.z - 1 || x < 0 || x >= dimensions.x - 1) return;
            if (needs_update != null) needs_update[z][x] = true;
        }

        public void MarkDirty() { /* Hook for save/undo systems */ }

        // -----------------------------------------------------------------------
        // RegenerateAllCells – mirrors regenerate_all_cells() in GDScript
        // -----------------------------------------------------------------------
        public void RegenerateAllCells(bool useThreads = false)
        {
            int cx = dimensions.x - 1;
            int cz = dimensions.z - 1;
            for (int z = 0; z < cz; z++)
                for (int x = 0; x < cx; x++)
                    needs_update[z][x] = true;
            RegenerateMesh(useThreads);
        }
    }
}
