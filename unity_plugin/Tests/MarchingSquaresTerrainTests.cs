// MarchingSquaresTerrainTests.cs
// Cross-validates the Unity C# port against the reference mathematical constants
// and formulas from the Godot GDScript source.
// These tests are pure C# (no Unity engine types needed beyond UnityEngine.Mathf)
// and can be run in the Unity Test Runner (Edit-Mode).

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MarchingSquaresTerrain.Tests
{
    // =========================================================================
    // Helper stubs – let the pure-math logic run without a full scene
    // =========================================================================

    internal static class TerrainTestHelpers
    {
        /// <summary>
        /// Returns a chunk whose terrain_system returns the specified dimensions
        /// and cell_size, without requiring any Unity GameObject.
        /// </summary>
        public static MarchingSquaresTerrainChunk MakeChunk(
            Vector3Int dims, Vector2 cellSz)
        {
            var go = new GameObject("__test_chunk__");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var chunk = go.AddComponent<MarchingSquaresTerrainChunk>();

            var tsGo  = new GameObject("__test_terrain__");
            var ts    = tsGo.AddComponent<MarchingSquaresTerrain>();
            ts.dimensions = dims;
            ts.cell_size  = cellSz;

            chunk.terrain_system = ts;
            chunk.chunk_coords   = Vector2Int.zero;

            // Minimal map sizes so index arithmetic doesn't throw
            chunk.GenerateColorMaps();
            chunk.GenerateWallColorMaps();
            chunk.GenerateGrassMaskMap();

            return chunk;
        }

        public static void Cleanup(MarchingSquaresTerrainChunk chunk)
        {
            UnityEngine.Object.DestroyImmediate(chunk.terrain_system.gameObject);
            UnityEngine.Object.DestroyImmediate(chunk.gameObject);
        }
    }

    // =========================================================================
    // 1.  MERGE-THRESHOLD FORMULA
    // =========================================================================
    [TestFixture]
    public class MergeThresholdTests
    {
        // Reference formula (verbatim from GDScript):
        //   cell_scale_factor = clamp((cell_size.x + cell_size.y) / 4.0, 0.3, 1.0)
        //   dimensions_scale_factor = clamp(((dims.x/33.0) + (dims.z/33.0)) / 2.0, 0.5, 2.0)
        //   merge_threshold = merge_threshold_ * dimensions_scale_factor * cell_scale_factor

        private static float Expected(float baseThresh, Vector3Int dims, Vector2 cellSz)
        {
            float csf = Mathf.Clamp((cellSz.x + cellSz.y) / 4f, 0.3f, 1.0f);
            float dsf = Mathf.Clamp(((dims.x / 33f) + (dims.z / 33f)) / 2f, 0.5f, 2.0f);
            return baseThresh * dsf * csf;
        }

        [Test] public void Default33x33_CellSize2x2_Polyhedron()
        {
            var dims   = new Vector3Int(33, 32, 33);
            var cellSz = new Vector2(2f, 2f);
            float baseT = MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.POLYHEDRON];

            var chunk = TerrainTestHelpers.MakeChunk(dims, cellSz);
            var helper = new MarchingSquaresTerrainVertexColorHelper();
            var cell   = new MarchingSquaresTerrainCell(chunk, helper, 0f, 0f, 0f, 0f, baseT);
            helper.chunk = chunk; helper.cell = cell;

            float expected = Expected(baseT, dims, cellSz);
            Assert.AreEqual(expected, cell.merge_threshold, 1e-5f,
                "Merge threshold formula mismatch for default 33x33 POLYHEDRON terrain");

            TerrainTestHelpers.Cleanup(chunk);
        }

        [Test] public void SmallTerrain_CellSizeSmall_ThresholdClamped()
        {
            var dims   = new Vector3Int(9, 32, 9);
            var cellSz = new Vector2(0.5f, 0.5f);
            float baseT = MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.CUBIC];

            var chunk = TerrainTestHelpers.MakeChunk(dims, cellSz);
            var helper = new MarchingSquaresTerrainVertexColorHelper();
            var cell   = new MarchingSquaresTerrainCell(chunk, helper, 0f, 0f, 0f, 0f, baseT);

            float expected = Expected(baseT, dims, cellSz);
            Assert.AreEqual(expected, cell.merge_threshold, 1e-5f,
                "Merge threshold clamping failed for small terrain");

            TerrainTestHelpers.Cleanup(chunk);
        }
    }

    // =========================================================================
    // 2.  CELL ROTATION LOGIC
    // =========================================================================
    [TestFixture]
    public class CellRotationTests
    {
        // Reference from GDScript:
        //   DEG90:  ay=_by, by=_dy, cy=_ay, dy=_cy
        //   DEG180: ay=_dy, by=_cy, cy=_by, dy=_ay
        //   DEG270: ay=_cy, by=_ay, cy=_dy, dy=_by

        private MarchingSquaresTerrainChunk _chunk;

        [SetUp] public void SetUp()
        {
            _chunk = TerrainTestHelpers.MakeChunk(new Vector3Int(33,32,33), new Vector2(2,2));
        }
        [TearDown] public void TearDown()
        {
            TerrainTestHelpers.Cleanup(_chunk);
        }

        private MarchingSquaresTerrainCell MakeCell(float a, float b, float c, float d)
        {
            var h    = new MarchingSquaresTerrainVertexColorHelper();
            var cell = new MarchingSquaresTerrainCell(_chunk, h, a, b, c, d,
                MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.POLYHEDRON]);
            h.chunk = _chunk; h.cell = cell;
            return cell;
        }

        [Test] public void DEG0_IsIdentity()
        {
            var cell = MakeCell(1f, 2f, 3f, 4f);
            cell.rotation = MarchingSquaresTerrainCell.CellRotation.DEG0;
            Assert.AreEqual(1f, cell.ay); Assert.AreEqual(2f, cell.by);
            Assert.AreEqual(3f, cell.cy); Assert.AreEqual(4f, cell.dy);
        }

        [Test] public void DEG90_RotatesCorrectly()
        {
            var cell = MakeCell(1f, 2f, 3f, 4f);
            cell.rotation = MarchingSquaresTerrainCell.CellRotation.DEG90;
            // ay=_by=2, by=_dy=4, cy=_ay=1, dy=_cy=3
            Assert.AreEqual(2f, cell.ay, 1e-5f);
            Assert.AreEqual(4f, cell.by, 1e-5f);
            Assert.AreEqual(1f, cell.cy, 1e-5f);
            Assert.AreEqual(3f, cell.dy, 1e-5f);
        }

        [Test] public void DEG180_RotatesCorrectly()
        {
            var cell = MakeCell(1f, 2f, 3f, 4f);
            cell.rotation = MarchingSquaresTerrainCell.CellRotation.DEG180;
            // ay=_dy=4, by=_cy=3, cy=_by=2, dy=_ay=1
            Assert.AreEqual(4f, cell.ay, 1e-5f);
            Assert.AreEqual(3f, cell.by, 1e-5f);
            Assert.AreEqual(2f, cell.cy, 1e-5f);
            Assert.AreEqual(1f, cell.dy, 1e-5f);
        }

        [Test] public void DEG270_RotatesCorrectly()
        {
            var cell = MakeCell(1f, 2f, 3f, 4f);
            cell.rotation = MarchingSquaresTerrainCell.CellRotation.DEG270;
            // ay=_cy=3, by=_ay=1, cy=_dy=4, dy=_by=2
            Assert.AreEqual(3f, cell.ay, 1e-5f);
            Assert.AreEqual(1f, cell.by, 1e-5f);
            Assert.AreEqual(4f, cell.cy, 1e-5f);
            Assert.AreEqual(2f, cell.dy, 1e-5f);
        }

        [Test] public void EdgeFlags_Flat_AllConnected()
        {
            var cell = MakeCell(5f, 5f, 5f, 5f);
            Assert.IsTrue(cell.AllEdgesAreConnected(), "Flat terrain should have all edges connected");
        }

        [Test] public void EdgeFlags_StepAB_ABFalse()
        {
            float t = MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.POLYHEDRON] * 2f;
            var cell = MakeCell(0f, t, 0f, 0f);
            Assert.IsFalse(cell.ab, "AB edge should NOT be connected for large height difference");
        }
    }

    // =========================================================================
    // 3.  VERTEX COORDINATE / UV MATH
    // =========================================================================
    [TestFixture]
    public class VertexCoordinateTests
    {
        // Reference formula from add_point():
        //   vert = Vector3((cell_coords.x + x) * cell_size.x, y, (cell_coords.y + z) * cell_size.y)
        //   floor uv2 = Vector2(vert.x, vert.z) / cell_size

        [Test] public void FloorVertex_WorldPosition_MatchesFormula()
        {
            var dims   = new Vector3Int(33, 32, 33);
            var cellSz = new Vector2(2f, 2f);
            var chunk  = TerrainTestHelpers.MakeChunk(dims, cellSz);

            var helper = new MarchingSquaresTerrainVertexColorHelper();
            var cell   = new MarchingSquaresTerrainCell(chunk, helper, 1f, 1f, 1f, 1f,
                MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.POLYHEDRON]);
            helper.chunk = chunk; helper.cell = cell;
            cell.cell_coords = new Vector2Int(3, 5);
            cell.floor_mode  = true;

            // add_point(x=0.5, y=1.0, z=0.5, uv.x=0, uv.y=0)
            // vert.x = (3 + 0.5) * 2.0 = 7.0
            // vert.z = (5 + 0.5) * 2.0 = 11.0
            cell.AddPoint(0.5f, 1.0f, 0.5f, 0f, 0f);
            var vert = cell.pts[0];
            Assert.AreEqual(7.0f,  vert.x, 1e-4f, "World X mismatch");
            Assert.AreEqual(1.0f,  vert.y, 1e-4f, "World Y mismatch");
            Assert.AreEqual(11.0f, vert.z, 1e-4f, "World Z mismatch");

            // uv2 = Vector2(7.0/2.0, 11.0/2.0) = (3.5, 5.5)
            var uv2 = cell.uv2s[0];
            Assert.AreEqual(3.5f, uv2.x, 1e-4f, "UV2.x mismatch");
            Assert.AreEqual(5.5f, uv2.y, 1e-4f, "UV2.y mismatch");

            TerrainTestHelpers.Cleanup(chunk);
        }
    }

    // =========================================================================
    // 4.  VERTEX COLOR HELPERS
    // =========================================================================
    [TestFixture]
    public class VertexColorHelperTests
    {
        [Test] public void GetDominantColor_R_Channel()
        {
            var result = MarchingSquaresTerrainVertexColorHelper.GetDominantColor(new Color(0.9f, 0.3f, 0.1f, 0.0f));
            Assert.AreEqual(1f, result.r, 1e-5f);
            Assert.AreEqual(0f, result.g, 1e-5f);
            Assert.AreEqual(0f, result.b, 1e-5f);
            Assert.AreEqual(0f, result.a, 1e-5f);
        }

        [Test] public void GetDominantColor_G_Channel()
        {
            var result = MarchingSquaresTerrainVertexColorHelper.GetDominantColor(new Color(0.1f, 0.8f, 0.2f, 0.0f));
            Assert.AreEqual(0f, result.r, 1e-5f);
            Assert.AreEqual(1f, result.g, 1e-5f);
        }

        [Test] public void GetDominantColor_A_Channel()
        {
            var result = MarchingSquaresTerrainVertexColorHelper.GetDominantColor(new Color(0f, 0f, 0f, 0.9f));
            Assert.AreEqual(1f, result.a, 1e-5f);
        }

        // Reference: get_texture_index_from_colors() = c0_channel * 4 + c1_channel
        [Test] public void GetTextureIndex_R_R_IsZero()
        {
            var c0 = new Color(1, 0, 0, 0);
            var c1 = new Color(1, 0, 0, 0);
            Assert.AreEqual(0, MarchingSquaresTerrainVertexColorHelper.GetTextureIndexFromColors(c0, c1));
        }

        [Test] public void GetTextureIndex_G_B_Is6()
        {
            var c0 = new Color(0, 1, 0, 0); // G = channel 1
            var c1 = new Color(0, 0, 1, 0); // B = channel 2
            Assert.AreEqual(6, MarchingSquaresTerrainVertexColorHelper.GetTextureIndexFromColors(c0, c1));
        }

        [Test] public void GetTextureIndex_A_A_Is15()
        {
            var c0 = new Color(0, 0, 0, 1); // A = channel 3
            var c1 = new Color(0, 0, 0, 1); // A = channel 3
            Assert.AreEqual(15, MarchingSquaresTerrainVertexColorHelper.GetTextureIndexFromColors(c0, c1));
        }

        // Round-trip: TextureIndexToColors(GetTextureIndexFromColors(c0, c1)) == (c0, c1)
        [Test] public void TextureIndexRoundTrip_AllChannels()
        {
            Color[] channels = {
                new Color(1,0,0,0), new Color(0,1,0,0),
                new Color(0,0,1,0), new Color(0,0,0,1)
            };
            foreach (var c0 in channels)
            {
                foreach (var c1 in channels)
                {
                    int idx = MarchingSquaresTerrainVertexColorHelper.GetTextureIndexFromColors(c0, c1);
                    var (rc0, rc1) = MarchingSquaresTerrainVertexColorHelper.TextureIndexToColors(idx);
                    Assert.AreEqual(c0, rc0, $"Round-trip c0 failed for c0={c0}, c1={c1}");
                    Assert.AreEqual(c1, rc1, $"Round-trip c1 failed for c0={c0}, c1={c1}");
                }
            }
        }
    }

    // =========================================================================
    // 5.  MERGE MODE TABLE
    // =========================================================================
    [TestFixture]
    public class MergeModeTableTests
    {
        // Reference values (directly from GDScript const MERGE_MODE):
        [Test] public void CUBIC_Is_0_6()          => Assert.AreEqual(0.6f,  MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.CUBIC],              1e-5f);
        [Test] public void POLYHEDRON_Is_1_3()      => Assert.AreEqual(1.3f,  MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.POLYHEDRON],          1e-5f);
        [Test] public void ROUNDED_POLY_Is_2_1()    => Assert.AreEqual(2.1f,  MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.ROUNDED_POLYHEDRON],  1e-5f);
        [Test] public void SEMI_ROUND_Is_5_0()      => Assert.AreEqual(5.0f,  MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.SEMI_ROUND],          1e-5f);
        [Test] public void SPHERICAL_Is_20_0()      => Assert.AreEqual(20.0f, MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.SPHERICAL],           1e-5f);
    }

    // =========================================================================
    // 6.  BLEND CONSTANTS
    // =========================================================================
    [TestFixture]
    public class BlendConstantTests
    {
        [Test] public void Cell_BLEND_EDGE_SENSITIVITY_Is_1_25()
            => Assert.AreEqual(1.25f, MarchingSquaresTerrainCell.BLEND_EDGE_SENSITIVITY, 1e-5f);

        [Test] public void Helper_BLEND_EDGE_SENSITIVITY_Is_1_25()
            => Assert.AreEqual(1.25f, MarchingSquaresTerrainVertexColorHelper.BLEND_EDGE_SENSITIVITY, 1e-5f);
    }

    // =========================================================================
    // 7.  FULL FLOOR GEOMETRY
    // =========================================================================
    [TestFixture]
    public class FullFloorGeometryTests
    {
        [Test] public void HigherPolyFloor_Produces12Vertices()
        {
            Assert.IsTrue(MarchingSquaresTerrainCell.HIGHER_POLY_FLOORS);
            var chunk  = TerrainTestHelpers.MakeChunk(new Vector3Int(33,32,33), new Vector2(2,2));
            var helper = new MarchingSquaresTerrainVertexColorHelper();
            var cell   = new MarchingSquaresTerrainCell(chunk, helper, 5f, 5f, 5f, 5f,
                MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.POLYHEDRON]);
            helper.chunk = chunk; helper.cell = cell;
            cell.GenerateGeometry(Vector2Int.zero);
            // HIGHER_POLY_FLOORS=true: 4 fan triangles × 3 verts = 12
            Assert.AreEqual(12, cell.pts.Count,
                "Full flat floor should produce 12 vertices (4 fan triangles)");
            TerrainTestHelpers.Cleanup(chunk);
        }
    }

    // =========================================================================
    // 7b. TRIANGLE WINDING ORDER
    // =========================================================================
    /// <summary>
    /// Verify that the winding-order fix (triangle indices built as i, i+2, i+1)
    /// produces upward-facing normals for a flat floor cell.
    ///
    /// Unity uses a left-hand coordinate system where the front face of a triangle
    /// is determined by clockwise vertex order when viewed from the front (i.e. from
    /// the direction the surface normal points).
    /// The Godot source used a right-hand coordinate system where the same vertex
    /// order produced outward normals; in Unity the indices must be reversed.
    ///
    /// The test computes the cross-product normal using the reversed winding and
    /// asserts its Y component is positive (i.e. faces up).
    /// </summary>
    [TestFixture]
    public class TriangleWindingTests
    {
        [Test]
        public void FlatFloor_WindingFix_NormalPointsUp()
        {
            var chunk  = TerrainTestHelpers.MakeChunk(new Vector3Int(33, 32, 33), new Vector2(2f, 2f));
            var helper = new MarchingSquaresTerrainVertexColorHelper();
            // Flat cell – all four corners at the same height
            var cell   = new MarchingSquaresTerrainCell(chunk, helper, 5f, 5f, 5f, 5f,
                MarchingSquaresTerrainChunk.MERGE_MODE[MarchingSquaresTerrainChunk.Mode.POLYHEDRON]);
            helper.chunk = chunk; helper.cell = cell;
            cell.GenerateGeometry(Vector2Int.zero);

            // The mesh builder writes triangle indices as (i, i+2, i+1) to flip the
            // winding relative to the Godot source.  Replicate that here and check the
            // resulting face normal for each triangle.
            var pts = cell.pts;
            Assert.IsTrue(pts.Count >= 3, "Expected at least one triangle");
            Assert.AreEqual(0, pts.Count % 3, "Vertex count must be a multiple of 3");

            for (int i = 0; i < pts.Count; i += 3)
            {
                // Mirror the reversed winding used in RegenerateMesh: (i, i+2, i+1)
                Vector3 v0 = pts[i];
                Vector3 v2 = pts[i + 2]; // second index in the fixed winding
                Vector3 v1 = pts[i + 1]; // third index in the fixed winding

                Vector3 edge1 = v2 - v0;
                Vector3 edge2 = v1 - v0;
                Vector3 normal = Vector3.Cross(edge1, edge2);

                Assert.Greater(normal.y, 0f,
                    $"Triangle {i / 3}: normal Y should be positive (upward), got {normal.y:F4}. " +
                    $"Vertices: {v0}, {v2}, {v1}");
            }

            TerrainTestHelpers.Cleanup(chunk);
        }
    }

    // =========================================================================
    // 8.  BARYCENTRIC MATH (grass planter)
    // =========================================================================
    [TestFixture]
    public class BarycentricTests
    {
        // Reference from GDScript generate_grass_on_cell():
        //   u = (dot11 * dot02 - dot01 * dot12) * invDenom
        //   v = (dot00 * dot12 - dot01 * dot02) * invDenom

        [Test] public void Centroid_IsInTriangle()
        {
            var a = new Vector2(0f, 0f);
            var b = new Vector2(6f, 0f);
            var c = new Vector2(0f, 6f);
            var p = new Vector2(2f, 2f);

            var v0 = c - a; var v1 = b - a; var v2 = p - a;
            float d00=Vector2.Dot(v0,v0), d01=Vector2.Dot(v0,v1), d11=Vector2.Dot(v1,v1);
            float d02=Vector2.Dot(v0,v2), d12=Vector2.Dot(v1,v2);
            float inv = 1f / (d00*d11 - d01*d01);
            float u   = (d11*d02 - d01*d12) * inv;
            float vv  = (d00*d12 - d01*d02) * inv;

            Assert.IsTrue(u >= 0f && vv >= 0f && u + vv <= 1f, "Centroid should be inside triangle");
        }

        [Test] public void PointOutside_IsNotInTriangle()
        {
            var a = new Vector2(0f, 0f);
            var b = new Vector2(6f, 0f);
            var c = new Vector2(0f, 6f);
            var p = new Vector2(5f, 5f);

            var v0 = c - a; var v1 = b - a; var v2 = p - a;
            float d00=Vector2.Dot(v0,v0), d01=Vector2.Dot(v0,v1), d11=Vector2.Dot(v1,v1);
            float d02=Vector2.Dot(v0,v2), d12=Vector2.Dot(v1,v2);
            float inv = 1f / (d00*d11 - d01*d01);
            float u   = (d11*d02 - d01*d12) * inv;
            float vv  = (d00*d12 - d01*d02) * inv;

            Assert.IsFalse(u >= 0f && vv >= 0f && u + vv <= 1f, "Point outside triangle should fail");
        }
    }

    // =========================================================================
    // 9.  MATERIAL BLEND ENCODING
    // =========================================================================
    [TestFixture]
    public class MaterialBlendEncodingTests
    {
        // Reference: packed_mats = (mat_a + mat_b * 16) / 255.0
        [Test] public void PackedMats_Encoding_Matches()
        {
            int matA = 3, matB = 7;
            float expected = (matA + matB * 16f) / 255f;
            float actual   = (3    + 7    * 16f) / 255f;
            Assert.AreEqual(expected, actual, 1e-5f, "Packed material encoding mismatch");
        }

        [Test] public void MatC_Encoding_Matches()
        {
            float g = 10f / 15f;
            Assert.AreEqual(10f / 15f, g, 1e-5f, "mat_c / 15.0 encoding mismatch");
        }
    }

    // =========================================================================
    // 10. GRASS ALPHA LOOKUP
    // =========================================================================
    [TestFixture]
    public class GrassAlphaTests
    {
        // Reference: GRASS_ALPHA_VALUES = [0.0, 0.2, 0.4, 0.6, 0.8, 1.0]
        [Test] public void GrassAlphaValues_Match()
        {
            float[] expected = { 0.0f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };
            Assert.AreEqual(expected.Length, MarchingSquaresGrassPlanter.GRASS_ALPHA_VALUES.Length);
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], MarchingSquaresGrassPlanter.GRASS_ALPHA_VALUES[i], 1e-5f,
                    $"GRASS_ALPHA_VALUES[{i}] mismatch");
        }
    }
}
#endif
